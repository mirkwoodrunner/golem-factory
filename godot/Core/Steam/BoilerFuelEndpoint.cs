using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Steam
{
    /// <summary>
    /// A Boiler's fuel hatch, as an ordinary <see cref="IItemEndpoint"/> on the cell the Boiler
    /// stands on -- so a golem <c>Push</c>es Coke into it exactly as it pushes into a depot or
    /// into the Clock Tower.
    ///
    /// <para>
    /// WHY THIS EXISTS AT ALL. <see cref="SteamBoiler.AddCoke"/> was written as "the only way
    /// Coke ever goes up" and then nothing in the game ever called it: a boiler was a sealed
    /// tank holding whatever it was constructed with. That makes §9's whole arc unreachable --
    /// Phase 1's 240 Coke is spent and never replaced, so §9's "~12 Boilers" and the ~23 cokers
    /// feeding them have nowhere to feed, and §3.1's "Coke becomes the contended throat of the
    /// entire game" reduces to a single opening allowance. The design assumes delivery
    /// throughout (§5.1 lists Coke's consumers as "every powered golem, <em>via its Boiler</em>")
    /// but never names the mechanism, because the mechanism is meant to be the ordinary one.
    /// </para>
    ///
    /// <para>
    /// Modelled on <c>ClockTowerInputEndpoint</c> deliberately, down to the shape of the two
    /// <c>CanGive</c> answers: THE BOILER IS NOT A SPECIAL CASE ANYWHERE IN <c>GolemEntity</c>.
    /// A golem facing it pushes through the same <c>BeginPush</c>, with the same per-type skip
    /// and the same consume-after-give ordering.
    /// </para>
    /// </summary>
    public sealed class BoilerFuelEndpoint : IItemEndpoint
    {
        private readonly SteamBoiler _boiler;
        private readonly string _displayName;

        public BoilerFuelEndpoint(SteamBoiler boiler, string displayName = null)
        {
            _boiler = boiler;
            _displayName = string.IsNullOrEmpty(displayName)
                ? (boiler != null && !string.IsNullOrEmpty(boiler.BoilerId) ? boiler.BoilerId : "Boiler")
                : displayName;
        }

        public SteamBoiler Boiler => _boiler;

        public string DisplayName => _displayName;

        // --- Take: never. -----------------------------------------------------------------
        // A boiler does not hand its fuel back. Returning false rather than throwing so a golem
        // facing the wrong way stalls cleanly, exactly as ClockTowerInputEndpoint refuses.
        //
        // This also closes the obvious exploit in the other direction: if fuel could be taken
        // back out, a boiler would be an uncapped Coke warehouse that the §1.2 per-item-type
        // buffer cap does not apply to.

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
        /// "Could this endpoint accept ANYTHING at all?" -- true for any live boiler.
        ///
        /// <para>
        /// §3.1 gives a Boiler no capacity, so it can never be full; see the note on
        /// <see cref="SteamBoiler.PeakCokeStock"/> for why a <c>CokeCapacity</c> was rejected
        /// rather than merely unimplemented. This is the untyped question
        /// <c>GolemEntity.BeginPush</c> uses for its early-out, and answering it per type here
        /// would abandon the whole push at the first non-Coke good in the hold.
        /// </para>
        /// </summary>
        public bool CanGive() => _boiler != null;

        /// <summary>
        /// The typed question: a boiler burns Coke and nothing else.
        ///
        /// <para>
        /// A golem pushing a mixed hold therefore delivers its Coke and KEEPS the rest for the
        /// next cycle rather than feeding Iron Plate into a firebox. Refusing rather than
        /// silently swallowing is the no-item-loss invariant every other endpoint protects, and
        /// it is exactly the per-type skip §1.2 put into <c>BeginPush</c>.
        /// </para>
        /// </summary>
        public bool CanGive(string itemType) => _boiler != null && itemType == ItemType.Coke;

        /// <summary>Burns-to-be: one unit of Coke onto the stock. Anything else is refused.</summary>
        public bool TryGive(ItemStack item)
        {
            if (!CanGive(item.ItemType))
            {
                return false;
            }

            _boiler.AddCoke(1);
            return true;
        }
    }
}
