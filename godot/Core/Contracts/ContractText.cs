using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Contracts
{
    /// <summary>
    /// Everything the player reads about contracts (docs/contracts-design.md §5): the office's
    /// prompt, the notice, the HUD line, the crate's lines and the alerts. Plain ink throughout:
    /// a contract is never red (invariant 2), and every amount comes from the data (invariant 13).
    /// </summary>
    public static class ContractText
    {
        public const string OfficeName = "City Office";

        /// <summary>The won tower's panel (docs/endless-design.md §3): the game goes on, at the office.</summary>
        public const string EndlessInvitation = "The city has more work for you: the City Office, beside the tower.";

        /// <summary>"34 of 60 Glass".</summary>
        public static string Progress(ContractBoard board, RecipeIngredient ask) =>
            $"{Clamp(board.Delivered(ask.itemType), ask.quantity)} of {ask.quantity} {ItemTiers.DisplayName(ask.itemType)}";

        private static int Clamp(int n, int max) => n < max ? n : max;

        /// <summary>"60 Glass and 20 Lens".</summary>
        public static string Goods(IEnumerable<RecipeIngredient> goods) =>
            And(goods.Where(g => g.quantity > 0).Select(g => g.quantity + " " + ItemTiers.DisplayName(g.itemType)).ToList());

        /// <summary>"a Rate rung on a Coal cart".</summary>
        public static string Rung(string itemType, ContractRungKind kind) =>
            $"a {(kind == ContractRungKind.Capacity ? "Capacity" : "Rate")} rung on a {ItemTiers.DisplayName(itemType)} cart";

        /// <summary>"15 Gear", "15 Gear and a Rate rung on a Coal cart", "nothing".</summary>
        public static string Reward(ContractReward reward)
        {
            if (reward == null || reward.IsEmpty)
            {
                return "nothing";
            }
            var parts = reward.goods.Where(g => g.quantity > 0).Select(g => g.quantity + " " + ItemTiers.DisplayName(g.itemType)).ToList();
            if (reward.HasRung)
            {
                parts.Add(Rung(reward.rungGood, reward.rungKind));
            }
            return And(parts);
        }

        private static string And(IReadOnlyList<string> parts) =>
            parts.Count == 0 ? "nothing"
            : parts.Count == 1 ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];

        /// <summary>"6:00", "12:05".</summary>
        public static string Clock(int seconds)
        {
            int s = seconds < 0 ? 0 : seconds;
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // --- The office -------------------------------------------------------------------------

        /// <summary>
        /// The office's prompt detail: "City Watch · 34 of 40 Iron Plate", "City Watch has work",
        /// "No work today", "roped off".
        /// </summary>
        public static string Prompt(ContractBoard board)
        {
            if (board == null || !board.IsOpen)
            {
                return "roped off";
            }
            ContractDefinition c = board.Current;
            if (c == null)
            {
                return board.ShelfEmpty ? "No work today" : "a reward waits";
            }
            if (board.State == ContractState.Posted)
            {
                return c.requester + " has work";
            }
            RecipeIngredient line = c.asks.FirstOrDefault(a => board.Delivered(a.itemType) < a.quantity);
            return c.requester + " · " + Progress(board, line.itemType == null ? c.asks[0] : line);
        }

        /// <summary>What the rope says before the office opens: "The City Office opens once you make Slag."</summary>
        public static string Closed(ContractBoard board)
        {
            if (board == null || !board.Enabled)
            {
                return "The City Office is closed in Creative.";
            }
            return "The City Office opens once " + Awaited(board) + ".";
        }

        /// <summary>The notice while no contract is up: "No work today. The city will have more once you make Brass."</summary>
        public static string Waiting(ContractBoard board)
        {
            if (board.Next == null)
            {
                return "No work today. The city has asked for all it needs for now.";
            }
            return "No work today. The city will have more once " + Awaited(board) + ".";
        }

        // "you make Brass", "the Clock Tower's stage 2 begins", "the work comes in".
        private static string Awaited(ContractBoard board)
        {
            string good = board.WaitingForGood;
            if (good != null)
            {
                return "you make " + ItemTiers.DisplayName(good);
            }
            if (board.WaitingForTowerComplete)
            {
                return "the Clock Tower stands";
            }
            int stage = board.WaitingForTowerStage;
            if (stage == 1)
            {
                return "the Clock Tower's site opens";
            }
            return stage > 1
                ? "the Clock Tower's stage " + stage.ToString(CultureInfo.InvariantCulture) + " begins"
                : "the work comes in";
        }

        /// <summary>The notice's reward line: "Reward: 15 Gear".</summary>
        public static string RewardLine(ContractDefinition c) => "Reward: " + Reward(c.reward);

        /// <summary>
        /// The notice's bonus line, never red: "Bonus within 6:00 of taking it: ...", "Bonus if done
        /// in 4:12: ...", and once the par has passed, just "Bonus missed".
        /// </summary>
        public static string BonusLine(ContractBoard board)
        {
            ContractDefinition c = board.Current;
            if (c == null || c.bonus == null || c.bonus.IsEmpty)
            {
                return "";
            }
            if (board.State == ContractState.Posted)
            {
                return $"Bonus within {Clock(c.parSeconds)} of taking it: {Reward(c.bonus)}";
            }
            return board.WithinPar
                ? $"Bonus if done in {Clock(board.ParSecondsLeft)}: {Reward(c.bonus)}"
                : "Bonus missed";
        }

        /// <summary>The HUD's tower panel line while a contract is up: "Contract · City Watch · 34 of 40 Iron Plate".</summary>
        public static string HudLine(ContractBoard board)
        {
            if (board == null || board.Current == null)
            {
                return "";
            }
            return board.State == ContractState.Posted
                ? "Contract · " + board.Current.requester + " has work at the City Office"
                : "Contract · " + Prompt(board);
        }

        /// <summary>"Waiting for you: 12 Gear" for goods the pack couldn't take.</summary>
        public static string ShelfGoodsLine(ContractBoard board) =>
            board.ShelfGoods.Count == 0 ? "" : "Waiting for you: " + Goods(board.ShelfGoods);

        /// <summary>"A Rate rung for a Coal cart: pick the cart".</summary>
        public static string ShelfRungLine(ShelfRung rung) =>
            $"{(rung.Kind == ContractRungKind.Capacity ? "A Capacity" : "A Rate")} rung for a {ItemTiers.DisplayName(rung.ItemType)} cart";

        /// <summary>Why a shelved rung can't go on any cart yet.</summary>
        public static string NoCartFor(ShelfRung rung) =>
            $"No {ItemTiers.DisplayName(rung.ItemType)} cart below its top rung: it waits here until there is one.";

        // --- The office's crate -----------------------------------------------------------------

        /// <summary>The crate's line: "Takes 26 more Glass and 20 more Lens".</summary>
        public static string CrateLine(ContractBoard board)
        {
            if (board?.Current == null)
            {
                return "Takes nothing: there is no contract";
            }
            if (board.State != ContractState.Accepted)
            {
                return "Takes nothing until you take the contract (E at the office)";
            }
            var more = board.Current.asks.Where(a => board.Remaining(a.itemType) > 0)
                .Select(a => board.Remaining(a.itemType) + " more " + ItemTiers.DisplayName(a.itemType)).ToList();
            return more.Count == 0 ? "Has all it asked for" : "Takes " + And(more);
        }

        /// <summary>Why the crate won't take a good, or null when it will.</summary>
        public static string CrateRefusal(ContractBoard board, string itemType)
        {
            ContractDefinition c = board?.Current;
            if (c == null)
            {
                return "There is no contract";
            }
            if (board.State != ContractState.Accepted)
            {
                return "Take the contract first";
            }
            if (!c.AsksFor(itemType))
            {
                return "The contract asks for " + And(c.asks.Select(a => ItemTiers.DisplayName(a.itemType)).ToList());
            }
            return board.OfficeBuffer.RoomFor(itemType) <= 0
                ? "It has all the " + ItemTiers.DisplayName(itemType) + " it asked for"
                : null;
        }

        // --- Alerts -----------------------------------------------------------------------------

        /// <summary>The overview's alert line for a contract's moment.</summary>
        public static string Alert(Events.ContractEvent e)
        {
            switch (e.Kind)
            {
                case Events.ContractEventKind.Posted:
                    return "The " + e.Requester + " posted a contract at the City Office";
                case Events.ContractEventKind.Completed:
                    return "Contract done for the " + e.Requester + (e.BonusEarned ? " · bonus earned" : "");
                default:
                    return "You took the " + e.Requester + "'s contract";
            }
        }
    }
}
