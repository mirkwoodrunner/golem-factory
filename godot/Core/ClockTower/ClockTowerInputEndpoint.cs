using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// The Clock Tower's input buffer tile, as an ordinary <see cref="IItemEndpoint"/> on a cell.
    ///
    /// <para>
    /// §7 asks for "an input buffer tile golems <c>Push</c> into like any other", and this is
    /// what makes that literally true: THE TOWER IS NOT A SPECIAL CASE ANYWHERE IN
    /// <c>GolemEntity</c>. A golem facing it pushes into it exactly as it pushes into a depot,
    /// through the same <c>BeginPush</c>, with the same per-type skip and the same
    /// consume-after-give ordering. Lives beside the site rather than in <c>World/</c> with the
    /// other three adapters only because it is the tower's own surface; it uses nothing but the
    /// site's public API, so <c>World/</c> gains no reverse reference to the tower.
    /// </para>
    ///
    /// <para>
    /// A PURE SINK: it never gives anything back. Goods delivered to the megaproject are
    /// consumed by it, which is the point -- the four Tier-5 goods are terminal by design (see
    /// the dead-end note in <c>Economy/ItemType</c>) and the tower is their sink.
    /// </para>
    /// </summary>
    public sealed class ClockTowerInputEndpoint : IItemEndpoint
    {
        private readonly ClockTowerSite _site;
        private readonly string _displayName;

        public ClockTowerInputEndpoint(ClockTowerSite site, string displayName = "Clock Tower")
        {
            _site = site;
            _displayName = string.IsNullOrEmpty(displayName) ? "Clock Tower" : displayName;
        }

        public ClockTowerSite Site => _site;

        public string DisplayName => _displayName;

        // --- Take: never. -----------------------------------------------------------------
        // A megaproject does not hand its materials back. Returning false rather than throwing
        // so a golem facing the wrong way stalls cleanly, exactly as ResourceNodeEndpoint
        // refuses to accept.

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
        /// "Could this endpoint accept ANYTHING at all?" -- true while a stage is running.
        ///
        /// <para>
        /// Deliberately NOT "does it accept the type in the golem's hand". This is the untyped
        /// question <c>GolemEntity.BeginPush</c> uses for its early-out, and answering it per
        /// type would abandon the whole push at the first undemanded good -- the identical
        /// deadlock a per-item-type-capped <c>StorageBuffer</c> hits when its Slag slot fills.
        /// See the long note on <c>IItemEndpoint.CanGive()</c>: the two overloads answer
        /// different questions and this is exactly the endpoint they diverge on.
        /// </para>
        /// </summary>
        public bool CanGive() => _site != null && _site.AcceptsAnything;

        /// <summary>
        /// The typed question, and the one carrying real backpressure: does the RUNNING STAGE
        /// demand this good?
        ///
        /// <para>
        /// A stage-2 tower refuses Lens, so a golem pushing a mixed hold delivers its Great Cogs
        /// and KEEPS the rest for the next cycle instead of feeding it into a hole. Refusing
        /// rather than silently swallowing is the no-item-loss invariant
        /// <c>StorageBufferEndpoint.TryGive</c> protects at the other end of the same push.
        /// </para>
        /// </summary>
        public bool CanGive(string itemType) => _site != null && _site.Demands(itemType);

        /// <summary>
        /// Delivers one unit. Stamped with the site's <see cref="ClockTowerSite.CurrentTick"/>,
        /// because <c>IItemEndpoint</c> has no tick to hand over and wall time has no place in a
        /// win condition; see the note on that property for the one-tick consequence.
        /// </summary>
        public bool TryGive(ItemStack item)
        {
            if (_site == null || string.IsNullOrEmpty(item.ItemType))
            {
                return false;
            }

            return _site.RecordDelivery(item.ItemType, 1, _site.CurrentTick) > 0;
        }
    }
}
