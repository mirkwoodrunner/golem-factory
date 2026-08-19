using System;
using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    /// <summary>
    /// What the Assembly Line tab's bay row says, and whether its button does anything. Pure,
    /// so the wording and the affordability rule are testable without a Canvas -- the same
    /// split <see cref="ConstructionCostPolicy"/> and <c>BufferTrendUtility</c> follow.
    ///
    /// <para>
    /// This row exists because §8's concurrent-golem cap went into the loop with no way to
    /// raise it: the station refused past ten golems and <c>AssemblyBayStructure.TryUpgrade</c>
    /// had no caller anywhere in the game, so the cap was a wall rather than a decision. §9's
    /// phases 4-6 need ~18 golems.
    /// </para>
    /// </summary>
    public static class AssemblyBayRowPolicy
    {
        /// <summary>
        /// The occupancy readout, e.g. <c>"Bays 7/10"</c>. Occupied first, because the number
        /// that changes is the one the player is watching.
        /// </summary>
        public static string FormatOccupancy(int occupied, int capacity) =>
            "Bays " + occupied + "/" + capacity;

        /// <summary>
        /// What one upgrade buys, e.g. <c>"+6 slots"</c>. Stated on the row rather than left to
        /// be inferred from the cap changing after the fact -- this is a purchase, and a
        /// purchase whose effect is only visible afterwards is a gamble.
        /// </summary>
        public static string FormatUpgradeEffect(int slotsPerUpgrade) =>
            "+" + slotsPerUpgrade + (slotsPerUpgrade == 1 ? " slot" : " slots");

        /// <summary>
        /// The cost column. Delegates to <see cref="ConstructionCostPolicy.FormatCost"/> rather
        /// than restating it, so the bay's price is written the same way a chassis's and a
        /// building's are -- three different spellings of "40 Scrap + 20 Iron Plate" would be
        /// three places to drift.
        /// </summary>
        public static string FormatCost(IReadOnlyList<RecipeIngredient> upgradeCost) =>
            ConstructionCostPolicy.FormatCost(upgradeCost);

        /// <summary>
        /// Whether the Upgrade button is live. Same reasoning as the Claim button beside it:
        /// non-interactable when unaffordable rather than clickable-but-always-refused, since
        /// <c>TryUpgrade</c> already refuses and the click was only ever a way to be told
        /// something the row could have said up front.
        /// </summary>
        public static bool CanAfford(Func<string, int> stockOf, IReadOnlyList<RecipeIngredient> upgradeCost) =>
            ConstructionCostPolicy.CanAfford(stockOf, upgradeCost);

        /// <summary>
        /// The line under the row after a click. Empty on success -- the readout itself has
        /// already changed, and a "done!" message the player has to dismiss adds nothing.
        /// </summary>
        public static string DescribeResult(
            bool upgraded, Func<string, int> stockOf, IReadOnlyList<RecipeIngredient> upgradeCost,
            int newCapacity)
        {
            if (upgraded)
            {
                return "";
            }

            // Names the shortfall by good and amount, exactly as a refused chassis does. A bare
            // "cannot afford" on a two-good bundle sends the player to look at their stockpile
            // to work out which half they are missing.
            string shortfall = ConstructionCostPolicy.FormatShortfall(stockOf, upgradeCost);
            return string.IsNullOrEmpty(shortfall)
                ? "Bay upgrade unavailable."
                : shortfall + " to add " + newCapacity + " bay slots.";
        }
    }
}
