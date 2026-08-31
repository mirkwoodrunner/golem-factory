using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A Scrap Recycler's tile, as an ordinary <see cref="IItemEndpoint"/> -- so a golem pushes
    /// junk onto it exactly as it pushes into a depot, and hauls the Scrap back off it exactly as
    /// it hauls from one.
    ///
    /// <para>
    /// <b>It goes BOTH ways, which is what a Slag Heap's tile does not.</b> The heap is a pure
    /// sink; this one is a machine in the middle of the logistics graph, and that is deliberate:
    /// output that appeared in the player's stockpile by magic would cost no logistics, and an
    /// automation game's whole subject is the carrying.
    /// </para>
    ///
    /// <para>
    /// <b>Two meanings on one tile</b>, modelled on <see cref="SlagHeapEndpoint"/> and
    /// <c>Steam.BoilerFuelEndpoint</c> before it. Coke is <em>fuel</em> and is stored; everything
    /// else is <em>feedstock</em> and is consumed. One cell rather than two, because a second
    /// cell would be a second building.
    /// </para>
    /// </summary>
    public sealed class ScrapRecyclerEndpoint : IItemEndpoint
    {
        private readonly ScrapRecycler _recycler;
        private readonly string _displayName;

        public ScrapRecyclerEndpoint(ScrapRecycler recycler, string displayName = null)
        {
            _recycler = recycler;
            _displayName = string.IsNullOrEmpty(displayName)
                ? (recycler != null && !string.IsNullOrEmpty(recycler.RecyclerId)
                    ? recycler.RecyclerId
                    : "Scrap Recycler")
                : displayName;
        }

        public ScrapRecycler Recycler => _recycler;

        public string DisplayName => _displayName;

        // --- Take: the recovered Scrap, and nothing else ------------------------------------
        // Coke is NOT takeable, for the same reason a boiler's fuel cannot be taken back out:
        // fuel that could be withdrawn again would make this an uncapped Coke warehouse that the
        // §1.2 per-item-type cap does not apply to.

        public bool TryTake(out ItemStack item)
        {
            item = default;
            if (_recycler == null || _recycler.TakeScrap(1) <= 0)
            {
                return false;
            }

            item = new ItemStack { ItemType = ItemType.Scrap };
            return true;
        }

        public string PeekAvailableType() =>
            _recycler != null && _recycler.ScrapStock > 0 ? ItemType.Scrap : null;

        public bool TryTake(string itemType, int quantity, out int taken)
        {
            taken = 0;
            if (_recycler == null || quantity <= 0)
            {
                return false;
            }

            if (!string.Equals(itemType, ItemType.Scrap, System.StringComparison.Ordinal))
            {
                return false;
            }

            taken = _recycler.TakeScrap(quantity);
            return taken > 0;
        }

        // --- Give ---------------------------------------------------------------------------

        /// <summary>
        /// "Could this hopper accept ANYTHING at all?" -- true while it exists, because a
        /// recycler that has run out of Coke can still be <em>refuelled</em>.
        ///
        /// <para>
        /// Answering false when the feedstock side is blocked would abandon the whole push,
        /// including the Coke that would fix it. That is the bug
        /// <see cref="SlagHeapEndpoint.CanGive()"/>'s comment already records, and it is the same
        /// bug here.
        /// </para>
        /// </summary>
        public bool CanGive() => _recycler != null;

        /// <summary>
        /// The typed question. Coke always -- it is fuel, and the recovery from a dry hopper
        /// depends on it being accepted. Everything else only while the batch it would complete
        /// can actually be paid for and stored.
        /// </summary>
        public bool CanGive(string itemType)
        {
            if (_recycler == null)
            {
                return false;
            }

            if (itemType == ItemType.Coke)
            {
                return true;
            }

            return _recycler.CanAccept(itemType);
        }

        public bool TryGive(ItemStack item)
        {
            if (_recycler == null)
            {
                return false;
            }

            if (item.ItemType == ItemType.Coke)
            {
                _recycler.AddCoke(1);
                return true;
            }

            return _recycler.TryRecycle(item.ItemType);
        }
    }
}
