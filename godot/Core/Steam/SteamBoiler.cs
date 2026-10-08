using GolemFactory.Compat;

namespace GolemFactory.Steam
{
    /// <summary>
    /// One placed Boiler: where it stands, how much Coke it holds, and the integer accounting
    /// that turns "1 Coke per powered golem per 10 s" into whole units burned on whole ticks.
    ///
    /// Plain C#, owned by <see cref="SteamNetwork"/>, which is itself owned by a thin
    /// SteamNetworkHolder -- the Holder pattern, exactly as PlacedBelt sits under BeltNetwork.
    /// </summary>
    public sealed class SteamBoiler
    {
        public SteamBoiler(string boilerId, Vector2Int cell, int startingCoke)
        {
            BoilerId = boilerId;
            Cell = cell;
            CokeStock = startingCoke > 0 ? startingCoke : 0;
            PeakCokeStock = CokeStock;
        }

        public string BoilerId { get; }
        public Vector2Int Cell { get; }

        /// <summary>Coke on hand. Never negative -- see <see cref="Accrue"/>.</summary>
        public int CokeStock { get; private set; }

        /// <summary>
        /// The most Coke this boiler has ever held. Exists ONLY for the §8 fuel gauge's "alert
        /// at 25 %": §3.1 gives a boiler no capacity, so there is no authored 100 % to take a
        /// quarter of. The high-water mark is the one reference derivable from the boiler's own
        /// history rather than invented -- for §9's Phase-1 boiler (starts at 240) it puts the
        /// alert at 60, which is what the design's worked example implies. Refuelling above the
        /// old mark raises it, so the gauge always measures against the fullest this boiler has
        /// genuinely been.
        ///
        /// Deliberately NOT a CokeCapacity field: a capacity would also imply "this boiler is
        /// full, your Coke has nowhere to go", a refusal rule §3.1 never sanctions and which
        /// §10's soft-lock audit has therefore never been run against.
        /// </summary>
        public int PeakCokeStock { get; private set; }

        /// <summary>
        /// Powered-golem-ticks owed but not yet paid for, in [0, 200). See
        /// <see cref="SteamNetwork.TicksPerCokePerPoweredGolem"/>: this is what makes the burn
        /// exactly proportional without ever touching a float.
        /// </summary>
        public int BurnAccumulator { get; private set; }

        /// <summary>How many golems this boiler is powering as of the last evaluation.</summary>
        public int PoweredGolemCount { get; internal set; }

        /// <summary>Of those, how many were working -- and so burning Coke -- at the last tick.</summary>
        public int WorkingGolemCount { get; internal set; }

        /// <summary>Refuel. The only way Coke ever goes up.</summary>
        public void AddCoke(int quantity)
        {
            if (quantity <= 0)
            {
                return;
            }

            CokeStock += quantity;
            if (CokeStock > PeakCokeStock)
            {
                PeakCokeStock = CokeStock;
            }
        }

        /// <summary>
        /// Test/bootstrap hook for draining a boiler to an exact figure without simulating the
        /// ticks that would get it there.
        /// </summary>
        public void SetCoke(int quantity)
        {
            CokeStock = quantity > 0 ? quantity : 0;
            if (CokeStock > PeakCokeStock)
            {
                PeakCokeStock = CokeStock;
            }
        }

        /// <summary>
        /// Charges <paramref name="poweredGolems"/> golem-ticks of upkeep and burns whatever
        /// whole Coke that has now added up to. Returns how many units were actually burned.
        ///
        /// INTEGER ONLY, ON PURPOSE. 5 powered golems is 0.05 Coke/tick; accumulating that as a
        /// float would make the burn depend on rounding order, and two identically-built
        /// factories would drift apart over a 60-minute session. Adding the powered count to an
        /// int accumulator and taking one Coke each time it crosses 100 is exactly proportional,
        /// exact in integers, and reproducible: 5 golems x 100 ticks = 500 = 5 Coke, every time.
        /// The remainder carries, so no fraction of a golem-tick is ever lost or double-charged.
        /// </summary>
        internal int Accrue(int poweredGolems)
        {
            if (poweredGolems <= 0)
            {
                // An idle boiler burns NOTHING (§3.1). This is the whole reason consumption is
                // proportional rather than flat: the starting Coke stock is a budget the player
                // spends by building, not a hidden timer running against them.
                return 0;
            }

            BurnAccumulator += poweredGolems;

            int burned = 0;
            while (BurnAccumulator >= SteamNetwork.TicksPerCokePerPoweredGolem && CokeStock > 0)
            {
                CokeStock--;
                BurnAccumulator -= SteamNetwork.TicksPerCokePerPoweredGolem;
                burned++;
            }

            // The `CokeStock > 0` guard is what keeps the stock off the floor: a boiler can
            // never pay with Coke it does not have. If it runs dry mid-crossing the leftover
            // accumulator STAYS -- it is upkeep genuinely incurred, and a later refuel settles
            // it immediately. It cannot grow without bound, because a boiler at zero stock
            // powers nothing (SteamNetwork.Evaluate) and so accrues nothing.
            return burned;
        }
    }
}
