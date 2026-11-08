using System.Collections.Generic;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Supply;

namespace GolemFactory.Contracts
{
    /// <summary>What a row of the office's notice does (docs/contracts-design.md §2, §4).</summary>
    public enum ContractAction
    {
        /// <summary>Take the posted contract.</summary>
        Accept = 0,

        /// <summary>The shelf's goods into the pack.</summary>
        Collect = 1,

        /// <summary>A shelved rung onto one cart.</summary>
        Rung = 2,
    }

    /// <summary>One row of the notice: what it does, what it says, whether it can now.</summary>
    public sealed class ContractRow
    {
        public ContractAction Action { get; internal set; }

        /// <summary>The good whose icon the row shows.</summary>
        public string ItemType { get; internal set; }

        public string Label { get; internal set; }
        public string Detail { get; internal set; }

        /// <summary>The right-hand column: "Take it", "free".</summary>
        public string Price { get; internal set; }

        public bool Enabled { get; internal set; }

        /// <summary>Why it's greyed, when it is.</summary>
        public string Note { get; internal set; }

        /// <summary>For a rung row: which shelved rung, and the cart it would go on (null when there is none yet).</summary>
        public int RungIndex { get; internal set; } = -1;
        public CartSpot Spot { get; internal set; }
    }

    /// <summary>
    /// The office's notice (docs/contracts-design.md §2): its title, the requester's words, the
    /// lines asked with what is delivered, the reward and the bonus, and the rows the player can
    /// press: take the contract, collect the shelf, put a rung on a cart. The node only draws
    /// these and forwards clicks, as the cart panel does with <see cref="SupplyPanel"/>.
    /// </summary>
    public sealed class ContractPanel
    {
        private readonly ContractBoard _board;

        public ContractPanel(ContractBoard board) => _board = board;

        public ContractBoard Board => _board;

        /// <summary>Who asks, or the office's own name while nothing is posted.</summary>
        public string Title => _board.Current?.requester ?? ContractText.OfficeName;

        /// <summary>Their words, or the waiting line.</summary>
        public string Words => _board.Current?.words ?? ContractText.Waiting(_board);

        /// <summary>The goods asked: "34 of 60 Glass", one line each. Empty while waiting.</summary>
        public IReadOnlyList<string> AskLines =>
            _board.Current == null
                ? new List<string>()
                : _board.Current.asks.Select(a => ContractText.Progress(_board, a)).ToList();

        /// <summary>The reward and bonus lines, while a contract is up.</summary>
        public string RewardLine => _board.Current == null ? "" : ContractText.RewardLine(_board.Current);

        public string BonusLine => ContractText.BonusLine(_board);

        /// <summary>How to fill it, once taken.</summary>
        public string HowLine =>
            _board.State == ContractState.Accepted
                ? "Fill it however you like: press I here to put goods in by hand, or have a golem or a belt deliver to the office."
                : _board.State == ContractState.Posted
                    ? "Take it, and the office's crate takes what it asks for. There is no deadline."
                    : "";

        public IReadOnlyList<ContractRow> Rows()
        {
            var rows = new List<ContractRow>();
            ContractDefinition c = _board.Current;
            if (c != null && _board.State == ContractState.Posted)
            {
                rows.Add(new ContractRow
                {
                    Action = ContractAction.Accept,
                    ItemType = c.asks[0].itemType,
                    Label = "Take the contract",
                    Detail = ContractText.Goods(c.asks),
                    Price = "Take it",
                    Enabled = true,
                });
            }
            IReadOnlyList<RecipeIngredient> shelf = _board.ShelfGoods;
            if (shelf.Count > 0)
            {
                rows.Add(new ContractRow
                {
                    Action = ContractAction.Collect,
                    ItemType = shelf[0].itemType,
                    Label = "Collect your reward",
                    Detail = ContractText.Goods(shelf),
                    Price = "into your pack",
                    Enabled = true,
                });
            }
            for (int i = 0; i < _board.ShelfRungs.Count; i++)
            {
                ShelfRung rung = _board.ShelfRungs[i];
                IReadOnlyList<CartSpot> carts = _board.CartsFor(rung);
                if (carts.Count == 0)
                {
                    rows.Add(new ContractRow
                    {
                        Action = ContractAction.Rung,
                        ItemType = rung.ItemType,
                        Label = ContractText.ShelfRungLine(rung),
                        Detail = "waits here",
                        Price = "free",
                        Enabled = false,
                        Note = ContractText.NoCartFor(rung),
                        RungIndex = i,
                    });
                    continue;
                }
                foreach (CartSpot spot in carts)
                {
                    rows.Add(new ContractRow
                    {
                        Action = ContractAction.Rung,
                        ItemType = rung.ItemType,
                        Label = ContractText.ShelfRungLine(rung),
                        Detail = "on this " + SupplyText.CartName(rung.ItemType) + ": " + RungTerms(spot.Cart, rung.Kind),
                        Price = "free",
                        Enabled = true,
                        RungIndex = i,
                        Spot = spot,
                    });
                }
            }
            return rows;
        }

        // "30 to 60 a minute", "holds 100 to 200": what this cart's next rung gives.
        private static string RungTerms(SupplyCart cart, ContractRungKind kind) =>
            kind == ContractRungKind.Rate
                ? $"{cart.RatePerMinute} to {cart.Ladder.Rates[cart.RateRung + 1]} a minute"
                : $"holds {cart.Capacity} to {cart.Ladder.Capacities[cart.CapacityRung + 1]}";

        /// <summary>Runs a row; the board refuses anything it shouldn't. Returns the status line.</summary>
        public string Press(ContractRow row)
        {
            if (row == null || !row.Enabled)
            {
                return row?.Note ?? "";
            }
            switch (row.Action)
            {
                case ContractAction.Accept:
                    return _board.Accept() ? "Taken. The office's crate takes what it asks for." : "Nothing to take.";
                case ContractAction.Collect:
                {
                    List<RecipeIngredient> moved = _board.CollectGoods();
                    if (moved.Count == 0)
                    {
                        return "Your pack is full: it waits here.";
                    }
                    string said = "Moved " + ContractText.Goods(moved) + " to your pack.";
                    return _board.ShelfGoods.Count > 0 ? said + " The rest waits here." : said;
                }
                case ContractAction.Rung:
                    return _board.ApplyRung(row.RungIndex, row.Spot).Message;
                default:
                    return "";
            }
        }
    }
}
