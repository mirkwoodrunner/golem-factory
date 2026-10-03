using Godot;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Paints the workshop floor from Core's <see cref="FloorLayout"/>, choosing each tile with
    /// <see cref="FloorTileVariant"/> -- the same two calls Unity's SandboxFloorGenerator makes,
    /// so the room is the same shape and the plank pattern is the same pattern.
    ///
    /// <para>
    /// The TileSet is built in code rather than authored as a .tres. Six single-tile atlases
    /// in a fixed order is data the generator already owns; a hand-authored TileSet would be a
    /// second copy of that order with nothing to keep the two in step.
    /// </para>
    /// </summary>
    public partial class FloorLayer : TileMapLayer
    {
        // Index order is FloorTileVariant's: four plank variants, then plate, then grate.
        private static readonly string[] TilePaths =
        {
            "res://art/floor_tile.png",
            "res://art/floor_tile_wood_b.png",
            "res://art/floor_tile_wood_c.png",
            "res://art/floor_tile_wood_d.png",
            "res://art/floor_tile_accent.png",
            "res://art/floor_tile_grate.png",
        };

        public override void _Ready()
        {
            var tileSet = new TileSet { TileSize = new Vector2I(GridConversions.CellPixels, GridConversions.CellPixels) };
            var sourceIds = new int[TilePaths.Length];
            for (int i = 0; i < TilePaths.Length; i++)
            {
                var atlas = new TileSetAtlasSource
                {
                    Texture = GD.Load<Texture2D>(TilePaths[i]),
                    TextureRegionSize = tileSet.TileSize,
                };
                atlas.CreateTile(Vector2I.Zero);
                sourceIds[i] = tileSet.AddSource(atlas);
            }
            TileSet = tileSet;

            // A TileMapLayer cell's square starts at its top-left corner; GridConversions puts a
            // cell's CENTRE on the cell coordinate. Shift by half a tile so they agree.
            Position = new Vector2(-GridConversions.CellPixels / 2f, -GridConversions.CellPixels / 2f);

            foreach (CoreVector2Int cell in FloorLayout.GetFloorCells())
            {
                int variant = FloorTileVariant.Select(cell.x, cell.y);
                SetCell(new Vector2I(cell.x, -cell.y), sourceIds[variant], Vector2I.Zero);
            }
        }
    }
}
