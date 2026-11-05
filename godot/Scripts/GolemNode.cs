using GolemFactory.Settings;
using System.Collections.Generic;
using Godot;
using GolemFactory.Data;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
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
            _dropTile?.QueueFree();
            _badge?.QueueFree(); // on the shared layer, not under this node
            if (_ownsWiring)
            {
                _world.Clock.Unregister(Entity);
                Entity.Remove();
            }
        }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.golems");
            if (Entity.IsRemoved)
            {
                QueueFree();
                return;
            }
            _time += (float)delta;
            SyncToEntity();
            RefreshBadge((float)delta);
            Animate((float)delta);
        }

        // --- Presentation: Unity's GolemVisual, GolemFacingIndicator, GroundShadow and
        // GolemStallIndicator, in one node. ------------------------------------------------------

        // GolemVisual's tuning (GolemPrefab.prefab).
        private const float BobAmplitude = 0.04f;
        private const float BobFrequency = 2.2f;
        private const float ShakeAmplitude = 0.08f;
        private const float ShakeFrequency = 45f;
        private const float ShakeDuration = 0.35f;

        // GolemFacingIndicator's tints; the tiles' own live in Core, where LightingRulesTests reads them.
        private static readonly Color SourceTint = Ugui.ToGodot(GolemTileOverlay.SourceTint);
        private static readonly Color TargetTint = Ugui.ToGodot(GolemTileOverlay.TargetTint);
        private static readonly Color ArrowTint = new Color(1f, 0.80f, 0.42f, 0.95f);

        // GolemStallIndicator: 1.75 cells above the feet.
        private const float BadgeLiftCells = 1.75f;

        private Sprite2D _body;
        private string _bodySprite;
        private Sprite2D _arrow;
        private Sprite2D _sourceTile;
        private Sprite2D _sourceStripes;

        /// <summary>Whether the source tile wears its colour-blind stripes: for a scenario.</summary>
        public bool SourceStripesVisible => _sourceStripes != null && _sourceStripes.IsVisibleInTree();

        // Dark teal ink: reads on the pale teal tile over any floor, lit or not.
        private static readonly Color StripeInk = new Color(0.03f, 0.20f, 0.20f, 0.62f);
        private static Texture2D _stripes;

        /// <summary>
        /// The source tile's stripes (Core's <see cref="ColourMarks.StripeAt"/>), inside the tile's
        /// 3-pixel border so its outline still reads. Made once and shared by every golem.
        /// </summary>
        private static Texture2D StripeTexture()
        {
            if (_stripes != null)
            {
                return _stripes;
            }
            const int size = 64, border = 3;
            Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            for (int y = border; y < size - border; y++)
            {
                for (int x = border; x < size - border; x++)
                {
                    if (ColourMarks.StripeAt(x, y))
                    {
                        image.SetPixel(x, y, StripeInk);
                    }
                }
            }
            _stripes = ImageTexture.CreateFromImage(image);
            return _stripes;
        }
        private Sprite2D _targetTile;
        private Sprite2D _sourceArrow;
        private Sprite2D _targetArrow;
        private Sprite2D _dropTile;
        private Sprite2D _dropArrow;
        private GolemMood _rawMood = (GolemMood)(-1);
        private float _rawHeld;
        private bool _shakePending;
        private bool _wasStopped;
        private CanvasLayer _badgeLayer;
        private PanelContainer _badge;
        private Label _badgeLabel;
        private StyleBoxFlat _badgeStyle;
        private GolemMood _mood = (GolemMood)(-1);
        private float _moodHeld;
        private float _shakeLeft;
        private float _time;

        /// <summary>The golem's mood as drawn (tint, bob, badge).</summary>
        public GolemMood DrawnMood => _mood;

        /// <summary>The body's scale as drawn: the work motion's squash and stretch.</summary>
        public Vector2 DrawnScale => _body.Scale;

        /// <summary>The body's lean, offset from its cell and tint as drawn -- for a scenario.</summary>
        public float DrawnSkew => _body.Skew;
        public Vector2 DrawnOffset => _body.Position;

        public Color DrawnModulate => _body.Modulate;

        /// <summary>Whether the mood badge is up, and what it says -- for a scenario.</summary>
        public bool BadgeVisible => _badge != null && _badge.Visible;
        public string BadgeText => _badgeLabel?.Text ?? "";

        /// <summary>The badge's rect on screen, as drawn -- for a scenario's overlap check.</summary>
        public Rect2 BadgeRect => new Rect2(_badge.Position, _badge.Size * _badge.Scale);

        /// <summary>The chassis sprite currently drawn.</summary>
        public string BodySprite => _bodySprite;

        /// <summary>
        /// Show the source and target tiles: where this golem takes from and gives to. Set by
        /// <see cref="RoutingFocusNode"/> on the nearest golem only, as Unity's
        /// RoutingFocusController did, so the floor is not a carpet of tiles.
        /// </summary>
        public bool ShowTiles { get; set; }

        /// <summary>The mouse is over this golem (<c>GolemHoverNode</c>): its tiles show while it is.</summary>
        public bool Hovered { get; set; }

        /// <summary>Its hover card is up, so its badge hides: one golem, one readout.</summary>
        public bool HoverCardUp { get; set; }

        public bool TilesVisible => _sourceTile != null && _sourceTile.Visible;

        /// <summary>Whether the facing arrow over the golem is drawn (only while hovered) -- for a scenario.</summary>
        public bool FacingArrowVisible => _arrow != null && _arrow.Visible;

        /// <summary>The source tile's arrow, as drawn -- for a scenario.</summary>
        public bool TileArrowsVisible => TilesVisible && _sourceArrow.Visible && _targetArrow.Visible;
        public int TileZ => _sourceTile.ZIndex;

        /// <summary>Whether the landing outline is up (while carried), and where -- for a scenario.</summary>
        public bool DropPreviewVisible => _dropTile != null && _dropTile.Visible;
        public Compat.Vector2Int DropPreviewCell => GridConversions.WorldToCell(_dropTile.GlobalPosition);
        public Color DropPreviewTint => _dropTile.Modulate;

        /// <summary>The source and target tiles, as drawn -- for a scenario.</summary>
        public Compat.Vector2Int SourceTileCell => GridConversions.WorldToCell(_sourceTile.GlobalPosition);
        public Compat.Vector2Int TargetTileCell => GridConversions.WorldToCell(_targetTile.GlobalPosition);

        private void BuildVisuals()
        {
            // The contact shadow under the feet, on the floor with the belts.
            Sprite2D shadow = SpritePivots.Make("ground_shadow");
            shadow.ZIndex = -1;
            shadow.ZAsRelative = false;
            AddChild(shadow);

            _body = new Sprite2D();
            AddChild(_body);
            ApplyFrame(carried: false);

            _arrow = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"), Modulate = ArrowTint, ZIndex = 1 };
            AddChild(_arrow);

            // The routing tiles live on the floor, not on the golem: top-level, so they stay on
            // their cells while the golem bobs and is carried.
            _sourceTile = MakeTile(SourceTint);
            _targetTile = MakeTile(TargetTint);
            _sourceArrow = MakeTileArrow(_sourceTile);
            _targetArrow = MakeTileArrow(_targetTile);
            _sourceStripes = new Sprite2D { Name = "Stripes", Texture = StripeTexture(), Visible = false };
            _sourceTile.AddChild(_sourceStripes);
            _sourceTile.MoveChild(_sourceStripes, 0); // under the arrow

            // The landing outline while carried: the build ghost's tile and colours (red where
            // [G] would refuse), with the facing arrow on it as the build cursor draws one.
            _dropTile = MakeTile(Colors.White);
            _dropArrow = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/facing_arrow.png") };
            _dropTile.AddChild(_dropArrow);

            _badgeLayer = SharedBadgeLayer();
            _badgeStyle = new StyleBoxFlat { ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
            _badge = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _badge.AddThemeStyleboxOverride("panel", _badgeStyle);
            _badgeLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            _badgeLabel.AddThemeFontSizeOverride("font_size", 12);
            _badgeLabel.AddThemeColorOverride("font_color", Colors.White);
            _badge.AddChild(_badgeLabel);
            _badgeLayer.AddChild(_badge);
            _badgeLabel.AddThemeFontOverride("font", _badgeFont ??= new FontVariation { BaseFont = _badgeLabel.GetThemeDefaultFont(), VariationEmbolden = 0.6f });

            EventBus.GolemStalled += OnGolemStalled;
        }

        // Every golem's badge on one CanvasLayer (4), in one emboldened font. A layer and a
        // FontVariation each per golem drew the same badges, but as 96 canvases with 96 glyph
        // caches, which broke every batch (the perf scenario's starved phase,
        // docs/performance-report-phase3.md).
        private static FontVariation _badgeFont;

        public const string BadgeLayerName = "GolemBadges";

        private static CanvasLayer _sharedBadgeLayer;

        private CanvasLayer SharedBadgeLayer()
        {
            if (_sharedBadgeLayer != null && IsInstanceValid(_sharedBadgeLayer))
            {
                return _sharedBadgeLayer;
            }
            // Deferred: a golem authored into a scene is readied while its parents are still
            // adding children, and the scene root refuses a child then.
            _sharedBadgeLayer = new CanvasLayer { Name = BadgeLayerName, Layer = 4 };
            (GetTree().CurrentScene ?? GetTree().Root).CallDeferred(Node.MethodName.AddChild, _sharedBadgeLayer);
            return _sharedBadgeLayer;
        }

        /// <summary>
        /// The facing arrow on a routing tile, at the tile's centre and 0.6 scale. The tile's
        /// tint is its SELF-modulate, so the arrow keeps its own colour; relative z 0 keeps it
        /// at the tile's global −1 (onboarding-feedback-design §4.2).
        /// </summary>
        private static Sprite2D MakeTileArrow(Sprite2D tile)
        {
            tile.SelfModulate = tile.Modulate;
            tile.Modulate = Colors.White;
            var arrow = new Sprite2D
            {
                Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"),
                Modulate = TileArrowTint,
                Scale = Vector2.One * 0.6f,
            };
            tile.AddChild(arrow);
            return arrow;
        }

        /// <summary>Above every standing sprite (global z 1) over a building; the tile's own z otherwise.</summary>
        private static void LiftArrow(Sprite2D arrow, bool covered)
        {
            arrow.ZAsRelative = !covered;
            arrow.ZIndex = covered ? 1 : 0;
        }

        /// <summary>Whether a tile's arrow rides above a building -- for a scenario.</summary>
        public bool SourceArrowLifted => !_sourceArrow.ZAsRelative && _sourceArrow.ZIndex > 0;
        public bool TargetArrowLifted => !_targetArrow.ZAsRelative && _targetArrow.ZIndex > 0;

        private static readonly Color TileArrowTint = new Color(1f, 0.96f, 0.86f, 0.95f);

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

        // Which file each (chassis, frame) resolves to, walked once: GolemFrames.Resolve's
        // fallback asks ResourceLoader.Exists, which is too slow for every golem every frame.
        private static readonly Dictionary<(string stem, GolemFrameKey key), string> ResolvedFrames =
            new Dictionary<(string, GolemFrameKey), string>();

        /// <summary>The frame GolemFrames picked this frame (a diagnostic for scenarios).</summary>
        public GolemFrameKey DrawnFrame { get; private set; }

        /// <summary>
        /// A diagnostic seam for the <c>art</c> scenario's gallery (Core's <c>ArtGallery</c>):
        /// when set, the body shows this frame whatever the golem is doing. Null in play.
        /// </summary>
        public GolemFrameKey? PinnedFrame { get; set; }

        /// <summary>
        /// The body's picture (docs/art-style-bible.md §4.4): Core picks the frame from the mood,
        /// the signature stroke, carrying and the facing; the file is resolved through
        /// GolemFrames' fallback, so with only a chassis's south base every frame is that base,
        /// and before a chassis is fitted, the generic copper golem. Only the texture changes.
        /// </summary>
        private void ApplyFrame(bool carried)
        {
            GolemProgram program = Entity.Program;
            string stem = GolemFrames.Stem(program.chassis?.chassisSprite);
            GolemWorkMotion.Motion motion = GolemWorkMotion.Motion.None;
            float phase = 0f;
            AppendageActionDefinition step = program.CurrentStep;
            if (program.State == GolemState.Running && step != null && program.StepProgressTicks > 0)
            {
                motion = GolemFrames.WorkMotionFor(program.chassis?.name, step.actionType);
                if (motion != GolemWorkMotion.Motion.None)
                {
                    float fraction = _world?.Clock != null && _world.Clock.State == Simulation.ClockState.Running
                        ? _world.Clock.TickFraction
                        : 0f;
                    phase = GolemFrames.StrokePhase(motion, program.StepProgressTicks + fraction, Entity.StepDurationTicks);
                }
            }
            GolemFrameKey key = PinnedFrame ?? GolemFrames.FrameFor(_mood, motion, phase, carried, _time, Entity.Facing);
            DrawnFrame = key;

            if (!ResolvedFrames.TryGetValue((stem, key), out string sprite))
            {
                sprite = GolemFrames.Resolve(stem, key, name => ResourceLoader.Exists("res://art/" + name));
                ResolvedFrames[(stem, key)] = sprite;
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

            ApplyFrame(carried);

            Vector2 step = GridConversions.FacingStep(Entity.Facing);
            // Only while hovered: drawn on every golem, it covered the art (the user, 2026-10-24).
            _arrow.Visible = Hovered;
            _arrow.Rotation = GridConversions.FacingToRotation(Entity.Facing);
            _arrow.Position = step * 0.42f + new Vector2(0f, -0.15f * GridConversions.CellPixels);

            // Carried, the golem is drawn in the player's hands, half a tile above the tile it
            // will land on -- so the floor shows that tile instead, and what the golem would
            // take from and push to from there, before [G] commits to it.
            Compat.Vector2Int? landing = carried ? hands.CarryDropCell : null;
            _dropTile.Visible = landing != null;
            if (landing is Compat.Vector2Int drop)
            {
                _dropTile.GlobalPosition = GridConversions.CellToWorld(drop);
                Compat.Color tint = BuildGhostVisuals.Evaluate(
                    BuildGhostVisuals.Classify(hands.CarryDropBlocked, affordable: true), _time,
                    pulse: _world?.Sandbox?.Motion.Pulse ?? true);
                _dropTile.Modulate = new Color(tint.r, tint.g, tint.b, tint.a);
                _dropArrow.Rotation = GridConversions.FacingToRotation(Entity.Facing);
                _dropArrow.Position = step * 0.28f;
            }

            // One answer for carry, focus and hover (GolemTileOverlay): carried, the tiles hang
            // off the landing; otherwise off the golem while it is focused or hovered.
            GolemTileOverlay.Decision tiles = GolemTileOverlay.Decide(Hovered, ShowTiles, landing, carried, Entity.Cell);
            _sourceTile.Visible = tiles.Show;
            _targetTile.Visible = tiles.Show;
            _sourceStripes.Visible = ColourMarks.Enabled; // shape as well as colour (menus-design §5)
            if (tiles.Show)
            {
                _sourceTile.GlobalPosition = GridConversions.CellToWorld(FacingUtility.SourceCell(tiles.Cell, Entity.Facing));
                _targetTile.GlobalPosition = GridConversions.CellToWorld(FacingUtility.TargetCell(tiles.Cell, Entity.Facing));
                // Goods flow one way: into the golem on the teal tile, out of it on the gold one,
                // so both arrows point along its facing.
                float rotation = GridConversions.FacingToRotation(Entity.Facing);
                _sourceArrow.Rotation = rotation;
                _targetArrow.Rotation = rotation;
                // A crate or stall on the tile covers it; its arrow then rides above the building
                // (GolemTileOverlay.Covered), else it stays on the floor with its tile.
                SpatialEndpointRegistry endpoints = WorldNode.Find(this)?.Sandbox?.Endpoints;
                LiftArrow(_sourceArrow, GolemTileOverlay.Covered(endpoints, FacingUtility.SourceCell(tiles.Cell, Entity.Facing)));
                LiftArrow(_targetArrow, GolemTileOverlay.Covered(endpoints, FacingUtility.TargetCell(tiles.Cell, Entity.Facing)));
            }
        }

        /// <summary>
        /// GolemVisual: tint by mood, an idle bob paced by the mood, and a short sideways shake
        /// the moment a stall is published. Only the BODY moves; the node stays on its cell, so
        /// the bob never changes what the golem sorts behind.
        /// </summary>
        private void Animate(float delta)
        {
            Compat.Color tint = GolemMoodPalette.Tint(_mood);
            _body.Modulate = new Color(tint.r, tint.g, tint.b, tint.a);

            World.MotionPreference motion = _world?.Sandbox?.Motion;
            if (motion != null && !motion.GolemSway)
            {
                // Reduced motion: the golem stands still on its cell. Its frames (ApplyFrame) still
                // show what it is doing, and its tint and badge its mood.
                _shakeLeft = 0f;
                _body.Scale = Vector2.One;
                _body.Skew = 0f;
                _body.Position = Vector2.Zero;
                bool impact = TryWorkPose(out GolemWorkMotion.Pose still) && still.Impact;
                AnimatePuffs(delta, impact);
                return;
            }

            if (_shakeLeft > 0f)
            {
                _shakeLeft -= delta;
                float shake = GolemAnimationUtility.ComputeShakeOffset(_time, _shakeLeft, ShakeDuration, ShakeAmplitude, ShakeFrequency);
                _body.Scale = Vector2.One;
                _body.Skew = 0f;
                _body.Position = new Vector2(shake * GridConversions.CellPixels, 0f);
                return;
            }
            bool working = TryWorkPose(out GolemWorkMotion.Pose pose);
            AnimatePuffs(delta, working && pose.Impact);
            if (working)
            {
                // A step with a work motion replaces the bob: the stroke is the "working" signal.
                // Forward and Tilt are along the facing; a lean only reads side-on, so a golem
                // facing north or south shifts without leaning.
                Vector2 forward = GridConversions.FacingStep(Entity.Facing);
                _body.Scale = new Vector2(pose.ScaleX, pose.ScaleY);
                _body.Skew = pose.Tilt * Mathf.Sign(forward.X);
                _body.Position = forward * pose.Forward + new Vector2(0f, -pose.Lift * GridConversions.CellPixels);
                if (pose.Glow > 0f)
                {
                    _body.Modulate = _body.Modulate.Lerp(_body.Modulate * GlowTint, pose.Glow);
                }
                return;
            }

            _body.Scale = Vector2.One;
            _body.Skew = 0f;
            GolemAnimationUtility.BobParameters bob = GolemAnimationUtility.BobFor(_mood, BobAmplitude, BobFrequency);
            float lift = GolemAnimationUtility.ComputeIdleBobOffset(_time, bob.Amplitude, bob.Frequency);
            _body.Position = new Vector2(0f, -lift * GridConversions.CellPixels);
        }

        // What the Overclocker's glow multiplies its tint toward: a forge-orange over-brightening.
        private static readonly Color GlowTint = new Color(1.6f, 1.15f, 0.7f, 1f);

        // Two steam puffs, one each side of the feet, reused for every stamp.
        private const string PuffSprite = "res://art/fx_steam_puff.png";
        private readonly Sprite2D[] _puffs = new Sprite2D[2];
        private float _puffAge = GolemWorkMotion.PuffSeconds;
        private bool _wasImpact;
        private FxLayer _fx;

        /// <summary>
        /// Starts the puffs on the frame a stamp lands, and moves them for their short life.
        /// They are the golem's children so they stand on its cell, and draw over its feet.
        /// </summary>
        private void AnimatePuffs(float delta, bool impact)
        {
            if (impact && !_wasImpact)
            {
                _puffAge = 0f;
                // The Overclocker runs hot: its stamp throws sparks too (docs/game-feel-design.md §4).
                if (Entity.Program.CurrentStep != null &&
                    GolemWorkMotion.MotionFor(Entity.Program.chassis?.name, Entity.Program.CurrentStep.actionType) == GolemWorkMotion.Motion.FastStamp)
                {
                    (_fx ??= FxLayer.Find(this))?.Spawn(FxKind.Spark, GlobalPosition + new Vector2(0f, -0.3f * GridConversions.CellPixels));
                }
            }
            _wasImpact = impact;
            _puffAge += delta;
            if (_world?.Sandbox?.Motion.Particles == false)
            {
                _puffAge = GolemWorkMotion.PuffSeconds; // no puffs on Reduced motion
            }

            // No puff art yet (it is to come from ComfyUI): the stamp still squashes, without puffs.
            if (!ResourceLoader.Exists(PuffSprite))
            {
                return;
            }

            var puff = GolemWorkMotion.Puff(_puffAge);
            for (int i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i] == null)
                {
                    if (puff.alpha <= 0f)
                    {
                        continue;
                    }
                    _puffs[i] = new Sprite2D { Texture = GD.Load<Texture2D>(PuffSprite), ShowBehindParent = false, ZIndex = 0 };
                    AddChild(_puffs[i]);
                }

                float side = i == 0 ? -1f : 1f;
                _puffs[i].Visible = puff.alpha > 0f;
                _puffs[i].Position = new Vector2(side * puff.drift, -puff.rise - 0.05f) * GridConversions.CellPixels;
                _puffs[i].Scale = new Vector2(puff.scale * side, puff.scale);
                _puffs[i].Modulate = new Color(1f, 1f, 1f, puff.alpha);
            }
        }

        /// <summary>
        /// The pose for the step the golem is running, if it has one. Progress includes the
        /// clock's tick fraction, so the stroke moves smoothly between ten-a-second ticks.
        /// </summary>
        private bool TryWorkPose(out GolemWorkMotion.Pose pose)
        {
            float fraction = _world?.Clock != null && _world.Clock.State == Simulation.ClockState.Running
                ? _world.Clock.TickFraction
                : 0f;
            return GolemWorkMotion.TryPose(Entity, fraction, out pose);
        }

        /// <summary>
        /// GolemStallIndicator: the mood badge over the golem. A mood must be HELD for its dwell
        /// (GolemMoodRules) before the badge shows, so a golem flickering between two states does
        /// not strobe. A stopped golem's badge names the reason. Placed through WorldHudRegistry
        /// so two badges never stack on one spot.
        /// </summary>
        private void RefreshBadge(float delta)
        {
            // The classified mood has a stopwatch of its own; what is DRAWN is the presented one
            // (GolemMoodRules.Presented), so Waiting's 2 s dwell covers the tint as well as the badge.
            GolemMood raw = Entity.Mood;
            if (raw != _rawMood)
            {
                _rawMood = raw;
                _rawHeld = 0f;
            }
            else
            {
                _rawHeld += delta;
            }

            // The entry shake: only for a golem that has just STOPPED. One that starts waiting
            // doesn't jolt, nor does a collecting Haul each time a good arrives.
            bool stopped = GolemMoodRules.IsStopped(raw);
            if (_shakePending)
            {
                _shakePending = false;
                if (stopped && !_wasStopped)
                {
                    _shakeLeft = ShakeDuration;
                }
            }
            _wasStopped = stopped;

            GolemMood mood = GolemMoodRules.Presented(raw, _rawHeld, (int)_mood < 0 ? raw : _mood);
            if (mood != _mood)
            {
                _mood = mood;
                _moodHeld = 0f;
                Compat.Color badge = GolemMoodPalette.Badge(mood);
                _badgeStyle.BgColor = new Color(badge.r, badge.g, badge.b, badge.a);
                _badge.Scale = Vector2.One * GolemMoodPalette.BadgeScale(mood);
                _badgeResizes = BadgeResizeFrames;
            }
            else
            {
                _moodHeld += delta;
            }

            // Held for its dwell since it was CLASSIFIED, once drawn: Waiting's badge comes with
            // its tint, not 2 s after it.
            float held = mood == raw ? _rawHeld : _moodHeld;
            bool show = GolemMoodRules.ShouldShowBadge(mood, held) && !HoverCardUp && !ScreensOpen(GetTree());
            if (show)
            {
                RefreshCaption(mood);
            }
            _badge.Visible = show;
            if (!show)
            {
                return;
            }

            var anchor = new Compat.Vector3(Position.X / GridConversions.CellPixels, -Position.Y / GridConversions.CellPixels + BadgeLiftCells, 0f);
            Transform2D canvas = GetViewport().GetCanvasTransform();
            if (_badgeResizes > 0)
            {
                // Only after its words or its scale changed (and the frame after, while the label
                // settles): a reset every frame re-laid out every badge, ~25 µs each
                // (docs/performance-report-phase3.md).
                _badgeResizes--;
                _badge.ResetSize();
            }
            // Its drawn width in cells, so the layout keeps it off a neighbour's badge two cells
            // away as well as one in its own column (WorldHudLayout.SpreadWideLabels).
            float cellOnScreen = GridConversions.CellPixels * Mathf.Max(0.01f, canvas.X.Length());
            float halfWidth = _badge.Size.X * _badge.Scale.X / cellOnScreen / 2f;
            float height = _badge.Size.Y * _badge.Scale.Y / cellOnScreen;
            Compat.Vector3 placed = UI.WorldHudRegistry.Resolve(anchor, Entity.GolemId, halfWidth, height);
            Vector2 screen = canvas *
                new Vector2(placed.x * GridConversions.CellPixels, -placed.y * GridConversions.CellPixels);
            _badge.Position = screen - new Vector2(_badge.Size.X * _badge.Scale.X / 2f, _badge.Size.Y * _badge.Scale.Y);

            // Faded while the player stands close, so the badge stops hiding the very tiles it
            // is about -- the golem's own cell and the pipe running into it (G10, from
            // playtest). From across the room, where it is a call to come over, it stays solid.
            _badge.Modulate = new Color(1f, 1f, 1f, BadgeAlphaFor(PlayerDistanceCells()));
        }

        /// <summary>Badge opacity at a player distance, in cells: solid far off, faint up close.</summary>
        public static float BadgeAlphaFor(float distanceCells) =>
            distanceCells < BadgeFadeNearCells ? BadgeFadedAlpha
            : distanceCells > BadgeFadeFarCells ? 1f
            : Mathf.Lerp(BadgeFadedAlpha, 1f, (distanceCells - BadgeFadeNearCells) / (BadgeFadeFarCells - BadgeFadeNearCells));

        private const float BadgeFadeNearCells = 2f;
        private const float BadgeFadeFarCells = 3.5f;
        private const float BadgeFadedAlpha = 0.25f;

        private float PlayerDistanceCells()
        {
            GolemFactory.World.SandboxWorld sandbox = WorldNode.Find(this)?.Sandbox;
            if (sandbox?.Interactor == null)
            {
                return float.MaxValue;
            }
            Compat.Vector3 player = sandbox.Interactor.Position;
            float dx = player.x - Entity.Cell.x, dy = player.y - Entity.Cell.y;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private const int BadgeResizeFrames = 2;
        private int _badgeResizes = BadgeResizeFrames;
        private UI.TileNames _tileNames;

        private UI.TileNames TileNames() => _tileNames ??= WorldNode.Find(this)?.Sandbox?.TileNames;

        private static ulong _screensFrame = ulong.MaxValue;
        private static bool _screensOpen;

        /// <summary>
        /// Whether a full screen is up, asked once a frame for every badge rather than once per
        /// golem: the group scan cost ~10 µs a golem at the endgame's 96.
        /// </summary>
        private static bool ScreensOpen(SceneTree tree)
        {
            ulong frame = Engine.GetProcessFrames();
            if (frame != _screensFrame)
            {
                _screensFrame = frame;
                _screensOpen = ModalScreens.AnyOpen(tree);
            }
            return _screensOpen;
        }

        /// <summary>
        /// The badge's words. A collecting Haul shows its progress ("5/10 Scrap"); a waiting
        /// golem what it waits for; a stopped one "[!] " and the short reason -- no id line, which
        /// is on the hover card now (onboarding-feedback-design §3.4, §13.2).
        /// </summary>
        private void RefreshCaption(GolemMood mood)
        {
            string progress = Entity.TryGetHaulProgress(out string good, out int held, out int batch)
                ? UI.StallDiagnostics.HaulProgress(held, batch, good)
                : null;
            string text;
            if (GolemMoodRules.IsStopped(mood) && !GolemMoodRules.IsStopped(_rawMood))
            {
                return; // red until the grey has been held: keep the red words with the red plate
            }
            if (GolemMoodRules.IsStopped(mood))
            {
                // In player names ("Crate (2,-1) full"), through the TileNames the hover card
                // reads: the ids ("Crate(2,-1)", "ScrapNode") used to show verbatim.
                text = "[!] " + (progress ?? UI.StallDiagnostics.DescribeShort(Entity, TileNames()));
            }
            else if (mood == GolemMood.Waiting)
            {
                text = progress ?? GolemMoodRules.WaitingCaption(Economy.ItemTiers.DisplayName(Entity.StallFacts.WantedGood));
            }
            else
            {
                text = GolemMoodRules.Caption(mood);
            }
            if (_badgeLabel.Text != text)
            {
                _badgeLabel.Text = text;
                _badgeResizes = BadgeResizeFrames;
            }
        }

        private void OnGolemStalled(GolemStalledEvent e)
        {
            if (e.GolemId == Entity.GolemId)
            {
                _shakePending = true;
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
