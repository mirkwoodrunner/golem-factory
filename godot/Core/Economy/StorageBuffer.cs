using System.Collections.Generic;

namespace GolemFactory.Economy
{
    // Real replacement for M4's Belts/DemoBuffer placeholder: per-item-type quantities
    // held in one buffer, so a buffer can hold a mix of resources and the inventory UI
    // can list them individually instead of one opaque total count.
    //
    // --- Capacity (docs/progression-design.md §11 item 3) --------------------------------
    // Capacity is PER ITEM TYPE, never per buffer. §10's "Buffer capacity deadlock" row is
    // explicit that a whole-buffer cap combined with untyped takes deadlocks permanently: a
    // full Iron Plate buffer would stall smelting, which stops Slag, which stops Glass, and
    // no rigid golem can ever drain the wrong type back out. Per-type capacity is exactly
    // what makes a full Slag slot stall the smelter *without* also blocking Iron Plate --
    // §5.3(c), the whole Slag economy, is that behaviour and nothing else.
    //
    // Same shape as GolemInventory.Stock's 12-per-type cap, for the same reason, and the
    // Unlimited sentinel follows World/ResourceNode.Infinite's established idiom rather
    // than inventing a second way to say "no limit".
    //
    // Capacity is OPT-IN: a buffer built without one is Unlimited and behaves byte for byte
    // as it did before this existed. Main.unity's demo economy, the player's stockpile and
    // every pre-existing test all take that branch, the same way spatial routing is opt-in
    // per golem rather than a flag day.
    public sealed class StorageBuffer
    {
        /// <summary>
        /// Sentinel for "no per-type cap", mirroring <see cref="World.ResourceNode.Infinite"/>.
        /// </summary>
        public const int Unlimited = -1;

        public string BufferId { get; }

        /// <summary>
        /// Units of EACH item type this buffer may hold, or <see cref="Unlimited"/>.
        /// Never a total across types -- see the class comment for why that distinction is
        /// the whole point.
        /// </summary>
        public int CapacityPerType { get; private set; }

        private readonly Dictionary<string, int> _quantities = new Dictionary<string, int>();

        // --- Gross flow bookkeeping (presentation) ------------------------------------
        // Monotone lifetime totals, NOT rates: the derivation stays in Economy/BufferRateTracker
        // exactly as it always has, and nothing in the simulation reads these back.
        //
        // They exist because a LEVEL cannot answer the question the rate readout is asked.
        // Sampling quantities can only ever see net change, so a line running 60/min in and
        // 60/min out -- a healthy, fully loaded line -- reads as "Steady 0/min", which is the
        // one case the player most wants to distinguish from a dead one.
        private readonly Dictionary<string, int> _deposited = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _withdrawn = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> Quantities => _quantities;

        public StorageBuffer(string bufferId) : this(bufferId, Unlimited) { }

        public StorageBuffer(string bufferId, int capacityPerType)
        {
            BufferId = bufferId;
            CapacityPerType = NormalizeCapacity(capacityPerType);
        }

        /// <summary>
        /// Re-caps this buffer in place. Mutates rather than rebuilding because
        /// <see cref="World.StorageBufferEndpoint"/> holds a direct reference to the instance --
        /// replacing the object would orphan every spatial endpoint already published on a cell.
        ///
        /// Contents above the new cap are kept, not discarded: silently voiding stock the player
        /// legitimately owns is worse than a buffer that simply has no room until it drains.
        /// </summary>
        public void SetCapacityPerType(int capacityPerType) =>
            CapacityPerType = NormalizeCapacity(capacityPerType);

        private static int NormalizeCapacity(int capacityPerType) =>
            capacityPerType < 0 ? Unlimited : capacityPerType;

        public bool IsUnlimited => CapacityPerType == Unlimited;

        /// <summary>
        /// Units of <paramref name="itemType"/> this buffer would still accept.
        /// <see cref="int.MaxValue"/> when unlimited -- a genuine number rather than a second
        /// sentinel, so callers can clamp against it arithmetically without special-casing.
        /// </summary>
        public int RoomFor(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return 0;
            }

            if (CapacityPerType == Unlimited)
            {
                return int.MaxValue;
            }

            int room = CapacityPerType - GetQuantity(itemType);
            return room > 0 ? room : 0;
        }

        /// <summary>
        /// Deposits up to <paramref name="amount"/>, clamped by the room left for this type.
        /// Returns the units ACTUALLY accepted -- a caller that assumed all of it went in
        /// would be quietly destroying goods, which is the same reasoning
        /// GolemInventory.Stock.Add records.
        ///
        /// Kept as one method rather than gaining an uncapped sibling: a second "just deposit
        /// it" entry point would be a silent bypass of the capacity this item exists to add,
        /// and the first ratio bug it caused would be invisible.
        /// </summary>
        public int Deposit(string itemType, int amount = 1)
        {
            // Registries guard against a null id rather than letting Dictionary<string,_>
            // throw; the same courtesy applies to a null item type here.
            if (itemType == null)
            {
                return 0;
            }

            // On an Unlimited buffer RoomFor is int.MaxValue, so this clamp never bites and
            // the method is byte for byte what it was before capacity existed -- including
            // the historical handling of a zero amount, which several call sites pass as a
            // no-op and which still creates the key.
            int accepted = amount;
            int room = RoomFor(itemType);
            if (accepted > room)
            {
                accepted = room;
            }

            _quantities.TryGetValue(itemType, out int current);
            _quantities[itemType] = current + accepted;

            // The ACCEPTED units, not the requested ones: a clamped deposit moved only what
            // it moved, and counting the refusal as throughput would make a jammed buffer
            // read as the busiest thing on screen.
            _deposited.TryGetValue(itemType, out int totalIn);
            _deposited[itemType] = totalIn + accepted;
            return accepted;
        }

        /// <summary>
        /// Empties this buffer without replacing it. Exists so
        /// <see cref="StorageBufferRegistry.Clear"/> can reset the economy for a load while
        /// keeping every buffer *instance* alive.
        ///
        /// That matters because Buildings/PlaceableDepot publishes a
        /// <see cref="World.StorageBufferEndpoint"/> holding a direct reference to the instance.
        /// When Clear() removed the objects instead, loading a save left every depot's endpoint
        /// pointing at a detached buffer while the registry and the HUD read a freshly created
        /// one -- so every golem Push into that depot afterwards landed somewhere nothing
        /// displayed and nothing could spend. No stall, no error, goods simply gone.
        ///
        /// Removes the keys rather than zeroing them, so an emptied buffer reads as empty
        /// (InventoryPanel lists Quantities.Keys, and zeroed keys would render as 0-rows where
        /// the "nothing here" state belongs).
        /// </summary>
        public void ClearContents()
        {
            _quantities.Clear();

            // The flow totals go with the contents. Clear() is the LOAD path, and carrying a
            // previous session's totals across it would have the rate tracker fit a slope over
            // a discontinuity -- the same reason the Clock Tower's rate windows are deliberately
            // not restored.
            _deposited.Clear();
            _withdrawn.Clear();
        }

        /// <summary>
        /// Units of <paramref name="itemType"/> ever accepted by this buffer. Monotone, so a
        /// sampler can difference two readings into a gross INFLOW rate.
        /// </summary>
        public int TotalDeposited(string itemType) =>
            itemType != null && _deposited.TryGetValue(itemType, out int total) ? total : 0;

        /// <summary>Units of <paramref name="itemType"/> ever taken out. The outflow half.</summary>
        public int TotalWithdrawn(string itemType) =>
            itemType != null && _withdrawn.TryGetValue(itemType, out int total) ? total : 0;

        public bool TryWithdraw(string itemType, int amount = 1)
        {
            if (!_quantities.TryGetValue(itemType, out int current) || current < amount)
            {
                return false;
            }

            _quantities[itemType] = current - amount;
            _withdrawn.TryGetValue(itemType, out int totalOut);
            _withdrawn[itemType] = totalOut + amount;
            return true;
        }

        public int GetQuantity(string itemType) => _quantities.TryGetValue(itemType, out int quantity) ? quantity : 0;
    }
}
