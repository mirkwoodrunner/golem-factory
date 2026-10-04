using System;
using System.Collections.Generic;
using System.Text;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    /// <summary>
    /// Pure affordability arithmetic for the golem construction panel and the build menu: can
    /// this cost be paid, and if not, what exactly is missing.
    /// <para>
    /// Split out from the panels because "not enough resources" is a useless thing to tell a
    /// player -- they need to know *which* resource and *how much more*, which is a small
    /// piece of real logic worth testing without a scene. Mirrors StorageBufferRegistry
    /// .TryWithdrawBundle's own rule that a non-positive quantity is always payable, so a
    /// panel's preview can never disagree with what the withdrawal actually does.
    /// </para>
    /// <para>
    /// COSTS ARE BUNDLES, not a Scrap/Brass pair (docs/progression-design.md §6, §11 item 8).
    /// The pair could not express a single §6 chassis cost from the Brass Presser on. Stock is
    /// read through a <c>Func&lt;string,int&gt;</c> rather than by taking a StorageBuffer, so
    /// this stays engine-free and testable with a dictionary literal -- the same
    /// "extract the math into a pure function" idiom GridCoordinateConverter established.
    /// </para>
    /// </summary>
    public static class ConstructionCostPolicy
    {
        /// <summary>
        /// A null or empty bundle is free, and so is an entry with a non-positive quantity.
        /// This is the zero-cost rule the int-pair version had, restated: M1's default
        /// zero-cost PlaceableBuilding must stay placeable against a buffer nobody has ever
        /// deposited into.
        /// </summary>
        public static bool CanAfford(Func<string, int> stockOf, IReadOnlyList<RecipeIngredient> cost)
        {
            if (cost == null || cost.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < cost.Count; i++)
            {
                RecipeIngredient entry = cost[i];
                if (entry.quantity <= 0 || string.IsNullOrEmpty(entry.itemType))
                {
                    continue;
                }

                // A missing stock reader means "you have nothing", not "everything is free":
                // the withdrawal it previews would fail on any positive quantity.
                int held = stockOf != null ? stockOf(entry.itemType) : 0;
                if (held < entry.quantity)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The cost line shown on every row. Free is stated explicitly rather than shown as an
        /// empty string, so a zero-cost chassis doesn't look like a rendering failure.
        /// </summary>
        public static string FormatCost(IReadOnlyList<RecipeIngredient> cost) =>
            Join(cost, null, "Free", "  +  ");

        /// <summary>
        /// What the player still has to go and get. Returns an empty string when affordable,
        /// so the caller can use emptiness as the "no problem" signal instead of duplicating
        /// the CanAfford test.
        ///
        /// <para>
        /// EVERY short good is named, not just the first. A four-item chassis cost that reported
        /// only its first shortfall would send the player off for Casings and back again for
        /// Gears -- and §8's legibility row asks for the specific missing amount by name.
        /// </para>
        /// </summary>
        public static string FormatShortfall(Func<string, int> stockOf, IReadOnlyList<RecipeIngredient> cost)
        {
            string missing = Join(cost, stockOf, "", ", ");
            return string.IsNullOrEmpty(missing) ? "" : "Need " + missing;
        }

        // One formatter for both lines. With no stock reader it prints the cost as authored;
        // with one it prints only the outstanding remainder ("what you still owe"), which is
        // what makes the two strings provably consistent about quantities and ordering.
        private static string Join(
            IReadOnlyList<RecipeIngredient> cost, Func<string, int> stockOf,
            string emptyText, string separator)
        {
            if (cost == null || cost.Count == 0)
            {
                return emptyText;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < cost.Count; i++)
            {
                RecipeIngredient entry = cost[i];
                if (entry.quantity <= 0 || string.IsNullOrEmpty(entry.itemType))
                {
                    continue;
                }

                int amount = entry.quantity;
                if (stockOf != null)
                {
                    amount -= stockOf(entry.itemType);
                    if (amount <= 0)
                    {
                        continue;
                    }
                }

                if (builder.Length > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(amount).Append(' ');
                if (stockOf != null)
                {
                    builder.Append("more ");
                }

                builder.Append(DisplayName(entry.itemType));
            }

            return builder.Length == 0 ? emptyText : builder.ToString();
        }

        /// <summary>
        /// "CopperIngot" -> "Copper Ingot". Item ids are PascalCase bare strings by project
        /// convention, and printing them raw is the difference between a cost line that reads
        /// as English and one that reads as a serialization detail. Purely presentational --
        /// nothing round-trips through this.
        /// </summary>
        public static string DisplayName(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return "";
            }

            var builder = new StringBuilder(itemType.Length + 4);
            for (int i = 0; i < itemType.Length; i++)
            {
                char c = itemType[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(itemType[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
