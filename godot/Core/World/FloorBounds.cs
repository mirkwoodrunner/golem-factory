namespace GolemFactory.World
{
    /// <summary>
    /// How far the workshop currently reaches, as runtime state rather than a constant
    /// (docs/progression-design.md §11 item 15, Floor Expansion).
    ///
    /// <para>
    /// <b>Why this exists rather than making <c>FloorLayout.HalfExtent</c> a variable.</b> Every
    /// method on <c>FloorLayout</c> takes its extents as parameters with <c>const</c> defaults,
    /// and a default argument must be a compile-time constant -- so turning the constant into a
    /// field would break every signature in the file. Holding the CURRENT extent here and
    /// passing it in leaves the layout math exactly as it was: pure, parameterised and testable
    /// without a scene, with the const still meaning "the room as authored".
    /// </para>
    ///
    /// <para>
    /// Only the north edge moves; see <c>FloorLayout.DefaultNorthExtent</c> for why growing
    /// symmetrically would drag the street and all nine market stalls south.
    /// </para>
    /// </summary>
    public sealed class FloorBounds
    {
        public int HalfExtent { get; }

        /// <summary>The back wall's current row. Starts at the authored extent.</summary>
        public int NorthExtent { get; private set; }

        public int MaxNorthExtent { get; }

        /// <summary>The authored back wall: the smallest the room can be.</summary>
        public int MinNorthExtent { get; }

        /// <summary>
        /// Bumped on every successful expansion, so views and the wall rebuild can tell they are
        /// looking at a different room without diffing cells. Same idiom
        /// <c>TechTreeProgressLedger.Version</c> uses.
        /// </summary>
        public int Version { get; private set; }

        public FloorBounds(
            int halfExtent = FloorLayout.HalfExtent,
            int northExtent = FloorLayout.DefaultNorthExtent,
            int maxNorthExtent = FloorLayout.MaxNorthExtent)
        {
            HalfExtent = halfExtent;
            NorthExtent = northExtent < halfExtent ? halfExtent : northExtent;
            MaxNorthExtent = maxNorthExtent < NorthExtent ? NorthExtent : maxNorthExtent;
            MinNorthExtent = NorthExtent;
        }

        /// <summary>
        /// A load: puts the back wall at <paramref name="northExtent"/>, clamped to the room's
        /// authored and maximum rows. Unlike <see cref="Expand"/> it can move the wall back in,
        /// for a save made before an expansion. Returns the previous extent.
        /// </summary>
        public int Restore(int northExtent)
        {
            int previous = NorthExtent;
            int target = northExtent < MinNorthExtent ? MinNorthExtent : northExtent > MaxNorthExtent ? MaxNorthExtent : northExtent;
            if (target != previous)
            {
                NorthExtent = target;
                Version++;
            }
            return previous;
        }

        // --- The Docks (docs/region-design.md) -------------------------------------------------

        /// <summary>What opens the Docks' gate; null keeps it shut (a bare test, a slice).</summary>
        public System.Func<bool> DocksGate { get; set; }

        public bool DocksOpen => DocksGate?.Invoke() == true;

        /// <summary>Rows bought north of the Docks' opening floor.</summary>
        public int DocksRows { get; private set; }

        /// <summary>The Docks' north row, or <see cref="Docks.Closed"/> while the gate is shut.</summary>
        public int DocksNorth => DocksOpen ? Docks.StartNorth + DocksRows : Docks.Closed;

        /// <summary>A purchase or a load: the Docks' bought rows, within the land there is. Bumps <see cref="Version"/>.</summary>
        public void RestoreDocks(int rows)
        {
            int target = System.Math.Max(0, System.Math.Min(rows, Docks.MaxNorth - Docks.StartNorth));
            if (target != DocksRows)
            {
                DocksRows = target;
                Version++;
            }
        }

        /// <summary>Whether <paramref name="cell"/> is ground: the workshop, the street, the square, or the open Docks.</summary>
        public bool IsGround(Compat.Vector2Int cell, int streetDepth = FloorLayout.StreetDepth) =>
            FloorLayout.IsInsideWorld(cell, HalfExtent, streetDepth, FloorLayout.StreetHalfExtent, NorthExtent)
            || Docks.Contains(cell, DocksNorth);

        /// <summary>Rows of expansion still available. Zero means the land has run out.</summary>
        public int RemainingRows => MaxNorthExtent - NorthExtent;

        public bool CanExpand => RemainingRows > 0;

        /// <summary>
        /// Pushes the back wall out by <paramref name="rows"/>, clamped to what is left. Returns
        /// how many rows were actually added, so a caller can refuse to charge for nothing.
        /// </summary>
        public int Expand(int rows)
        {
            if (rows <= 0)
            {
                return 0;
            }

            int added = rows < RemainingRows ? rows : RemainingRows;
            if (added <= 0)
            {
                return 0;
            }

            NorthExtent += added;
            Version++;
            return added;
        }
    }
}
