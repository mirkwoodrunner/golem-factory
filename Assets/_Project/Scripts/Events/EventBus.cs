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
        // ExtractFromNode: the destination belt segment has no room for another item.
        BeltFull,
        // LoadIntoBuffer: nothing has reached the end of the source belt segment yet.
        BeltEmpty,
        // Refine: the source storage buffer doesn't hold the recipe's input item.
        BufferEmpty
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

        public GolemStalledEvent(string golemId) : this(golemId, StallReason.None, null, 0) { }

        public GolemStalledEvent(string golemId, StallReason reason, string resourceId, int stepIndex)
        {
            GolemId = golemId;
            Reason = reason;
            ResourceId = resourceId;
            StepIndex = stepIndex;
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

    public static class EventBus
    {
        public static event Action<TickAdvancedEvent> TickAdvanced;
        public static event Action<ThresholdCrossedEvent> ThresholdCrossed;
        public static event Action<GolemCompletedEvent> GolemCompleted;
        public static event Action<GolemStalledEvent> GolemStalled;
        public static event Action<GolemResumedEvent> GolemResumed;
        public static event Action<GolemTriggerFiredEvent> GolemTriggerFired;

        public static void Publish(TickAdvancedEvent e) => TickAdvanced?.Invoke(e);
        public static void Publish(ThresholdCrossedEvent e) => ThresholdCrossed?.Invoke(e);
        public static void Publish(GolemCompletedEvent e) => GolemCompleted?.Invoke(e);
        public static void Publish(GolemStalledEvent e) => GolemStalled?.Invoke(e);
        public static void Publish(GolemResumedEvent e) => GolemResumed?.Invoke(e);
        public static void Publish(GolemTriggerFiredEvent e) => GolemTriggerFired?.Invoke(e);
    }
}
