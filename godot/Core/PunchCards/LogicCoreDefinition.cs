using GolemFactory.Compat;

namespace GolemFactory.PunchCards
{
    public enum TriggerType
    {
        Interval,
        Threshold,
        Signal,
        AlwaysOn
    }

    public sealed class LogicCoreDefinition
    {
        // ScriptableObject.name in Unity: the asset's file name, which the catalog,
        // the save file and the Ledger all key on. Plain data now, so it is a field.
        public string name;

        public TriggerType triggerType = TriggerType.AlwaysOn;

        // "Interval trigger: fires every N ticks."
        public int intervalTicks = 10;

        // "Threshold trigger: the StorageBuffer id to watch."
        public string thresholdBufferId;

        // "Threshold trigger: the item type within that buffer to watch."
        public string thresholdItemType;

        // "Threshold trigger: fires when the linked inventory crosses this quantity."
        public int thresholdQuantity = 100;

        // "Signal trigger: fires when the named golem completes its cycle."
        public string signalGolemId;
    }
}
