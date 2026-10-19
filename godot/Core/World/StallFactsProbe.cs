using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
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

        public StallFacts Read(GolemEntity golem)
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

        private bool IsFed(GolemEntity golem, IItemEndpoint source, string good)
        {
            if (source is BeltSegmentEndpoint belt)
            {
                return BeltFed(golem, belt.Segment, good);
            }
            return PushedInto(golem, Store(source), good);
        }

        /// <summary>Some other golem pushes <paramref name="good"/> (any, when null) into <paramref name="store"/>.</summary>
        private bool PushedInto(GolemEntity self, object store, string good)
        {
            if (store == null)
            {
                return false;
            }
            foreach (GolemEntity other in Others(self))
            {
                if (Store(EndpointAt(PushCell(other))) == store && MayPush(other, good))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A belt is fed when its run, walked upstream, reaches a segment a golem pushes onto.
        /// The pushed-onto segments and the upstream links are gathered once, so the walk is
        /// linear in belts plus golems, not their product.
        /// </summary>
        private bool BeltFed(GolemEntity self, BeltSegment segment, string good)
        {
            if (segment == null || _belts == null)
            {
                return false;
            }
            var pushed = new HashSet<BeltSegment>();
            foreach (GolemEntity other in Others(self))
            {
                if (EndpointAt(PushCell(other)) is BeltSegmentEndpoint onto && onto.Segment != null && MayPush(other, good))
                {
                    pushed.Add(onto.Segment);
                }
            }
            if (pushed.Count == 0)
            {
                return false;
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
                    if (!upstream.TryGetValue(next, out List<BeltSegment> feeders))
                    {
                        upstream[next] = feeders = new List<BeltSegment>();
                    }
                    feeders.Add(belt.Segment);
                }
            }
            return Reaches(segment, pushed, at => upstream.TryGetValue(at, out List<BeltSegment> feeders)
                ? feeders : (IEnumerable<BeltSegment>)Array.Empty<BeltSegment>());
        }

        /// <summary>Whether a walk from <paramref name="start"/> along <paramref name="links"/> reaches one of <paramref name="goals"/>.</summary>
        private static bool Reaches(BeltSegment start, HashSet<BeltSegment> goals, Func<BeltSegment, IEnumerable<BeltSegment>> links)
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
                if (goals.Contains(at))
                {
                    return true;
                }
                foreach (BeltSegment next in links(at))
                {
                    frontier.Push(next);
                }
            }
            return false;
        }

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
                if (Store(EndpointAt(other.SourceCell)) == store && other.Program.appendages.Any(IsTakeStep))
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
                    other.Program.appendages.Any(IsTakeStep))
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
