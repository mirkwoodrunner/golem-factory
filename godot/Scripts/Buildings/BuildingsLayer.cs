using System.Collections.Generic;
using System.Linq;
using Godot;
using CoreVector2 = GolemFactory.Compat.Vector2;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Data;
using GolemFactory.Economy;
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

        /// <summary>The short effects (dust, sparks, puffs, pops): one layer, above everything standing.</summary>
        public FxLayer Fx { get; private set; }

        // Buildings drawn at startup are already standing; only one placed later lands.
        private bool _started;

        public override void _Ready()
        {
            YSortEnabled = true;
            _world = WorldNode.Find(this);
            SandboxWorld sandbox = _world.Sandbox;
            Fx = new FxLayer { Name = "Fx" };
            AddChild(Fx);
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
            _started = true;
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
            var view = new BuildingView(building, entry, _world, Fx, landing: _started && !_world.Sandbox.Restoring);
            _views[building] = view;
            AddChild(view);
        }

        private void OnRemoved(PlaceableBuilding building)
        {
            if (_views.Remove(building, out BuildingView view))
            {
                // Coming down: a squash flat and dust, then gone (docs/game-feel-design.md §4). A
                // load clearing the world just clears it.
                if (_world.Sandbox.Restoring)
                {
                    view.QueueFree();
                }
                else
                {
                    view.Demolish();
                }
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
        private readonly HandCrankBench _bench;
        private readonly PlaceableBoiler _boiler;
        private Sprite2D _sprite;
        private PointLight2D _glow;
        private readonly FxLayer _fx;
        private readonly ICrate _crate;

        // The game-feel clocks (FeelRules), in real seconds; below zero, not playing.
        private float _landAge = -1f;
        private float _demolishAge = -1f;
        private float _bumpAge = -1f;
        private float _sincePop = 99f;
        private float _revealAge = -1f;
        private int _deposits;
        private long _nextPuffTick;

        /// <summary>True while it lands, from its placing; a scenario reads it.</summary>
        public bool Landing => _landAge >= 0f && _landAge < FeelRules.PlaceSeconds;

        /// <summary>True from its demolishing until it frees itself.</summary>
        public bool Demolishing => _demolishAge >= 0f;

        /// <summary>A pipe piece's picture while it carries steam (LightingRules.PipeHot).</summary>
        public static string HotPipe(string name) => name + "_hot";

        // Where steam_boiler.png's firebox is, in the picture's own pixels.
        private static readonly Vector2 BoilerFireboxPixel = new Vector2(31f, 76f);
        private readonly Dictionary<string, Texture2D> _itemTextures = new Dictionary<string, Texture2D>();
        private Texture2D _fallbackItem;

        public PlaceableBuilding Building => _building;

        /// <summary>A boiler's firebox glow (LightingRules.BoilerGlow); null on anything else.</summary>
        public PointLight2D Glow => _glow;

        /// <summary>The sprite currently drawn (a belt's lane, a pipe's piece, or the building).</summary>
        public string SpriteName { get; private set; }

        public BuildingView() { } // Godot needs a parameterless constructor for every node type.

        public BuildingView(PlaceableBuilding building, PlaceableEntry entry, WorldNode world, FxLayer fx = null, bool landing = false)
        {
            _fx = fx;
            _crate = building.GetPart<ICrate>();
            _landAge = landing ? 0f : -1f;
            _building = building;
            _entry = entry;
            _world = world;
            _belt = building.GetPart<PlaceableBelt>();
            _pipe = building.GetPart<PlaceableSteamPipe>();
            _tower = building.IsFixture ? building.GetPart<PlaceableClockTower>() : null;
            _bench = building.GetPart<HandCrankBench>();
            _boiler = building.GetPart<PlaceableBoiler>();
            Name = $"{building.name}_{building.Cell.x}_{building.Cell.y}";
            // The tower's FEET are its footprint's south edge, and a standing sprite's feet go on
            // its node's origin (y-sort compares origins): so the node moves there, not the picture.
            Position = _tower != null
                ? GridConversions.FootprintFeet(building.Cell, TownSquare.TowerSize)
                : GridConversions.CellToWorld(building.Cell);
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
            _deposits = _crate?.Buffer?.Deposits ?? 0;
            if (_landAge >= 0f)
            {
                SpawnDust();
            }

            if (_boiler != null)
            {
                _glow = new PointLight2D
                {
                    Name = "BoilerGlow",
                    Texture = LightTexture.Banded(),
                    TextureScale = LightTexture.ScaleFor(LightingRules.BoilerGlowRadiusCells),
                    Color = Ugui.ToGodot(LightingRules.BoilerGlowColor),
                    Position = _sprite.Position + _sprite.Offset + BoilerFireboxPixel,
                };
                AddChild(_glow);
                RefreshGlow();
            }
        }

        private void RefreshGlow()
        {
            SteamBoiler boiler = _boiler.Boiler;
            float energy = boiler == null ? 0f : LightingRules.BoilerGlow(boiler.CokeStock, boiler.WorkingGolemCount);
            _glow.Energy = energy;
            _glow.Enabled = energy > 0f;
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
                // Five pieces cover all sixteen neighbour masks, each authored East; each has a
                // hot picture, worn while a fuelled boiler feeds it (LightingRules.PipeHot).
                string role = _pipe.Shape switch
                {
                    PipeShape.Straight => "straight",
                    PipeShape.Corner => "corner",
                    PipeShape.Tee => "tee",
                    PipeShape.Cross => "cross",
                    _ => "end",
                };
                string piece = Shape(role, "steam_pipe");
                SetSprite(PipeIsHot() ? HotPipe(piece) : piece, centred: true);
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
            // While the stage event glides in, the tower still wears the stage before; the new
            // one appears as the reveal strikes (docs/game-feel-design.md §3).
            Player.CameraDirector camera = _world.Sandbox?.Camera;
            bool hold = camera != null && !camera.TowerRevealed &&
                (camera.TowerPhase == Player.TowerEventPhase.GlideIn || camera.TowerPhase == Player.TowerEventPhase.Reveal);
            bool revealing = _heldBack && !hold;
            _heldBack = hold;
            if (revealing)
            {
                Reveal();
            }
            string name = ClockTowerArt.SpriteFor(_tower.Site, hold);
            if (name == SpriteName)
            {
                return;
            }
            SpriteName = name;
            _sprite.Texture = GD.Load<Texture2D>("res://art/" + name + ".png");
            _sprite.Centered = false;
            _sprite.Offset = new Vector2(-ClockTowerArt.CanvasWidth / 2f, -ClockTowerArt.CanvasHeight);
        }

        private bool PipeIsHot()
        {
            SandboxWorld sandbox = _world.Sandbox;
            return sandbox != null && _pipe.IsRegistered
                && LightingRules.PipeHot(sandbox.Steam.CarriesSteam(_pipe.Cell, sandbox.Clock.CurrentTick));
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

        /// <summary>The building came down: it squashes flat in a puff of dust, then frees itself.</summary>
        public void Demolish()
        {
            if (_demolishAge >= 0f)
            {
                return;
            }
            _demolishAge = 0f;
            SpawnDust();
        }

        private bool IsFloorTile => _belt != null || _pipe != null;

        private void SpawnDust()
        {
            if (_fx == null)
            {
                return;
            }
            // A floor tile raises a little; a standing building a cloud either side of its feet.
            Vector2 at = GlobalPosition + new Vector2(0f, IsFloorTile ? 0f : -6f);
            _fx.Spawn(FxKind.Dust, at + new Vector2(-14f, 0f));
            if (!IsFloorTile)
            {
                _fx.Spawn(FxKind.Dust, at + new Vector2(14f, 0f));
            }
        }

        /// <summary>The landing, the coming down, a crate's bump and the tower's reveal, on the sprite.</summary>
        private void UpdateFeel(float delta)
        {
            if (_demolishAge >= 0f)
            {
                _demolishAge += delta;
                var d = FeelRules.DemolishSquash(_demolishAge);
                _sprite.Scale = new Vector2(d.x, d.y);
                _sprite.Modulate = new Color(1f, 1f, 1f, d.alpha);
                if (_demolishAge >= FeelRules.DemolishSeconds)
                {
                    QueueFree();
                }
                return;
            }

            float sx = 1f, sy = 1f;
            if (_landAge >= 0f && _landAge < FeelRules.PlaceSeconds)
            {
                _landAge += delta;
                if (!IsFloorTile)
                {
                    (sx, sy) = FeelRules.PlaceSquash(_landAge);
                }
            }
            if (_bumpAge >= 0f)
            {
                _bumpAge += delta;
                var b = FeelRules.CrateBump(_bumpAge);
                sx *= b.x;
                sy *= b.y;
                if (_bumpAge >= FeelRules.CratePopSeconds)
                {
                    _bumpAge = -1f;
                }
            }
            if (_revealAge >= 0f)
            {
                _revealAge += delta;
                var r = FeelRules.TowerReveal(_revealAge);
                sx *= r.scale;
                sy *= r.scale;
                _sprite.Modulate = new Color(1f + 0.5f * r.flash, 1f + 0.4f * r.flash, 1f + 0.2f * r.flash);
                if (_revealAge >= FeelRules.TowerRevealSeconds)
                {
                    _revealAge = -1f;
                    _sprite.Modulate = Colors.White;
                }
            }
            // Floor tiles rotate about their centre; a squash on them would read as a wobble.
            if (!IsFloorTile)
            {
                _sprite.Scale = new Vector2(sx, sy);
            }
        }

        /// <summary>A good landed in this crate: the crate bumps and the good pops out of the lid.</summary>
        private void UpdateCrate(float delta)
        {
            _sincePop += delta;
            StorageBuffer buffer = _crate?.Buffer;
            if (buffer == null || buffer.Deposits == _deposits)
            {
                return;
            }
            _deposits = buffer.Deposits;
            if (_sincePop < FeelRules.CratePopMinInterval || _fx == null)
            {
                return;
            }
            _sincePop = 0f;
            _bumpAge = 0f;
            Vector2 lid = GlobalPosition + new Vector2(0f, -40f);
            _fx.Spawn(FxKind.GoodPop, lid, ItemIcons.Load(buffer.LastDepositedType));
            _fx.Spawn(FxKind.Glint, lid + new Vector2(10f, -6f));
        }

        /// <summary>A burning boiler puffs from its chimney on the factory's clock, so not while paused.</summary>
        private void UpdateBoilerPuff()
        {
            SandboxWorld sandbox = _world.Sandbox;
            SteamBoiler boiler = _boiler.Boiler;
            if (_fx == null || boiler == null || sandbox == null || boiler.CokeStock <= 0 || boiler.WorkingGolemCount <= 0)
            {
                return;
            }
            long now = sandbox.Clock.CurrentTick;
            if (now < _nextPuffTick)
            {
                return;
            }
            _nextPuffTick = now + (long)System.Math.Max(1f, FeelRules.BoilerPuffInterval * sandbox.Clock.TicksPerSecond);
            Vector2 top = _sprite.Position + _sprite.Offset + new Vector2(_sprite.Texture.GetWidth() / 2f + 1f, 4f);
            _fx.Spawn(FxKind.Puff, GlobalPosition + top);
        }

        private bool _heldBack;

        /// <summary>True while the tower's swell and flash play.</summary>
        public bool Revealing => _revealAge >= 0f;

        /// <summary>The Clock Tower's new stage appears: a swell, a warm flash and dust.</summary>
        private void Reveal()
        {
            _revealAge = 0f;
            if (_fx != null)
            {
                for (int i = -2; i <= 2; i++)
                {
                    _fx.Spawn(FxKind.Dust, GlobalPosition + new Vector2(i * 40f, -4f));
                }
            }
        }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.buildings");
            UpdateFeel((float)delta);
            if (_crate != null)
            {
                UpdateCrate((float)delta);
            }
            if (_boiler != null)
            {
                UpdateBoilerPuff();
            }
            if (_tower != null)
            {
                RefreshTower(); // a stage completing, or the rope coming down, changes the picture
            }
            if (_bench != null)
            {
                // The wheel turns while the bench makes progress (HandCrankBench.SpriteFor).
                SetSprite(_bench.SpriteFor(_entry?.Sprite ?? "ghost_placeholder"), centred: false);
            }
            if (_glow != null)
            {
                RefreshGlow();
            }
            if (_pipe != null)
            {
                RefreshShape(); // hot or cold follows the boilers' Coke, which no shape change reports
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
            using var perf = NodePerf.Measure("node.buildings");
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
