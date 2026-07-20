using UnityEngine;

namespace GolemFactory.PunchCards
{
    public enum AppendageActionType
    {
        Haul,
        ExtractFromNode,
        Refine,
        LoadIntoBuffer
    }

    [CreateAssetMenu(fileName = "NewAppendageAction", menuName = "Golem Factory/Punch Cards/Appendage Action")]
    public sealed class AppendageActionDefinition : ScriptableObject
    {
        public AppendageActionType actionType;
        public int durationTicks = 1;
        public string sourceId;
        public string destinationId;
    }
}
