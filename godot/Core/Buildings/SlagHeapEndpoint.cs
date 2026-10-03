using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A Slag Heap's tile, as an ordinary <see cref="IItemEndpoint"/> -- so a golem pushes Slag
    /// onto it exactly as it pushes into a depot, a boiler's fuel hatch or the Clock Tower.
    ///
    /// <para>
    /// <b>It accepts two item types and means different things by them.</b> Slag is what the
    /// heap is FOR, and it is destroyed. Coke is the heap's fuel, and it is stored. Modelled on
    /// <see cref="Steam.BoilerFuelEndpoint"/>, which already established that a building's tile
    /// can be picky about types without <c>GolemEntity</c> knowing anything about it: the
    /// per-type <c>CanGive</c> is what makes a mixed hold deliver its Slag and keep the rest.
    /// </para>
    ///
    /// <para>
    /// Two types rather than a separate fuel tile because a heap is one cell and a second cell
    /// would be a second building. The alternative -- drawing Coke from the player's stockpile
    /// at a distance -- was rejected for the reason every other consumer in this game is fed by
    /// hand or by golem: fuel that teleports is fuel that costs no logistics, and §5.3(c)'s
    /// whole point is that disposal competes for a good someone has to carry.
    /// </para>
    /// </summary>
    public sealed class SlagHeapEndpoint : IItemEndpoint
    {
        private readonly SlagHeap _heap;
        private readonly string _displayName;

        public SlagHeapEndpoint(SlagHeap heap, string displayName = null)
        {
            _heap = heap;
            _displayName = string.IsNullOrEmpty(displayName)
                ? (heap != null && !string.IsNullOrEmpty(heap.HeapId) ? heap.HeapId : "Slag Heap")
                : displayName;
        }

        public SlagHeap Heap => _heap;

        public string DisplayName => _displayName;

        // --- Take: never. A heap does not give anything back. Its Slag is destroyed and its
        // Coke is committed -- if fuel could be taken out again it would be an uncapped Coke
        // warehouse that the §1.2 per-item-type cap does not apply to, which is the same
        // exploit BoilerFuelEndpoint closes.

        public bool TryTake(out ItemStack item)
        {
            item = default;
            return false;
        }

        public string PeekAvailableType() => null;

        public bool TryTake(string itemType, int quantity, out int taken)
        {
            taken = 0;
            return false;
        }

        // --- Give -------------------------------------------------------------------------

        /// <summary>
        /// "Could this endpoint accept ANYTHING at all?" -- true while the heap could take
        /// either good. A heap out of Coke can still be REFUELLED, so this stays true even when
        /// it can no longer void: answering false would abandon the whole push, including the
        /// Coke that would fix it.
        /// </summary>
        public bool CanGive() => _heap != null;

        /// <summary>
        /// The typed question. Coke always (it is fuel); Slag only while the heap can pay to
        /// burn it. A heap that has run dry therefore refuses Slag and keeps taking Coke, which
        /// is exactly the recovery the player needs.
        /// </summary>
        public bool CanGive(string itemType)
        {
            if (_heap == null)
            {
                return false;
            }

            if (itemType == ItemType.Coke)
            {
                return true;
            }

            return itemType == ItemType.Slag && _heap.CanVoid();
        }

        public bool TryGive(ItemStack item)
        {
            if (_heap == null)
            {
                return false;
            }

            if (item.ItemType == ItemType.Coke)
            {
                _heap.AddCoke(1);
                return true;
            }

            return item.ItemType == ItemType.Slag && _heap.TryVoid();
        }
    }
}
