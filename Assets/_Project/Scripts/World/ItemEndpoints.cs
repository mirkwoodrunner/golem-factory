using GolemFactory.Belts;
using GolemFactory.Economy;

namespace GolemFactory.World
{
    // Adapters wrapping the three existing item-bearing systems as spatial endpoints.
    //
    // All three live here in World/ rather than next to the types they wrap, on purpose:
    // Belts/ must keep having no reverse reference to Golems/ or to the spatial layer
    // (see the architecture notes), so the belt adapter wraps BeltSegment strictly from the
    // outside using only its existing public surface. Nothing in Belts/ or Economy/ changed.

    /// <summary>
    /// A map deposit. Take-only: you cannot push an item back into the ground.
    /// </summary>
    public sealed class ResourceNodeEndpoint : IItemEndpoint
    {
        private readonly ResourceNode _node;

        public ResourceNodeEndpoint(ResourceNode node) => _node = node;

        public ResourceNode Node => _node;

        public string DisplayName => _node != null ? _node.NodeId : "node";

        public bool TryTake(out ItemStack item)
        {
            if (_node == null)
            {
                item = default;
                return false;
            }

            return _node.TryExtract(out item);
        }

        // A node holds exactly one type for its whole life, so peeking is just reading it --
        // but a depleted node must report null, not its type, or a golem would keep committing
        // to an Extract that can never yield anything.
        public string PeekAvailableType() =>
            _node != null && !_node.IsDepleted ? _node.ItemType : null;

        public bool TryTake(string itemType, int quantity, out int taken)
        {
            taken = 0;
            if (_node == null || string.IsNullOrEmpty(itemType) || quantity <= 0)
            {
                return false;
            }

            // A node cannot hand over a type it isn't made of. Checked once up front rather
            // than per unit because ItemType never changes.
            if (_node.ItemType != itemType)
            {
                return false;
            }

            // Loops TryExtract rather than decrementing RemainingQuantity directly, so finite
            // depletion stays the node's business and a partially-drained node yields what it
            // has instead of all-or-nothing failing.
            while (taken < quantity && _node.TryExtract(out ItemStack _))
            {
                taken++;
            }

            return taken > 0;
        }

        // A node is a pure source; it never accepts. CanGive is false rather than throwing so
        // a golem facing the wrong way stalls cleanly instead of crashing the tick loop.
        public bool CanGive() => false;

        public bool TryGive(ItemStack item) => false;
    }

    /// <summary>
    /// One conveyor lane. Takes from the head (only once an item has actually travelled the
    /// full length, per BeltSegment.TryPeekHead) and gives at the tail.
    /// </summary>
    public sealed class BeltSegmentEndpoint : IItemEndpoint
    {
        private readonly BeltSegment _segment;

        public BeltSegmentEndpoint(BeltSegment segment) => _segment = segment;

        public BeltSegment Segment => _segment;

        public string DisplayName => _segment != null ? _segment.SegmentId : "belt";

        public bool TryTake(out ItemStack item)
        {
            if (_segment == null)
            {
                item = default;
                return false;
            }

            return _segment.TryRemoveHead(out item);
        }

        // Goes through TryPeekHead, NOT _segment.Items[0], so the "has actually travelled the
        // full length" rule (BeltSegment.TryPeekHead: Progress >= Length) is honoured exactly
        // as TryRemoveHead honours it. Reading Items[0] directly would report an item that is
        // still in transit as available, and a Haul would then appear to stall for no visible
        // reason one tick and succeed the next.
        public string PeekAvailableType()
        {
            if (_segment == null || !_segment.TryPeekHead(out ItemStack head))
            {
                return null;
            }

            return head.ItemType;
        }

        public bool TryTake(string itemType, int quantity, out int taken)
        {
            taken = 0;
            if (_segment == null || string.IsNullOrEmpty(itemType) || quantity <= 0)
            {
                return false;
            }

            // A belt is ordered, so this can only take a contiguous run of matching heads --
            // it stops at the first head of a different type rather than reaching past it.
            // That is the correct rigid behaviour: a mixed belt is a player-visible problem,
            // not something a golem is allowed to reorder its way out of.
            while (taken < quantity &&
                   _segment.TryPeekHead(out ItemStack head) &&
                   head.ItemType == itemType &&
                   _segment.TryRemoveHead(out ItemStack _))
            {
                taken++;
            }

            return taken > 0;
        }

        public bool CanGive() => _segment != null && _segment.CanEnqueue();

        public bool TryGive(ItemStack item) => _segment != null && _segment.TryEnqueue(item);
    }

    /// <summary>
    /// A storage buffer. Both directions. Withdrawing needs an item type, and the endpoint
    /// contract has no way to ask for one, so a take pulls from <see cref="PreferredItemType"/>
    /// when set and otherwise from whatever the buffer currently holds most recently -- good
    /// enough for a Haul step, which is type-agnostic by design.
    /// </summary>
    public sealed class StorageBufferEndpoint : IItemEndpoint
    {
        private readonly StorageBuffer _buffer;

        public StorageBufferEndpoint(StorageBuffer buffer) => _buffer = buffer;

        public StorageBuffer Buffer => _buffer;

        /// <summary>Optional item type this endpoint prefers to dispense on TryTake.</summary>
        public string PreferredItemType { get; set; }

        public string DisplayName => _buffer != null ? _buffer.BufferId : "buffer";

        public bool TryTake(out ItemStack item)
        {
            item = default;
            if (_buffer == null)
            {
                return false;
            }

            string type = ResolveTakeableType();
            if (type == null)
            {
                return false;
            }

            if (!_buffer.TryWithdraw(type))
            {
                return false;
            }

            item = new ItemStack { ItemType = type };
            return true;
        }

        // Reuses ResolveTakeableType so "what would an untyped take give me" and "what does
        // this endpoint currently offer" can never disagree.
        public string PeekAvailableType() => _buffer != null ? ResolveTakeableType() : null;

        public bool TryTake(string itemType, int quantity, out int taken)
        {
            taken = 0;
            if (_buffer == null || string.IsNullOrEmpty(itemType) || quantity <= 0)
            {
                return false;
            }

            int held = _buffer.GetQuantity(itemType);
            int wanted = quantity < held ? quantity : held;
            if (wanted <= 0)
            {
                return false;
            }

            // One withdrawal of the clamped amount, not a loop: StorageBuffer.TryWithdraw is
            // all-or-nothing, and clamping to what's held first is what turns that into the
            // partial take the contract promises.
            if (!_buffer.TryWithdraw(itemType, wanted))
            {
                return false;
            }

            taken = wanted;
            return true;
        }

        private string ResolveTakeableType()
        {
            if (!string.IsNullOrEmpty(PreferredItemType) && _buffer.GetQuantity(PreferredItemType) > 0)
            {
                return PreferredItemType;
            }

            foreach (var pair in _buffer.Quantities)
            {
                if (pair.Value > 0)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        // StorageBuffer has no capacity model at all (Deposit always succeeds), so a buffer
        // always has room. Stated explicitly rather than inherited by accident, because the
        // whole point of CanGive is that callers rely on it before consuming a source.
        public bool CanGive() => _buffer != null;

        public bool TryGive(ItemStack item)
        {
            if (_buffer == null || string.IsNullOrEmpty(item.ItemType))
            {
                return false;
            }

            _buffer.Deposit(item.ItemType);
            return true;
        }
    }
}
