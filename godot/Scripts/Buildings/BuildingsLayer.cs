using System.Collections.Generic;
using System.Linq;
using Godot;
using CoreVector2 = GolemFactory.Compat.Vector2;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Data;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Draws every building BuildModeController has placed: one <see cref="BuildingView"/> per
    /// Core <see cref="PlaceableBuilding"/>, created on <c>BuildingPlaced</c>, freed on
    /// <c>BuildingRemoved</c>, and re-shaped on <c>ConnectedShapesChanged</c>.
    ///
    /// <para>
    /// The building's rules all live in Core; a view only reads them. This is Unity's prefab
    /// visuals (a SpriteRenderer, a belt's Lane child, a pipe's piece) without the prefab: the
    /// sprites come from <c>placeables.json</c> by the building's prefab key. Sits in the
    /// y-sorted entity layer, so a depot draws in front of the golem standing behind it; belts
    /// and pipes are floor tiles and draw beneath everything that stands.
    /// </para>
    /// </summary>
    public partial class BuildingsLayer : Node2D
    {
        public const string GroupName = "buildings_layer";

        private readonly Dictionary<PlaceableBuilding, BuildingView> _views = new Dictionary<PlaceableBuilding, BuildingView>();
        private WorldNode _world;
        private Dictionary<string, PlaceableEntry> _entries;

        public int ViewCount => _views.Count;

        /// <summary>Views of the player's buildings: every view but the fixtures' (the Clock Tower).</summary>
        public int PlayerViewCount => _views.Keys.Count(b => !b.IsFixture);

        public bool TryGetView(PlaceableBuilding building, out BuildingView view) => _views.TryGetValue(building, out view);

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            YSortEnabled = true;
            _world = WorldNode.Find(this);
            SandboxWorld sandbox = _world.Sandbox;
            _entries = sandbox.AllPlaceables.ToDictionary(p => p.Key);

            sandbox.Build.BuildingPlaced += OnPlaced;
            sandbox.Build.BuildingRemoved += OnRemoved;
            sandbox.Build.ConnectedShapesChanged += OnShapesChanged;
            foreach (PlaceableBuilding existing in sandbox.Build.Buildings)
            {
                OnPlaced(existing);
            }

            // The Clock Tower stands on its site from the start (G10), a fixture rather than
            // one of the player's buildings.
            if (sandbox.ClockTowerBuilding != null)
            {
                OnPlaced(sandbox.ClockTowerBuilding);
            }
        }

        public override void _ExitTree()
        {
            if (_world?.Sandbox == null)
            {
                return;
            }
            _world.Sandbox.Build.BuildingPlaced -= OnPlaced;
            _world.Sandbox.Build.BuildingRemoved -= OnRemoved;
            _world.Sandbox.Build.ConnectedShapesChanged -= OnShapesChanged;
        }

        private void OnPlaced(PlaceableBuilding building)
        {
            if (_views.ContainsKey(building))
            {
                return;
            }
            _entries.TryGetValue(building.PrefabKey ?? building.name, out PlaceableEntry entry);
            var view = new BuildingView(building, entry, _world);
            _views[building] = view;
            AddChild(view);
        }

        private void OnRemoved(PlaceableBuilding building)
        {
            if (_views.Remove(building, out BuildingView view))
            {
                view.QueueFree();
            }
        }

        private void OnShapesChanged()
        {
            foreach (BuildingView view in _views.Values)
            {
                view.RefreshShape();
            }
        }
    }

    /// <summary>One placed building, drawn.</summary>
    public partial class BuildingView : Node2D
    {
        private readonly PlaceableBuilding _building;
        private readonly PlaceableEntry _entry;
        private readonly WorldNode _world;
        private readonly PlaceableBelt _belt;
        private readonly PlaceableSteamPipe _pipe;
        private readonly PlaceableClockTower _tower;
        private Sprite2D _sprite;
        private readonly Dictionary<string, Texture2D> _itemTextures = new Dictionary<string, Texture2D>();
        private Texture2D _fallbackItem;

        public PlaceableBuilding Building => _building;

        /// <summary>The sprite currently drawn (a belt's lane, a pipe's piece, or the building).</summary>
        public string SpriteName { get; private set; }

        public BuildingView() { } // Godot needs a parameterless constructor for every node type.

        public BuildingView(PlaceableBuilding building, PlaceableEntry entry, WorldNode world)
        {
            _building = building;
            _entry = entry;
            _world = world;
            _belt = building.GetPart<PlaceableBelt>();
            _pipe = building.GetPart<PlaceableSteamPipe>();
            _tower = building.IsFixture ? building.GetPart<PlaceableClockTower>() : null;
            Name = $"{building.name}_{building.Cell.x}_{building.Cell.y}";
            Position = GridConversions.CellToWorld(building.Cell);
            if (_tower != null)
            {
                // The tower's FEET are its footprint's south edge, a cell and a half below its
                // centre cell, and a standing sprite's feet go on its node's origin (y-sort
                // compares origins): so the node moves there, not the picture.
                Position += new Vector2(0f, TownSquare.TowerSize * GridConversions.CellPixels / 2f);
            }
        }

        public override void _Ready()
        {
            _sprite = new Sprite2D();
            AddChild(_sprite);

            if (_belt != null || _pipe != null)
            {
                // Floor tiles: under everything that stands, whatever its y.
                ZIndex = -1;
            }
            if (_belt != null)
            {
                // A belt's TILE goes one lower (-2) than its cargo (this node's _Draw, -1), so
                // every lane is under every item: drawn at one z, an item crossing into the next
                // cell vanished under that cell's tile.
                _sprite.ZIndex = -1;
            }
            RefreshShape();
        }

        public void RefreshShape()
        {
            if (_sprite == null)
            {
                return;
            }

            if (_belt != null && _building.GetPart<PlaceableBeltSplitter>() != null)
            {
                // A splitter has no facing: one picture, never rotated.
                SetSprite(Shape("splitter", "belt_tile"), centred: true);
                _sprite.Rotation = 0f;
                return;
            }

            if (_belt != null)
            {
                // The lane is authored pointing East and rotated to the belt's facing; corners
                // are separate pictures, chosen by Core's BeltShapeRules.
                string role = _belt.Shape switch
                {
                    BeltShape.CornerLeft => "cornerLeft",
                    BeltShape.CornerRight => "cornerRight",
                    _ => "straight",
                };
                SetSprite(Shape(role, "belt_tile"), centred: true);
                _sprite.Rotation = GridConversions.FacingToRotation(_belt.Facing);
                return;
            }

            if (_pipe != null)
            {
                // Five pieces cover all sixteen neighbour masks, each authored East.
                string role = _pipe.Shape switch
                {
                    PipeShape.Straight => "straight",
                    PipeShape.Corner => "corner",
                    PipeShape.Tee => "tee",
                    PipeShape.Cross => "cross",
                    _ => "end",
                };
                SetSprite(Shape(role, "steam_pipe"), centred: true);
                _sprite.Rotation = GridConversions.FacingToRotation(_pipe.ShapeOrientation);
                return;
            }

            if (_tower != null)
            {
                RefreshTower();
                return;
            }

            SetSprite(_entry?.Sprite ?? "ghost_placeholder", centred: false);
        }

        /// <summary>
        /// The Clock Tower on its site (G10) wears the picture for how far it has been built:
        /// roped off, then one per completed stage (Core's ClockTowerArt). Every picture shares
        /// one canvas, its bottom row on this node's origin (the footprint's south edge, see the
        /// constructor), so the stages swap in place.
        /// </summary>
        private void RefreshTower()
        {
            string name = ClockTowerArt.SpriteFor(_tower.Site);
            if (name == SpriteName)
            {
                return;
            }
            SpriteName = name;
            _sprite.Texture = GD.Load<Texture2D>("res://art/" + name + ".png");
            _sprite.Centered = false;
            _sprite.Offset = new Vector2(-ClockTowerArt.CanvasWidth / 2f, -ClockTowerArt.CanvasHeight);
        }

        private string Shape(string role, string fallback) =>
            _entry != null && _entry.ShapeSprites.TryGetValue(role, out string name) && name != null ? name : fallback;

        private void SetSprite(string name, bool centred)
        {
            if (name == SpriteName)
            {
                return;
            }
            SpriteName = name;
            _sprite.Texture = GD.Load<Texture2D>("res://art/" + name + ".png");
            if (centred)
            {
                _sprite.Centered = true;
                _sprite.Offset = Vector2.Zero;
            }
            else
            {
                SpritePivots.Apply(_sprite, name);
            }
        }

        public override void _Process(double delta)
        {
            if (_tower != null)
            {
                RefreshTower(); // a stage completing, or the rope coming down, changes the picture
            }
            if (_belt?.Segment != null)
            {
                QueueRedraw();
            }
        }

        /// <summary>
        /// A belt's cargo, drawn rather than one node per item -- the rule Unity's
        /// BeltSegmentVisual kept with its fixed pool. Along a straight lane from the back edge
        /// to the front, as Unity drew it on corners too, interpolated between ticks.
        /// </summary>
        public override void _Draw()
        {
            BeltSegment segment = _belt?.Segment;
            if (segment == null || segment.Items.Count == 0)
            {
                return;
            }

            // In from the side that feeds it, through the centre, out of the side it hands to --
            // or short of the edge at a dead end (Core's BeltCargoPath).
            BeltNetwork network = _world.Sandbox?.Belts;
            bool isSplitter = _building.GetPart<PlaceableBeltSplitter>() != null;
            CoreVector2 entry = BeltCargoPath.Entry(network, _building.Cell, segment, _belt.Facing, isSplitter);
            CoreVector2 exit = BeltCargoPath.Exit(network, _building.Cell, segment, _belt.Facing, isSplitter);
            float step = _world.Sandbox?.Conveyor.StepPerTick ?? 1f;
            float tickFraction = _world.Clock.TickFraction;
            IReadOnlyList<ItemStack> items = segment.Items;
            for (int i = 0; i < items.Count; i++)
            {
                float predicted = BeltFlowUtility.PredictProgressAfterAdvance(items, i, segment.Length, step);
                float display = BeltFlowUtility.ComputeDisplayProgress(items[i].Progress, predicted, tickFraction);
                CoreVector2 at = BeltCargoPath.Point(entry, exit, display / segment.Length);
                Texture2D texture = ItemTexture(items[i].ItemType);
                // Core's +y is north; Godot's is down.
                var pixel = new Vector2(at.x, -at.y) * GridConversions.CellPixels;
                DrawTexture(texture, pixel - texture.GetSize() / 2f);
            }
        }

        private Texture2D ItemTexture(string itemType)
        {
            if (_itemTextures.TryGetValue(itemType ?? "", out Texture2D cached))
            {
                return cached;
            }
            Texture2D texture = _entry != null && _entry.ShapeSprites.TryGetValue("item:" + itemType, out string name)
                ? GD.Load<Texture2D>("res://art/" + name + ".png")
                : _fallbackItem ??= GD.Load<Texture2D>("res://art/" + Shape("fallbackItem", "ghost_placeholder") + ".png");
            _itemTextures[itemType ?? ""] = texture;
            return texture;
        }
    }
}
