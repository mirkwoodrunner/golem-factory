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

        private const string WaterPath = "res://art/docks_water.png";

        // The street's two cobbles, chosen by SandboxLayout.StreetTileVariant.
        private static readonly string[] StreetPaths =
        {
            "res://art/street_cobble.png",
            "res://art/street_cobble_b.png",
        };

        /// <summary>Also pave the market street south of the shop front.</summary>
        [Export] public bool PaintStreet { get; set; }

        private int[] _floorSources;
        private int[] _streetSources;
        private int _waterId;
        private FloorBounds _bounds;
        private int _docksPainted = Docks.Closed;

        /// <summary>The Docks' north row as last painted, for a scenario.</summary>
        public int DocksPainted => _docksPainted;

        public override void _Process(double delta)
        {
            // The Docks open with the first Zeppelin and grow by purchase; their cobbles follow
            // (docs/region-design.md §1). A read of one int a frame.
            if (_bounds != null && PaintStreet && _bounds.DocksNorth != _docksPainted)
            {
                PaintDocks(_bounds.DocksNorth);
            }
        }

        /// <summary>
        /// Paints the Docks' lane and floor with the street's cobbles and the harbour water two rows
        /// under the quay; erases them when a load closes or shrinks them.
        /// </summary>
        public void PaintDocks(int north)
        {
            if (_docksPainted != Docks.Closed)
            {
                foreach (CoreVector2Int cell in Docks.Cells(_docksPainted))
                {
                    if (!Docks.InLane(cell))
                    {
                        EraseCell(new Vector2I(cell.x, -cell.y));
                    }
                }
                for (int x = Docks.LaneWest; x <= Docks.East; x++)
                {
                    EraseCell(new Vector2I(x, -(Docks.Bottom - 1)));
                    EraseCell(new Vector2I(x, -(Docks.Bottom - 2)));
                }
            }
            _docksPainted = north;
            if (north == Docks.Closed)
            {
                return;
            }
            foreach (CoreVector2Int cell in Docks.Cells(north))
            {
                SetCell(new Vector2I(cell.x, -cell.y), _streetSources[SandboxLayout.StreetTileVariant(cell)], Vector2I.Zero);
            }
            for (int x = Docks.West; x <= Docks.East; x++)
            {
                SetCell(new Vector2I(x, -(Docks.Bottom - 1)), _waterId, Vector2I.Zero);
                SetCell(new Vector2I(x, -(Docks.Bottom - 2)), _waterId, Vector2I.Zero);
            }
        }

        /// <summary>The floor's z. Everything drawn on the floor uses -1, above this.</summary>
        public const int FloorZ = -10;

        public override void _Ready()
        {
            var tileSet = new TileSet { TileSize = new Vector2I(GridConversions.CellPixels, GridConversions.CellPixels) };
            int[] sourceIds = AddSources(tileSet, TilePaths);
            int[] streetIds = AddSources(tileSet, StreetPaths);
            _waterId = AddSources(tileSet, new[] { WaterPath })[0];
            TileSet = tileSet;
            _floorSources = sourceIds;
            _streetSources = streetIds;

            // Z-index is global within a canvas layer, so the floor sits well below the -1 that
            // floor-level things use (belts, pipes, the build ghost, prop contact shadows).
            // At z 0 it drew over all of them.
            ZIndex = FloorZ;

            // A TileMapLayer cell's square starts at its top-left corner; GridConversions puts a
            // cell's CENTRE on the cell coordinate. Shift by half a tile so they agree.
            Position = new Vector2(-GridConversions.CellPixels / 2f, -GridConversions.CellPixels / 2f);

            WorldNode world = WorldNode.Find(this);
            FloorBounds bounds = world?.Bounds ?? new FloorBounds();
            _bounds = world?.Setup != null ? bounds : null;
            PaintRows(-bounds.HalfExtent, bounds.NorthExtent, bounds.HalfExtent);

            // A Floor Expansion purchase announces exactly the rows it added; plank them.
            if (world?.Setup != null)
            {
                world.Sandbox.FloorExpansion.RowsAdded += (from, to) => PaintRows(from, to, bounds.HalfExtent);
                world.Sandbox.FloorExpansion.RowsRemoved += (from, to) => EraseRows(from, to, bounds.HalfExtent);
            }

            if (PaintStreet)
            {
                foreach (CoreVector2Int cell in FloorLayout.GetStreetCells())
                {
                    SetCell(new Vector2I(cell.x, -cell.y), streetIds[SandboxLayout.StreetTileVariant(cell)], Vector2I.Zero);
                }

                // The Docks' lane, paved from the start: its gate is roped until they open.
                for (int x = Docks.LaneWest; x < Docks.West; x++)
                {
                    for (int y = Docks.Bottom; y <= Docks.LaneTop; y++)
                    {
                        var lane = new CoreVector2Int(x, y);
                        SetCell(new Vector2I(x, -y), streetIds[SandboxLayout.StreetTileVariant(lane)], Vector2I.Zero);
                    }
                }

                // The town square, cobbled like the street it opens off.
                foreach (CoreVector2Int cell in TownSquare.Cells())
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

        /// <summary>
        /// Takes the planks off rows <paramref name="fromRow"/>..<paramref name="toRow"/>: a
        /// load of a save made before those rows were bought.
        /// </summary>
        public void EraseRows(int fromRow, int toRow, int halfExtent)
        {
            for (int y = fromRow; y <= toRow; y++)
            {
                for (int x = -halfExtent; x <= halfExtent; x++)
                {
                    EraseCell(new Vector2I(x, -y));
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
