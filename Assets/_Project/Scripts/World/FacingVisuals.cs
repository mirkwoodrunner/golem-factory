using UnityEngine;

namespace GolemFactory.World
{
    // Turning a grid Facing into something you can point an arrow along on screen.
    //
    // This is its own file, and its own pure static, because the grid and the screen genuinely
    // disagree and every previous attempt to eyeball it got the diagonal wrong. FacingUtility
    // works in *simulation* space, where the grid is a plain square top-down grid and North is
    // (0, +1). The camera renders that isometrically, so North does not point up on screen --
    // it points up-and-LEFT, while East points up-and-right. An arrow rotated by the naive
    // 0/90/180/270 would contradict the belt it was drawn on.
    //
    // Derived from GridCoordinateConverter's own projection rather than hardcoded angles, so
    // it stays correct if the scene's cell size is ever retuned.
    public static class FacingVisuals
    {
        /// <summary>
        /// The project's standard isometric cell size (see the Grid in Main/Sandbox). Used by
        /// the convenience overloads so callers that have no converter to hand still agree.
        /// </summary>
        public static readonly Vector2 DefaultCellSize = new Vector2(1f, 0.5f);

        /// <summary>
        /// Unit-length on-screen direction a golem/belt facing <paramref name="facing"/> points.
        /// </summary>
        public static Vector2 ScreenDirection(Facing facing, Vector2 cellSize)
        {
            Vector2Int delta = FacingUtility.Delta(facing);
            var converter = new GridCoordinateConverter(cellSize);

            // Project the one-cell step through the same world transform the tilemap uses. The
            // step is taken from the origin, so this is exactly the on-screen offset from a
            // tile to the tile in front of it.
            Vector3 world = converter.CellFractionToWorld(new Vector2(delta.x, delta.y));
            var screen = new Vector2(world.x, world.y);
            return screen.sqrMagnitude > 0f ? screen.normalized : Vector2.up;
        }

        public static Vector2 ScreenDirection(Facing facing) => ScreenDirection(facing, DefaultCellSize);

        /// <summary>
        /// Z rotation, in degrees, that points a sprite drawn facing +X along
        /// <paramref name="facing"/>.
        /// </summary>
        public static float ScreenAngleDegrees(Facing facing, Vector2 cellSize)
        {
            Vector2 direction = ScreenDirection(facing, cellSize);
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        public static float ScreenAngleDegrees(Facing facing) => ScreenAngleDegrees(facing, DefaultCellSize);

        /// <summary>
        /// Short player-facing name for a facing. Uses the compass words the design doc uses
        /// rather than screen words ("up-left"), so the label matches the grid the stall
        /// messages name cells in.
        /// </summary>
        public static string Describe(Facing facing)
        {
            switch (facing)
            {
                case Facing.North:
                    return "N";
                case Facing.East:
                    return "E";
                case Facing.South:
                    return "S";
                case Facing.West:
                    return "W";
                default:
                    return "?";
            }
        }
    }
}
