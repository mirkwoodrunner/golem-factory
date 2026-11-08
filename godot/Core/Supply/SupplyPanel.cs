using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Supply
{
    /// <summary>The player-facing lines about a cart. Never an id (root CLAUDE.md).</summary>
    public static class SupplyText
    {
        private const string Sep = InteractionTargeting.DetailSeparator;

        /// <summary>"84 of 200 · +60 a minute"; "200 of 200 · full"; Creative's "never runs out".</summary>
        public static string Holding(SupplyCart cart)
        {
            if (cart.IsInfinite)
            {
                return "never runs out";
            }
            string held = $"{cart.Stock} of {cart.Capacity}";
            return held + Sep + (cart.Stock >= cart.Capacity ? "full" : $"+{cart.RatePerMinute} a minute");
        }

        /// <summary>What a cart of this ladder gives when added: "30 a minute · holds 100".</summary>
        public static string Terms(CartLadder ladder) =>
            $"{ladder.Rates[0]} a minute" + Sep + $"holds {ladder.Capacities[0]}";

        public static string CartName(string itemType) => ItemTiers.DisplayName(itemType) + " cart";
    }

    /// <summary>The kinds of row the cart panel shows (docs/supply-design.md §2).</summary>
    public enum SupplyAction
    {
        Add = 0,
        Rate = 1,
        Capacity = 2,
        Change = 3,
        Remove = 4,
    }

    /// <summary>One row of the cart panel: what it does, what it costs, whether it can now.</summary>
    public sealed class SupplyRow
    {
        public SupplyAction Action { get; internal set; }

        /// <summary>The good an Add or Change row is for; null for the others.</summary>
        public string ItemType { get; internal set; }

        public string Label { get; internal set; }
        public string Detail { get; internal set; }

        /// <summary>The price, or what comes back for Remove, or "At the top".</summary>
        public string Price { get; internal set; }

        public bool Enabled { get; internal set; }

        /// <summary>Why it's greyed, when it is: the shortfall, a good not yet seen, a full pack.</summary>
        public string Note { get; internal set; }
    }

    /// <summary>
    /// The cart panel's rules (docs/supply-design.md §2): its title, its status line and its
    /// rows, built from the street and the wallet, and the press that runs a row. The node only
    /// draws these and forwards clicks, as the Crate screen does with its readout.
    /// </summary>
    public sealed class SupplyPanel
    {
        private readonly SupplyStreet _street;
        private readonly Func<PlayerWallet> _wallet;
        private readonly Func<string, bool> _seen;

        public SupplyPanel(SupplyStreet street, Func<PlayerWallet> wallet, Func<string, bool> seen)
        {
            _street = street;
            _wallet = wallet;
            _seen = seen ?? (_ => true);
        }

        public string Title(CartSpot spot) => spot?.Cart == null ? "Cart spot" : SupplyText.CartName(spot.Cart.ItemType);

        public string Status(CartSpot spot)
        {
            if (spot?.Cart == null)
            {
                return _street.Offers().Count == 0 ? "Nothing to sell here yet." : "Roped off. Add a cart here:";
            }
            return SupplyText.Holding(spot.Cart);
        }

        public IReadOnlyList<SupplyRow> Rows(CartSpot spot)
        {
            var rows = new List<SupplyRow>();
            if (spot == null)
            {
                return rows;
            }
            PlayerWallet wallet = _wallet();
            SupplyCart cart = spot.Cart;
            if (cart == null)
            {
                foreach (CartLadder ladder in _street.Offers())
                {
                    rows.Add(Priced(SupplyAction.Add, ladder.ItemType, SupplyText.CartName(ladder.ItemType),
                        SupplyText.Terms(ladder), ladder.AddPrice, wallet));
                }
                return rows;
            }

            IReadOnlyList<RecipeIngredient> rate = SupplyStreet.NextRateCost(cart);
            rows.Add(rate == null
                ? Top(SupplyAction.Rate, "Rate", $"{cart.RatePerMinute} a minute")
                : Priced(SupplyAction.Rate, null, "Rate",
                    $"{cart.RatePerMinute} to {cart.Ladder.Rates[cart.RateRung + 1]} a minute", rate, wallet));
            IReadOnlyList<RecipeIngredient> capacity = SupplyStreet.NextCapacityCost(cart);
            rows.Add(capacity == null
                ? Top(SupplyAction.Capacity, "Capacity", $"holds {cart.Capacity}")
                : Priced(SupplyAction.Capacity, null, "Capacity",
                    $"holds {cart.Capacity} to {cart.Ladder.Capacities[cart.CapacityRung + 1]}", capacity, wallet));

            bool kept = _street.IsLastKept(cart);
            foreach (CartLadder ladder in _street.Offers().Where(l => l.ItemType != cart.ItemType))
            {
                SupplyRow change = Priced(SupplyAction.Change, ladder.ItemType, "Change to " + SupplyText.CartName(ladder.ItemType),
                    "keeps its rungs", ladder.AddPrice, wallet);
                if (kept)
                {
                    Grey(change, KeptNote(cart));
                }
                else if (change.Enabled && wallet.FirstMisfit(Stock(cart)) is string full)
                {
                    Grey(change, "pack full of " + ItemTiers.DisplayName(full));
                }
                rows.Add(change);
            }

            List<RecipeIngredient> back = SupplyStreet.RemovalReturns(cart);
            var remove = new SupplyRow
            {
                Action = SupplyAction.Remove,
                Label = "Remove",
                Detail = "half its price back",
                Price = back.Count == 0 ? "nothing back" : "returns " + ConstructionCostPolicy.FormatCost(back),
                Enabled = true,
            };
            if (kept)
            {
                Grey(remove, KeptNote(cart));
            }
            else if (wallet.FirstMisfit(back) is string misfit)
            {
                Grey(remove, "pack full of " + ItemTiers.DisplayName(misfit));
            }
            rows.Add(remove);
            return rows;
        }

        /// <summary>Runs a row. The street refuses anything it shouldn't, whatever the row said.</summary>
        public SupplyResult Press(CartSpot spot, SupplyRow row)
        {
            if (spot == null || row == null)
            {
                return SupplyResult.Refused("Nothing to do.");
            }
            switch (row.Action)
            {
                case SupplyAction.Add: return _street.Add(spot, row.ItemType);
                case SupplyAction.Rate: return _street.UpgradeRate(spot);
                case SupplyAction.Capacity: return _street.UpgradeCapacity(spot);
                case SupplyAction.Change: return _street.Change(spot, row.ItemType);
                case SupplyAction.Remove: return _street.Remove(spot);
                default: return SupplyResult.Refused("Nothing to do.");
            }
        }

        private SupplyRow Priced(
            SupplyAction action, string itemType, string label, string detail,
            IReadOnlyList<RecipeIngredient> price, PlayerWallet wallet)
        {
            var row = new SupplyRow
            {
                Action = action,
                ItemType = itemType,
                Label = label,
                Detail = detail,
                Price = ConstructionCostPolicy.FormatCost(price),
                Enabled = true,
            };
            List<string> unseen = price.Where(p => p.quantity > 0 && !_seen(p.itemType))
                .Select(p => ItemTiers.DisplayName(p.itemType)).Distinct().ToList();
            if (unseen.Count > 0)
            {
                row.Price = "Requires: " + string.Join(", ", unseen);
                Grey(row, row.Price);
            }
            else if (!wallet.CanAfford(price))
            {
                Grey(row, ConstructionCostPolicy.FormatShortfall(wallet, price));
            }
            return row;
        }

        private static SupplyRow Top(SupplyAction action, string label, string detail) =>
            new SupplyRow { Action = action, Label = label, Detail = detail, Price = "At the top", Enabled = false, Note = "At the top" };

        private static void Grey(SupplyRow row, string note)
        {
            row.Enabled = false;
            row.Note = note;
        }

        private static string KeptNote(SupplyCart cart) => $"the street needs one {SupplyText.CartName(cart.ItemType)}";

        private static List<RecipeIngredient> Stock(SupplyCart cart) =>
            cart.IsInfinite || cart.Stock <= 0
                ? new List<RecipeIngredient>()
                : new List<RecipeIngredient> { new RecipeIngredient { itemType = cart.ItemType, quantity = cart.Stock } };
    }
}
