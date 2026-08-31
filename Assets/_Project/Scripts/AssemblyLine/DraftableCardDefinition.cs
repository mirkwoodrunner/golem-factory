using System.Collections.Generic;
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

        // --- Legacy Scrap-only cost -------------------------------------------------------
        // KEPT, and still authoritative when claimCost below is empty. §8 generalises the claim
        // cost to a bundle, but M9's demo cards are authored against these three and Main.unity
        // drafts from them; migrating by deletion would silently reprice every existing card.
        // Same additive fork spatial routing, steam and the machine model all ride.
        [Tooltip("LEGACY. Scrap cost when the card first appears, used only when Claim Cost is empty.")]
        public int baseCost = 20;

        [Tooltip("LEGACY. Scrap removed per second the card sits unclaimed.")]
        public float decayPerSecond = 1f;

        [Tooltip("LEGACY. Cost never decays below this floor.")]
        public int minCost = 2;

        // --- §8.2: item-bundle claim costs -------------------------------------------------
        [Tooltip("What claiming this card costs, as an item bundle. Empty falls back to the " +
                 "legacy Scrap fields above. Tier-N cards should cost Tier-(N-1) goods -- the " +
                 "actual tech spend.")]
        public List<RecipeIngredient> claimCost = new List<RecipeIngredient>();

        [Tooltip("Fraction of the whole bundle removed per second the card sits unclaimed. " +
                 "Decay scales every good in it, so a bundle gets cheaper the way a Scrap price " +
                 "did -- the tabletop's take-it-now-or-wait tension, unchanged.")]
        public float decayFractionPerSecond = 0.01f;

        [Tooltip("Floor, as a fraction of the authored bundle. A card never becomes free.")]
        public float minCostFraction = 0.25f;

        /// <summary>Whether this card is priced as a bundle (§8) or by the legacy Scrap fields.</summary>
        public bool HasBundleCost => claimCost != null && claimCost.Count > 0;

        // --- §8.3: prerequisites ------------------------------------------------------------
        [Tooltip("Cards that must already be claimed before this one may even APPEAR on the " +
                 "line. The Aether Containment card cannot show up before you hold its inputs' cards.")]
        public List<DraftableCardDefinition> prerequisiteCards = new List<DraftableCardDefinition>();

        [Tooltip("An item type the player must have PRODUCED before this card may appear. §8's " +
                 "worked example: Aether Containment waits on a Lens actually being made.")]
        public string prerequisiteItemProduced;

        /// <summary>
        /// §8.4: recipe and chassis cards LEAVE the pool when claimed; generic Logic Cores and
        /// the movement verbs keep cycling.
        ///
        /// <para>
        /// Without this the refill queue re-enqueues everything forever and the tech tree never
        /// terminates -- the player drafts the same Brass Presser card for the rest of the game
        /// while the cards they have not seen sit behind it in the queue.
        /// </para>
        /// </summary>
        [Tooltip("Claimed once and gone from the pool. True for recipes and chassis; false for " +
                 "generic cards that should keep cycling.")]
        //
        // DEFAULTS FALSE, deliberately. Unity gives a field absent from an existing .asset the
        // C# initializer's value, so defaulting true would silently make every card authored
        // before §8 -- Main.unity's whole demo deck -- unique, and change how that scene behaves
        // without anyone touching it. The authoring pass sets it true where §8 wants it.
        public bool isUnique;

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
