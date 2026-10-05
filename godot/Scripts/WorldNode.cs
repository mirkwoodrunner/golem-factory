using Godot;
using GolemFactory.Belts;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Simulation;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The scene's one owner of simulation state: a Core <see cref="SandboxWorld"/>.
    ///
    /// <para>
    /// In Unity this was seven Holder MonoBehaviours plus SimulationClockRunner, wired by
    /// SandboxBootstrap sweeping the scene at Start. Since G5 the registries AND that wiring live
    /// in Core's SandboxWorld, so this node only loads the data, owns the world, and advances
    /// its clock each frame. The properties below forward to it so a node can keep asking
    /// <c>World.Buffers</c> without knowing about the composition.
    /// </para>
    ///
    /// <para>
    /// Must be the FIRST child of the scene root: siblings run _Ready in tree order, and every
    /// other node looks this one up in its own _Ready.
    /// </para>
    /// </summary>
    public partial class WorldNode : Node
    {
        public const string GroupName = "golem_world";

        /// <summary>Ticks per second at 1x, Unity's SimulationClockRunner default.</summary>
        [Export] public float TicksPerSecond { get; set; } = 10f;

        /// <summary>Belt segment length in ticks, BeltNetworkHolder's default.</summary>
        [Export] public int BeltSegmentLengthTicks { get; set; } = 4;

        /// <summary>
        /// Whether to compose the full Sandbox from <c>res://data/sandbox.json</c> and
        /// <c>placeables.json</c>: stalls, market, buffer policy, bounds, starter bench and
        /// station, the build menu. On in Sandbox.tscn; off in test scenes (LoopSlice) that
        /// author their own world on bare belts and a clock.
        /// </summary>
        [Export] public bool ApplySandboxSetup { get; set; }

        /// <summary>The composed world. Null only before _Ready.</summary>
        public SandboxWorld Sandbox { get; private set; }

        public SimulationClock Clock => Sandbox.Clock;
        public ConveyorSystem Conveyor => Sandbox.Conveyor;
        public SpatialEndpointRegistry Endpoints => Sandbox.Endpoints;
        public ResourceNodeRegistry Nodes => Sandbox.Nodes;
        public StorageBufferRegistry Buffers => Sandbox.Buffers;
        public BeltNetwork Belts => Sandbox.Belts;
        public DefinitionSet Definitions => Sandbox.Definitions;

        /// <summary>The applied setup, or null when <see cref="ApplySandboxSetup"/> is off.</summary>
        public SandboxSetup Setup => Sandbox?.Setup;

        public TruckloadMarket Market => Sandbox?.Market;

        /// <summary>The room's live extent (Floor Expansion grows it northward).</summary>
        public FloorBounds Bounds => Sandbox?.Bounds ?? new FloorBounds();

        public static WorldNode Find(Node from) =>
            from.GetTree().GetFirstNodeInGroup(GroupName) as WorldNode;

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            // Core warns through an event it can raise without an engine; route it to Godot's
            // console so an authoring mistake shows up in the editor's Output panel.
            Compat.Debug.Logged += message => GD.Print(message);
            Compat.Debug.Warned += message => GD.PushWarning(message);
            Compat.Debug.Errored += message => GD.PushError(message);

            // Strict loaders: a bad data file throws here, at startup, naming the file and field.
            DefinitionSet definitions = DefinitionLoader.Load(Read);

            Sandbox = ApplySandboxSetup
                ? SandboxWorld.Compose(
                    definitions,
                    SandboxSetup.Parse(Read("sandbox.json")),
                    PlaceableCatalog.Load(Read(PlaceableCatalog.FileName), definitions),
                    BeltSegmentLengthTicks, TicksPerSecond)
                : new SandboxWorld(definitions, BeltSegmentLengthTicks, TicksPerSecond);

            Sandbox.Clock.Play();
        }

        public override void _Process(double delta) => Sandbox.Advance((float)delta);

        private static string Read(string file) => FileAccess.GetFileAsString("res://data/" + file);
    }
}
