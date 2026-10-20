using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;

namespace GolemFactory.World
{
    /// <summary>
    /// Reads the facts a stalled golem's triage needs from the world around it
    /// (docs/onboarding-feedback-design.md §3.2): does something feed the tile behind it, drain
    /// the tile in front, is a truckload on its way. Presentation-side and read-only; it runs
    /// only for a stalled golem, so a running factory pays nothing.
    ///
    /// <para>
    /// Places are compared by the STORE behind them, not by cell: every Post crate shares one
    /// buffer, the tower's nine cells one input, so "a golem pushes into a Post crate" feeds
    /// every Post crate without a second rule.
    /// </para>
    /// </summary>
    public sealed class StallFactsProbe
    {
        private readonly Func<IEnumerable<GolemEntity>> _golems;
        private readonly SpatialEndpointRegistry _endpoints;
        private readonly BeltNetwork _belts;
        private readonly Func<TruckloadMarket> _market;

        public StallFactsProbe(Func<IEnumerable<GolemEntity>> golems, SpatialEndpointRegistry endpoints,
            BeltNetwork belts, Func<TruckloadMarket> market = null)
        {
            _golems = golems;
            _endpoints = endpoints;
            _belts = belts;
            _market = market;
        }

        /// <summary>
        /// The endgame profile's sampler (docs/performance-report-template.md §3): <c>probe.read</c>
        /// times each read, <c>loop.walk</c> counts the golems the feeder walk visits. Null, the
        /// default, measures nothing.
        /// </summary>
        public PerfSampler Perf { get; set; }

        private int _walked;

        public StallFacts Read(GolemEntity golem)
        {
            PerfSampler perf = Perf;
            if (perf == null)
            {
                return ReadNow(golem);
            }
            _walked = 0;
            using (perf.Start(PerfSampler.ProbeRead))
            {
                StallFacts facts = ReadNow(golem);
                perf.Tally(PerfSampler.LoopWalk, _walked);
                return facts;
            }
        }

        private StallFacts ReadNow(GolemEntity golem)
        {
            if (golem == null || golem.Program == null)
            {
                return default;
            }

            AppendageActionDefinition step = golem.Program.CurrentStep;
            AppendageActionType action = step != null ? step.actionType : AppendageActionType.Haul;
            IItemEndpoint source = EndpointAt(golem.SourceCell);
            IItemEndpoint target = EndpointAt(golem.TargetCell);
            string wanted = WantedGood(golem, step, source);

            bool sourceGives = Gives(source);
            bool truckload = source is ResourceNodeEndpoint node && node.Node != null &&
                             (_market?.Invoke()?.IsInTransit(node.Node.NodeId) ?? false);
            return new StallFacts(action, sourceGives,
                sourceGives && IsFed(golem, source, wanted),
                IsDrained(golem, target),
                truckload,
                wanted);
        }

        // --- The good ------------------------------------------------------------------------

        /// <summary>
        /// The good the stalled step needs (§4.3): the stall's own good when it names one; the
        /// step's good; for an untyped take, the first input of the program's recipe the hold
        /// lacks; at a stall, the stall's good; else null.
        /// </summary>
        public static string WantedGood(GolemEntity golem, AppendageActionDefinition step, IItemEndpoint source)
        {
            if (golem.StallReason == StallReason.MissingItem && !string.IsNullOrEmpty(golem.StallResourceId))
            {
                return golem.StallResourceId;
            }

            if (step != null && step.actionType == AppendageActionType.Haul)
            {
                string typed = golem.Program.GetItemTypeAt(golem.Program.CurrentStepIndex);
                if (!string.IsNullOrEmpty(typed))
                {
                    return typed;
                }
            }

            if (step != null && step.actionType != AppendageActionType.Haul &&
                step.actionType != AppendageActionType.ExtractFromNode)
            {
                return null;
            }

            foreach (AppendageActionDefinition card in golem.Program.appendages)
            {
                if (card != null && card.actionType == AppendageActionType.Assemble && card.recipe != null)
                {
                    foreach (RecipeIngredient input in card.recipe.inputs)
                    {
                        if (golem.Inventory.GetInput(input.itemType) < input.quantity)
                        {
                            return input.itemType;
                        }
                    }
                }
            }

            return source is ResourceNodeEndpoint node && node.Node != null ? node.Node.ItemType : null;
        }

        // --- Fed -----------------------------------------------------------------------------

        private static bool Gives(IItemEndpoint source) =>
            source is StorageBufferEndpoint || source is FilteredBufferEndpoint ||
            source is BeltSegmentEndpoint || source is ResourceNodeEndpoint || source is ScrapRecyclerEndpoint;

        /// <summary>
        /// Fed: some other golem pushes the good into the source, and that feeder will deliver.
        /// A feeder delivers unless it is itself stalled on an empty take whose every feeder
        /// leads back round to a golem already asking: a loop of golems each waiting on the next,
        /// with nothing in it, never starts on its own, so every golem in it is stuck, not
        /// waiting (Phase 2 critique). A feeder stuck for any other reason -- nothing feeds it,
        /// it faces the wrong way, no steam -- still counts: one red at the root, grey behind (§3.2).
        /// </summary>
        private bool IsFed(GolemEntity golem, IItemEndpoint source, string good)
        {
            var visited = new HashSet<GolemEntity> { golem };
            foreach (GolemEntity feeder in Feeders(golem, source, good))
            {
                if (Delivers(feeder, visited))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The golems that make <paramref name="golem"/> wait (docs/onboarding-feedback-design.md
        /// §17.3): the direct feeders of its source for the good it wants, found as
        /// <see cref="IsFed"/> finds them, kept only when they deliver, in the order of the number
        /// in their id, at most <paramref name="max"/>. Empty exactly when <see cref="Read"/> says
        /// the source doesn't give or isn't fed (§17.2's one answer). Read for the hovered golem
        /// only, when its card is composed; never per frame for every golem.
        /// </summary>
        public IReadOnlyList<GolemEntity> FeedersOf(GolemEntity golem, int max = 3)
        {
            var named = new List<GolemEntity>();
            if (golem == null || golem.Program == null || max <= 0)
            {
                return named;
            }
            IItemEndpoint source = EndpointAt(golem.SourceCell);
            if (!Gives(source))
            {
                return named;
            }
            string wanted = WantedGood(golem, golem.Program.CurrentStep, source);
            foreach (GolemEntity feeder in Feeders(golem, source, wanted).OrderBy(NumberOrder).ThenBy(g => g.GolemId, StringComparer.Ordinal))
            {
                // A fresh visited set each: one feeder's walk can't mark a golem another reaches.
                if (Delivers(feeder, new HashSet<GolemEntity> { golem }))
                {
                    named.Add(feeder);
                    if (named.Count >= max)
                    {
                        break;
                    }
                }
            }
            return named;
        }

        /// <summary>Numbered ids first, by number; ids without one after them.</summary>
        private static long NumberOrder(GolemEntity golem)
        {
            int number = UI.GolemNames.Number(golem.GolemId);
            return number >= 0 ? number : long.MaxValue;
        }

        private bool Delivers(GolemEntity feeder, HashSet<GolemEntity> visited)
        {
            if (feeder.Program.State != GolemState.Stalled || !IsEmptyTake(feeder))
            {
                return true;
            }
            if (!visited.Add(feeder))
            {
                // Round a loop, or already found not to deliver: the walk is depth-first and
                // returns the moment any path delivers, so a golem seen twice never did.
                return false;
            }
            _walked++;
            IItemEndpoint source = EndpointAt(feeder.SourceCell);
            if (!Gives(source))
            {
                return true; // stuck at the root: it is red, and the golems behind it grey
            }
            bool fed = false;
            foreach (GolemEntity next in Feeders(feeder, source, WantedGood(feeder, feeder.Program.CurrentStep, source)))
            {
                fed = true;
                if (Delivers(next, visited))
                {
                    return true;
                }
            }
            // Nothing feeds it at all: red at the root, so it counts. Fed only round a loop: no.
            return !fed;
        }

        /// <summary>A golem stalled at a Haul or Extract on an empty (or wrong-good) source.</summary>
        private static bool IsEmptyTake(GolemEntity golem)
        {
            switch (golem.StallReason)
            {
                case StallReason.BufferEmpty:
                case StallReason.BeltEmpty:
                    return true;
                case StallReason.MissingItem:
                    return StallFacts.IsTakeAction(golem.Program.CurrentStep?.actionType);
                default:
                    return false;
            }
        }

        /// <summary>The other golems that may put <paramref name="good"/> (any, when null) on <paramref name="source"/>.</summary>
        private IEnumerable<GolemEntity> Feeders(GolemEntity self, IItemEndpoint source, string good) =>
            source is BeltSegmentEndpoint belt
                ? BeltFeeders(self, belt.Segment, good)
                : PushedInto(self, Store(source), good);

        /// <summary>Other golems that push <paramref name="good"/> (any, when null) into <paramref name="store"/>.</summary>
        private IEnumerable<GolemEntity> PushedInto(GolemEntity self, object store, string good) =>
            store == null
                ? Enumerable.Empty<GolemEntity>()
                : Others(self).Where(other => Store(EndpointAt(PushCell(other))) == store && MayPush(other, good)).ToList();

        /// <summary>
        /// The golems pushing onto a segment that a belt's run, walked upstream, reaches. The
        /// pushed-onto segments and the upstream links are gathered once, so the walk is
        /// linear in belts plus golems, not their product.
        /// </summary>
        private List<GolemEntity> BeltFeeders(GolemEntity self, BeltSegment segment, string good)
        {
            var feeders = new List<GolemEntity>();
            if (segment == null || _belts == null)
            {
                return feeders;
            }
            var pushers = new Dictionary<BeltSegment, List<GolemEntity>>();
            foreach (GolemEntity other in Others(self))
            {
                if (EndpointAt(PushCell(other)) is BeltSegmentEndpoint onto && onto.Segment != null && MayPush(other, good))
                {
                    if (!pushers.TryGetValue(onto.Segment, out List<GolemEntity> onSegment))
                    {
                        pushers[onto.Segment] = onSegment = new List<GolemEntity>();
                    }
                    onSegment.Add(other);
                }
            }
            if (pushers.Count == 0)
            {
                return feeders;
            }
            var upstream = new Dictionary<BeltSegment, List<BeltSegment>>();
            foreach (PlacedBelt belt in _belts.Belts)
            {
                if (belt.Segment == null)
                {
                    continue;
                }
                foreach (BeltSegment next in belt.Segment.Outputs)
                {
                    if (!upstream.TryGetValue(next, out List<BeltSegment> before))
                    {
                        upstream[next] = before = new List<BeltSegment>();
                    }
                    before.Add(belt.Segment);
                }
            }
            foreach (BeltSegment reached in Walk(segment, at => upstream.TryGetValue(at, out List<BeltSegment> before)
                ? before : (IEnumerable<BeltSegment>)Array.Empty<BeltSegment>()))
            {
                if (pushers.TryGetValue(reached, out List<GolemEntity> onSegment))
                {
                    feeders.AddRange(onSegment);
                }
            }
            return feeders;
        }

        /// <summary>Every segment a walk from <paramref name="start"/> along <paramref name="links"/> reaches, start included.</summary>
        private static IEnumerable<BeltSegment> Walk(BeltSegment start, Func<BeltSegment, IEnumerable<BeltSegment>> links)
        {
            var visited = new HashSet<BeltSegment>();
            var frontier = new Stack<BeltSegment>();
            frontier.Push(start);
            while (frontier.Count > 0)
            {
                BeltSegment at = frontier.Pop();
                if (!visited.Add(at))
                {
                    continue;
                }
                yield return at;
                foreach (BeltSegment next in links(at))
                {
                    frontier.Push(next);
                }
            }
        }

        /// <summary>Whether a walk from <paramref name="start"/> along <paramref name="links"/> reaches one of <paramref name="goals"/>.</summary>
        private static bool Reaches(BeltSegment start, HashSet<BeltSegment> goals, Func<BeltSegment, IEnumerable<BeltSegment>> links) =>
            Walk(start, links).Any(goals.Contains);

        /// <summary>
        /// Could <paramref name="golem"/> push <paramref name="good"/>? Its recipe's outputs and
        /// byproducts, its typed Hauls' goods, and anything at all for an untyped Haul or an
        /// Extract, whose source isn't known well enough to rule a good out.
        /// </summary>
        private static bool MayPush(GolemEntity golem, string good)
        {
            GolemProgram program = golem.Program;
            if (!program.appendages.Any(IsPushStep))
            {
                return false;
            }
            if (string.IsNullOrEmpty(good))
            {
                return true;
            }
            for (int i = 0; i < program.appendages.Count; i++)
            {
                AppendageActionDefinition card = program.appendages[i];
                if (card == null)
                {
                    continue;
                }
                switch (card.actionType)
                {
                    case AppendageActionType.Assemble:
                        if (card.recipe != null && (card.recipe.outputItemType == good ||
                            (card.recipe.HasByproduct && card.recipe.byproductItemType == good)))
                        {
                            return true;
                        }
                        break;
                    case AppendageActionType.ExtractFromNode:
                        if (!program.HasAssembleStep)
                        {
                            return true;
                        }
                        break;
                    case AppendageActionType.Haul:
                        if (!program.HasAssembleStep)
                        {
                            string typed = program.GetItemTypeAt(i);
                            if (string.IsNullOrEmpty(typed) || typed == good)
                            {
                                return true;
                            }
                        }
                        break;
                }
            }
            return false;
        }

        private static bool IsPushStep(AppendageActionDefinition card) =>
            card != null && (card.actionType == AppendageActionType.Push ||
                             card.actionType == AppendageActionType.LoadIntoBuffer ||
                             card.actionType == AppendageActionType.FreightLaunch);

        /// <summary>Where a golem's goods land: its bound mast for a Zeppelin's launch, else the tile in front.</summary>
        private static Vector2Int PushCell(GolemEntity golem) =>
            golem.Program.appendages.Any(c => c != null && c.actionType == AppendageActionType.FreightLaunch) &&
            golem.BoundMastCell is Vector2Int mast
                ? mast
                : golem.TargetCell;

        // --- Drained -------------------------------------------------------------------------

        private bool IsDrained(GolemEntity golem, IItemEndpoint target)
        {
            if (target is BoilerFuelEndpoint || target is ClockTowerInputEndpoint ||
                target is SlagHeapEndpoint || target is ScrapRecyclerEndpoint)
            {
                return true;
            }
            if (target is BeltSegmentEndpoint belt)
            {
                return BeltDrained(golem, belt.Segment);
            }
            // A stall never takes a push, however busy its customers keep it: chapter 4's
            // Scavenger turned to face the Scrap stall read grey, since the first Scavenger
            // extracts from that stall.
            if (target is ResourceNodeEndpoint)
            {
                return false;
            }
            return TakenFrom(golem, Store(target));
        }

        private bool TakenFrom(GolemEntity self, object store)
        {
            if (store == null)
            {
                return false;
            }
            foreach (GolemEntity other in Others(self))
            {
                if (Store(EndpointAt(other.SourceCell)) == store && MayTake(other, self.PushStock))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Could <paramref name="taker"/> take any of what <paramref name="stuck"/> holds? A typed
        /// Haul takes only its good, so a golem hauling Coal from a crate full of Scrap drains
        /// nothing (Phase 2 critique: the pusher read grey forever); an untyped Haul or an
        /// Extract may take anything. An empty stock asks only for a take step.
        /// </summary>
        private static bool MayTake(GolemEntity taker, GolemInventory.Stock stuck)
        {
            GolemProgram program = taker.Program;
            for (int i = 0; i < program.appendages.Count; i++)
            {
                AppendageActionDefinition card = program.appendages[i];
                if (!IsTakeStep(card))
                {
                    continue;
                }
                string typed = card.actionType == AppendageActionType.Haul ? program.GetItemTypeAt(i) : null;
                if (string.IsNullOrEmpty(typed) || stuck == null || stuck.TypesInOrder.Count == 0 || stuck.Get(typed) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A belt is drained when its run, walked downstream, reaches a segment a golem takes from.</summary>
        private bool BeltDrained(GolemEntity self, BeltSegment segment)
        {
            if (segment == null)
            {
                return false;
            }
            var taken = new HashSet<BeltSegment>();
            foreach (GolemEntity other in Others(self))
            {
                if (EndpointAt(other.SourceCell) is BeltSegmentEndpoint from && from.Segment != null &&
                    MayTake(other, self.PushStock))
                {
                    taken.Add(from.Segment);
                }
            }
            return taken.Count > 0 && Reaches(segment, taken, at => at.Outputs);
        }

        private static bool IsTakeStep(AppendageActionDefinition card) =>
            card != null && StallFacts.IsTakeAction(card.actionType);

        // --- Helpers -------------------------------------------------------------------------

        /// <summary>Every other golem that could feed or drain: live, standing, programmed. Never the golem itself.</summary>
        private IEnumerable<GolemEntity> Others(GolemEntity self) =>
            (_golems?.Invoke() ?? Enumerable.Empty<GolemEntity>())
            .Where(g => g != null && g != self && !g.IsRemoved && !g.IsHeld && g.HasRunnableProgram);

        private IItemEndpoint EndpointAt(Vector2Int cell) =>
            _endpoints != null && _endpoints.TryGetEndpoint(cell, out IItemEndpoint endpoint) ? endpoint : null;

        /// <summary>What a place stores into: a crate's buffer (shared by every Post crate), else the endpoint itself.</summary>
        private static object Store(IItemEndpoint endpoint)
        {
            switch (endpoint)
            {
                case null: return null;
                case StorageBufferEndpoint buffer: return (object)buffer.Buffer ?? buffer;
                case FilteredBufferEndpoint filtered: return (object)filtered.Buffer ?? filtered;
                case ResourceNodeEndpoint node: return (object)node.Node ?? node;
                default: return endpoint;
            }
        }
    }
}
