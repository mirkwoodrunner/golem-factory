using System.Collections.Generic;
using System.Linq;
using GolemFactory.Save;
using GolemFactory.World;

namespace GolemFactory.Supply
{
    /// <summary>
    /// A save from before carts (docs/supply-design.md §5): its nine stalls come back as carts at
    /// rung 0 on their places, each holding the stall's saved stock, and a truckload still on the
    /// road is delivered into its stall's cart. A load never clamps, so a cart may hold more than
    /// its capacity; it simply doesn't trickle until it's below. The player loses nothing.
    /// </summary>
    public static class SupplyMigration
    {
        /// <summary>Said once, at the player and in the load's status line.</summary>
        public const string Notice =
            "The market sells carts now. Each stall is a cart that refills itself, holding what the stall held.";

        /// <summary>Whether a save's market names any stall: then it's a save from before carts.</summary>
        public static bool HasStalls(MarketEntry market) =>
            market != null && (market.stallIds.Count > 0 || market.cartStallIds.Count > 0);

        /// <summary>
        /// One cart for each old stall, with the stock the save gave it and the truckload it had
        /// on the road. A stall the save doesn't mention comes back empty, as it was at the start.
        /// </summary>
        public static List<SupplyCartEntry> CartsFor(MarketEntry market, IEnumerable<SandboxSetup.LegacyStall> stalls)
        {
            var stock = new Dictionary<string, long>();
            if (market != null)
            {
                for (int i = 0; i < market.stallIds.Count && i < market.stallStocks.Count; i++)
                {
                    Add(stock, market.stallIds[i], market.stallStocks[i]);
                }
                for (int i = 0; i < market.cartStallIds.Count && i < market.cartQuantities.Count; i++)
                {
                    Add(stock, market.cartStallIds[i], market.cartQuantities[i]);
                }
            }
            return (stalls ?? Enumerable.Empty<SandboxSetup.LegacyStall>()).Select(s => new SupplyCartEntry
            {
                x = s.x,
                y = s.y,
                itemType = s.itemType,
                stock = stock.TryGetValue(s.id ?? "", out long held) ? (int)System.Math.Min(held, int.MaxValue) : 0,
            }).ToList();
        }

        private static void Add(Dictionary<string, long> stock, string id, int quantity)
        {
            if (string.IsNullOrEmpty(id) || quantity <= 0)
            {
                return;
            }
            stock[id] = (stock.TryGetValue(id, out long held) ? held : 0) + quantity;
        }
    }
}
