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
