using Godot;
using GolemFactory.Data;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>The two programs the slice needs. Hardcoded: there is no Workbench yet (G7).</summary>
    public enum SliceProgram
    {
        /// <summary>ExtractFromNode from the tile behind, Push onto the tile in front.</summary>
        Extractor,

        /// <summary>Haul Scrap from the tile behind, Push onto the tile in front.</summary>
        Unloader,
    }

    /// <summary>
    /// A golem on the floor: draws a Core <see cref="GolemEntity"/>.
    ///
    /// <para>
    /// Two ways in. A golem a construction station builds already exists, fully wired, when
    /// the station raises <c>GolemSpawned</c>; <see cref="Host"/> wraps it, and this node only
    /// draws. A golem authored into the scene (the slice's unloader) has no station, so this
    /// node wires it itself, call for call the way the station's SpawnGolem does.
    /// </para>
    ///
    /// <para>
    /// The picture follows the entity -- cell and facing are read every frame -- because the
    /// entity is the truth and things other than this node move it (a station's
    /// <c>SetPlacement</c>, the [G] carry in G6).
    /// </para>
    /// </summary>
    public partial class GolemNode : Node2D
    {
        [Export] public string GolemId { get; set; } = "Golem-001";
        [Export] public Vector2I Cell { get; set; }
        [Export] public Facing Facing { get; set; } = Facing.East;
        [Export] public SliceProgram ProgramKind { get; set; } = SliceProgram.Extractor;

        public GolemEntity Entity { get; private set; }

        /// <summary>Completed cycles since spawn, counted from <see cref="EventBus.GolemCompleted"/>.</summary>
        public int CompletedCycles { get; private set; }

        private WorldNode _world;
        private bool _ownsWiring;

        /// <summary>A node for a golem something else already built and wired.</summary>
        public static GolemNode Host(GolemEntity entity) =>
            new GolemNode { Entity = entity, GolemId = entity.GolemId, Name = entity.GolemId };

        public override void _EnterTree() => AddToGroup(GolemNodeGroup.Name);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);

            if (Entity == null)
            {
                // Authored into the scene: no station built it, so wire it here, in SpawnGolem's
                // order.
                _ownsWiring = true;
                Entity = new GolemEntity();
                Entity.Configure(GolemId, _world.Conveyor);
                Entity.ConfigureEconomy(_world.Nodes, _world.Buffers);
                Entity.ConfigureSpatial(_world.Endpoints, GridConversions.ToCore(Cell), Facing);
                Entity.MarkRuntimeSpawned();
                ApplySliceProgram(Entity.Program, ProgramKind, _world.Definitions);
                Entity.Attach();
                _world.Clock.Register(Entity);
            }
            EventBus.GolemCompleted += OnGolemCompleted;

            BuildVisuals();
            SyncToEntity();
        }

        public override void _ExitTree()
        {
            if (Entity == null)
            {
                return;
            }
            EventBus.GolemCompleted -= OnGolemCompleted;
            EventBus.GolemStalled -= OnGolemStalled;
            _sourceTile?.QueueFree();
            _targetTile?.QueueFree();
            if (_ownsWiring)
            {
                _world.Clock.Unregister(Entity);
                Entity.Remove();
            }
        }

        public override void _Process(double delta)
        {
            if (Entity.IsRemoved)
            {
                QueueFree();
                return;
            }
            _time += (float)delta;
            SyncToEntity();
            Animate((float)delta);
            RefreshBadge((float)delta);
        }

        // --- Presentation: Unity's GolemVisual, GolemFacingIndicator, GroundShadow and
        // GolemStallIndicator, in one node. ------------------------------------------------------

        // GolemVisual's tuning (GolemPrefab.prefab).
        private const float BobAmplitude = 0.04f;
        private const float BobFrequency = 2.2f;
        private const float ShakeAmplitude = 0.08f;
        private const float ShakeFrequency = 45f;
        private const float ShakeDuration = 0.35f;
        private const string FallbackSprite = "golem_generic_copper.png";

        // GolemFacingIndicator's tints.
        private static readonly Color SourceTint = new Color(0.37f, 0.90f, 0.84f, 0.72f);
        private static readonly Color TargetTint = new Color(1f, 0.90f, 0.66f, 0.80f);
        private static readonly Color ArrowTint = new Color(1f, 0.80f, 0.42f, 0.95f);

        // GolemStallIndicator: 1.75 cells above the feet.
        private const float BadgeLiftCells = 1.75f;

        private Sprite2D _body;
        private string _bodySprite;
        private Sprite2D _arrow;
        private Sprite2D _sourceTile;
        private Sprite2D _targetTile;
        private CanvasLayer _badgeLayer;
        private PanelContainer _badge;
        private Label _badgeLabel;
        private StyleBoxFlat _badgeStyle;
        private GolemMood _mood = (GolemMood)(-1);
        private float _moodHeld;
        private StallReason _renderedReason;
        private string _renderedResource;
        private int _renderedShortfall;
        private float _shakeLeft;
        private float _time;

        /// <summary>The golem's mood as drawn (tint, bob, badge).</summary>
        public GolemMood DrawnMood => _mood;

        /// <summary>Whether the mood badge is up, and what it says -- for a scenario.</summary>
        public bool BadgeVisible => _badge != null && _badge.Visible;
        public string BadgeText => _badgeLabel?.Text ?? "";

        /// <summary>The chassis sprite currently drawn.</summary>
        public string BodySprite => _bodySprite;

        /// <summary>
        /// Show the source and target tiles: where this golem takes from and gives to. Set by
        /// <see cref="RoutingFocusNode"/> on the nearest golem only, as Unity's
        /// RoutingFocusController did, so the floor is not a carpet of tiles.
        /// </summary>
        public bool ShowTiles { get; set; }

        public bool TilesVisible => _sourceTile != null && _sourceTile.Visible;

        private void BuildVisuals()
        {
            // The contact shadow under the feet, on the floor with the belts.
            Sprite2D shadow = SpritePivots.Make("ground_shadow");
            shadow.ZIndex = -1;
            shadow.ZAsRelative = false;
            AddChild(shadow);

            _body = new Sprite2D();
            AddChild(_body);
            ApplyChassisSprite();

            _arrow = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"), Modulate = ArrowTint, ZIndex = 1 };
            AddChild(_arrow);

            // The routing tiles live on the floor, not on the golem: top-level, so they stay on
            // their cells while the golem bobs and is carried.
            _sourceTile = MakeTile(SourceTint);
            _targetTile = MakeTile(TargetTint);

            _badgeLayer = new CanvasLayer { Layer = 4 };
            AddChild(_badgeLayer);
            _badgeStyle = new StyleBoxFlat { ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
            _badge = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _badge.AddThemeStyleboxOverride("panel", _badgeStyle);
            _badgeLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            _badgeLabel.AddThemeFontSizeOverride("font_size", 12);
            _badgeLabel.AddThemeColorOverride("font_color", Colors.White);
            _badge.AddChild(_badgeLabel);
            _badgeLayer.AddChild(_badge);
            _badgeLabel.AddThemeFontOverride("font", new FontVariation { BaseFont = _badgeLabel.GetThemeDefaultFont(), VariationEmbolden = 0.6f });

            EventBus.GolemStalled += OnGolemStalled;
        }

        private Sprite2D MakeTile(Color tint)
        {
            var tile = new Sprite2D
            {
                Texture = GD.Load<Texture2D>("res://art/build_ghost_tile.png"),
                Modulate = tint,
                TopLevel = true,
                ZIndex = -1,
                ZAsRelative = false,
                Visible = false,
            };
            AddChild(tile);
            return tile;
        }

        /// <summary>The chassis's own art once one is fitted; a generic copper golem before.</summary>
        private void ApplyChassisSprite()
        {
            string sprite = Entity.Program.chassis?.chassisSprite;
            if (string.IsNullOrEmpty(sprite))
            {
                sprite = FallbackSprite;
            }
            if (!sprite.EndsWith(".png"))
            {
                sprite += ".png";
            }
            if (sprite == _bodySprite)
            {
                return;
            }
            _bodySprite = sprite;
            _body.Texture = GD.Load<Texture2D>("res://art/" + sprite);
            GridConversions.StandOnCell(_body);
        }

        private void SyncToEntity()
        {
            // A golem in the player's hands rides with them ([G]); Core says where.
            Player.PlayerInteractor hands = _world.Setup != null ? _world.Sandbox.Interactor : null;
            bool carried = hands != null && hands.CarriedGolem == Entity;
            if (carried)
            {
                Compat.Vector3 at = hands.PositionOf(Entity);
                Position = new Vector2(at.x * GridConversions.CellPixels, -at.y * GridConversions.CellPixels);
            }
            else
            {
                Position = GridConversions.CellToWorld(Entity.Cell);
            }

            ApplyChassisSprite();

            Vector2 step = GridConversions.FacingStep(Entity.Facing);
            _arrow.Rotation = GridConversions.FacingToRotation(Entity.Facing);
            _arrow.Position = step * 0.42f + new Vector2(0f, -0.15f * GridConversions.CellPixels);

            bool tiles = ShowTiles && !carried;
            _sourceTile.Visible = tiles;
            _targetTile.Visible = tiles;
            if (tiles)
            {
                _sourceTile.GlobalPosition = GridConversions.CellToWorld(Entity.SourceCell);
                _targetTile.GlobalPosition = GridConversions.CellToWorld(Entity.TargetCell);
            }
        }

        /// <summary>
        /// GolemVisual: tint by mood, an idle bob paced by the mood, and a short sideways shake
        /// the moment a stall is published. Only the BODY moves; the node stays on its cell, so
        /// the bob never changes what the golem sorts behind.
        /// </summary>
        private void Animate(float delta)
        {
            Compat.Color tint = GolemMoodPalette.Tint(Entity.Mood);
            _body.Modulate = new Color(tint.r, tint.g, tint.b, tint.a);

            if (_shakeLeft > 0f)
            {
                _shakeLeft -= delta;
                float shake = GolemAnimationUtility.ComputeShakeOffset(_time, _shakeLeft, ShakeDuration, ShakeAmplitude, ShakeFrequency);
                _body.Position = new Vector2(shake * GridConversions.CellPixels, 0f);
                return;
            }
            GolemAnimationUtility.BobParameters bob = GolemAnimationUtility.BobFor(Entity.Mood, BobAmplitude, BobFrequency);
            float lift = GolemAnimationUtility.ComputeIdleBobOffset(_time, bob.Amplitude, bob.Frequency);
            _body.Position = new Vector2(0f, -lift * GridConversions.CellPixels);
        }

        /// <summary>
        /// GolemStallIndicator: the mood badge over the golem. A mood must be HELD for its dwell
        /// (GolemMoodRules) before the badge shows, so a golem flickering between two states does
        /// not strobe. A stopped golem's badge names the reason. Placed through WorldHudRegistry
        /// so two badges never stack on one spot.
        /// </summary>
        private void RefreshBadge(float delta)
        {
            GolemMood mood = Entity.Mood;
            if (mood != _mood)
            {
                _mood = mood;
                _moodHeld = 0f;
                Compat.Color badge = GolemMoodPalette.Badge(mood);
                _badgeStyle.BgColor = new Color(badge.r, badge.g, badge.b, badge.a);
                _badge.Scale = Vector2.One * GolemMoodPalette.BadgeScale(mood);
                RefreshCaption(mood);
            }
            else
            {
                _moodHeld += delta;
                if (GolemMoodRules.IsStopped(mood) &&
                    (Entity.StallReason != _renderedReason || Entity.StallResourceId != _renderedResource || Entity.StallShortfall != _renderedShortfall))
                {
                    RefreshCaption(mood);
                }
            }

            bool show = GolemMoodRules.ShouldShowBadge(mood, _moodHeld) && !ModalScreens.AnyOpen(GetTree());
            _badge.Visible = show;
            if (!show)
            {
                return;
            }

            var anchor = new Compat.Vector3(Position.X / GridConversions.CellPixels, -Position.Y / GridConversions.CellPixels + BadgeLiftCells, 0f);
            Compat.Vector3 placed = UI.WorldHudRegistry.Resolve(anchor, Entity.GolemId);
            Vector2 screen = GetViewport().GetCanvasTransform() *
                new Vector2(placed.x * GridConversions.CellPixels, -placed.y * GridConversions.CellPixels);
            _badge.ResetSize();
            _badge.Position = screen - new Vector2(_badge.Size.X * _badge.Scale.X / 2f, _badge.Size.Y * _badge.Scale.Y);
        }

        private void RefreshCaption(GolemMood mood)
        {
            if (!GolemMoodRules.IsStopped(mood))
            {
                _badgeLabel.Text = GolemMoodRules.Caption(mood);
                _renderedReason = StallReason.None;
                _renderedResource = null;
                _renderedShortfall = 0;
                return;
            }
            _renderedReason = Entity.StallReason;
            _renderedResource = Entity.StallResourceId;
            _renderedShortfall = Entity.StallShortfall;
            _badgeLabel.Text = "[!] " + Entity.GolemId + "\n" +
                UI.StallDiagnostics.DescribeShort(_renderedReason, _renderedResource, _renderedShortfall);
        }

        private void OnGolemStalled(GolemStalledEvent e)
        {
            if (e.GolemId == Entity.GolemId)
            {
                _shakeLeft = ShakeDuration;
            }
        }

        private void OnGolemCompleted(GolemCompletedEvent e)
        {
            if (e.GolemId == GolemId)
            {
                CompletedCycles++;
            }
        }

        /// <summary>
        /// Fits the slice's hardcoded program: the authored cards, not copies -- definitions are
        /// shared, as Unity's assets were, and a golem's per-slot quantity lives on its own
        /// GolemProgram (TryAddAppendage seeds it). Assigns the chassis only if the golem has
        /// none, so a station-built golem keeps the one it was paid for.
        /// </summary>
        public static void ApplySliceProgram(GolemProgram program, SliceProgram kind, DefinitionSet definitions)
        {
            if (program.chassis == null)
            {
                program.TryAssignChassis(definitions.Chassis["ClockworkScavenger"]);
            }
            program.logicCore = definitions.LogicCores["AlwaysOnCore"];
            program.TryAddAppendage(definitions.Appendages[kind == SliceProgram.Extractor ? "ExtractScrap" : "HaulScrap"]);
            program.TryAddAppendage(definitions.Appendages["PushOutput"]);
        }
    }
}
