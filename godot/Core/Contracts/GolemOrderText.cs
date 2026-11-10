using System.Collections.Generic;
using GolemFactory.Economy;

namespace GolemFactory.Contracts
{
    /// <summary>
    /// What the player reads about golem orders (docs/golem-sales-design.md §4): the notice's
    /// lines, the HUD's, the office's sale prompt and the alerts. Plain ink: an order is never red,
    /// and every amount comes from the data.
    /// </summary>
    public static class GolemOrderText
    {
        /// <summary>"Brass Pressers".</summary>
        public static string Plural(string chassis) => ItemTiers.DisplayName(chassis) + "s";

        /// <summary>"3 Brass Pressers", "1 Brass Presser".</summary>
        public static string Golems(string chassis, int count) =>
            count + " " + (count == 1 ? ItemTiers.DisplayName(chassis) : Plural(chassis));

        /// <summary>"Golem order · Tramway Company": the notice's heading.</summary>
        public static string Heading(GolemOrder order) => "Golem order · " + order.Requester;

        /// <summary>"Wants 3 Brass Pressers · 1 delivered".</summary>
        public static string Asks(GolemOrderBoard board) =>
            "Wants " + Golems(board.Current.Chassis, board.Current.Count) + " · " + board.Delivered + " delivered";

        /// <summary>"Reward: 40 Gear, more for golems with cards".</summary>
        public static string RewardLine(GolemOrderBoard board)
        {
            List<PunchCards.RecipeIngredient> goods = board.BaseReward;
            string paid = goods.Count == 0 ? "nothing" : ContractText.Goods(goods);
            return "Reward: " + paid + ", more for golems with cards";
        }

        /// <summary>"Carry one here and press E, or build a Golem Foundry": how an order is filled, naming the device's key.</summary>
        public static string HowTo =>
            $"Carry a golem here and press {Settings.KeyNames.Interact} to sell it, or build a Golem Foundry to make them.";

        /// <summary>The HUD's line while an order is open: "Golem order · 1 of 3 Brass Pressers".</summary>
        public static string HudLine(GolemOrderBoard board) =>
            board?.Current == null ? "" : "Golem order · " + board.Delivered + " of " + Golems(board.Current.Chassis, board.Current.Count);

        /// <summary>The office's prompt while carrying a golem the order wants: "sell Presser #4 to the Tramway Company".</summary>
        public static string SellPrompt(string golemName, GolemOrder order) =>
            "[" + Settings.KeyNames.Interact + "] sell " + golemName + " to the " + order.Requester;

        /// <summary>"Sold Presser #4 to the Tramway Company · 2 of 3".</summary>
        public static string Sold(string golemName, GolemOrder order, int delivered) =>
            "Sold " + golemName + " to the " + order.Requester + " · " + delivered + " of " + order.Count;

        /// <summary>"Order done for the Tramway Company".</summary>
        public static string SoldLast(string golemName, string requester) =>
            "Sold " + golemName + " · order done for the " + requester;
    }
}
