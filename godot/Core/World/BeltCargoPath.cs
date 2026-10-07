using GolemFactory.Belts;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Where a belt's cargo is drawn inside its cell (G10, from playtest). Unity drew every item
    /// on a straight line from the back edge to the front, which got three cases visibly wrong:
    /// a corner's cargo entered through the wrong side, a splitter's ran along a facing it does
    /// not have, and a dead end's front item sat half off the end of the belt.
    ///
    /// <para>
    /// The path is now two legs, entry edge to centre and centre to exit, in cell units with
    /// Unity's frame (+y north). A straight belt's two legs are one line, so it draws exactly as
    /// before. Purely presentational: nothing here changes where an item IS, only where it is
    /// drawn, and <see cref="BeltSegment"/> still has one Progress per item.
    /// </para>
    /// </summary>
    public static class BeltCargoPath
    {
        /// <summary>
        /// How far past the centre a dead end's front item stops: far enough to read as the end
        /// of the line, short enough that a half-cell item stays on the belt.
        /// </summary>
        public const float DeadEndStop = 0.2f;

        private static readonly Facing[] SideScanOrder = { Facing.North, Facing.East, Facing.South, Facing.West };

        /// <summary>The drawn point at <paramref name="t"/> (0 entering, 1 leaving) along entry → centre → exit.</summary>
        public static Vector2 Point(Vector2 entry, Vector2 exit, float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            // Entry leg shrinks toward the centre; exit leg grows out of it.
            return t < 0.5f ? entry * (1f - t * 2f) : exit * ((t - 0.5f) * 2f);
        }

        /// <summary>
        /// The edge cargo enters by: the side of the first neighbour that feeds this lane (the
        /// same fixed scan order <c>PlaceableBelt.RefreshShape</c> uses), else the back edge --
        /// or, for a splitter, which has no back, the centre.
        /// </summary>
        public static Vector2 Entry(BeltNetwork network, Vector2Int cell, BeltSegment segment, Facing facing, bool isSplitter)
        {
            foreach (Facing side in SideScanOrder)
            {
                if (network != null && network.TryGetBelt(FacingUtility.TargetCell(cell, side), out PlacedBelt neighbour)
                    && Feeds(neighbour.Segment, segment))
                {
                    return Edge(side);
                }
            }
            return isSplitter ? Vector2.zero : Edge(FacingUtility.Opposite(facing));
        }

        /// <summary>
        /// The edge cargo leaves by. A belt leaves by its front -- stopping short of it when
        /// nothing takes its cargo. A splitter leaves toward the branch its round-robin will hand
        /// the next item to, and holds at its centre with no branches at all.
        /// </summary>
        public static Vector2 Exit(BeltNetwork network, Vector2Int cell, BeltSegment segment, Facing facing, bool isSplitter)
        {
            if (segment == null || segment.Outputs.Count == 0)
            {
                return isSplitter ? Vector2.zero : Direction(facing) * DeadEndStop;
            }
            if (!isSplitter)
            {
                return Edge(facing);
            }

            BeltSegment next = segment.Outputs[segment.HandoffCursor];
            foreach (Facing side in SideScanOrder)
            {
                if (network != null && network.TryGetBelt(FacingUtility.TargetCell(cell, side), out PlacedBelt neighbour)
                    && neighbour.Segment == next)
                {
                    return Edge(side);
                }
            }
            return Vector2.zero;
        }

        private static bool Feeds(BeltSegment from, BeltSegment to)
        {
            if (from == null)
            {
                return false;
            }
            for (int i = 0; i < from.Outputs.Count; i++)
            {
                if (from.Outputs[i] == to)
                {
                    return true;
                }
            }
            return false;
        }

        private static Vector2 Edge(Facing side) => Direction(side) * 0.5f;

        private static Vector2 Direction(Facing facing)
        {
            Vector2Int step = FacingUtility.TargetCell(Vector2Int.zero, facing);
            return new Vector2(step.x, step.y);
        }
    }
}
