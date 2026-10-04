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

        // The street's two cobbles, chosen by SandboxLayout.StreetTileVariant.
        private static readonly string[] StreetPaths =
        {
            "res://art/street_cobble.png",
            "res://art/street_cobble_b.png",
        };

        /// <summary>Also pave the market street south of the shop front.</summary>
        [Export] public bool PaintStreet { get; set; }

        private int[] _floorSources;

        /// <summary>The floor's z. Everything drawn on the floor uses -1, above this.</summary>
        public const int FloorZ = -10;

        public override void _Ready()
        {
            var tileSet = new TileSet { TileSize = new Vector2I(GridConversions.CellPixels, GridConversions.CellPixels) };
            int[] sourceIds = AddSources(tileSet, TilePaths);
            int[] streetIds = AddSources(tileSet, StreetPaths);
            TileSet = tileSet;
            _floorSources = sourceIds;

            // Z-index is global within a canvas layer, so the floor sits well below the -1 that
            // floor-level things use (belts, pipes, the build ghost, prop contact shadows).
            // At z 0 it drew over all of them.
            ZIndex = FloorZ;

            // A TileMapLayer cell's square starts at its top-left corner; GridConversions puts a
            // cell's CENTRE on the cell coordinate. Shift by half a tile so they agree.
            Position = new Vector2(-GridConversions.CellPixels / 2f, -GridConversions.CellPixels / 2f);

            FloorBounds bounds = WorldNode.Find(this)?.Bounds ?? new FloorBounds();
            PaintRows(-bounds.HalfExtent, bounds.NorthExtent, bounds.HalfExtent);

            if (PaintStreet)
            {
                foreach (CoreVector2Int cell in FloorLayout.GetStreetCells())
                {
                    SetCell(new Vector2I(cell.x, -cell.y), streetIds[SandboxLayout.StreetTileVariant(cell)], Vector2I.Zero);
                }
            }
        }

        /// <summary>
        /// Paints the workshop's plank floor on rows <paramref name="fromRow"/>..<paramref name="toRow"/>
        /// (Core frame). Floor Expansion calls this for the rows a purchase added.
        /// </summary>
        public void PaintRows(int fromRow, int toRow, int halfExtent)
        {
            for (int y = fromRow; y <= toRow; y++)
            {
                for (int x = -halfExtent; x <= halfExtent; x++)
                {
                    SetCell(new Vector2I(x, -y), _floorSources[FloorTileVariant.Select(x, y)], Vector2I.Zero);
                }
            }
        }

        private static int[] AddSources(TileSet tileSet, string[] paths)
        {
            var ids = new int[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                var atlas = new TileSetAtlasSource
                {
                    Texture = GD.Load<Texture2D>(paths[i]),
                    TextureRegionSize = tileSet.TileSize,
                };
                atlas.CreateTile(Vector2I.Zero);
                ids[i] = tileSet.AddSource(atlas);
            }
            return ids;
        }
    }
}
