using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.Steam
{
    // Steam power (docs/progression-design.md §3.1, §11 item 4).
    //
    // A plain-C# manager, owned by a thin SteamNetworkHolder per the Holder pattern -- the same
    // arrangement BeltNetwork/BeltNetworkHolder and GridMap/GridMapHolder use. It owns three
    // things and nothing else:
    //
    //   * WHERE the boilers and pipes are, and therefore which cells have steam (the undirected
    //     flood fill, which lives as pure statics in SteamPipeRules);
    //   * WHICH golems are powered, decided by a total order that does not change from tick to
    //     tick (see the three determinism notes below);
    //   * HOW MUCH Coke that costs, in whole units, on whole ticks (SteamBoiler.Accrue).
    //
    // It does not know what a GolemEntity is: a consumer is a bare string id plus a cell, the
    // same bare-string-id convention belts, buffers and nodes already use. GolemEntity opts in
    // by calling ConfigureSteam; a golem that never does is exempt and always runs.
    //
    // ---------------------------------------------------------------------------------------
    // THE THREE DETERMINISM DECISIONS. §3.1 is silent on all three and each one, got wrong,
    // makes the system unplayable rather than merely imprecise.
    //
    // (1) WHICH 8, when more than 8 golems can reach one boiler.
    //     Decided by SteamPipeRules.CompareCells on the golem's CELL, tie-broken by golem id.
    //     A golem's cell is factory layout -- it is identical between two identically-built
    //     factories and it does not move unless the player moves it, so the same 8 stay powered
    //     tick after tick. REJECTED: Dictionary/HashSet iteration order (not contractual, and
    //     rehashing on growth silently reshuffles it, so adding a ninth golem anywhere could
    //     flip which of the first eight are powered); registration/scene order (identical
    //     factories built in a different order would behave differently, and a save/load that
    //     re-registers golems would repower a different eight); nearest-first by distance
    //     (ties are the common case on a grid and still need this tiebreak underneath).
    //     An unstable choice here is not a rounding error -- golems would flicker between
    //     powered and NoSteam every tick, which is unplayable and untestable.
    //
    // (2) TWO BOILERS ON ONE PIPE NETWORK, whose shared reach exceeds what one can serve.
    //     Boilers are walked in cell order (again SteamPipeRules.CompareCells, tie-broken by
    //     boiler id) and each claims up to 8 not-yet-claimed golems from its own reachable set,
    //     in the golem order from (1). First-come by a stable order, so adding a second boiler
    //     never re-shuffles the golems the first one was already powering -- it only picks up
    //     the overflow, which is exactly what a player who just built it expects to see.
    //     REJECTED: load balancing across boilers (moves golems between boilers whenever the
    //     count changes, so a boiler's burn rate -- and its countdown -- would jump every time
    //     anything anywhere was built); and nearest-boiler assignment (needs a tiebreak anyway,
    //     and can leave a boiler idle while an adjacent one is over-subscribed).
    //
    // (3) FRACTIONAL BURN. Integer accumulator, no floats. See SteamBoiler.Accrue.
    // ---------------------------------------------------------------------------------------
    public sealed class SteamNetwork
    {
        /// <summary>
        /// §3.1: 1 Coke per powered golem per 10 s, and SimulationClock.TicksPerSecond is 10 --
        /// so 100 ticks. Expressed in ticks rather than seconds because the burn must not depend
        /// on the clock's speed multiplier or on frame timing: a factory run at 4x speed burns
        /// the same Coke per unit of WORK DONE, which is the only reading that keeps a ratio a
        /// ratio.
        /// </summary>
        public const int TicksPerCokePerPoweredGolem = 100;

        /// <summary>§3.1: a Boiler powers at most 8 golems.</summary>
        public const int MaxGolemsPerBoiler = 8;

        /// <summary>
        /// Costs from §3.1, recorded here rather than wired into placement, because Iron Plate
        /// does not exist as an ItemType until §1.5 authors it (and §11 item 8 replaces the
        /// scrap/brass cost pair with an item bundle at the same time). Invented as a runtime
        /// cost now, they would have to be re-expressed twice.
        /// </summary>
        public const int BoilerScrapCost = 30;
        public const int BoilerIronPlateCost = 10;
        public const int SteamPipeIronPlateCost = 1;

        private readonly Dictionary<string, SteamBoiler> _boilers = new Dictionary<string, SteamBoiler>();
        private readonly HashSet<Vector2Int> _pipes = new HashSet<Vector2Int>();
        private readonly Dictionary<string, Vector2Int> _consumerCells = new Dictionary<string, Vector2Int>();

        // Sorted views of the two dictionaries above, rebuilt only when membership or a cell
        // changes. Dictionary iteration order is explicitly not contractual, so nothing that
        // decides who gets powered is ever allowed to read _boilers/_consumerCells directly.
        private readonly List<SteamBoiler> _boilerOrder = new List<SteamBoiler>();
        private readonly List<string> _consumerOrder = new List<string>();
        private bool _orderDirty = true;

        // boilerId -> the cells it can power. Depends only on TOPOLOGY (boiler and pipe
        // positions), never on fuel or on golems, so it survives every tick in which nothing
        // was built and the per-tick cost is a set lookup per golem.
        private readonly Dictionary<string, HashSet<Vector2Int>> _poweredCellsByBoiler =
            new Dictionary<string, HashSet<Vector2Int>>();
        private bool _topologyDirty = true;

        // golemId -> the boiler currently powering it. Rebuilt per tick.
        private readonly Dictionary<string, string> _poweredBy = new Dictionary<string, string>();

        // Scratch, reused across the flood fill so a factory-sized pipe run does not allocate
        // two sets per boiler per rebuild.
        private readonly HashSet<Vector2Int> _connectedScratch = new HashSet<Vector2Int>();

        private long _evaluatedTick = long.MinValue;
        private long _burnedTick = long.MinValue;
        private bool _evaluationDirty = true;

        public int BoilerCount => _boilers.Count;
        public int PipeCount => _pipes.Count;
        public int ConsumerCount => _consumerCells.Count;

        /// <summary>Boilers in the stable cell order every assignment decision uses.</summary>
        public IReadOnlyList<SteamBoiler> BoilersInOrder
        {
            get
            {
                RebuildOrders();
                return _boilerOrder;
            }
        }

        // --- Topology -----------------------------------------------------------------------

        public SteamBoiler RegisterBoiler(string boilerId, Vector2Int cell, int startingCoke)
        {
            if (string.IsNullOrEmpty(boilerId))
            {
                return null;
            }

            SteamBoiler existing;
            if (_boilers.TryGetValue(boilerId, out existing))
            {
                return existing;
            }

            var boiler = new SteamBoiler(boilerId, cell, startingCoke);
            _boilers[boilerId] = boiler;
            MarkTopologyDirty();
            return boiler;
        }

        public bool TryGetBoiler(string boilerId, out SteamBoiler boiler)
        {
            // Guards a null id rather than letting the raw Dictionary lookup throw -- the same
            // convention ResourceNodeRegistry/StorageBufferRegistry/ConveyorSystem follow, so an
            // unset id is a false, not an exception inside Tick.
            if (string.IsNullOrEmpty(boilerId))
            {
                boiler = null;
                return false;
            }

            return _boilers.TryGetValue(boilerId, out boiler);
        }

        public bool RemoveBoiler(string boilerId)
        {
            if (string.IsNullOrEmpty(boilerId) || !_boilers.Remove(boilerId))
            {
                return false;
            }

            _poweredCellsByBoiler.Remove(boilerId);
            MarkTopologyDirty();
            return true;
        }

        public bool HasPipe(Vector2Int cell) => _pipes.Contains(cell);

        /// <summary>
        /// Whether a boiler stands on <paramref name="cell"/>. Linear over the boilers, which is
        /// affordable because a factory has a handful of them and this is asked on placement, not
        /// per tick -- the same trade BuildModeController.TryFindGolemAt documents.
        ///
        /// <para>
        /// Exists for the PICTURE, not for the flood fill: <see cref="PipeShapeRules"/> has to
        /// know that a pipe laid against a boiler joins on to it, or the run visibly stops one
        /// cell short of the thing feeding it. The reachability walk never needs this -- it seeds
        /// from the boiler's own neighbours rather than asking any cell what is on it.
        /// </para>
        /// </summary>
        public bool HasBoilerAt(Vector2Int cell)
        {
            foreach (SteamBoiler boiler in _boilers.Values)
            {
                if (boiler.Cell == cell)
                {
                    return true;
                }
            }

            return false;
        }

        public bool AddPipe(Vector2Int cell)
        {
            if (!_pipes.Add(cell))
            {
                return false;
            }

            MarkTopologyDirty();
            return true;
        }

        /// <summary>
        /// Pulls one pipe up. Everything downstream of the gap loses steam on the very next
        /// evaluation, because the reachable set is re-derived wholesale rather than patched --
        /// the "always re-render from data" idiom BeltNetwork.Relink and WorkbenchController
        /// .RebuildUI already use, and here it is load-bearing rather than merely tidy: a cached
        /// stale reachable set is precisely progression-design §9's Phase 6 beat (a stage bar
        /// freezing because one extractor lost steam to a paved-over pipe) failing to happen.
        /// </summary>
        public bool RemovePipe(Vector2Int cell)
        {
            if (!_pipes.Remove(cell))
            {
                return false;
            }

            MarkTopologyDirty();
            return true;
        }

        // --- Consumers ----------------------------------------------------------------------

        /// <summary>
        /// Registers (or repositions) a steam consumer. Idempotent, so a golem may call it on
        /// every placement change without the network accumulating ghosts.
        /// </summary>
        public void RegisterConsumer(string consumerId, Vector2Int cell)
        {
            if (string.IsNullOrEmpty(consumerId))
            {
                return;
            }

            Vector2Int existing;
            if (_consumerCells.TryGetValue(consumerId, out existing) && existing == cell)
            {
                return;
            }

            _consumerCells[consumerId] = cell;
            MarkOrderDirty();
        }

        public bool UnregisterConsumer(string consumerId)
        {
            if (string.IsNullOrEmpty(consumerId) || !_consumerCells.Remove(consumerId))
            {
                return false;
            }

            MarkOrderDirty();
            return true;
        }

        public bool HasConsumer(string consumerId) =>
            !string.IsNullOrEmpty(consumerId) && _consumerCells.ContainsKey(consumerId);

        // --- Evaluation and burn -------------------------------------------------------------

        /// <summary>
        /// Whether <paramref name="consumerId"/> has steam as of <paramref name="tick"/>.
        ///
        /// Evaluation is LAZY and tick-stamped rather than done only in <see cref="Tick"/>, so
        /// the answer cannot depend on whether the holder happens to be registered with the
        /// SimulationClock before or after the golems asking. The clock ticks registrants in
        /// registration order, and a golem constructed at runtime registers after everything
        /// seeded at bootstrap -- keying power off "did the network tick yet this frame" would
        /// have made a golem's first tick after construction silently unpowered.
        /// </summary>
        public bool IsPowered(string consumerId, long tick)
        {
            if (string.IsNullOrEmpty(consumerId))
            {
                return false;
            }

            Evaluate(tick);
            return _poweredBy.ContainsKey(consumerId);
        }

        /// <summary>
        /// Why <paramref name="consumerId"/> has no steam, for the stall badge and the alerts
        /// strip: no boiler's pipes reach its tile, the boilers that do are out of Coke, or a
        /// fuelled one does but is already at <see cref="MaxGolemsPerBoiler"/>.
        /// </summary>
        public SteamShortage Diagnose(string consumerId, long tick)
        {
            if (string.IsNullOrEmpty(consumerId) || !_consumerCells.TryGetValue(consumerId, out Vector2Int cell))
            {
                return SteamShortage.None;
            }

            Evaluate(tick);
            if (_poweredBy.ContainsKey(consumerId))
            {
                return SteamShortage.None;
            }

            bool reached = false;
            for (int b = 0; b < _boilerOrder.Count; b++)
            {
                SteamBoiler boiler = _boilerOrder[b];
                if (_poweredCellsByBoiler.TryGetValue(boiler.BoilerId, out HashSet<Vector2Int> reach) && reach.Contains(cell))
                {
                    reached = true;
                    if (boiler.CokeStock > 0)
                    {
                        return SteamShortage.BoilerAtCapacity;
                    }
                }
            }
            return reached ? SteamShortage.BoilerOutOfCoke : SteamShortage.NoPipe;
        }

        /// <summary>
        /// Whether any boiler's pipes reach <paramref name="cell"/> -- a golem standing there
        /// would be on the grid -- regardless of Coke or the 8-golem cap. For the guide (G10).
        /// </summary>
        public bool Reaches(Vector2Int cell, long tick)
        {
            Evaluate(tick);
            foreach (HashSet<Vector2Int> reach in _poweredCellsByBoiler.Values)
            {
                if (reach.Contains(cell))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryGetPoweringBoiler(string consumerId, long tick, out string boilerId)
        {
            boilerId = null;
            if (string.IsNullOrEmpty(consumerId))
            {
                return false;
            }

            Evaluate(tick);
            return _poweredBy.TryGetValue(consumerId, out boilerId);
        }

        /// <summary>Total golems drawing steam right now, across every boiler.</summary>
        public int PoweredGolemCount(long tick)
        {
            Evaluate(tick);
            return _poweredBy.Count;
        }

        /// <summary>
        /// The powered count as of the last settled evaluation, WITHOUT triggering one.
        ///
        /// This exists for the fuel gauge, and the distinction matters: the HUD has no tick of
        /// its own, so if it called the overload above it would have to invent a tick number --
        /// and evaluating at an invented tick would stamp _evaluatedTick with it, so a HUD
        /// repainting at 60 fps would decide when the simulation re-derives who has power. A
        /// read-only view can never be allowed to perturb the sim it is reporting on.
        /// </summary>
        public int LastEvaluatedPoweredCount => _poweredBy.Count;

        /// <summary>Coke on hand across every boiler -- what the fuel gauge reads.</summary>
        public int TotalCokeStock
        {
            get
            {
                int total = 0;
                foreach (SteamBoiler boiler in _boilers.Values)
                {
                    total += boiler.CokeStock;
                }

                return total;
            }
        }

        /// <summary>Reference figure for the gauge's 25 % alert; see SteamBoiler.PeakCokeStock.</summary>
        public int TotalPeakCokeStock
        {
            get
            {
                int total = 0;
                foreach (SteamBoiler boiler in _boilers.Values)
                {
                    total += boiler.PeakCokeStock;
                }

                return total;
            }
        }

        /// <summary>
        /// One simulation tick: settle who is powered, then charge each boiler for the golems it
        /// is actually powering. Both halves are idempotent per tick -- Evaluate is tick-stamped
        /// and the burn is guarded by <see cref="_burnedTick"/> -- so registering this with the
        /// clock twice cannot double-charge the player.
        /// </summary>
        public void Tick(long tick)
        {
            Evaluate(tick);

            if (_burnedTick == tick)
            {
                return;
            }

            _burnedTick = tick;

            RebuildOrders();
            for (int i = 0; i < _boilerOrder.Count; i++)
            {
                _boilerOrder[i].Accrue(_boilerOrder[i].PoweredGolemCount);
            }

            // Deliberately NOT marking the evaluation dirty here. A boiler that burns its last
            // Coke on this tick must still count as having powered its golems FOR this tick --
            // it was charged for them. Invalidating here would let a golem that ticks after the
            // network read "unpowered" on a tick it was already billed for, so the golem would
            // stall on a tick the player paid for. The tick stamp handles it: the next tick has
            // a different number, re-derives, and finds a boiler at zero powering nothing.
        }

        public void Clear()
        {
            _boilers.Clear();
            _pipes.Clear();
            _consumerCells.Clear();
            _poweredCellsByBoiler.Clear();
            _poweredBy.Clear();
            _boilerOrder.Clear();
            _consumerOrder.Clear();
            _orderDirty = true;
            _topologyDirty = true;
            _evaluationDirty = true;
            _evaluatedTick = long.MinValue;
            _burnedTick = long.MinValue;
        }

        private void MarkTopologyDirty()
        {
            _topologyDirty = true;
            _orderDirty = true;
            _evaluationDirty = true;
        }

        private void MarkOrderDirty()
        {
            _orderDirty = true;
            _evaluationDirty = true;
        }

        /// <summary>
        /// Settles the powered set for <paramref name="tick"/>. Cheap to call repeatedly: it
        /// short-circuits unless the tick moved on or something was built/moved since.
        /// </summary>
        private void Evaluate(long tick)
        {
            if (!_evaluationDirty && _evaluatedTick == tick)
            {
                return;
            }

            _evaluatedTick = tick;
            _evaluationDirty = false;

            RebuildOrders();
            RebuildTopology();

            _poweredBy.Clear();

            for (int b = 0; b < _boilerOrder.Count; b++)
            {
                SteamBoiler boiler = _boilerOrder[b];
                boiler.PoweredGolemCount = 0;

                // A BOILER THAT CANNOT PAY POWERS NOTHING. §3.1's golems do not run on credit:
                // when the Coke runs out the golems it was powering lose steam and stall NoSteam
                // the same tick. Checked here rather than after the fact so nothing is ever
                // granted power the boiler then fails to fund.
                if (boiler.CokeStock <= 0)
                {
                    continue;
                }

                HashSet<Vector2Int> reach;
                if (!_poweredCellsByBoiler.TryGetValue(boiler.BoilerId, out reach))
                {
                    continue;
                }

                int claimed = 0;
                for (int c = 0; c < _consumerOrder.Count && claimed < MaxGolemsPerBoiler; c++)
                {
                    string consumerId = _consumerOrder[c];
                    if (_poweredBy.ContainsKey(consumerId))
                    {
                        continue;
                    }

                    if (!reach.Contains(_consumerCells[consumerId]))
                    {
                        continue;
                    }

                    _poweredBy[consumerId] = boiler.BoilerId;
                    claimed++;
                }

                boiler.PoweredGolemCount = claimed;
            }
        }

        private void RebuildOrders()
        {
            if (!_orderDirty)
            {
                return;
            }

            _orderDirty = false;

            _boilerOrder.Clear();
            foreach (SteamBoiler boiler in _boilers.Values)
            {
                _boilerOrder.Add(boiler);
            }

            _boilerOrder.Sort(CompareBoilers);

            _consumerOrder.Clear();
            foreach (string consumerId in _consumerCells.Keys)
            {
                _consumerOrder.Add(consumerId);
            }

            _consumerOrder.Sort(CompareConsumers);
        }

        // Cell first, id second. The id tiebreak is not reachable in the shipped game (GridMap
        // allows one occupant per cell) but it is what makes this a TOTAL order rather than a
        // partial one, and a comparison that returns 0 for two distinct entries hands the
        // decision straight back to List.Sort's unstable introsort -- the exact
        // non-determinism this ordering exists to remove.
        private static int CompareBoilers(SteamBoiler a, SteamBoiler b)
        {
            int byCell = SteamPipeRules.CompareCells(a.Cell, b.Cell);
            return byCell != 0 ? byCell : string.CompareOrdinal(a.BoilerId, b.BoilerId);
        }

        private int CompareConsumers(string a, string b)
        {
            int byCell = SteamPipeRules.CompareCells(_consumerCells[a], _consumerCells[b]);
            return byCell != 0 ? byCell : string.CompareOrdinal(a, b);
        }

        private void RebuildTopology()
        {
            if (!_topologyDirty)
            {
                return;
            }

            _topologyDirty = false;
            _poweredCellsByBoiler.Clear();

            for (int i = 0; i < _boilerOrder.Count; i++)
            {
                SteamBoiler boiler = _boilerOrder[i];
                var powered = new HashSet<Vector2Int>();
                SteamPipeRules.CollectConnectedPipes(boiler.Cell, _pipes, _connectedScratch);
                SteamPipeRules.CollectPoweredCells(boiler.Cell, _connectedScratch, powered);
                _poweredCellsByBoiler[boiler.BoilerId] = powered;
            }
        }
    }
}
