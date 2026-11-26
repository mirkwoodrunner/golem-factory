namespace GolemFactory.Buildings
{
    /// <summary>
    /// The costed sink for R4's byproduct (docs/progression-design.md §5.3(c), §11 item 10):
    /// voids Slag at <b>1 Coke per 4 Slag</b>.
    ///
    /// <para>
    /// <b>Why disposal has to cost something.</b> R4 yields 1 Slag per 2 Iron Plate forever,
    /// whether the player wants it or not, and per-item-type buffer capacity means a full Slag
    /// slot stops the smelter depositing its byproduct -- taking iron, gears, casings and every
    /// branch below with it. So Slag must be routed every cycle, and the design gives it exactly
    /// two outlets: the Glass line, which turns it into Lenses, or this, which burns it. Free
    /// disposal would make that a non-choice; pricing it in <b>Coke</b> makes throwing Slag away
    /// compete directly with the boilers and the smelters for the scarcest intermediate, which
    /// is what makes expanding the Glass line a real reward rather than flavour.
    /// </para>
    ///
    /// <para>
    /// <b>Integer accumulator, no floats</b>, following §1.4's boiler discipline: four units in,
    /// one Coke out, remainder carried. 1 Coke per 4 Slag is exact in integers at every scale, so
    /// two identical factories void identical amounts for identical fuel.
    /// </para>
    ///
    /// <para>
    /// Plain C# with no engine references, like <c>SteamBoiler</c> beside it -- the placeable is
    /// a thin wrapper, and every rule here is testable without a scene.
    /// </para>
    /// </summary>
    public sealed class SlagHeap
    {
        /// <summary>§5.3(c): four Slag voided per Coke burned, when no data says otherwise.</summary>
        public const int DefaultSlagPerCoke = 4;

        /// <summary>
        /// Slag voided per Coke burned: the Slag Heap prefab's <c>slagPerCoke</c> in
        /// placeables.json. Data, so the guide's carrier plan can read it rather than copy it.
        /// </summary>
        public int SlagPerCoke { get; }

        /// <summary>
        /// Coke a heap will hold (2026-11-26, the user's call: a machine always holds less than
        /// the pack). A demolished heap hands its Coke back, and the demolition is refused when
        /// the pack can't take it, so an uncapped heap fed past the pack's cap stood stuck. 50, a
        /// crate's size, under the base pack's 100. Full, it refuses Coke, and the golem bringing
        /// it stalls as at a full crate. <c>MachineStorageTests</c> pins it under the pack.
        /// </summary>
        public const int CokeCapacity = 50;

        public string HeapId { get; }

        /// <summary>Fuel on hand. Delivered like a boiler's -- see <c>SlagHeapEndpoint</c>.</summary>
        public int CokeStock { get; private set; }

        /// <summary>
        /// Slag voided since the last Coke was burned, in [0, <see cref="SlagPerCoke"/>). The
        /// carried remainder that makes the ratio exact rather than approximately right.
        /// </summary>
        public int PendingSlag { get; private set; }

        /// <summary>Lifetime total, for the §8 HUD line ("Slag 96/min → Glass 32 · Voided 64").</summary>
        public int TotalVoided { get; private set; }

        public SlagHeap(string heapId, int startingCoke = 0, int slagPerCoke = DefaultSlagPerCoke)
        {
            HeapId = heapId;
            SlagPerCoke = slagPerCoke < 1 ? 1 : slagPerCoke;
            CokeStock = startingCoke < 0 ? 0 : startingCoke;
        }

        /// <summary>Whether the heap holds all the Coke it will take.</summary>
        public bool IsCokeFull => CokeStock >= CokeCapacity;

        /// <summary>
        /// Refuel, up to <see cref="CokeCapacity"/>. Returns how much went in. A heap over the cap
        /// (a save from before it; a load never clamps) takes none and burns down to it.
        /// </summary>
        public int AddCoke(int quantity)
        {
            int room = CokeCapacity - CokeStock;
            if (quantity > room)
            {
                quantity = room;
            }

            if (quantity <= 0)
            {
                return 0;
            }

            CokeStock += quantity;
            return quantity;
        }

        /// <summary>
        /// Whether one more unit of Slag can be taken right now.
        ///
        /// <para>
        /// A heap out of Coke <b>refuses</b>, and that refusal is the mechanic rather than an
        /// edge case: the Slag backs up, the smelter stalls, and the player is told -- by the
        /// thing stopping -- that disposal has a running cost they stopped paying. Voiding for
        /// free when the fuel ran out would quietly delete the entire §5.3(c) decision.
        /// </para>
        ///
        /// <para>
        /// Only the unit that CROSSES the threshold needs fuel in hand; the three before it are
        /// carried on the accumulator. That is what makes the last Coke void four Slag rather
        /// than one.
        /// </para>
        /// </summary>
        public bool CanVoid() => PendingSlag + 1 < SlagPerCoke || CokeStock > 0;

        /// <summary>
        /// Takes one unit of Slag. Returns false when the heap cannot pay for it, leaving the
        /// unit with its owner -- never accepted-then-lost, which is the no-item-loss invariant
        /// every endpoint in this game protects.
        /// </summary>
        public bool TryVoid()
        {
            if (!CanVoid())
            {
                return false;
            }

            PendingSlag++;
            TotalVoided++;
            Events.EventBus.Consumed(Economy.ItemType.Slag, 1);

            if (PendingSlag >= SlagPerCoke)
            {
                // Charged on the crossing, not per unit: a per-unit fractional burn would need
                // floats, and §1.4 settled that argument for the whole project.
                PendingSlag -= SlagPerCoke;
                CokeStock--;
                Events.EventBus.Consumed(Economy.ItemType.Coke, 1);
            }

            return true;
        }

        /// <summary>
        /// Coke this heap would need to void <paramref name="slagUnits"/> more, given what it
        /// has already carried. For a readout that can warn before the line stalls.
        /// </summary>
        public int CokeNeededFor(int slagUnits)
        {
            if (slagUnits <= 0)
            {
                return 0;
            }

            return (PendingSlag + slagUnits) / SlagPerCoke;
        }
    }
}
