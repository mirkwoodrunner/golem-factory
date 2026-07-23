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

    public readonly struct GolemStalledEvent
    {
        public readonly string GolemId;
        public GolemStalledEvent(string golemId) => GolemId = golemId;
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

        public static void Publish(TickAdvancedEvent e) => TickAdvanced?.Invoke(e);
        public static void Publish(ThresholdCrossedEvent e) => ThresholdCrossed?.Invoke(e);
        public static void Publish(GolemCompletedEvent e) => GolemCompleted?.Invoke(e);
        public static void Publish(GolemStalledEvent e) => GolemStalled?.Invoke(e);
        public static void Publish(GolemResumedEvent e) => GolemResumed?.Invoke(e);
    }
}
