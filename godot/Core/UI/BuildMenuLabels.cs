using System.Collections.Generic;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    /// <summary>
    /// What the compact build menu says (G10, from playtest: "the side bar for building is big.
    /// Could you make it smaller? Maybe have cost appear when hovering over? Also, add numbers as
    /// quick select?"). A row is now just its hotkey and a readable name; the cost and a line on
    /// what the building is for moved into a hover card.
    /// </summary>
    public static class BuildMenuLabels
    {
        /// <summary>Hotkeys in row order: 1-9, then 0, then - and =. Demolish has its own.</summary>
        public static readonly string[] RowKeys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

        public const string DemolishKey = "X";

        /// <summary>The hotkey for the row at <paramref name="index"/>, or "" past the twelfth.</summary>
        public static string KeyFor(int index) => index >= 0 && index < RowKeys.Length ? RowKeys[index] : "";

        // Names the camel-case split gets wrong or long.
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["GolemConstructionStationPrefab"] = "Golem Station",
            ["HandCrankBenchPrefab"] = "Hand-Crank Bench",
        };

        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["DepotPrefab"] = "A door into the stockpile: golems push into it and haul out of it.",
            ["GolemConstructionStationPrefab"] = "Builds golems. Another one lets you build in a second place.",
            ["BeltPrefab"] = "Carries goods a cell at a time. Drag to lay a run.",
            ["BeltSplitterPrefab"] = "Hands goods out to every belt leading away from it, in turn.",
            ["BoilerPrefab"] = "Burns Coke to power the golems beside it and along its pipes.",
            ["SteamPipePrefab"] = "Carries a boiler's steam to the golems beside it. Drag to lay a run.",
            ["ClockTowerPrefab"] = "The great work: four stages fed by your whole factory.",
            ["HandCrankBenchPrefab"] = "Crank simple recipes by hand. Hold E to crank, R to change recipe.",
            ["FreightMastPrefab"] = "A landing mast for Zeppelin freight between distant sites.",
            ["SlagHeapPrefab"] = "Burns Coke to get rid of Slag.",
            ["ScrapRecyclerPrefab"] = "Burns Coke to turn junk, Slag included, back into Scrap.",
        };

        /// <summary>The row's name: "Golem Station", "Belt Splitter", "Steam Pipe".</summary>
        public static string NameFor(string placeableKey)
        {
            if (string.IsNullOrEmpty(placeableKey))
            {
                return "";
            }
            if (Names.TryGetValue(placeableKey, out string name))
            {
                return name;
            }
            string bare = placeableKey.EndsWith("Prefab") && placeableKey.Length > "Prefab".Length
                ? placeableKey.Substring(0, placeableKey.Length - "Prefab".Length)
                : placeableKey;
            return ItemTiers.DisplayName(bare); // the same camel-case split item names use
        }

        /// <summary>One line on what the building is for, or "" for one this table has not met.</summary>
        public static string DescriptionFor(string placeableKey) =>
            placeableKey != null && Descriptions.TryGetValue(placeableKey, out string text) ? text : "";

        /// <summary>
        /// The hover card's cost line: "30 Scrap + 10 Iron Plate", "Free", or, short of it,
        /// "30 Scrap + 10 Iron Plate  ·  short 4 Iron Plate" -- the number the player acts on.
        /// </summary>
        public static string CostLine(IReadOnlyList<PunchCards.RecipeIngredient> cost, System.Func<string, int> stockOf, out bool affordable)
        {
            affordable = true;
            if (cost == null || cost.Count == 0)
            {
                return "Free";
            }

            var shortParts = new List<string>();
            foreach (PunchCards.RecipeIngredient c in cost)
            {
                int have = stockOf?.Invoke(c.itemType) ?? 0;
                if (have < c.quantity)
                {
                    shortParts.Add((c.quantity - have) + " " + ItemTiers.DisplayName(c.itemType));
                }
            }
            affordable = shortParts.Count == 0;
            string line = ConstructionCostPolicy.FormatCost(cost);
            return affordable ? line : line + "  ·  short " + string.Join(", ", shortParts);
        }

        public const string DemolishName = "Demolish";

        public const string DemolishDescription =
            "Removes a building or golem. You get back everything it cost and everything it held.";
    }
}
