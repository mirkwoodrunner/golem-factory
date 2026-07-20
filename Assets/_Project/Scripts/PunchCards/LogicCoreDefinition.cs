using UnityEngine;

namespace GolemFactory.PunchCards
{
    public enum TriggerType
    {
        Interval,
        Threshold,
        Signal,
        AlwaysOn
    }

    [CreateAssetMenu(fileName = "NewLogicCore", menuName = "Golem Factory/Punch Cards/Logic Core")]
    public sealed class LogicCoreDefinition : ScriptableObject
    {
        public TriggerType triggerType = TriggerType.AlwaysOn;

        [Tooltip("Interval trigger: fires every N ticks.")]
        public int intervalTicks = 10;

        [Tooltip("Threshold trigger: fires when the linked inventory crosses this quantity.")]
        public int thresholdQuantity = 100;

        [Tooltip("Signal trigger: fires when the named golem completes its cycle.")]
        public string signalGolemId;
    }
}
