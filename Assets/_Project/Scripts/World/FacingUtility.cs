using UnityEngine;

namespace GolemFactory.World
{
    // Pure, engine-light cell math for facing-based spatial routing -- the same
    // "extract the math into a static that a test can call without a scene" split
    // GridCoordinateConverter / YSortUtility / BeltSignalUtility / StallDiagnostics use.
    //
    // The grid here is the *simulation* grid, which is a plain square top-down grid
    // (World/GridMap is Vector2Int-indexed and deliberately decoupled from rendering).
    // Isometric is presentation only and lives entirely in GridCoordinateConverter and the
    // scene's Grid/Tilemap, so North really is (0, +1) here even though it renders as
    // up-and-right on screen.
    public static class FacingUtility
    {
        /// <summary>Unit cell step in the given direction. North is (0,+1), East (+1,0).</summary>
        public static Vector2Int Delta(Facing facing)
        {
            switch (facing)
            {
                case Facing.North:
                    return new Vector2Int(0, 1);
                case Facing.East:
                    return new Vector2Int(1, 0);
                case Facing.South:
                    return new Vector2Int(0, -1);
                case Facing.West:
                    return new Vector2Int(-1, 0);
                default:
                    return Vector2Int.zero;
            }
        }

        /// <summary>
        /// The tile a golem at <paramref name="cell"/> pushes *to* -- the one in front of it.
        /// </summary>
        public static Vector2Int TargetCell(Vector2Int cell, Facing facing) => cell + Delta(facing);

        /// <summary>
        /// The tile a golem at <paramref name="cell"/> pulls *from* -- the one behind it.
        /// Behind, not beside: a single axis keeps the read "source -> golem -> target" as one
        /// straight line, which is the whole readability argument for spatial routing.
        /// </summary>
        public static Vector2Int SourceCell(Vector2Int cell, Facing facing) => cell - Delta(facing);

        public static Facing Opposite(Facing facing) => RotateClockwise(RotateClockwise(facing));

        /// <summary>
        /// Next facing clockwise (N -> E -> S -> W -> N). Clockwise in *grid* terms; the
        /// isometric camera rotates that presentation but not the underlying cell math.
        /// </summary>
        public static Facing RotateClockwise(Facing facing) => (Facing)(((int)facing + 1) & 3);
    }
}
