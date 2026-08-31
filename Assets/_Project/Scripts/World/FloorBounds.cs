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
        }

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
