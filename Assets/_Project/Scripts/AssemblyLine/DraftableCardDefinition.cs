using UnityEngine;
using GolemFactory.PunchCards;

namespace GolemFactory.AssemblyLine
{
    // A card that can appear on the Assembly Line for drafting -- wraps exactly one of
    // Chassis/LogicCore/Appendage (mirrors UI/WorkbenchCard's one-of-two-fields pattern,
    // extended to three since the Assembly Line drafts chassis too, unlike the Workbench's
    // cards which are Logic Core/Appendage only).
    [CreateAssetMenu(fileName = "NewDraftableCard", menuName = "Golem Factory/Assembly Line/Draftable Card")]
    public sealed class DraftableCardDefinition : ScriptableObject
    {
        public ChassisDefinition chassis;
        public LogicCoreDefinition logicCore;
        public AppendageActionDefinition appendage;

        [Tooltip("Scrap cost when the card first appears on the line.")]
        public int baseCost = 20;

        [Tooltip("Scrap cost removed per second the card sits on the line, uncliamed.")]
        public float decayPerSecond = 1f;

        [Tooltip("Cost never decays below this floor.")]
        public int minCost = 2;

        public string DisplayName
        {
            get
            {
                if (chassis != null)
                {
                    return chassis.name;
                }
                if (logicCore != null)
                {
                    return logicCore.name;
                }
                return appendage != null ? appendage.name : "(empty)";
            }
        }
    }
}
