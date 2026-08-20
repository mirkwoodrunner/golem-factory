using GolemFactory.Belts;
using GolemFactory.Economy;

namespace GolemFactory.World
{
    /// <summary>
    /// A depot tile that has been given a label: <b>this crate is for Iron Plate</b>.
    ///
    /// <para>
    /// One concept doing two jobs, and the second one is the valuable half. A filter restricts
    /// what may be pushed IN, which makes a depot a sorter. It also names what comes OUT --
    /// which is the thing that was actually broken: <see cref="StorageBufferEndpoint"/> has
    /// carried a <c>PreferredItemType</c> since it was written and NOTHING HAS EVER SET IT, so a
    /// golem hauling from a depot has always pulled whichever good the buffer happened to
    /// enumerate first. With a label, a player can run two lines off one stockpile.
    /// </para>
    ///
    /// <para>
    /// <b>Why a wrapper rather than a flag on <see cref="StorageBufferEndpoint"/>.</b> That class
    /// is published by depots, freight masts and every buffer the bootstrap authors, and its own
    /// comments are careful about why its typed and untyped <c>CanGive</c> answer differently.
    /// Putting a branch for a depot-only feature on all of those would spread that reasoning
    /// around; wrapping keeps it in one place and makes the filtered case its own testable
    /// object. Everything except the two typed questions delegates.
    /// </para>
    /// </summary>
    public sealed class FilteredBufferEndpoint : IItemEndpoint, IFilteredEndpoint
    {
        private readonly StorageBuffer _buffer;
        private readonly string _filterItemType;

        public FilteredBufferEndpoint(StorageBuffer buffer, string filterItemType)
        {
            _buffer = buffer;
            // Normalised to null once, here, so every comparison below is against one
            // representation. A serialized "" and a code-written null are the same crate.
            _filterItemType = string.IsNullOrEmpty(filterItemType) ? null : filterItemType;
        }

        public StorageBuffer Buffer => _buffer;

        /// <summary>The good this crate is labelled for. Never null on a filtered endpoint.</summary>
        public string AcceptedItemType => _filterItemType;

        public string DisplayName => _buffer != null ? _buffer.BufferId : "depot";

        // --- Take ---------------------------------------------------------------------------

        public bool TryTake(out ItemStack item)
        {
            item = default;
            if (_buffer == null || _filterItemType == null)
            {
                return false;
            }

            if (!_buffer.TryWithdraw(_filterItemType))
            {
                return false;
            }

            item = new ItemStack { ItemType = _filterItemType };
            return true;
        }

        /// <summary>
        /// The label, or null when the crate is empty of it. Deliberately NOT "whatever else is
        /// in the shared stockpile": the whole point of labelling a crate is that what comes out
        /// of it is predictable, and an untyped take that reached past the label would make a
        /// filtered depot dispense the one good the player specifically excluded.
        /// </summary>
        public string PeekAvailableType() =>
            _buffer != null && _filterItemType != null && _buffer.GetQuantity(_filterItemType) > 0
                ? _filterItemType
                : null;

        public bool TryTake(string itemType, int quantity, out int taken)
        {
            taken = 0;
            if (_buffer == null || _filterItemType == null || quantity <= 0)
            {
                return false;
            }

            if (!string.Equals(itemType, _filterItemType, System.StringComparison.Ordinal))
            {
                return false;
            }

            int held = _buffer.GetQuantity(itemType);
            int wanted = quantity < held ? quantity : held;
            if (wanted <= 0)
            {
                return false;
            }

            // One clamped withdrawal, not a loop -- StorageBuffer.TryWithdraw is all-or-nothing,
            // and clamping first is what turns that into the partial take the contract promises.
            if (!_buffer.TryWithdraw(itemType, wanted))
            {
                return false;
            }

            taken = wanted;
            return true;
        }

        // --- Give ---------------------------------------------------------------------------

        /// <summary>
        /// "Could this crate accept ANYTHING at all?" -- and for a labelled crate the honest
        /// answer is "only if there is room for its own good".
        ///
        /// <para>
        /// THIS DIVERGES FROM <see cref="StorageBufferEndpoint.CanGive()"/> ON PURPOSE, and the
        /// divergence is not a §10 deadlock. That rule -- a full Slag slot must never block Iron
        /// Plate -- is about one type blocking a DIFFERENT type, and a labelled crate has only
        /// one type by construction. Answering false here is what lets <c>PushStockInto</c>'s
        /// early-out skip a full labelled crate in one question instead of walking every type in
        /// the golem's hold to be told no each time, exactly as it already does for a full belt.
        /// </para>
        /// </summary>
        public bool CanGive() =>
            _buffer != null && _filterItemType != null && _buffer.RoomFor(_filterItemType) > 0;

        public bool CanGive(string itemType) =>
            _buffer != null &&
            _filterItemType != null &&
            string.Equals(itemType, _filterItemType, System.StringComparison.Ordinal) &&
            _buffer.RoomFor(itemType) > 0;

        public bool TryGive(ItemStack item)
        {
            if (_buffer == null || _filterItemType == null)
            {
                return false;
            }

            if (!string.Equals(item.ItemType, _filterItemType, System.StringComparison.Ordinal))
            {
                return false;
            }

            // Deposit returns what it accepted, so a full slot refuses the unit rather than
            // swallowing it -- the no-item-loss invariant, unchanged from the unfiltered case.
            return _buffer.Deposit(item.ItemType, 1) > 0;
        }
    }
}
