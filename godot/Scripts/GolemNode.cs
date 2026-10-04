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
        private Sprite2D _arrow;
        private Label _moodLabel;

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

            string sprite = Entity.Program.chassis != null ? Entity.Program.chassis.chassisSprite : null;
            var body = new Sprite2D
            {
                Texture = GD.Load<Texture2D>("res://art/" + (sprite ?? "chassis_clockwork_scavenger.png")),
            };
            GridConversions.StandOnCell(body);
            AddChild(body);

            _arrow = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"), ZIndex = 1 };
            AddChild(_arrow);

            _moodLabel = new Label
            {
                Position = new Vector2(-48f, -92f),
                Size = new Vector2(96f, 20f),
                HorizontalAlignment = HorizontalAlignment.Center,
                ZIndex = 2,
            };
            _moodLabel.AddThemeFontSizeOverride("font_size", 12);
            _moodLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            _moodLabel.AddThemeConstantOverride("outline_size", 4);
            AddChild(_moodLabel);

            SyncToEntity();
        }

        public override void _ExitTree()
        {
            if (Entity == null)
            {
                return;
            }
            EventBus.GolemCompleted -= OnGolemCompleted;
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
            SyncToEntity();
            _moodLabel.Text = $"{Entity.Mood} · {CompletedCycles}";
        }

        private void SyncToEntity()
        {
            Position = GridConversions.CellToWorld(Entity.Cell);
            _arrow.Rotation = GridConversions.FacingToRotation(Entity.Facing);
            _arrow.Position = GridConversions.FacingStep(Entity.Facing) * 0.45f;
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
