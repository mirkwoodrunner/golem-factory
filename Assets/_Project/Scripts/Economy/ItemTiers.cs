using System.Collections.Generic;
using System.Text;

namespace GolemFactory.Economy
{
    /// <summary>
    /// How deep a good sits in the economy, and what to call it on screen.
    ///
    /// <para>
    /// <b>This is a transcription, not a second source of truth.</b> The grouping is already
    /// written down in <see cref="ItemType"/>'s comments -- Tier 0 raw, Tier 1 basic processing,
    /// Tier 2 metals, Tier 3 components, Tier 4 mechanisms, Tier 5 megaproject -- taken verbatim
    /// from docs/progression-design.md §5.1. What it did not have was a way for CODE to ask.
    /// <c>ItemTiersTests</c> pins every <see cref="ItemType"/> constant to a tier, so a 25th good
    /// cannot be added without deciding where it sits.
    /// </para>
    ///
    /// <para>
    /// Two systems need this and they need it for different reasons, which is why it is its own
    /// file rather than a private table inside either. The Scrap Recycler prices a good BY its
    /// depth (Buildings/ScrapRecycler), and the Artificer's Ledger shows a good's depth as
    /// context. A private copy in each would be two tables to keep in step.
    /// </para>
    ///
    /// <para>
    /// Engine-free static, like <see cref="ItemType"/> beside it and every other pure rule in
    /// this project.
    /// </para>
    /// </summary>
    public static class ItemTiers
    {
        /// <summary>Returned for an item type this table has never heard of.</summary>
        public const int Unknown = -1;

        /// <summary>The deepest tier §5.1 defines. Tier 5 goods are terminal in the Clock Tower.</summary>
        public const int MaxTier = 5;

        // Ordered by tier, and within a tier in the order §5.1 lists them. That ordering is
        // load-bearing for CanonicalOrder below, which is what makes a depot's filter cycle read
        // as "shallow goods first" rather than as dictionary order.
        private static readonly string[] Order =
        {
            // Tier 0 -- raw, node-extracted, hand-harvestable
            ItemType.Scrap, ItemType.Coal, ItemType.CopperOre, ItemType.ZincOre, ItemType.Aether,
            // Tier 1 -- basic processing, 1 input
            ItemType.Coke, ItemType.IronPlate, ItemType.Slag, ItemType.Glass,
            // Tier 2 -- metals
            ItemType.CopperIngot, ItemType.ZincIngot, ItemType.Brass, ItemType.CopperWire,
            // Tier 3 -- components
            ItemType.Gear, ItemType.Casing, ItemType.Lens, ItemType.Mainspring, ItemType.AetherCell,
            // Tier 4 -- mechanisms
            ItemType.Mechanism, ItemType.Regulator,
            // Tier 5 -- megaproject goods
            ItemType.FrameSection, ItemType.GreatCog, ItemType.AetherConduit, ItemType.ChronometerCore,
        };

        private static readonly Dictionary<string, int> TierByType = BuildTierIndex();

        private static readonly Dictionary<string, int> PositionByType = BuildPositionIndex();

        /// <summary>
        /// Every good §5.1 defines, shallowest first. The canonical ordering for any list of item
        /// types the player reads -- a filter cycle, a ledger column, an inventory panel.
        /// </summary>
        public static IReadOnlyList<string> CanonicalOrder => Order;

        /// <summary>
        /// This good's tier, or <see cref="Unknown"/> for an id no table entry names.
        ///
        /// <para>
        /// <b>Unknown is a refusal, not a default.</b> Callers that price a good by tier must
        /// treat it as "I do not handle this" rather than substituting a tier -- silently valuing
        /// an unrecognised good is how a future item becomes an exploit.
        /// </para>
        /// </summary>
        public static int TierOf(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return Unknown;
            }

            return TierByType.TryGetValue(itemType, out int tier) ? tier : Unknown;
        }

        public static bool IsKnown(string itemType) => TierOf(itemType) != Unknown;

        /// <summary>
        /// Where this good sits in <see cref="CanonicalOrder"/>, or <see cref="int.MaxValue"/> for
        /// an unknown one -- so a sort puts strangers at the end rather than at the front.
        /// </summary>
        public static int CanonicalPosition(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return int.MaxValue;
            }

            return PositionByType.TryGetValue(itemType, out int position) ? position : int.MaxValue;
        }

        /// <summary>
        /// The id as a human reads it: <c>IronPlate</c> becomes <c>Iron Plate</c>.
        ///
        /// <para>
        /// Derived from the id rather than authored as a second table on purpose. The ids ARE
        /// camel-cased English (§5.1 chose them that way), so a lookup table would be 24 entries
        /// that could only ever go out of step with the constants beside them. An unknown id
        /// splits the same way and comes back readable, which is the right answer for a good this
        /// table has not been taught yet.
        /// </para>
        /// </summary>
        public static string DisplayName(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return "";
            }

            var text = new StringBuilder(itemType.Length + 4);
            for (int i = 0; i < itemType.Length; i++)
            {
                char c = itemType[i];
                // A space goes before an upper-case letter that follows a lower-case one, so
                // "IronPlate" splits and an all-caps id or a leading capital does not.
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(itemType[i - 1]))
                {
                    text.Append(' ');
                }

                text.Append(c);
            }

            return text.ToString();
        }

        private static Dictionary<string, int> BuildTierIndex()
        {
            // Tier boundaries, as counts, matching the grouped rows in Order above. Written as
            // sizes rather than as a per-item literal so adding a good means editing one row in
            // two places that are three lines apart, not twenty-four scattered entries.
            int[] sizes = { 5, 4, 4, 5, 2, 4 };

            var index = new Dictionary<string, int>(Order.Length);
            int position = 0;
            for (int tier = 0; tier < sizes.Length; tier++)
            {
                for (int i = 0; i < sizes[tier]; i++)
                {
                    index[Order[position]] = tier;
                    position++;
                }
            }

            return index;
        }

        private static Dictionary<string, int> BuildPositionIndex()
        {
            var index = new Dictionary<string, int>(Order.Length);
            for (int i = 0; i < Order.Length; i++)
            {
                index[Order[i]] = i;
            }

            return index;
        }
    }
}
