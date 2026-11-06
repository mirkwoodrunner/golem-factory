using Godot;
using GolemFactory.Belts;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Menus;
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

        /// <summary>
        /// What this scene load is for (docs/menus-design.md §6): the title over its showcase, a
        /// new game, or a save to load. Taken once, here, before any other node is ready.
        /// </summary>
        public LaunchKind Launch { get; private set; }

        /// <summary>Whether this world is the title's showcase: watched, never played or saved.</summary>
        public bool IsShowcase => Launch == LaunchKind.Title;

        private System.Action<string> _logged, _warned, _errored;

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
            // Kept, so _ExitTree can take them off again: the events are static, and a scene
            // reload (starting a game from the title) printed every line once per load.
            _logged = message => GD.Print(message);
            _warned = message => GD.PushWarning(message);
            _errored = message => GD.PushError(message);
            Compat.Debug.Logged += _logged;
            Compat.Debug.Warned += _warned;
            Compat.Debug.Errored += _errored;

            // Only a scene with the whole Sandbox has a title; a test scene is always a game.
            Launch = ApplySandboxSetup ? GameLaunch.Take(PlaytestNode.UnderScenario) : LaunchKind.NewGame;

            // Strict loaders: a bad data file throws here, at startup, naming the file and field.
            DefinitionSet definitions = DefinitionLoader.Load(Read);

            if (ApplySandboxSetup)
            {
                SandboxSetup setup = SandboxSetup.Parse(Read("sandbox.json"));
                // The showcase asks no playtest questions and writes no report: nobody is playing.
                setup.playtest &= !IsShowcase;
                Sandbox = SandboxWorld.Compose(
                    definitions, setup,
                    PlaceableCatalog.Load(Read(PlaceableCatalog.FileName), definitions),
                    BeltSegmentLengthTicks, TicksPerSecond);
            }
            else
            {
                Sandbox = new SandboxWorld(definitions, BeltSegmentLengthTicks, TicksPerSecond);
            }

            Sandbox.Clock.Play();

            // Where the slots, the autosave and the kit's save go (docs/menus-design.md §7). The
            // showcase never saves.
            Sandbox.SaveFolder = IsShowcase ? null : SavePaths.Folder;

            // The player's Reduced motion (docs/menus-design.md §5), now and whenever it changes.
            Sandbox.Motion.Reduced = GameSettingsStore.Load().ReducedMotion;
            GameSettingsStore.Applied += OnSettingsApplied;

            // The sound (docs/audio-design.md): plays what the world's AudioDirector decides.
            AddChild(new AudioNode { Name = "Audio" });
        }

        private void OnSettingsApplied(Settings.GameSettings settings) => Sandbox.Motion.Reduced = settings.ReducedMotion;

        public override void _ExitTree()
        {
            GameSettingsStore.Applied -= OnSettingsApplied;
            Compat.Debug.Logged -= _logged;
            Compat.Debug.Warned -= _warned;
            Compat.Debug.Errored -= _errored;
            // Off the static bus, so the next scene's world is the only one listening.
            Sandbox?.Dispose();
        }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.world");
            // Unity's WorldHudSolver: lay out last frame's floating labels (the mood badges)
            // before any view asks where its own goes. This node is the scene's first child, so
            // its _Process runs before every view's.
            UI.WorldHudRegistry.Solve((int)Engine.GetProcessFrames());
            Sandbox.Advance((float)delta);
        }

        private static string Read(string file) => FileAccess.GetFileAsString("res://data/" + file);
    }
}
