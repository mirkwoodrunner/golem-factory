using System;
using System.Collections.Generic;
using GolemFactory.Events;

namespace GolemFactory.ClockTower
{
    // The Clock Tower (docs/progression-design.md §7, §11 item 9).
    //
    // A plain-C# manager, owned by a thin ClockTowerSiteHolder per the Holder pattern -- the same
    // arrangement SteamNetwork/SteamNetworkHolder, BeltNetwork/BeltNetworkHolder and
    // GridMap/GridMapHolder use. It owns four things and nothing else:
    //
    //   * WHICH STAGE is running, and how far through it the player is (an integer accumulator;
    //     see ClockTowerProgress for why not a float);
    //   * TWO 60-SECOND ROLLING WINDOWS per tracked item -- units DELIVERED into the tower, and
    //     units FRESHLY ASSEMBLED anywhere in the factory;
    //   * A SUPPLY-PRESSURE METER per demanded item;
    //   * WHETHER THE TOWER IS FINISHED, which is the win.
    //
    // It does not know what a GolemEntity is, and it holds no UnityEngine types at all beyond
    // what the ScriptableObject stage definitions bring: a delivery is an item-type string and a
    // quantity, the same bare-string-id convention belts, buffers and nodes already use. Goods
    // reach it through ClockTowerInputEndpoint, which is an ordinary IItemEndpoint on a cell --
    // §7 asks for "an input buffer tile golems Push into like any other", so the tower needed no
    // special case anywhere in GolemEntity.
    //
    // ---------------------------------------------------------------------------------------
    // WHAT MAY NEVER HAPPEN HERE. §7: "Stages cannot fail -- only take longer. That is the
    // rubric-5 guarantee." So: progress is only ever added to and is never negative; the stage
    // index only ever increases; and no supply condition, however bad, does anything worse than
    // hold the per-tick increment at zero. There is no failure branch in this file because there
    // is no failure state in the design.
    // ---------------------------------------------------------------------------------------
    //
    // DETERMINISM, same discipline §1.4 established for the steam network:
    //
    // (1) THE min ACROSS DEMANDED ITEMS walks the stage's AUTHORED demand List by index, never a
    //     Dictionary. Dictionary iteration order is not contractual and rehashing on growth
    //     silently reshuffles it, so two identically-supplied towers could report different
    //     starved items -- and the starved item is the one thing §8 puts in front of the player.
    //     Ties go to the first line in authored order, matching GolemEntity.BeginAssemble's rule
    //     for naming the first short ingredient of a recipe.
    //
    // (2) ALL ARITHMETIC IS INTEGER. Progress accumulates in millionths of a nominal tick and
    //     the meters in six-hundredths of a unit, both chosen so the per-tick step is exact.
    //     See ClockTowerProgress and SupplyPressureMeter.
    //
    // (3) EVALUATE, THEN DECAY, within one tick. A meter drained to exactly zero by THIS tick's
    //     own decay must not also freeze this tick's progress. At exactly 1x supply the meter
    //     returns to zero precisely when the next unit is due, so the other order would freeze
    //     one tick in every delivery interval and make a perfectly-supplied stage run ~1 % long
    //     -- a stage would miss its own nominal duration for no reason the player could see.
    //     With this order, a meter reading empty means "nothing has arrived for a full drain
    //     period", which is what §7 means by starved.
    public sealed class ClockTowerSite
    {
        private readonly List<ClockTowerStageDefinition> _stages =
            new List<ClockTowerStageDefinition>();

        // Keyed by item type. Dictionaries are fine for LOOKUP -- what must never be a dictionary
        // is the ITERATION that decides the min (see determinism note 1), which walks the stage's
        // authored demand list instead.
        private readonly Dictionary<string, RollingSupplyWindow> _delivered =
            new Dictionary<string, RollingSupplyWindow>(StringComparer.Ordinal);
        private readonly Dictionary<string, RollingSupplyWindow> _freshlyProduced =
            new Dictionary<string, RollingSupplyWindow>(StringComparer.Ordinal);
        private readonly Dictionary<string, SupplyPressureMeter> _meters =
            new Dictionary<string, SupplyPressureMeter>(StringComparer.Ordinal);

        // Every window, for the per-tick trim. Held as a flat list so trimming never has to walk
        // a Dictionary -- trimming is order-independent, but keeping the rule "no Dictionary
        // iteration in Tick" absolute is cheaper than re-arguing it at every future edit.
        private readonly List<RollingSupplyWindow> _allWindows = new List<RollingSupplyWindow>();

        // The union of every stage's demanded item types. Fresh-production events arrive for
        // EVERY Assemble in the factory -- twenty-odd goods across a hundred golems -- and the
        // tower cares about five of them. Filtering on arrival keeps the tracked set bounded and
        // fixed for the whole session rather than growing with whatever the player happens to
        // build; it changes no behaviour, because an item no stage demands can never appear in a
        // min.
        private readonly HashSet<string> _trackedTypes = new HashSet<string>(StringComparer.Ordinal);

        private int _stageIndex;
        private long _progressUnits;
        private bool _complete;

        // The last evaluation's results, kept so the HUD can read them without triggering one.
        // Same rule as SteamNetwork.LastEvaluatedPoweredCount: a read-only view must never
        // perturb the simulation it is reporting on.
        private long _stageScaledMultiplier;
        private string _limitingItemType;
        private string _starvedItemType;
        private int _starvedDeficitPerMinute;

        /// <summary>
        /// The tick this site was last advanced to. Deliveries arriving through
        /// <see cref="ClockTowerInputEndpoint"/> are stamped with it, because
        /// <c>IItemEndpoint.TryGive</c> has no tick to hand over and inventing one from wall
        /// time would put a float in the middle of the win condition.
        ///
        /// <para>
        /// A golem that pushes before the site's own <c>Tick</c> in the same frame therefore
        /// credits the previous tick number. That is reproducible -- clock registration order is
        /// fixed for a given scene -- and it shifts a sample by at most one tick inside a
        /// six-hundred-tick window, which at steady supply costs at most one unit off the rate
        /// once per delivery interval.
        /// </para>
        /// </summary>
        public long CurrentTick { get; private set; }

        public int StageCount => _stages.Count;

        /// <summary>1-based for display; 0 once every stage is done.</summary>
        public int ActiveStageNumber
        {
            get
            {
                ClockTowerStageDefinition stage = ActiveStage;
                return stage != null ? stage.stageNumber : 0;
            }
        }

        /// <summary>Index into the stage list, monotonically non-decreasing for the site's life.</summary>
        public int StageIndex => _stageIndex;

        public ClockTowerStageDefinition ActiveStage =>
            !_complete && _stageIndex >= 0 && _stageIndex < _stages.Count ? _stages[_stageIndex] : null;

        /// <summary>
        /// THE WIN. §7: stage 4 completion "leaves the save running -- the Clock Tower is a win,
        /// not a game-over", so this flag stops the tower accruing and nothing else. The clock
        /// keeps ticking, every golem keeps running, and the player keeps their factory.
        /// </summary>
        public bool IsComplete => _complete;

        /// <summary>Tick the final stage completed on, or -1 while the tower is unfinished.</summary>
        public long CompletedTick { get; private set; } = -1;

        public long ProgressUnits => _progressUnits;

        public long RequiredProgressUnits
        {
            get
            {
                ClockTowerStageDefinition stage = ActiveStage;
                return stage != null ? stage.RequiredProgressUnits() : 0L;
            }
        }

        public int ProgressPercent =>
            _complete ? 100 : ClockTowerProgress.ProgressPercent(_progressUnits, RequiredProgressUnits);

        /// <summary>The stage multiplier as of the last tick, in <c>ProgressScale</c> units.</summary>
        public long StageScaledMultiplier => _stageScaledMultiplier;

        /// <summary>The stage multiplier as of the last tick, in thousandths (1000 == 1.00x).</summary>
        public int StageMultiplierMilli => ClockTowerProgress.MultiplierMilli(_stageScaledMultiplier);

        /// <summary>
        /// The demanded item currently holding the multiplier down -- the first line in authored
        /// order achieving the minimum. Null when no stage is running.
        /// </summary>
        public string LimitingItemType => _limitingItemType;

        /// <summary>
        /// The demanded item whose supply-pressure meter has bottomed out, or null if none has.
        /// This is what §7's "an alert names the starved item" reports on, and while it is
        /// non-null progress is frozen at zero.
        /// </summary>
        public string StarvedItemType => _starvedItemType;

        /// <summary>§7's "the HUD shows the deficit in items/min" for the starved line.</summary>
        public int StarvedDeficitPerMinute => _starvedDeficitPerMinute;

        // --- Stages ---------------------------------------------------------------------------

        /// <summary>
        /// Loads the tower's stages, in order, and resets it to the start of the first.
        /// Malformed and null entries are dropped with the rest kept, matching the authoring-edge
        /// validation rule: a bad stage asset must not take the tower (or the tick loop) down.
        /// </summary>
        public void SetStages(IEnumerable<ClockTowerStageDefinition> stages)
        {
            _stages.Clear();
            _trackedTypes.Clear();

            if (stages != null)
            {
                foreach (ClockTowerStageDefinition stage in stages)
                {
                    if (stage == null || !stage.IsWellFormed())
                    {
                        continue;
                    }

                    _stages.Add(stage);
                    for (int i = 0; i < stage.demands.Count; i++)
                    {
                        _trackedTypes.Add(stage.demands[i].itemType);
                    }
                }
            }

            Reset();
        }

        /// <summary>
        /// Whether building has begun: anything delivered, or any stage or progress made (a
        /// load). The Ledger's Clock Tower node asks this of the town square's fixture, which
        /// stands from the start and so is "built" only once it has been fed.
        /// </summary>
        public bool HasStarted => _started || _stageIndex > 0 || _progressUnits > 0L || _complete;

        private bool _started;

        // Whether anything has been delivered in the running stage: the starved alarm is armed
        // by the stage's first delivery. Tower-wide "has started" raised it the moment a new
        // stage began, and on every load mid-stage, before anyone could feed it (from review).
        // Once armed, a line never delivered at all IS starved -- §8's stage-2 introduction, where
        // Frame Sections flow and Great Cog has no line yet. Cleared at each stage, on Reset, and
        // so on a load.
        private bool _fedThisStage;

        /// <summary>Back to the start of stage 1, with every window and meter emptied.</summary>
        public void Reset()
        {
            _started = false;
            _fedThisStage = false;
            _stageIndex = 0;
            _progressUnits = 0L;
            _complete = false;
            CompletedTick = -1;
            _stageScaledMultiplier = 0L;
            _limitingItemType = null;
            _starvedItemType = null;
            _starvedDeficitPerMinute = 0;

            for (int i = 0; i < _allWindows.Count; i++)
            {
                _allWindows[i].Clear();
            }

            foreach (SupplyPressureMeter meter in _meters.Values)
            {
                meter.Reset();
            }
        }

        /// <summary>
        /// Puts the megaproject back where a save left it: which stage was running, how far into
        /// it, and whether it had already been finished.
        ///
        /// <para>
        /// PROGRESS IS RESTORED; THE RATE WINDOWS ARE NOT, and that asymmetry is deliberate.
        /// Stage progress is a durable achievement -- tens of minutes of a whole factory's output,
        /// by far the most expensive number in a save file. The 60-second delivery and
        /// fresh-production windows are estimates of what a factory is doing *right now*, stamped
        /// with tick numbers that <c>SaveData</c> does not persist (it is explicitly "continue
        /// where you left off", not a simulation snapshot), so restoring them would mean replaying
        /// samples against a tick counter that has restarted. They rebuild within a minute of the
        /// factory running, which is the honest answer: a tower resumes at the rate its factory
        /// can actually supply, not the rate it managed before the player quit.
        /// </para>
        ///
        /// <para>
        /// Must be called AFTER <see cref="SetStages"/>, which calls <see cref="Reset"/>.
        /// </para>
        /// </summary>
        public void RestoreProgress(int stageIndex, long progressUnits, bool complete)
        {
            // Clamped rather than trusted: a save written against a different stage list (an
            // edited asset, an older build) must not index off the end of it every tick.
            _stageIndex = stageIndex < 0 ? 0 : (stageIndex > _stages.Count ? _stages.Count : stageIndex);
            _progressUnits = progressUnits < 0L ? 0L : progressUnits;
            _complete = complete || _stageIndex >= _stages.Count;
            // A stage saved part-built had been fed, so its alarm is armed: a load into a factory
            // whose line has since broken must still say so. (Unarmed, it never would, since no
            // delivery ever comes. The cost: right after a load, a working line's alarm can show
            // until its first delivery lands, because the meters are not saved.)
            _fedThisStage = _progressUnits > 0L;
        }

        /// <summary>
        /// Whether the site is open to deliveries (G10). Null means always: a site nobody gates
        /// works as it always did. The Sandbox ropes its tower off until the factory has built a
        /// Zeppelin, so the endgame project waits for the end game.
        /// </summary>
        public Func<bool> OpenWhen { get; set; }

        public bool IsOpen => OpenWhen == null || OpenWhen();

        /// <summary>What the rope's sign says while the site is closed.</summary>
        public string ClosedReason { get; set; } = "roped off";

        /// <summary>
        /// Whether the tower would accept <paramref name="itemType"/> right now -- i.e. whether
        /// the running stage demands it. What <see cref="ClockTowerInputEndpoint"/>'s typed
        /// <c>CanGive</c> answers.
        /// </summary>
        public bool Demands(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return false;
            }

            ClockTowerStageDefinition stage = ActiveStage;
            if (stage == null)
            {
                return false;
            }

            for (int i = 0; i < stage.demands.Count; i++)
            {
                if (string.Equals(stage.demands[i].itemType, itemType, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the tower would accept ANYTHING at all -- the untyped question
        /// <c>IItemEndpoint.CanGive()</c> asks. True while a stage is running.
        /// </summary>
        public bool AcceptsAnything
        {
            get
            {
                ClockTowerStageDefinition stage = ActiveStage;
                return stage != null && stage.demands.Count > 0;
            }
        }

        /// <summary>The rate a demanded item is arriving at, in items per minute over 60 s.</summary>
        public int DeliveryRatePerMinute(string itemType) => RateIn(_delivered, itemType);

        /// <summary>
        /// The rate that item is being FRESHLY ASSEMBLED at anywhere in the factory, over the
        /// same 60 s window.
        /// </summary>
        public int FreshProductionRatePerMinute(string itemType) => RateIn(_freshlyProduced, itemType);

        /// <summary>§7's <c>min(deliveryRate, freshProductionRate)</c>.</summary>
        public int EffectiveRatePerMinute(string itemType) =>
            ClockTowerProgress.EffectiveRate(
                DeliveryRatePerMinute(itemType), FreshProductionRatePerMinute(itemType));

        /// <summary>The supply-pressure meter for an item, in whole units of <c>[0, 60]</c>.</summary>
        public int MeterUnits(string itemType)
        {
            SupplyPressureMeter meter;
            return TryFindMeter(itemType, out meter) ? meter.Units : 0;
        }

        // --- Inputs ---------------------------------------------------------------------------

        /// <summary>
        /// Credits <paramref name="quantity"/> units delivered into the tower on
        /// <paramref name="tick"/>: it counts toward the delivery window AND raises the
        /// supply-pressure meter by one per unit. Returns how many units were accepted.
        ///
        /// <para>
        /// Items no stage ever demands are refused (0) rather than swallowed. The endpoint
        /// already refuses them at <c>CanGive(itemType)</c>, so a golem pushing a mixed hold
        /// keeps the rest instead of destroying it -- the same per-type skip
        /// <c>GolemEntity.BeginPush</c> does for a full buffer slot.
        /// </para>
        /// </summary>
        public int RecordDelivery(string itemType, int quantity, long tick)
        {
            if (quantity <= 0 || !_trackedTypes.Contains(itemType ?? string.Empty))
            {
                return 0;
            }

            WindowFor(_delivered, itemType).Record(tick, quantity);
            MeterFor(itemType).Credit(quantity);
            _started = true;
            _fedThisStage = true;
            return quantity;
        }

        /// <summary>
        /// Records that <paramref name="quantity"/> units of <paramref name="itemType"/> were
        /// genuinely ASSEMBLED on <paramref name="tick"/>. Fed from
        /// <see cref="ItemAssembledEvent"/> by the holder.
        ///
        /// <para>
        /// This is the half of the multiplier a stockpile cannot fake, and the whole reason the
        /// event exists. See <see cref="ClockTowerProgress.EffectiveRate"/>.
        /// </para>
        /// </summary>
        public void RecordFreshProduction(string itemType, int quantity, long tick)
        {
            if (quantity <= 0 || !_trackedTypes.Contains(itemType ?? string.Empty))
            {
                return;
            }

            WindowFor(_freshlyProduced, itemType).Record(tick, quantity);
        }

        // --- The tick -------------------------------------------------------------------------

        /// <summary>
        /// One simulation tick: age the windows out, evaluate this tick's multiplier, add it to
        /// the stage's progress, decay the meters, and advance the stage if it just finished.
        ///
        /// <para>
        /// The order of the middle two is load-bearing; see determinism note (3) at the top.
        /// </para>
        /// </summary>
        public void Tick(long tick)
        {
            CurrentTick = tick;

            if (_complete)
            {
                // The win leaves the simulation running (§7). The tower simply stops accruing.
                _stageScaledMultiplier = 0L;
                return;
            }

            // Aged out every tick, open or not: a roped-off site still records fresh production,
            // and untrimmed, those lists grew for the whole game before the Zeppelin and then
            // reported hours of output as one minute's rate (from review).
            for (int i = 0; i < _allWindows.Count; i++)
            {
                _allWindows[i].Trim(tick);
            }

            // A roped-off site (G10) accrues nothing, exactly as one with no stage.
            ClockTowerStageDefinition stage = IsOpen ? ActiveStage : null;
            if (stage == null)
            {
                _stageScaledMultiplier = 0L;
                _limitingItemType = null;
                _starvedItemType = null;
                _starvedDeficitPerMinute = 0;
                return;
            }

            Evaluate(stage);

            // The only place progress ever changes, and it can only go up: _stageScaledMultiplier
            // is clamped to [0, 3 x ProgressScale] by ClockTowerProgress.ScaledRatio. §7's
            // "frozen, never negative".
            _progressUnits += _stageScaledMultiplier;

            for (int i = 0; i < stage.demands.Count; i++)
            {
                MeterFor(stage.demands[i].itemType).Decay(stage.demands[i].ratePerMinute);
            }

            if (_progressUnits >= stage.RequiredProgressUnits())
            {
                AdvanceStage(stage, tick);
            }
        }

        // Settles this tick's multiplier, the line that is holding it down, and whether anything
        // is starved. Walks the AUTHORED demand list by index -- determinism note (1).
        private void Evaluate(ClockTowerStageDefinition stage)
        {
            _limitingItemType = null;
            _starvedItemType = null;
            _starvedDeficitPerMinute = 0;

            List<StageDemand> demands = stage.demands;
            if (demands.Count == 0)
            {
                _stageScaledMultiplier = 0L;
                return;
            }

            long minimum = long.MaxValue;

            for (int i = 0; i < demands.Count; i++)
            {
                StageDemand demand = demands[i];

                int delivered = DeliveryRatePerMinute(demand.itemType);
                int fresh = FreshProductionRatePerMinute(demand.itemType);
                int effective = ClockTowerProgress.EffectiveRate(delivered, fresh);

                SupplyPressureMeter meter;
                bool starved = !TryFindMeter(demand.itemType, out meter) || meter.IsEmpty;

                // §7: "If any meter hits 0, progress is 0." Expressed as this line contributing a
                // ratio of zero rather than as an early return, so the walk still finds the FIRST
                // starved line in authored order when several are empty at once.
                long scaled = starved
                    ? 0L
                    : ClockTowerProgress.ScaledRatio(effective, demand.ratePerMinute);

                // Strictly less-than, so ties go to the earlier line in authored order.
                if (scaled < minimum)
                {
                    minimum = scaled;
                    _limitingItemType = demand.itemType;
                }

                // Not before this STAGE has had a delivery (G10, seen in a playtest-kit frame): the
                // moment the rope came down the HUD raised "starved of FrameSection - progress
                // frozen" over a site nobody had had a chance to feed, which read as something
                // broken. Progress still freezes; only the alarm waits.
                if (starved && _starvedItemType == null && _fedThisStage)
                {
                    _starvedItemType = demand.itemType;

                    // The deficit is measured against DELIVERY, not against the effective rate:
                    // the meter is fed by deliveries and by nothing else, so "get this many more
                    // per minute into the tower" is the sentence that actually clears the alert.
                    // The Fresh column of the §8 readout is where a production shortfall shows.
                    int deficit = demand.ratePerMinute - delivered;
                    _starvedDeficitPerMinute = deficit > 0 ? deficit : 0;
                }
            }

            _stageScaledMultiplier = minimum;
        }

        // Monotonic by construction: _stageIndex only ever increments and _progressUnits is reset
        // to zero, never carried backwards. Overflow past 100 % is DISCARDED rather than carried
        // into the next stage -- the next stage's lines have their own windows to fill and their
        // own meters to charge, and handing it a head start it did not earn would let a burst on
        // one good pay for a stage that does not demand it.
        private void AdvanceStage(ClockTowerStageDefinition completed, long tick)
        {
            _progressUnits = 0L;
            _stageIndex++;
            _fedThisStage = false;

            bool isFinal = _stageIndex >= _stages.Count;
            if (isFinal)
            {
                _complete = true;
                CompletedTick = tick;
            }

            EventBus.Publish(new ClockTowerStageCompletedEvent(
                completed.stageNumber, completed.stageName, isFinal, tick));
        }

        // --- Readout --------------------------------------------------------------------------

        /// <summary>
        /// The whole §8 readout in one struct: <c>Required / Delivered / Fresh / xmultiplier</c>
        /// per demanded item, plus the stage headline and the starved-item alert. Reads only the
        /// last settled evaluation, so calling it from a HUD repainting at 60 fps cannot perturb
        /// the simulation.
        /// </summary>
        public ClockTowerReading BuildReading()
        {
            if (_complete)
            {
                return ClockTowerReading.Completed(
                    _stages.Count > 0 ? _stages[_stages.Count - 1].stageName : null, CompletedTick);
            }

            // A roped-off site is dormant, and says why (G10).
            if (!IsOpen)
            {
                return ClockTowerReading.Dormant(ClosedReason);
            }

            ClockTowerStageDefinition stage = ActiveStage;
            if (stage == null)
            {
                return ClockTowerReading.Dormant();
            }

            var rows = new List<ClockTowerDemandReading>(stage.demands.Count);
            for (int i = 0; i < stage.demands.Count; i++)
            {
                StageDemand demand = stage.demands[i];

                int delivered = DeliveryRatePerMinute(demand.itemType);
                int fresh = FreshProductionRatePerMinute(demand.itemType);
                int effective = ClockTowerProgress.EffectiveRate(delivered, fresh);

                SupplyPressureMeter meter;
                bool starved = !TryFindMeter(demand.itemType, out meter) || meter.IsEmpty;

                long scaled = starved
                    ? 0L
                    : ClockTowerProgress.ScaledRatio(effective, demand.ratePerMinute);

                rows.Add(new ClockTowerDemandReading(
                    demand.itemType, demand.ratePerMinute, delivered, fresh,
                    ClockTowerProgress.MultiplierMilli(scaled),
                    meter != null ? meter.Units : 0,
                    starved));
            }

            return ClockTowerReading.Running(
                stage.stageNumber, stage.stageName, ProgressPercent, StageMultiplierMilli,
                _limitingItemType, _starvedItemType, _starvedDeficitPerMinute, rows);
        }

        // --- Plumbing -------------------------------------------------------------------------

        private int RateIn(Dictionary<string, RollingSupplyWindow> windows, string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return 0;
            }

            RollingSupplyWindow window;
            return windows.TryGetValue(itemType, out window) ? window.UnitsInWindow : 0;
        }

        private RollingSupplyWindow WindowFor(
            Dictionary<string, RollingSupplyWindow> windows, string itemType)
        {
            RollingSupplyWindow window;
            if (windows.TryGetValue(itemType, out window))
            {
                return window;
            }

            window = new RollingSupplyWindow();
            windows[itemType] = window;
            _allWindows.Add(window);
            return window;
        }

        // Created on first use rather than pre-registered per stage, so a meter for an item that
        // two consecutive stages both demand CARRIES OVER instead of resetting to empty. A
        // Frame Section line that has been running through stage 1 should not be declared starved
        // the instant stage 2 begins just because the tower turned a page.
        private SupplyPressureMeter MeterFor(string itemType)
        {
            SupplyPressureMeter meter;
            if (_meters.TryGetValue(itemType, out meter))
            {
                return meter;
            }

            meter = new SupplyPressureMeter();
            _meters[itemType] = meter;
            return meter;
        }

        private bool TryFindMeter(string itemType, out SupplyPressureMeter meter)
        {
            meter = null;
            return !string.IsNullOrEmpty(itemType) && _meters.TryGetValue(itemType, out meter);
        }
    }
}
