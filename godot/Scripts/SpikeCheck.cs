using System.Linq;
using Godot;
using GolemFactory.Economy;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The slice's end-to-end check, run headless:
    ///
    /// <code>
    /// Godot_console.exe --headless --path godot --fixed-fps 60 -- --spike-check
    /// </code>
    ///
    /// Inert unless <c>--spike-check</c> follows the <c>--</c>. When armed it presses the
    /// station's [E] itself, lets the simulation run <see cref="TicksToRun"/> ticks, and checks
    /// the whole loop: the extractor took from the node and filled the belt, the belt carried
    /// it, the unloader hauled it off the end into the depot. It prints PASS or FAIL with the
    /// numbers and quits with exit code 0 or 1.
    ///
    /// <para>
    /// <c>--fixed-fps 60</c> makes every frame a fixed 1/60 s of game time regardless of how
    /// fast the machine renders, so the run is deterministic and finishes as fast as it can.
    /// </para>
    /// </summary>
    public partial class SpikeCheck : Node
    {
        public const string Flag = "--spike-check";

        /// <summary>A minute of simulated time at 10 ticks/s.</summary>
        [Export] public long TicksToRun { get; set; } = 600;

        /// <summary>Also arm the auto-build, without checking or quitting -- for screenshots.</summary>
        public const string DemoFlag = "--spike-demo";

        [Export] public NodePath StationPath { get; set; }
        [Export] public NodePath DepotPath { get; set; }

        private bool _checking;
        private WorldNode _world;
        private ConstructionStationNode _station;
        private DepotNode _depot;

        public override void _Ready()
        {
            string[] args = OS.GetCmdlineUserArgs();
            _checking = args.Contains(Flag);
            bool demo = args.Contains(DemoFlag);
            if (!_checking && !demo)
            {
                SetProcess(false);
                return;
            }

            _world = WorldNode.Find(this);
            _station = GetNode<ConstructionStationNode>(StationPath);
            _depot = GetNode<DepotNode>(DepotPath);
            // Deferred so every sibling has finished _Ready before a golem joins the tree.
            Callable.From(_station.Interact).CallDeferred();
        }

        public override void _Process(double delta)
        {
            if (!_checking || _world.Clock.CurrentTick < TicksToRun)
            {
                return;
            }

            GolemNode extractor = _station.Built;
            GolemNode unloader = GetTree().GetNodesInGroup(GolemNodeGroup.Name)
                .OfType<GolemNode>().FirstOrDefault(g => g != extractor);
            int depotScrap = _depot.Buffer.GetQuantity(ItemType.Scrap);
            int onBelts = GetTree().GetNodesInGroup(BeltRun.GroupName).OfType<BeltRun>().Sum(r => r.ItemCount);

            bool pass = extractor != null && extractor.CompletedCycles >= 10
                && unloader != null && unloader.CompletedCycles >= 10
                && depotScrap >= 10;

            GD.Print($"[spike-check] {(pass ? "PASS" : "FAIL")} after {_world.Clock.CurrentTick} ticks: " +
                     $"extractor cycles={extractor?.CompletedCycles ?? -1} ({extractor?.Entity.Mood}), " +
                     $"unloader cycles={unloader?.CompletedCycles ?? -1} ({unloader?.Entity.Mood}), " +
                     $"depot scrap={depotScrap}, scrap on belts={onBelts}");
            GetTree().Quit(pass ? 0 : 1);
            SetProcess(false);
        }
    }
}
