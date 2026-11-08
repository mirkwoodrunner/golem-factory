using System.Collections.Generic;
using GolemFactory.Economy;

namespace GolemFactory.Supply
{
    /// <summary>
    /// Which sprite a spot shows (docs/supply-design.md §1): the roped-off spot, or its cart in
    /// the market-cart art of its good. The node only loads the name.
    /// </summary>
    public static class SupplyArt
    {
        public const string SpotSprite = "cart_spot";

        private static readonly Dictionary<string, string> Carts = new Dictionary<string, string>
        {
            { ItemType.Scrap, "stall_scrap" },
            { ItemType.Coal, "stall_coal" },
            { ItemType.CopperOre, "stall_copper_ore" },
            { ItemType.ZincOre, "stall_zinc_ore" },
            { ItemType.Aether, "stall_aether" },
        };

        /// <summary>A cart's sprite by its good; a good with no cart art shows the Scrap cart.</summary>
        public static string CartSprite(string itemType) =>
            itemType != null && Carts.TryGetValue(itemType, out string name) ? name : "stall_scrap";

        public static string SpriteFor(CartSpot spot) =>
            spot?.Cart == null ? SpotSprite : CartSprite(spot.Cart.ItemType);
    }
}
