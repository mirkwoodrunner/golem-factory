using GolemFactory.Belts;

namespace GolemFactory.World
{
    // A static map deposit golems extract from via ExtractFromNode -- the real
    // replacement for M4's "every node is an infinite placeholder" hack (see
    // Golems/GolemEntity.cs's M4-era TryExtractFromNode). Quantity is finite by
    // default; pass Infinite for an unlimited node (e.g. the M2-era ScrapNode demo).
    public sealed class ResourceNode
    {
        public const int Infinite = -1;

        public string NodeId { get; }
        public string ItemType { get; }
        public int RemainingQuantity { get; private set; }

        public ResourceNode(string nodeId, string itemType, int remainingQuantity = Infinite)
        {
            NodeId = nodeId;
            ItemType = itemType;
            RemainingQuantity = remainingQuantity;
        }

        public bool IsDepleted => RemainingQuantity == 0;

        /// <summary>
        /// A truckload arriving (progression-design §13.2). Adds to a finite node's stock and
        /// returns what was actually added.
        ///
        /// <para>
        /// A DELIVERY TO AN INFINITE NODE IS A NO-OP, deliberately, rather than an error or a
        /// silent conversion to finite: <see cref="Infinite"/> is what Creative Mode leaves
        /// every stall on, and a market order placed in that state has nothing to deliver
        /// because the stall never runs out. That keeps one code path for both modes instead of
        /// a creative-only branch inside the market.
        /// </para>
        /// </summary>
        public int Deliver(int quantity)
        {
            if (quantity <= 0 || RemainingQuantity == Infinite)
            {
                return 0;
            }

            RemainingQuantity += quantity;
            return quantity;
        }

        public bool TryExtract(out ItemStack item)
        {
            if (IsDepleted)
            {
                item = default;
                return false;
            }

            if (RemainingQuantity > 0)
            {
                RemainingQuantity--;
            }

            item = new ItemStack { ItemType = ItemType };
            return true;
        }
    }
}
