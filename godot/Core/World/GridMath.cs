using System;
using System.Runtime.CompilerServices;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// The one definition of which cell a point is in. A cell's centre is its integer
    /// coordinate, so cell <c>c</c> covers <c>[c - 0.5, c + 0.5)</c> on each axis: the point is
    /// shifted half a cell and floored. Every snap in the game goes through here
    /// (<see cref="GridCoordinateConverter"/> in Core, <c>GridConversions</c> at the Node
    /// boundary), so the pointer, the build ghost and the player's feet agree on an edge.
    ///
    /// <para>
    /// Static, inlined and all value types: a snap on every frame of a hover or a drag costs a
    /// few instructions and no allocation. Flooring, not rounding: rounding half to even made
    /// even cells own both their edges and odd cells neither.
    /// </para>
    /// </summary>
    public static class GridMath
    {
        /// <summary>The largest integer not above <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(float value) => (int)MathF.Floor(value);

        /// <summary>The cell coordinate holding a position measured in cells.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SnapToCell(float cells) => FloorToInt(cells + 0.5f);

        /// <summary>The cell holding a point measured in cells on both axes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2Int SnapToCell(float xCells, float yCells) =>
            new Vector2Int(SnapToCell(xCells), SnapToCell(yCells));

        /// <summary>The cell coordinate holding a world position, for cells <paramref name="cellSize"/> wide.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WorldToCell(float world, float cellSize) => SnapToCell(world / cellSize);

        /// <summary>The world position of a cell's centre.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CellToWorld(int cell, float cellSize) => cell * cellSize;
    }
}
