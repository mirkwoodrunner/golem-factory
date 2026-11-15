using System.Collections.Generic;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Steam;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The tick-synchronised drawing layer: what moves on the factory's clock without a node of
    /// its own. It does two things.
    ///
    /// <para>
    /// <b>Once a frame</b> it tells the shaders where the clock is
    /// (<see cref="TickShaderGlobals.Push"/>). That is all the per-frame work there is: a belt's
    /// bands, a pipe's glints and a gear's turning are worked out on the GPU from that one
    /// number, so a thousand belts cost what one does.
    /// </para>
    ///
    /// <para>
    /// <b>When the factory changes</b> (a building placed or removed, a belt or pipe re-shaped, a
    /// boiler lit or run dry) it refills three <see cref="TileBatch"/>es from Core's list of
    /// buildings: every belt, every pipe that carries steam, every splitter's gear. Each batch is
    /// one draw call. The build events only mark the layer stale; the refill happens once, on
    /// the next frame, however many events a load raised.
    /// </para>
    ///
    /// <para>
    /// It reads the world and draws. It never writes to it, and nothing in Core knows it exists.
    /// It sits after <c>Entities</c> in the scene, so at the floor's z it draws over the belt and
    /// pipe tiles and under the goods and everything that stands.
    /// </para>
    /// </summary>
    public partial class TickVisualsNode : Node2D
    {
        public const string GroupName = "tick_visuals";
        public const string PerfSection = "node.tickfx";

        public const string ConveyorShader = "res://shaders/conveyor_flow.gdshader";
        public const string ConduitShader = "res://shaders/conduit_pulse.gdshader";
        public const string GearShader = "res://shaders/clockwork_gear.gdshader";

        // conduit_pulse.gdshader's pieces, by Core's shape.
        private const float PieceStraight = 0f, PieceEnd = 1f, PieceCorner = 2f, PieceTee = 3f, PieceCross = 4f;

        private WorldNode _world;
        private bool _stale = true;
        private int _heat = int.MinValue;

        /// <summary>Every belt's running surface (z −2: over the lane's tile, under its cargo).</summary>
        public TileBatch Belts { get; private set; }

        /// <summary>Every belt splitter's hub gear (z −2, with the belts).</summary>
        public TileBatch Gears { get; private set; }

        /// <summary>The glints on every pipe that carries steam (z −1, with the pipes).</summary>
        public TileBatch Pipes { get; private set; }

        /// <summary>How many times the batches have been refilled from the world.</summary>
        public int Refills { get; private set; }

        public static TickVisualsNode Find(Node from) => from.GetTree().GetFirstNodeInGroup(GroupName) as TickVisualsNode;

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            float cell = GridConversions.CellPixels;

            Belts = new TileBatch("Belts", GD.Load<Shader>(ConveyorShader), cell, z: -2);
            Belts.Material.SetShaderParameter("cell_px", cell);

            Gears = new TileBatch("Gears", GD.Load<Shader>(GearShader), cell / 2f, z: -2);
            Gears.Material.SetShaderParameter("instanced", true);
            Gears.Material.SetShaderParameter("turns_per_tick", 1f / TickVisuals.GearTicksPerTurn);

            Pipes = new TileBatch("Pipes", GD.Load<Shader>(ConduitShader), cell, z: -1);
            Pipes.Material.SetShaderParameter("cell_px", cell);
            Pipes.Material.SetShaderParameter("pulse_ticks", (float)TickVisuals.ConduitPulseTicks);
            Pipes.Material.SetShaderParameter("pulse_cells", (float)TickVisuals.ConduitPulseCells);

            AddChild(Belts.Node);
            AddChild(Gears.Node);
            AddChild(Pipes.Node);

            SandboxWorld sandbox = _world.Sandbox;
            sandbox.Build.BuildingPlaced += OnBuildingChanged;
            sandbox.Build.BuildingRemoved += OnBuildingChanged;
            sandbox.Build.ConnectedShapesChanged += MarkStale;

            // A new scene's shaders start from this world's clock, not the last one's.
            TickShaderGlobals.Reset();
        }

        public override void _ExitTree()
        {
            if (_world?.Sandbox == null)
            {
                return;
            }
            _world.Sandbox.Build.BuildingPlaced -= OnBuildingChanged;
            _world.Sandbox.Build.BuildingRemoved -= OnBuildingChanged;
            _world.Sandbox.Build.ConnectedShapesChanged -= MarkStale;
        }

        private void OnBuildingChanged(PlaceableBuilding building) => _stale = true;

        private void MarkStale() => _stale = true;

        /// <summary>
        /// While set, each frame's managed allocation by this layer is added to
        /// <see cref="AllocatedBytes"/>: the <c>tick-visuals</c> scenario's proof that it is none.
        /// </summary>
        public bool CountAllocations { get; set; }

        /// <summary>Bytes allocated by this layer's frames while <see cref="CountAllocations"/> was set.</summary>
        public long AllocatedBytes { get; set; }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure(PerfSection);
            if (!CountAllocations)
            {
                Sync();
                return;
            }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Sync();
            AllocatedBytes += System.GC.GetAllocatedBytesForCurrentThread() - before;
        }

        /// <summary>
        /// A frame's work: the clock to the shaders, and the batches refilled if the factory
        /// changed. Public so a scenario can measure it.
        /// </summary>
        public void Sync()
        {
            SandboxWorld sandbox = _world.Sandbox;
            TickShaderGlobals.Push(sandbox);

            // Which pipes are hot follows the boilers' Coke, which no build event reports: one
            // number over the boilers says whether any of them has been lit or has run dry.
            int heat = HeatOf(sandbox.Steam);
            if (heat != _heat)
            {
                _heat = heat;
                _stale = true;
            }
            if (_stale)
            {
                Refill(sandbox);
            }
        }

        private static int HeatOf(SteamNetwork steam)
        {
            IReadOnlyList<SteamBoiler> boilers = steam.BoilersInOrder;
            int heat = boilers.Count;
            for (int i = 0; i < boilers.Count; i++)
            {
                heat = heat * 31 + (boilers[i].CokeStock > 0 ? 1 : 0);
            }
            return heat;
        }

        private void Refill(SandboxWorld sandbox)
        {
            _stale = false;
            Refills++;
            Belts.Begin();
            Gears.Begin();
            Pipes.Begin();

            long tick = sandbox.Clock.CurrentTick;
            IReadOnlyList<PlaceableBuilding> buildings = sandbox.Build.Buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                PlaceableBuilding building = buildings[i];
                Vector2 at = GridConversions.CellToWorld(building.Cell);

                PlaceableBelt belt = building.GetPart<PlaceableBelt>();
                if (belt != null)
                {
                    if (building.GetPart<PlaceableBeltSplitter>() != null)
                    {
                        // Neighbouring splitters turn opposite ways, as meshed gears would.
                        float way = ((building.Cell.x + building.Cell.y) & 1) == 0 ? 1f : -1f;
                        Gears.Add(at, 0f, new Color(way, 0f, 0f, 0f));
                    }
                    else
                    {
                        Belts.Add(at, GridConversions.FacingToRotation(belt.Facing), new Color((float)(int)belt.Shape, 0f, 0f, 0f));
                    }
                    continue;
                }

                PlaceableSteamPipe pipe = building.GetPart<PlaceableSteamPipe>();
                if (pipe != null && pipe.IsRegistered && LightingRules.PipeHot(sandbox.Steam.CarriesSteam(pipe.Cell, tick)))
                {
                    float rotation = GridConversions.FacingToRotation(pipe.ShapeOrientation);
                    int quarterTurns = Mathf.PosMod(Mathf.RoundToInt(rotation / (Mathf.Pi / 2f)), 4);
                    Pipes.Add(at, rotation, new Color(PieceOf(pipe.Shape), WavePlace(building.Cell), quarterTurns, 0f));
                }
            }

            Belts.Commit();
            Gears.Commit();
            Pipes.Commit();
        }

        private static float PieceOf(PipeShape shape) => shape switch
        {
            PipeShape.Straight => PieceStraight,
            PipeShape.Corner => PieceCorner,
            PipeShape.Tee => PieceTee,
            PipeShape.Cross => PieceCross,
            _ => PieceEnd,
        };

        /// <summary>
        /// Where a cell sits in the glints' wave, which runs east and south: its column plus its
        /// row down the screen, wrapped to the glints' spacing so the number stays small.
        /// </summary>
        private static float WavePlace(CoreVector2Int cell) =>
            Mathf.PosMod(cell.x - cell.y, TickVisuals.ConduitPulseCells);
    }
}
