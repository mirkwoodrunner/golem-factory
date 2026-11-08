using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GolemFactory.Contracts;
using GolemFactory.Economy;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// What the player reads about a Golem Foundry (docs/golem-sales-design.md §2.2): its hover
    /// and prompt line, the Crate screen's cap line and refusals. Plain ink: waiting is never red,
    /// and every amount comes from the data.
    /// </summary>
    public static class FoundryText
    {
        /// <summary>
        /// "Waiting for a golem order", "Brass Presser · needs 40 Scrap and 5 Gear",
        /// "Building a Brass Presser · 45%", "A Brass Presser waits for an order of its kind".
        /// </summary>
        public static string CapLine(PlaceableGolemFoundry foundry)
        {
            switch (foundry.State)
            {
                case FoundryState.Holding:
                    return "A " + ItemTiers.DisplayName(foundry.Chassis) + " waits for an order of its kind";
                case FoundryState.Building:
                {
                    long total = foundry.BuildTicks <= 0 ? 1 : foundry.BuildTicks;
                    int percent = (int)(foundry.ProgressTicks * 100 / total);
                    return "Building a " + ItemTiers.DisplayName(foundry.Chassis) + " · " + percent.ToString(CultureInfo.InvariantCulture) + "%";
                }
                case FoundryState.Short:
                {
                    List<PunchCards.RecipeIngredient> missing = foundry.Missing();
                    return ItemTiers.DisplayName(foundry.OrderedChassis) + " · needs " + ContractText.Goods(missing);
                }
                default:
                    return "Waiting for a golem order at the City Office";
            }
        }

        /// <summary>Why a good can't go in: "There is no golem order", "A Brass Presser takes Scrap, Iron Plate and Gear".</summary>
        public static string Refusal(PlaceableGolemFoundry foundry, string itemType)
        {
            string chassis = foundry.OrderedChassis;
            if (chassis == null)
            {
                return "There is no golem order";
            }
            if (foundry.Needs(itemType) <= 0)
            {
                return "A " + ItemTiers.DisplayName(chassis) + " takes "
                    + string.Join(", ", foundry.Cost.Select(c => ItemTiers.DisplayName(c.itemType)));
            }
            return "It holds all the " + ItemTiers.DisplayName(itemType) + " a " + ItemTiers.DisplayName(chassis) + " takes";
        }
    }
}
