using Godot;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Simulation;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The scene's one owner of simulation state: the clock and every registry a golem or a
    /// building needs.
    ///
    /// <para>
    /// In Unity this was seven Holder MonoBehaviours plus SimulationClockRunner, each a thin
    /// shell around one plain object so the Inspector had something to point at. Godot nodes
    /// find each other by path or group instead, so one node owning all of them does the same
    /// job; each Core object is still constructed and owned exactly as before, which is the
    /// part of the Holder pattern that mattered.
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

        public SimulationClock Clock { get; } = new SimulationClock();
        public ConveyorSystem Conveyor { get; } = new ConveyorSystem();
        public SpatialEndpointRegistry Endpoints { get; } = new SpatialEndpointRegistry();
        public ResourceNodeRegistry Nodes { get; } = new ResourceNodeRegistry();
        public StorageBufferRegistry Buffers { get; } = new StorageBufferRegistry();
        public BeltNetwork Belts { get; } = new BeltNetwork();

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

            Belts.Configure(Conveyor, Endpoints, BeltSegmentLengthTicks);

            // Same registration order as SandboxBootstrap: belts advance before any golem
            // ticks, so a golem sees this tick's belt state.
            Clock.TicksPerSecond = TicksPerSecond;
            Clock.Register(Conveyor);
            Clock.Play();
        }

        public override void _Process(double delta) => Clock.Advance((float)delta);
    }
}
