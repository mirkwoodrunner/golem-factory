using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.AssemblyLine
{
    // A card the player can claim on the Assembly Line -- wraps exactly one of
    // Chassis/LogicCore/Appendage (mirrors UI/WorkbenchCard's one-of-two-fields pattern,
    // extended to three since the Assembly Line drafts chassis too, unlike the Workbench's
    // cards which are Logic Core/Appendage only).
    public sealed class DraftableCardDefinition
    {
        // ScriptableObject.name in Unity: the asset's file name, which the catalog,
        // the save file and the Ledger all key on. Plain data now, so it is a field.
        public string name;

        public ChassisDefinition chassis;
        public LogicCoreDefinition logicCore;
        public AppendageActionDefinition appendage;

        // --- What it costs --------------------------------------------------------------------
        // What claiming this card costs, as an item bundle, always in full (the decaying draft
        // price went with the draft row, 2026-11-19). Empty is free: the movement verbs, Repeat and
        // Freight Launch. Tier-N cards cost Tier-(N-1) goods -- the actual tech spend.
        public List<RecipeIngredient> claimCost = new List<RecipeIngredient>();

        /// <summary>Whether claiming this card costs anything.</summary>
        public bool HasBundleCost => claimCost != null && claimCost.Count > 0;

        // --- §8.3: prerequisites ------------------------------------------------------------
        // Cards that must already be claimed before this one is offered. The
        // Aether Containment card cannot show up before you hold its inputs' cards.
        public List<DraftableCardDefinition> prerequisiteCards = new List<DraftableCardDefinition>();

        // An item type the player must have PRODUCED before this card is offered. §8's worked
        // example: Aether Containment waits on a Lens actually being made.
        public string prerequisiteItemProduced;

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
                return appendage != null ? appendage.Label : "(empty)";
            }
        }
    }
}
