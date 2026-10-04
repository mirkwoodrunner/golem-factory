using Godot;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The one place a Core cell becomes a Godot position, and back.
    ///
    /// <para>
    /// Core's grid is Unity's: +y is NORTH, up the screen. Godot's 2D +y points DOWN the screen,
    /// so the y axis flips here and nowhere else. Every rule in Core keeps reasoning in its own
    /// frame (FacingUtility, BeltPlacementRules, FloorLayout), which is what let those files
    /// port byte for byte, and no Node does its own arithmetic on cells.
    /// </para>
    /// </summary>
    public static class GridConversions
    {
        public const int CellPixels = 64;

        /// <summary>Centre of <paramref name="cell"/> in Godot world pixels.</summary>
        public static Vector2 CellToWorld(CoreVector2Int cell) =>
            new Vector2(cell.x * CellPixels, -cell.y * CellPixels);

        public static Vector2 CellToWorld(Vector2I cell) => CellToWorld(ToCore(cell));

        /// <summary>The cell whose square contains <paramref name="world"/>.</summary>
        public static CoreVector2Int WorldToCell(Vector2 world) =>
            new CoreVector2Int(
                Mathf.FloorToInt(world.X / CellPixels + 0.5f),
                Mathf.FloorToInt(-world.Y / CellPixels + 0.5f));

        // Inspector-authored cells are written in Core's frame (north = +y), so they convert
        // field for field; only positions flip.
        public static CoreVector2Int ToCore(Vector2I cell) => new CoreVector2Int(cell.X, cell.Y);

        /// <summary>
        /// A Core facing as a Godot rotation in radians, for a sprite authored pointing EAST.
        /// FacingVisuals answers counter-clockwise degrees in a y-up frame; Godot rotates
        /// clockwise in a y-down one, so the sign flips.
        /// </summary>
        public static float FacingToRotation(Facing facing) =>
            Mathf.DegToRad(-FacingVisuals.ScreenAngleDegrees(facing));

        /// <summary>One cell in <paramref name="facing"/>'s direction, in pixels.</summary>
        public static Vector2 FacingStep(Facing facing)
        {
            CoreVector2Int d = FacingUtility.Delta(facing);
            return new Vector2(d.x * CellPixels, -d.y * CellPixels);
        }

        /// <summary>
        /// Places a standing sprite the way Unity's BottomCenter pivot did: its feet on the
        /// bottom edge of the cell rather than its middle on the cell's centre.
        /// </summary>
        public static void StandOnCell(Sprite2D sprite)
        {
            sprite.Centered = false;
            Vector2 size = sprite.Texture.GetSize();
            sprite.Offset = new Vector2(-size.X / 2f, CellPixels / 2f - size.Y);
        }
    }
}
