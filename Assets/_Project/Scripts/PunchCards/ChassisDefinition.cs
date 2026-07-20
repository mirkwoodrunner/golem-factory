using UnityEngine;

namespace GolemFactory.PunchCards
{
    [CreateAssetMenu(fileName = "NewChassis", menuName = "Golem Factory/Punch Cards/Chassis")]
    public sealed class ChassisDefinition : ScriptableObject
    {
        public int maxAppendageSlots = 2;
        public int tier = 1;
        public int scrapCost;
        public int brassCost;
    }
}
