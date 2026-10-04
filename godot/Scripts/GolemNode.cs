using Godot;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>The two programs the spike's slice needs. Hardcoded: there is no Workbench yet.</summary>
    public enum SliceProgram
    {
        /// <summary>ExtractFromNode from the tile behind, Push onto the tile in front.</summary>
        Extractor,

        /// <summary>Haul Scrap from the tile behind, Push onto the tile in front.</summary>
        Unloader,
    }

    /// <summary>
    /// A golem on the floor: owns a Core <see cref="GolemEntity"/> and draws it.
    ///
    /// <para>
    /// Wiring is the construction station's, call for call -- Configure, ConfigureEconomy,
    /// ConfigureSpatial, TryAssignChassis, then clock registration -- so this golem rides the
    /// same IsSpatiallyPlaced fork and machine-model semantics a station-built golem does in
    /// Unity. Nothing here decides anything about execution; it only asks.
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
        private Label _moodLabel;

        public override void _EnterTree() => AddToGroup(GolemNodeGroup.Name);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);

            Entity = new GolemEntity();
            Entity.Configure(GolemId, _world.Conveyor);
            Entity.ConfigureEconomy(_world.Nodes, _world.Buffers);
            Entity.ConfigureSpatial(_world.Endpoints, GridConversions.ToCore(Cell), Facing);
            Entity.MarkRuntimeSpawned();
            BuildProgram(Entity.Program, ProgramKind);
            Entity.Attach();
            _world.Clock.Register(Entity);
            EventBus.GolemCompleted += OnGolemCompleted;

            Position = GridConversions.CellToWorld(Cell);
            var body = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/chassis_clockwork_scavenger.png") };
            GridConversions.StandOnCell(body);
            AddChild(body);

            var arrow = new Sprite2D
            {
                Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"),
                Rotation = GridConversions.FacingToRotation(Facing),
                Position = GridConversions.FacingStep(Facing) * 0.45f,
                ZIndex = 1,
            };
            AddChild(arrow);

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
        }

        public override void _ExitTree()
        {
            if (Entity == null)
            {
                return;
            }
            EventBus.GolemCompleted -= OnGolemCompleted;
            _world.Clock.Unregister(Entity);
            Entity.Detach();
        }

        public override void _Process(double delta) =>
            _moodLabel.Text = $"{Entity.Mood} · {CompletedCycles}";

        private void OnGolemCompleted(GolemCompletedEvent e)
        {
            if (e.GolemId == GolemId)
            {
                CompletedCycles++;
            }
        }

        private static void BuildProgram(GolemProgram program, SliceProgram kind)
        {
            var chassis = new ChassisDefinition { name = "ClockworkScavenger", maxAppendageSlots = 3 };
            program.TryAssignChassis(chassis);
            program.logicCore = new LogicCoreDefinition { name = "AlwaysOn", triggerType = TriggerType.AlwaysOn };

            if (kind == SliceProgram.Extractor)
            {
                program.TryAddAppendage(new AppendageActionDefinition
                {
                    name = "ExtractScrap",
                    actionType = AppendageActionType.ExtractFromNode,
                });
            }
            else
            {
                program.TryAddAppendage(new AppendageActionDefinition
                {
                    name = "HaulScrap",
                    actionType = AppendageActionType.Haul,
                    inputItemType = ItemType.Scrap,
                });
            }
            program.TryAddAppendage(new AppendageActionDefinition { name = "Push", actionType = AppendageActionType.Push });
        }
    }
}
