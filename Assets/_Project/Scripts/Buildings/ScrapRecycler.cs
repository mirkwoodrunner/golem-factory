using GolemFactory.Economy;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// The junk hopper: throw anything in, get Scrap out, burn Coke doing it
    /// (docs/cozy-automation-design.md §4b).
    ///
    /// <para>
    /// <b>The gap it fills.</b> Slag has two outlets -- the Glass line (R3) and the Slag Heap.
    /// Everything else in the game has none. A mis-ordered truckload, a decommissioned line's
    /// leftover Casings, a depot of Glass nobody wants: there was no way to get rid of any of it.
    /// §10's escape hatch from an over-built factory is <em>deleting golems</em>, and there was
    /// no equivalent for goods.
    /// </para>
    ///
    /// <para>
    /// <b>It is <see cref="SlagHeap"/>'s pattern with the sign flipped.</b> The heap destroys and
    /// charges; this charges <em>and returns something</em>. Same integer accumulator, same
    /// carried remainder, same charge-on-the-crossing discipline §1.4's boiler established -- so
    /// two identical factories recover identical amounts for identical fuel. Plain C# with no
    /// engine references, like the heap beside it.
    /// </para>
    ///
    /// <para>
    /// <b>Why it does not delete the Slag Heap.</b> Slag is tier 1, worth 2 points, so 2 Slag is
    /// one batch: <b>2 Slag per Coke, returning 1 Scrap</b>, against the heap's 4 Slag per Coke
    /// returning nothing. Exactly half the disposal rate, plus a good. A real choice rather than
    /// a dominant strategy -- and §5.3(c)'s decision is untouched, because the recycler competes
    /// for the same Coke, harder.
    /// </para>
    ///
    /// <para>
    /// <b>Why it cannot be looped, and why the obvious argument is the wrong one.</b> The first
    /// pass at this reasoned in Scrap and concluded every cycle was lossy. That is not true --
    /// R4 turns 2 Scrap into 2 Iron Plate plus a Slag, and R19 turns 1 Copper Ingot into 3 Copper
    /// Wire, so ANY positive per-unit value multiplies across a recipe whose output count exceeds
    /// its input count. No integer tier table can avoid that.
    /// </para>
    ///
    /// <para>
    /// It does not matter, for two reasons that do hold. First, <b>Scrap is free at the market</b>
    /// -- §10 forbids a soft-lock, so Scrap truckloads cost nothing and a player can already
    /// order it forever. A machine whose only output is Scrap therefore cannot be an economic
    /// exploit; what it actually saves is the walk. Second, and structurally,
    /// <b>this is a strict Coke sink</b>: it consumes Coke on every batch and can never produce
    /// or return any, so the currency any loop would have to close in is spent monotonically. A
    /// test pins both.
    /// </para>
    ///
    /// <para>
    /// What the tier table buys, then, is not exploit-proofing but <em>fairness</em>: a Casing
    /// represents four recipes' work and comes back worth more than a lump of Coal does. And it
    /// is never worth more than a rounding error -- the deepest good in the game returns three
    /// Scrap for something that took minutes of a whole factory to build, so recycling is always
    /// obviously worse than using the thing.
    /// </para>
    /// </summary>
    public sealed class ScrapRecycler
    {
        /// <summary>
        /// Scrap-points one unit of a good is worth, indexed by <see cref="ItemTiers"/> tier.
        /// <b>TUNING.</b>
        ///
        /// <para>
        /// Rising steeply on purpose: a Casing represents four recipes' worth of work and a lump
        /// of Coal represents none, so "worth more the deeper it is" is the only ordering that
        /// does not make throwing away finished components feel like vandalism.
        /// </para>
        /// </summary>
        private static readonly int[] PointsByTier = { 1, 2, 3, 5, 8, 13 };

        /// <summary>Points that buy one Scrap. <b>TUNING.</b></summary>
        public const int PointsPerScrap = 4;

        /// <summary>Coke burned per Scrap produced. <b>TUNING.</b></summary>
        public const int CokePerScrap = 1;

        /// <summary>
        /// Scrap this hopper will hold before it stops accepting. <b>TUNING.</b>
        ///
        /// <para>
        /// Twice a golem's per-type cap, so one <c>Haul(12)</c> does not empty it and a collector
        /// running at half the recycler's rate still keeps up. A cap at all is the point: a
        /// recycler nobody collects from backs up and stops, which is a <em>visible</em>
        /// consequence rather than an invisible infinite sink.
        /// </para>
        /// </summary>
        public const int OutputCapacity = 24;

        public string RecyclerId { get; }

        /// <summary>Fuel on hand. Delivered by golem or by hand, like a boiler's.</summary>
        public int CokeStock { get; private set; }

        /// <summary>Scrap waiting to be hauled out, capped at <see cref="OutputCapacity"/>.</summary>
        public int ScrapStock { get; private set; }

        /// <summary>
        /// Points banked since the last Scrap was made, in [0, <see cref="PointsPerScrap"/>).
        /// The carried remainder that makes every ratio exact rather than approximately right.
        /// </summary>
        public int PendingPoints { get; private set; }

        /// <summary>Lifetime total of units accepted, for a readout.</summary>
        public int TotalRecycled { get; private set; }

        public ScrapRecycler(string recyclerId, int startingCoke = 0)
        {
            RecyclerId = recyclerId;
            CokeStock = startingCoke < 0 ? 0 : startingCoke;
        }

        /// <summary>
        /// Scrap-points one unit of <paramref name="itemType"/> is worth, or 0 for a good this
        /// machine will not take.
        ///
        /// <para>
        /// <b>Two goods are worth nothing on purpose.</b> An item type <see cref="ItemTiers"/>
        /// has never heard of is refused rather than defaulted -- silently valuing an
        /// unrecognised good is how a future item becomes an exploit. And <see cref="ItemType.Coke"/>
        /// is refused <em>as feedstock</em> because it is the fuel: pushing Coke into a recycler
        /// refuels it, exactly as it does a Slag Heap or a boiler. That fork lives in
        /// <c>ScrapRecyclerEndpoint</c>, where the tile's two meanings belong.
        /// </para>
        /// </summary>
        public static int PointsFor(string itemType)
        {
            if (itemType == ItemType.Coke)
            {
                return 0;
            }

            int tier = ItemTiers.TierOf(itemType);
            return tier >= 0 && tier < PointsByTier.Length ? PointsByTier[tier] : 0;
        }

        public void AddCoke(int quantity)
        {
            if (quantity > 0)
            {
                CokeStock += quantity;
            }
        }

        /// <summary>
        /// Whether one more unit of <paramref name="itemType"/> can be taken right now.
        ///
        /// <para>
        /// Only the unit that CROSSES the threshold needs fuel and output room in hand; the ones
        /// before it ride the accumulator -- the same rule that makes a Slag Heap's last Coke
        /// void four Slag rather than one. A hopper that has run dry therefore keeps accepting
        /// until the batch would actually complete, and then stops.
        /// </para>
        /// </summary>
        public bool CanAccept(string itemType)
        {
            int points = PointsFor(itemType);
            if (points <= 0)
            {
                return false;
            }

            // A FULL HOPPER REFUSES OUTRIGHT, at any accumulator level. This is the one place
            // the rule deliberately differs from SlagHeap's "only the crossing unit pays": there,
            // the three units before the crossing are genuinely free and refusing them would
            // change the published ratio. Here, banked points against a full output have no path
            // to being paid out except somebody collecting -- so accepting them would be taking
            // in goods the machine cannot process, which is exactly the accepted-then-stuck
            // behaviour that makes backpressure illegible. A recycler nobody empties should
            // visibly stop, not quietly keep swallowing.
            if (ScrapStock >= OutputCapacity)
            {
                return false;
            }

            // Below the crossing: nothing is spent and nothing is produced, so it always fits.
            if (PendingPoints + points < PointsPerScrap)
            {
                return true;
            }

            return CokeStock >= CokePerScrap;
        }

        /// <summary>
        /// Takes one unit. Returns false when the hopper cannot pay for it, leaving the unit with
        /// its owner -- never accepted-then-lost, the no-item-loss invariant every endpoint in
        /// this game protects.
        /// </summary>
        public bool TryRecycle(string itemType)
        {
            if (!CanAccept(itemType))
            {
                return false;
            }

            PendingPoints += PointsFor(itemType);
            TotalRecycled++;

            // A `while`, not an `if`: a tier-5 good is 13 points and buys three whole Scrap at
            // once. An `if` would bank the surplus and hand out one, which is not the ratio.
            while (PendingPoints >= PointsPerScrap &&
                   CokeStock >= CokePerScrap &&
                   ScrapStock < OutputCapacity)
            {
                PendingPoints -= PointsPerScrap;
                CokeStock -= CokePerScrap;
                ScrapStock++;
            }

            return true;
        }

        /// <summary>
        /// Removes up to <paramref name="quantity"/> Scrap for a golem's <c>Haul</c>. Partial
        /// takes are normal, as everywhere else -- stalling a golem that can make progress would
        /// deadlock every under-supplied line in the factory.
        /// </summary>
        public int TakeScrap(int quantity)
        {
            if (quantity <= 0)
            {
                return 0;
            }

            int taken = quantity < ScrapStock ? quantity : ScrapStock;
            ScrapStock -= taken;
            return taken;
        }

        /// <summary>
        /// Restores a saved hopper. Set, not add, for the same reason
        /// <c>SteamBoiler.SetCoke</c> is: a rebuilt building was placed with whatever the prefab
        /// starts with, and adding to that would mint goods on every load.
        /// </summary>
        public void Restore(int cokeStock, int scrapStock, int pendingPoints)
        {
            CokeStock = cokeStock > 0 ? cokeStock : 0;
            ScrapStock = Clamp(scrapStock, 0, OutputCapacity);
            PendingPoints = Clamp(pendingPoints, 0, PointsPerScrap - 1);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
