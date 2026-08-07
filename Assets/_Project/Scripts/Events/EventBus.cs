using System;

namespace GolemFactory.Events
{
    public readonly struct TickAdvancedEvent
    {
        public readonly long Tick;
        public TickAdvancedEvent(long tick) => Tick = tick;
    }

    public readonly struct ThresholdCrossedEvent
    {
        public readonly string InventoryId;
        public readonly int Quantity;

        public ThresholdCrossedEvent(string inventoryId, int quantity)
        {
            InventoryId = inventoryId;
            Quantity = quantity;
        }
    }

    public readonly struct GolemCompletedEvent
    {
        public readonly string GolemId;
        public GolemCompletedEvent(string golemId) => GolemId = golemId;
    }

    // Why a golem's current step failed its precondition. Deliberately specific enough to be
    // self-describing without also carrying the AppendageActionType -- "BeltEmpty" already
    // implies a LoadIntoBuffer, "NodeEmpty" an ExtractFromNode. Phrasing for the player lives
    // in UI/StallDiagnostics.cs, not here.
    public enum StallReason
    {
        // Not stalled / reason unknown (the value a bare GolemStalledEvent(id) carries).
        None = 0,
        // A required holder/registry reference or a source/destination id isn't wired up.
        Unconfigured,
        // ExtractFromNode: the resource node is depleted or has no such id.
        NodeEmpty,
        // The destination has no room for anything this step is carrying.
        //
        // Named for the only destination that could be full when it was introduced (a belt,
        // via ExtractFromNode), and NOT renamed or renumbered since -- StallReason is
        // append-only and serialized by index, so the member name is the cheap part and the
        // value is the load-bearing one. It now also covers a Push whose target StorageBuffer
        // has hit its per-item-type cap for every type in the golem's hold; ResourceId carries
        // the endpoint's DisplayName either way, so the player reads "ScrapBuffer full", which
        // is exactly right for a buffer too.
        //
        // Only a push that moved NOTHING reports this. A partial push -- some types delivered,
        // one refused because that slot is full -- is progress and must not stall, or a
        // smelter would stop dead on a Slag backlog it is still successfully clearing Plate
        // through. See GolemEntity.BeginPush.
        BeltFull,
        // LoadIntoBuffer: nothing has reached the end of the source belt segment yet.
        BeltEmpty,
        // Refine: the source storage buffer doesn't hold the recipe's input item.
        BufferEmpty,
        // Spatial routing: nothing that can supply an item sits on the tile behind the golem.
        // Distinct from NodeEmpty (a source exists but is spent) because the player's fix is
        // different: rotate/reposition the golem, don't wait for the node to refill.
        NoSourceAtTile,
        // Spatial routing: nothing that can accept an item sits on the tile in front.
        NoTargetAtTile,

        // APPEND ONLY below this line, for the same reason AppendageActionType is append-only:
        // StallReason is stored as an int in GolemStalledEvent and read back by the badge and
        // the alerts strip, and inserting a value would repoint every existing one.

        // Haul/ExtractFromNode: the golem's own INPUT stock is already at the per-type cap for
        // the type it is trying to pull, so there is nowhere to put another unit. ResourceId
        // carries the blocked *item type*, not a belt/node/buffer id -- the fix is downstream
        // (something has to consume it), and the player can't act on that without knowing
        // which good backed up.
        InputFull,

        // Assemble: the golem's own OUTPUT stock is at the per-type cap for the type it would
        // produce, so the recipe cannot deposit. Same convention -- ResourceId is the item
        // type, which progression-design §8 requires by name for exactly this reason: an
        // Assemble golem blocked on Slag rather than on its product is the whole Slag economy.
        OutputFull,

        // The item type a step NAMED is not available where the step needs it. ResourceId
        // carries the item type, same convention as the two above.
        //
        // Distinct from BufferEmpty/BeltEmpty, which mean the place is empty. The split is the
        // whole point: a typed Haul(Aether) against a buffer holding 500 Scrap used to report
        // "no input in ScrapBuffer", pointing the player at a buffer that is visibly full. With
        // §5.3(b)/(c) making mixed buffers the normal case, that misdirection gets worse rather
        // than better, and naming the wrong culprit is exactly the failure mode
        // UI/StallDiagnostics exists to prevent. "Nothing here holds Aether" and "this is empty"
        // have different fixes: look upstream of the buffer, versus wait or rotate the golem.
        //
        // Also what Assemble reports when any of its recipe's 1-4 inputs is short. ResourceId
        // names the FIRST short input in the recipe's authored order (deterministic across two
        // identically-programmed golems) and GolemStalledEvent.Shortfall carries how many more
        // of it are needed -- progression-design §8 requires both.
        MissingItem,

        // No steam reaches this golem's tile (docs/progression-design.md §3.1). ResourceId
        // carries the GOLEM'S OWN CELL as a string, matching NoSourceAtTile/NoTargetAtTile --
        // the actionable fact is a place, not a good: run a pipe to it, move it next to one, or
        // build a second boiler because the one it depends on is already at its 8-golem cap.
        //
        // This is the existing rigidity rule applied to a NEW PRECONDITION, not a departure
        // from it: an unpowered golem retries the same step every tick until steam returns,
        // exactly as it does for an empty node or a full belt. It is checked before any step's
        // side effect runs, so an unpowered golem never half-executes.
        NoSteam,

        // The node behind this golem is already worked by its maximum crew of two
        // (docs/progression-design.md §3.2 -- "the seam collapses if over-crewed"). ResourceId
        // carries the NODE ID, matching NodeEmpty's convention rather than the item-type one:
        // the fix is to take this golem somewhere else, and the player needs to know which
        // seam is over-subscribed to work out where.
        //
        // Deliberately NOT folded into NodeEmpty. "This seam is spent" and "this seam already
        // has a full crew" have opposite fixes -- wait/expand versus relocate -- and the node
        // in question is visibly still full of ore, so reporting it as empty would read as a
        // bug. Appended, because StallReason is serialized by index (see above).
        NodeCrowded
    }

    // Which kind of trigger fired. Mirrors PunchCards.TriggerType minus AlwaysOn, which is
    // continuous rather than an event and so is never published (it would fire every tick and
    // drown the channel out).
    public enum TriggerKind
    {
        Interval,
        Threshold,
        Signal
    }

    // Published on the *transition* into Stalled (and again if the reason changes while
    // stalled), not every tick -- republishing at TicksPerSecond re-armed GolemVisual's stall
    // shake 10x/second so it never decayed. Anything that needs "who is stalled right now"
    // must therefore reconcile against GolemEntity.Program.State rather than trusting the
    // event stream alone; see UI/StallTracker.Reconcile.
    public readonly struct GolemStalledEvent
    {
        public readonly string GolemId;
        public readonly StallReason Reason;
        // The belt/node/buffer id whose precondition failed, so the player is told *which*
        // resource is blocking rather than only that something is.
        public readonly string ResourceId;
        public readonly int StepIndex;

        // How many MORE units of ResourceId the step needed, or 0 when the stall carries no
        // meaningful amount. progression-design §8 requires Assemble to report "the specific
        // short ingredient AND amount" -- on R15 (10 Casing + 6 Iron Plate + 4 Brass), "no
        // Casing" leaves the player unable to tell a one-unit hiccup from a dead line.
        //
        // Carried beside ResourceId rather than inside it: ResourceId is the BARE item type by
        // established convention (InputFull/OutputFull/MissingItem), which the badge, the alerts
        // strip and the test suite all rely on.
        public readonly int Shortfall;

        public GolemStalledEvent(string golemId) : this(golemId, StallReason.None, null, 0, 0) { }

        // Kept for every publisher and test that has no amount to report -- widening the struct
        // must not force a rewrite of call sites that were already correct.
        public GolemStalledEvent(string golemId, StallReason reason, string resourceId, int stepIndex)
            : this(golemId, reason, resourceId, stepIndex, 0) { }

        public GolemStalledEvent(
            string golemId, StallReason reason, string resourceId, int stepIndex, int shortfall)
        {
            GolemId = golemId;
            Reason = reason;
            ResourceId = resourceId;
            StepIndex = stepIndex;
            Shortfall = shortfall;
        }
    }

    // Fires the moment a golem's Interval/Threshold/Signal trigger actually admits a cycle.
    // Without it the M7 chain reaction (buffer crosses threshold -> refiner runs -> its
    // completion signals the shipper) happened entirely invisibly.
    public readonly struct GolemTriggerFiredEvent
    {
        public readonly string GolemId;
        public readonly TriggerKind Kind;

        public GolemTriggerFiredEvent(string golemId, TriggerKind kind)
        {
            GolemId = golemId;
            Kind = kind;
        }
    }

    // M6: the counterpart GolemStalledEvent never had -- fired exactly once when a golem
    // transitions out of Stalled, so listeners (stall indicator, alerts panel) can turn
    // themselves off without polling GolemEntity.Program.State every frame.
    public readonly struct GolemResumedEvent
    {
        public readonly string GolemId;
        public GolemResumedEvent(string golemId) => GolemId = golemId;
    }

    // FRESH PRODUCTION (docs/progression-design.md §7). Published the moment an Assemble step
    // actually completes and its product lands in the golem's output stock -- once for the
    // output and, separately, once for the byproduct, because R4 producing Slag is genuinely
    // fresh Slag and the Clock Tower has to be able to say so.
    //
    // THIS EXISTS FOR EXACTLY ONE REASON: it is the signal that closes the hoard-blitz hole.
    // The tower's multiplier is min(deliveryRate, freshProductionRate) over 60 s windows, and
    // without a "something was genuinely made just now" signal a warehouse dump is
    // indistinguishable from a running factory -- a player could over-produce stage-4 goods
    // during stages 1-3 and unload at 3x to finish the climax in three minutes.
    //
    // ONLY Assemble publishes this. Not Haul, not Push (those move goods that already existed,
    // which is the very thing being guarded against), and not the legacy id-routed Refine,
    // which §1.3 superseded and which nothing new is built on.
    //
    // Carries the TICK rather than letting the listener stamp its own. The Clock Tower's
    // windows are tick-keyed for determinism, and a listener that stamped arrivals with
    // "whatever tick I last saw" would shift a production event by the gap between the golem's
    // Tick and the tower's -- reproducible, but for no reason when the producer already knows.
    public readonly struct ItemAssembledEvent
    {
        public readonly string GolemId;
        public readonly string ItemType;
        public readonly int Quantity;
        public readonly long Tick;

        /// <summary>
        /// True for the recipe's optional second output. Purely informational -- a byproduct is
        /// fresh production of its own type exactly like the main output -- but the two are
        /// published separately so a listener that cares (a Slag readout, say) does not have to
        /// re-derive which is which from the recipe.
        /// </summary>
        public readonly bool IsByproduct;

        public ItemAssembledEvent(string golemId, string itemType, int quantity, long tick)
            : this(golemId, itemType, quantity, tick, false) { }

        public ItemAssembledEvent(
            string golemId, string itemType, int quantity, long tick, bool isByproduct)
        {
            GolemId = golemId;
            ItemType = itemType;
            Quantity = quantity;
            Tick = tick;
            IsByproduct = isByproduct;
        }
    }

    // A Clock Tower stage finished (docs/progression-design.md §7). Fired once per stage, on
    // the tick the stage's progress reaches 100 %.
    //
    // IsFinalStage is the WIN, and it is deliberately a flag on the ordinary stage event rather
    // than a separate "game over" one: §7 is explicit that stage 4 completion "leaves the save
    // running -- the Clock Tower is a win, not a game-over". Nothing in the simulation stops,
    // and the only thing listening for the flag is presentation.
    public readonly struct ClockTowerStageCompletedEvent
    {
        public readonly int StageNumber;
        public readonly string StageName;
        public readonly bool IsFinalStage;
        public readonly long Tick;

        public ClockTowerStageCompletedEvent(
            int stageNumber, string stageName, bool isFinalStage, long tick)
        {
            StageNumber = stageNumber;
            StageName = stageName;
            IsFinalStage = isFinalStage;
            Tick = tick;
        }
    }

    public static class EventBus
    {
        public static event Action<TickAdvancedEvent> TickAdvanced;
        public static event Action<ThresholdCrossedEvent> ThresholdCrossed;
        public static event Action<GolemCompletedEvent> GolemCompleted;
        public static event Action<GolemStalledEvent> GolemStalled;
        public static event Action<GolemResumedEvent> GolemResumed;
        public static event Action<GolemTriggerFiredEvent> GolemTriggerFired;
        public static event Action<ItemAssembledEvent> ItemAssembled;
        public static event Action<ClockTowerStageCompletedEvent> ClockTowerStageCompleted;

        public static void Publish(TickAdvancedEvent e) => TickAdvanced?.Invoke(e);
        public static void Publish(ThresholdCrossedEvent e) => ThresholdCrossed?.Invoke(e);
        public static void Publish(GolemCompletedEvent e) => GolemCompleted?.Invoke(e);
        public static void Publish(GolemStalledEvent e) => GolemStalled?.Invoke(e);
        public static void Publish(GolemResumedEvent e) => GolemResumed?.Invoke(e);
        public static void Publish(GolemTriggerFiredEvent e) => GolemTriggerFired?.Invoke(e);
        public static void Publish(ItemAssembledEvent e) => ItemAssembled?.Invoke(e);
        public static void Publish(ClockTowerStageCompletedEvent e) => ClockTowerStageCompleted?.Invoke(e);
    }
}
