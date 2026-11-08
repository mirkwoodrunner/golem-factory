using System.Collections.Generic;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Supply;

namespace GolemFactory.Overview
{
    /// <summary>One cart on the overview's market view: its good, its stock, and its flow.</summary>
    public readonly struct MarketRow
    {
        public readonly Vector2Int Cell;
        public readonly string ItemType;

        /// <summary>"Coal cart".</summary>
        public readonly string Name;

        /// <summary>"34 of 60"; "never runs out" for a Creative cart.</summary>
        public readonly string Holds;

        /// <summary>Stock over capacity, 0 to 1 (1 for a Creative cart).</summary>
        public readonly float Fill;

        /// <summary>Goods a minute over the last minute: brought by its trickle, and taken by golems and hands.</summary>
        public readonly float In;
        public readonly float Out;

        /// <summary>Its rung's rate, goods a minute: what it brings while it isn't full.</summary>
        public readonly int Rate;

        /// <summary>What is happening to it: "full", "filling", "keeping up", "running down", "empty · refilling".</summary>
        public readonly string State;

        /// <summary>True while it is taken from faster than it fills, or empty: the line that needs a look.</summary>
        public readonly bool Short;

        public MarketRow(Vector2Int cell, string itemType, string name, string holds, float fill, float @in, float @out,
            int rate, string state, bool isShort)
        {
            Cell = cell;
            ItemType = itemType;
            Name = name;
            Holds = holds;
            Fill = fill;
            In = @in;
            Out = @out;
            Rate = rate;
            State = state;
            Short = isShort;
        }
    }

    /// <summary>
    /// The overview's market view (docs/supply-design.md §4): every cart on the street, west to
    /// east, its stock and its goods in and out over the last minute (<see cref="CartFlow"/>).
    /// Composed fresh from the street whenever it is drawn; it holds nothing of its own. A cart
    /// taken from faster than it fills is marked short, which is a fact, not an alarm: a cart
    /// running down is waited on, grey (§4), and only a golem it starves goes red.
    /// </summary>
    public static class MarketReadout
    {
        public const string Full = "full";
        public const string Filling = "filling";
        public const string KeepingUp = "keeping up";
        public const string RunningDown = "running down";
        public const string Empty = "empty · refilling";
        public const string Endless = "never runs out";

        public static IReadOnlyList<MarketRow> Rows(SupplyStreet street)
        {
            if (street == null)
            {
                return new List<MarketRow>();
            }
            return street.Spots.Where(s => s.Cart != null).OrderBy(s => s.Cell.x).ThenBy(s => s.Cell.y)
                .Select(s => Row(s.Cell, s.Cart)).ToList();
        }

        public static MarketRow Row(Vector2Int cell, SupplyCart cart)
        {
            string name = SupplyText.CartName(cart.ItemType);
            if (cart.IsInfinite)
            {
                // Its stock never falls, so its flow isn't counted (CartFlow reads the stock).
                return new MarketRow(cell, cart.ItemType, name, Endless, 1f, 0f, 0f, cart.RatePerMinute, Endless, false);
            }
            float fill = cart.Capacity <= 0 ? 0f : System.Math.Min(1f, (float)cart.Stock / cart.Capacity);
            float @in = cart.Flow.InPerMinute;
            float @out = cart.Flow.OutPerMinute;
            string state;
            bool isShort;
            if (cart.Stock >= cart.Capacity)
            {
                state = Full;
                isShort = false;
            }
            else if (cart.Stock <= 0)
            {
                state = Empty;
                isShort = true;
            }
            else if (@in - @out < -Tolerance(cart))
            {
                state = RunningDown;
                isShort = true;
            }
            else if (@out > 0f && @in - @out <= Tolerance(cart))
            {
                state = KeepingUp;
                isShort = false;
            }
            else
            {
                state = Filling;
                isShort = false;
            }
            return new MarketRow(cell, cart.ItemType, name, $"{cart.Stock} of {cart.Capacity}", fill, @in, @out, cart.RatePerMinute, state, isShort);
        }

        /// <summary>
        /// How far in and out may differ, goods a minute, and still read as level: a tenth of its
        /// rate, at least one. A cart kept topped up by golems taking what it brings is keeping
        /// up, not running down because one good fell in the minute's last second.
        /// </summary>
        public static float Tolerance(SupplyCart cart) => System.Math.Max(1f, cart.RatePerMinute / 10f);

        /// <summary>"6 carts · 11 empty spots".</summary>
        public static string Summary(SupplyStreet street)
        {
            if (street == null)
            {
                return "";
            }
            int carts = street.Carts.Count();
            int empty = street.Spots.Count - carts;
            return $"{carts} cart{(carts == 1 ? "" : "s")} · {empty} empty spot{(empty == 1 ? "" : "s")}";
        }

        /// <summary>What the pointer says over a row: its rung's rate, and what to do when it is short.</summary>
        public static string Tooltip(MarketRow row)
        {
            if (row.State == Endless)
            {
                return "A Creative cart: it never runs out.";
            }
            string rate = $"It brings {row.Rate} a minute while it isn't full.";
            return row.Short
                ? rate + $" Taken faster than that, it runs down: press {Settings.KeyNames.Inventory} at the cart and buy Rate, or add another cart."
                : rate;
        }
    }
}
