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
    /// the tile in front, does the cart behind refill itself. Presentation-side and read-only; it runs
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
        private readonly Func<string, bool> _refills;

        /// <param name="refills">Whether a node id is a cart that refills itself; null, none does.</param>
        public StallFactsProbe(Func<IEnumerable<GolemEntity>> golems, SpatialEndpointRegistry endpoints,
            BeltNetwork belts, Func<string, bool> refills = null)
        {
            _golems = golems;
            _endpoints = endpoints;
            _belts = belts;
            _refills = refills;
        }

        /// <summary>
        /// The endgame profile's sampler (docs/performance-report-template.md §3): <c>probe.read</c>
        /// times each read, <c>loop.walk</c> counts the golems the feeder walk visits. Null, the
        /// default, measures nothing.
        /// </summary>
        public PerfSampler Perf { get; set; }

        private int _walked;

        /// <summary>
        /// The presentation frame, when the probe has one (<c>SandboxWorld</c> wires its own,
        /// moved on by every frame and by every building placed or removed). Within a frame the
        /// probe keeps its indices of who pushes into and takes from each store, and rebuilds
        /// them only when a golem moves, turns, is carried, set down, removed, or gains or loses
        /// a runnable program (<see cref="IndexStale"/>). Programs, holds and stalls are read
        /// live, so a read answers exactly as it did before the indices. Null, as on a bare
        /// probe in a test: the indices are rebuilt on every read.
        /// </summary>
        public Func<long> Frame { get; set; }

        private bool _indexed;
        private long _indexFrame;
        private long _indexEpoch = -1;

        // What each golem looked like when the indices were built, in roster order.
        private readonly List<IndexedGolem> _indexedAs = new List<IndexedGolem>();

        /// <summary>
        /// What decides a golem's place in the indices: where it stands and faces (its source and
        /// target), its mast and whether it launches (its push cell), and whether it runs.
        /// </summary>
        private readonly struct IndexedGolem
        {
            public readonly GolemEntity Golem;
            public readonly Vector2Int Cell;
            public readonly Facing Facing;
            public readonly Vector2Int? Mast;
            public readonly bool Launches;
            public readonly bool Live;

            public IndexedGolem(GolemEntity golem)
            {
                Golem = golem;
                Cell = golem?.Cell ?? default;
                Facing = golem?.Facing ?? default;
                Mast = golem?.BoundMastCell;
                Launches = golem != null && golem.Program != null && HasLaunch(golem.Program);
                Live = StallFactsProbe.Live(golem);
            }

            public bool Describes(GolemEntity golem) =>
                Golem == golem && (golem == null ||
                    (Live == StallFactsProbe.Live(golem) && Cell == golem.Cell && Facing == golem.Facing &&
                     Mast == golem.BoundMastCell && Launches == HasLaunch(golem.Program)));
        }

        // Who pushes into, and takes from, each store and each belt lane: every golem that could
        // feed or drain (live, standing, programmed), in roster order; the asker is excluded at
        // the query.
        private readonly Dictionary<object, List<GolemEntity>> _pushersByStore = new Dictionary<object, List<GolemEntity>>();
        private readonly Dictionary<BeltSegment, List<GolemEntity>> _pushersBySegment = new Dictionary<BeltSegment, List<GolemEntity>>();
        private readonly Dictionary<object, List<GolemEntity>> _takersByStore = new Dictionary<object, List<GolemEntity>>();
        private readonly Dictionary<BeltSegment, List<GolemEntity>> _takersBySegment = new Dictionary<BeltSegment, List<GolemEntity>>();
        private readonly Dictionary<BeltSegment, List<BeltSegment>> _upstream = new Dictionary<BeltSegment, List<BeltSegment>>();
        private bool _upstreamBuilt;

        // The pushers a belt lane's run reaches, walked upstream once per lane per index.
        private readonly Dictionary<BeltSegment, List<GolemEntity>> _feedersBySegment = new Dictionary<BeltSegment, List<GolemEntity>>();

        // The indices are rebuilt every presentation frame, and a starved factory reads them
        // for every stalled golem. Their lists and the walks' sets are therefore kept and
        // refilled, never made anew: a rebuild and a read allocate nothing once they have run.
        private readonly Stack<List<GolemEntity>> _golemLists = new Stack<List<GolemEntity>>();
        private readonly Stack<List<BeltSegment>> _segmentLists = new Stack<List<BeltSegment>>();
        private readonly HashSet<GolemEntity> _visitedScratch = new HashSet<GolemEntity>();
        private readonly HashSet<BeltSegment> _walkVisited = new HashSet<BeltSegment>();
        private readonly Stack<BeltSegment> _walkFrontier = new Stack<BeltSegment>();
        private readonly List<GolemEntity> _rosterScratch = new List<GolemEntity>();

        public StallFacts Read(GolemEntity golem)
        {
            PerfSampler perf = Perf;
            if (perf == null)
            {
                EnsureIndex();
                return ReadNow(golem);
            }
            _walked = 0;
            using (perf.Start(PerfSampler.ProbeRead))
            {
                EnsureIndex();
                StallFacts facts = ReadNow(golem);
                perf.Tally(PerfSampler.LoopWalk, _walked);
                return facts;
            }
        }

        /// <summary>Whether the indices no longer describe the golems: one moved, turned, was carried or removed, or changed whether it runs.</summary>
        private bool IndexStale(IReadOnlyList<GolemEntity> golems)
        {
            if (golems.Count != _indexedAs.Count)
            {
                return true;
            }
            for (int i = 0; i < golems.Count; i++)
            {
                if (!_indexedAs[i].Describes(golems[i]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The roster as a list, walked by index: a <c>foreach</c> over the interface boxes an
        /// enumerator on every read. A source that is not a list is copied into a kept one.
        /// </summary>
        private IReadOnlyList<GolemEntity> Roster()
        {
            IEnumerable<GolemEntity> golems = _golems?.Invoke();
            if (golems is IReadOnlyList<GolemEntity> list)
            {
                return list;
            }
            _rosterScratch.Clear();
            if (golems != null)
            {
                _rosterScratch.AddRange(golems);
            }
            return _rosterScratch;
        }

        private static bool Live(GolemEntity golem) =>
            golem != null && !golem.IsRemoved && !golem.IsHeld && golem.HasRunnableProgram;

        /// <summary>Builds the indices unless they still describe this frame's golems.</summary>
        private void EnsureIndex()
        {
            IReadOnlyList<GolemEntity> golems = Roster();
            long? frame = Frame?.Invoke();
            // Within a frame the index stands until a golem's layout changes (GolemLayout): the
            // per-read walk over every golem made a starved frame cost the square of the golem
            // count (endless-design §4). Each new frame still rebuilds it whole.
            if (frame.HasValue && _indexed && _indexFrame == frame.Value
                && (_indexEpoch == GolemLayout.Epoch || !IndexStale(golems)))
            {
                _indexEpoch = GolemLayout.Epoch;
                return;
            }
            _indexEpoch = GolemLayout.Epoch;
            _indexed = frame.HasValue;
            _indexFrame = frame ?? 0;
            _indexedAs.Clear();
            Recycle(_pushersByStore, _golemLists);
            Recycle(_pushersBySegment, _golemLists);
            Recycle(_takersByStore, _golemLists);
            Recycle(_takersBySegment, _golemLists);
            Recycle(_feedersBySegment, _golemLists);
            Recycle(_upstream, _segmentLists);
            _upstreamBuilt = false;
            for (int i = 0; i < golems.Count; i++)
            {
                GolemEntity golem = golems[i];
                _indexedAs.Add(new IndexedGolem(golem));
                if (!Live(golem))
                {
                    continue;
                }
                IItemEndpoint into = EndpointAt(PushCell(golem));
                if (into is BeltSegmentEndpoint onto && onto.Segment != null)
                {
                    Add(_pushersBySegment, onto.Segment, golem);
                }
                object store = Store(into);
                if (store != null)
                {
                    Add(_pushersByStore, store, golem);
                }
                IItemEndpoint from = EndpointAt(golem.SourceCell);
                if (from is BeltSegmentEndpoint off && off.Segment != null)
                {
                    Add(_takersBySegment, off.Segment, golem);
                }
                object source = Store(from);
                if (source != null)
                {
                    Add(_takersByStore, source, golem);
                }
            }
        }

        /// <summary>Empties an index, keeping its lists for the next build.</summary>
        private static void Recycle<TKey, TItem>(Dictionary<TKey, List<TItem>> index, Stack<List<TItem>> kept)
        {
            foreach (KeyValuePair<TKey, List<TItem>> entry in index)
            {
                entry.Value.Clear();
                kept.Push(entry.Value);
            }
            index.Clear();
        }

        private void Add<TKey>(Dictionary<TKey, List<GolemEntity>> index, TKey key, GolemEntity golem)
        {
            if (!index.TryGetValue(key, out List<GolemEntity> list))
            {
                index[key] = list = _golemLists.Count > 0 ? _golemLists.Pop() : new List<GolemEntity>();
            }
            list.Add(golem);
        }

        private static readonly List<GolemEntity> None = new List<GolemEntity>();

        private static List<GolemEntity> At<TKey>(Dictionary<TKey, List<GolemEntity>> index, TKey key) =>
            key != null && index.TryGetValue(key, out List<GolemEntity> list) ? list : None;

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
            bool refills = source is ResourceNodeEndpoint node && node.Node != null &&
                           (_refills?.Invoke(node.Node.NodeId) ?? false);
            return new StallFacts(action, sourceGives,
                sourceGives && IsFed(golem, source, wanted),
                IsDrained(golem, target),
                refills,
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
            // One kept set: a read is never inside another read's walk.
            HashSet<GolemEntity> visited = _visitedScratch;
            visited.Clear();
            visited.Add(golem);
            List<GolemEntity> candidates = FeederCandidates(source);
            for (int i = 0; i < candidates.Count; i++)
            {
                GolemEntity feeder = candidates[i];
                if (feeder != golem && MayPush(feeder, good) && Delivers(feeder, visited))
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
            EnsureIndex();
            IItemEndpoint source = EndpointAt(golem.SourceCell);
            if (!Gives(source))
            {
                return named;
            }
            string wanted = WantedGood(golem, golem.Program.CurrentStep, source);
            foreach (GolemEntity feeder in FeederCandidates(source).Where(g => g != golem && MayPush(g, wanted))
                         .OrderBy(NumberOrder).ThenBy(g => g.GolemId, StringComparer.Ordinal))
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
            string wanted = WantedGood(feeder, feeder.Program.CurrentStep, source);
            List<GolemEntity> candidates = FeederCandidates(source);
            for (int i = 0; i < candidates.Count; i++)
            {
                GolemEntity next = candidates[i];
                if (next == feeder || !MayPush(next, wanted))
                {
                    continue;
                }
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

        /// <summary>
        /// Every golem that pushes onto <paramref name="source"/>: into its store, or onto a belt
        /// lane its run reaches upstream. The asker and the good are the caller's to rule out
        /// (<c>other != self &amp;&amp; MayPush(other, good)</c>), so one list serves every asker.
        /// </summary>
        private List<GolemEntity> FeederCandidates(IItemEndpoint source) =>
            source is BeltSegmentEndpoint belt
                ? BeltFeeders(belt.Segment)
                : At(_pushersByStore, Store(source));

        /// <summary>
        /// The golems pushing onto a segment that a belt's run, walked upstream, reaches. The
        /// pushed-onto segments and the upstream links are gathered once, and each lane's
        /// answer is kept until the index is rebuilt, so the walk is linear in belts plus
        /// golems, not their product.
        /// </summary>
        private List<GolemEntity> BeltFeeders(BeltSegment segment)
        {
            if (segment == null || _belts == null || _pushersBySegment.Count == 0)
            {
                return None;
            }
            if (_feedersBySegment.TryGetValue(segment, out List<GolemEntity> feeders))
            {
                return feeders;
            }
            _feedersBySegment[segment] = feeders = _golemLists.Count > 0 ? _golemLists.Pop() : new List<GolemEntity>();
            Dictionary<BeltSegment, List<BeltSegment>> upstream = Upstream();

            // Depth first, every lane once, start included: the order the walk has always had.
            _walkVisited.Clear();
            _walkFrontier.Clear();
            _walkFrontier.Push(segment);
            while (_walkFrontier.Count > 0)
            {
                BeltSegment reached = _walkFrontier.Pop();
                if (!_walkVisited.Add(reached))
                {
                    continue;
                }
                List<GolemEntity> pushers = At(_pushersBySegment, reached);
                for (int i = 0; i < pushers.Count; i++)
                {
                    feeders.Add(pushers[i]);
                }
                if (upstream.TryGetValue(reached, out List<BeltSegment> before))
                {
                    for (int i = 0; i < before.Count; i++)
                    {
                        _walkFrontier.Push(before[i]);
                    }
                }
            }
            return feeders;
        }

        /// <summary>Each belt lane's feeding lanes, gathered once per index.</summary>
        private Dictionary<BeltSegment, List<BeltSegment>> Upstream()
        {
            if (_upstreamBuilt)
            {
                return _upstream;
            }
            _upstreamBuilt = true;
            foreach (PlacedBelt belt in _belts.Belts)
            {
                if (belt.Segment == null)
                {
                    continue;
                }
                IReadOnlyList<BeltSegment> outputs = belt.Segment.Outputs;
                for (int i = 0; i < outputs.Count; i++)
                {
                    BeltSegment next = outputs[i];
                    if (!_upstream.TryGetValue(next, out List<BeltSegment> before))
                    {
                        _upstream[next] = before = _segmentLists.Count > 0 ? _segmentLists.Pop() : new List<BeltSegment>();
                    }
                    before.Add(belt.Segment);
                }
            }
            return _upstream;
        }

        /// <summary>
        /// Could <paramref name="golem"/> push <paramref name="good"/>? Its recipe's outputs and
        /// byproducts, its typed Hauls' goods, and anything at all for an untyped Haul or an
        /// Extract, whose source isn't known well enough to rule a good out.
        /// </summary>
        private static bool MayPush(GolemEntity golem, string good)
        {
            GolemProgram program = golem.Program;
            bool pushes = false;
            for (int i = 0; i < program.appendages.Count && !pushes; i++)
            {
                pushes = IsPushStep(program.appendages[i]);
            }
            if (!pushes)
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
            HasLaunch(golem.Program) && golem.BoundMastCell is Vector2Int mast
                ? mast
                : golem.TargetCell;

        private static bool HasLaunch(GolemProgram program)
        {
            List<AppendageActionDefinition> cards = program?.appendages;
            if (cards == null)
            {
                return false;
            }
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && cards[i].actionType == AppendageActionType.FreightLaunch)
                {
                    return true;
                }
            }
            return false;
        }

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
            List<GolemEntity> takers = At(_takersByStore, store);
            for (int i = 0; i < takers.Count; i++)
            {
                if (takers[i] != self && MayTake(takers[i], self.PushStock))
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
            if (segment == null || _takersBySegment.Count == 0)
            {
                return false;
            }
            _walkVisited.Clear();
            _walkFrontier.Clear();
            _walkFrontier.Push(segment);
            while (_walkFrontier.Count > 0)
            {
                BeltSegment reached = _walkFrontier.Pop();
                if (!_walkVisited.Add(reached))
                {
                    continue;
                }
                List<GolemEntity> takers = At(_takersBySegment, reached);
                for (int i = 0; i < takers.Count; i++)
                {
                    if (takers[i] != self && MayTake(takers[i], self.PushStock))
                    {
                        return true;
                    }
                }
                IReadOnlyList<BeltSegment> outputs = reached.Outputs;
                for (int i = 0; i < outputs.Count; i++)
                {
                    _walkFrontier.Push(outputs[i]);
                }
            }
            return false;
        }

        private static bool IsTakeStep(AppendageActionDefinition card) =>
            card != null && StallFacts.IsTakeAction(card.actionType);

        // --- Helpers -------------------------------------------------------------------------

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
