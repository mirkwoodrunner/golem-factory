using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.Steam;
using GolemFactory.UI;

namespace GolemFactory.World
{
    /// <summary>
    /// The whole Sandbox, composed: every registry Unity kept on a Holder, and the wiring
    /// SandboxBootstrap.Start did by sweeping the scene (milestone G5).
    ///
    /// <para>
    /// Unity needed four late-wiring seams because a prefab could not hold a scene reference, so
    /// a building placed after the one-shot sweep had to ask the bootstrap to wire it. Here the
    /// composition IS the bootstrap and outlives startup, so a station the player places is
    /// wired by the same <see cref="ConfigureStation"/> call the starter station was -- the seam
    /// survives only as the interface BuildModeController already asks through.
    /// </para>
    ///
    /// <para>
    /// Engine-free on purpose: the Godot scene wraps one of these (WorldNode), and a test or a
    /// scenario can build the same world with no scene at all. The registration ORDER below is
    /// SandboxBootstrap's, and it matters in two places -- the buffer policy runs before anything
    /// creates the stockpile (a buffer takes its capacity at creation), and belts register on the
    /// clock before any golem, so a golem sees this tick's belt state.
    /// </para>
    /// </summary>
    public sealed class SandboxWorld : IPlacedStationConfigurator
    {
        private static readonly GridCoordinateConverter UnitCells = new GridCoordinateConverter(new Vector2(1f, 1f));

        private readonly List<GolemEntity> _golems = new List<GolemEntity>();
        private readonly List<GolemConstructionStation> _stations = new List<GolemConstructionStation>();
        private GolemConstructionStation _stationTemplate;
        private bool _requireSteamPower;
        private string _stockpileBufferId = "FactoryStockpile";

        public SimulationClock Clock { get; } = new SimulationClock();
        public ConveyorSystem Conveyor { get; } = new ConveyorSystem();
        public SpatialEndpointRegistry Endpoints { get; } = new SpatialEndpointRegistry();
        public ResourceNodeRegistry Nodes { get; } = new ResourceNodeRegistry();
        public StorageBufferRegistry Buffers { get; } = new StorageBufferRegistry();
        public BeltNetwork Belts { get; } = new BeltNetwork();
        public GridMap Grid { get; } = new GridMap();
        public SteamNetwork Steam { get; } = new SteamNetwork();
        public FreightMastRegistry Masts { get; } = new FreightMastRegistry();
        public ClockTowerSiteHolder ClockTower { get; } = new ClockTowerSiteHolder();
        public NodeExtractorRegistry ExtractorCap { get; } = new NodeExtractorRegistry();
        public AssemblyBayStructure AssemblyBay { get; } = new AssemblyBayStructure();
        public BuildModeController Build { get; } = new BuildModeController();
        public FloorExpansionService FloorExpansion { get; } = new FloorExpansionService();

        public FloorBounds Bounds { get; private set; } = new FloorBounds();
        public DefinitionSet Definitions { get; private set; }
        public SandboxSetup Setup { get; private set; }
        public TruckloadMarket Market { get; private set; }
        public IReadOnlyList<PlaceableEntry> Placeables { get; private set; } = new List<PlaceableEntry>();
        public List<ResourceNodeMarker> Markers { get; } = new List<ResourceNodeMarker>();
        public HandCrankBench StarterBench { get; private set; }
        public GolemConstructionStation StarterStation { get; private set; }

        /// <summary>
        /// Buildings the setup authored rather than the player placed: the starter bench and
        /// station. Unity's were scene objects carrying a PlaceableBuilding, which is how the
        /// player's [E] found them; here they are buildings for the same reason. They are NOT
        /// GridMap occupants and NOT in BuildModeController.Buildings, matching Unity -- a click
        /// with a placeable on an occupied tile demolishes, and these were never bought.
        /// </summary>
        public IReadOnlyList<PlaceableBuilding> AuthoredBuildings => _authoredBuildings;

        /// <summary>The player's hands: [E], hold-to-crank, [R], [G], the prompt. Wired to this world.</summary>
        public PlayerInteractor Interactor { get; } = new PlayerInteractor();

        private readonly List<PlaceableBuilding> _authoredBuildings = new List<PlaceableBuilding>();

        public string StockpileBufferId => _stockpileBufferId;

        /// <summary>Every live golem, whichever station built it.</summary>
        public IReadOnlyList<GolemEntity> Golems => _golems;

        /// <summary>A station built a golem. The scene hosts a node for it.</summary>
        public event Action<GolemEntity> GolemSpawned;

        /// <summary>The wrecking bar took a golem. The scene frees its node.</summary>
        public event Action<GolemEntity> GolemDismantled;

        /// <summary>
        /// A world with only the belts and clock -- what the spike's slice and the older test
        /// scenes ran on. <see cref="Compose"/> builds the full Sandbox.
        /// </summary>
        public SandboxWorld(DefinitionSet definitions, int beltSegmentLengthTicks = 4, float ticksPerSecond = 10f)
        {
            Definitions = definitions;
            Belts.Configure(Conveyor, Endpoints, beltSegmentLengthTicks);
            Clock.TicksPerSecond = ticksPerSecond;
            Clock.Register(Conveyor);
        }

        /// <summary>The full Sandbox: <paramref name="setup"/> applied, the build menu stocked.</summary>
        public static SandboxWorld Compose(
            DefinitionSet definitions, SandboxSetup setup, IReadOnlyList<PlaceableEntry> placeables,
            int beltSegmentLengthTicks = 4, float ticksPerSecond = 10f)
        {
            var world = new SandboxWorld(definitions, beltSegmentLengthTicks, ticksPerSecond);
            world.Apply(setup, placeables);
            return world;
        }

        private void Apply(SandboxSetup setup, IReadOnlyList<PlaceableEntry> placeables)
        {
            Setup = setup;
            Placeables = placeables ?? new List<PlaceableEntry>();
            _stockpileBufferId = setup.stockpileBufferId;
            _requireSteamPower = setup.requireSteamPower;
            Bounds = new FloorBounds(FloorLayout.HalfExtent, setup.startingNorthExtent);

            // SandboxBootstrap.Start, in its order.
            setup.ApplyBufferPolicy(Buffers);
            setup.RegisterNodes(Nodes);
            foreach (SandboxSetup.NodeEntry entry in setup.nodes)
            {
                var marker = new ResourceNodeMarker { Position = new Vector3(entry.x, entry.y, 0f) };
                marker.Configure(Nodes, entry.id);
                marker.RegisterAsSpatialEndpoint(Endpoints, UnitCells);
                Markers.Add(marker);
            }

            // Steam ticks on the simulation clock: Coke burns per unit of work, so the burn has
            // to follow Play/Pause and the speed multiplier.
            Clock.Register(new TickAdapter(Steam.Tick));

            FloorExpansion.Configure(Bounds, Buffers, _stockpileBufferId);
            if (setup.floorExpansion != null)
            {
                FloorExpansion.ConfigureCost(
                    setup.floorExpansion.scrapPerRow, setup.floorExpansion.ironPlatePerRow, setup.floorExpansion.rowsPerPurchase);
            }

            // Ticked whether or not a tower is built: an unbuilt tower accrues nothing.
            ClockTower.Configure(Definitions.ClockTowerStages.Values.OrderBy(s => s.name));
            ClockTower.Attach();
            Clock.Register(ClockTower);

            if (setup.starterBench != null)
            {
                var bench = new PlaceableBuilding { name = "StarterHandCrankBench", Cell = SandboxSetup.CellOf(setup.starterBench) };
                StarterBench = bench.AddPart(new HandCrankBench());
                StarterBench.Configure(Buffers, _stockpileBufferId, Definitions.Recipes.Values.OrderBy(r => r.name));
                Clock.Register(StarterBench);
                _authoredBuildings.Add(bench);
            }

            Market = setup.BuildMarket(Nodes);
            Clock.Register(Market);

            WireBuildMode();

            if (setup.starterStation != null)
            {
                var stationBuilding = new PlaceableBuilding
                {
                    name = "StarterConstructionStation",
                    Cell = SandboxSetup.CellOf(setup.starterStation),
                    Facing = SandboxSetup.FacingOf(setup.starterStation),
                };
                StarterStation = stationBuilding.AddPart(new GolemConstructionStation());
                _authoredBuildings.Add(stationBuilding);
                StarterStation.SetPlacement(SandboxSetup.CellOf(setup.starterStation), SandboxSetup.FacingOf(setup.starterStation));
                StarterStation.ConfigureBuildRoster(RosterFor(setup.starterStation.roster), () => new GolemEntity());
                AddStation(StarterStation);
            }

            WireInteractor();
        }

        /// <summary>
        /// SandboxBootstrap's PlayerInteractor wiring. The screens (construction, Workbench,
        /// Management) are the scene's to hand over, through <see cref="PlayerInteractor.Configure"/>
        /// again once it has them; until then [E] at a station reports that no panel is available.
        /// </summary>
        private void WireInteractor()
        {
            Interactor.Configure(InteractRange, Buffers, _stockpileBufferId, null, null);
            Interactor.ConfigureAffordance(null, "E");
            Interactor.ConfigureWorld(() => Markers, InteractableBuildings, () => _golems);
            Interactor.ConfigureMarket(Market, () => Clock.CurrentTick, Buffers, _stockpileBufferId);
            Interactor.ConfigureBuildMode(Build);
            Interactor.ConfigureGolemPlacement(Grid, new Vector2(1f, 1f));
            Interactor.Attach();
        }

        /// <summary>
        /// Hands the player's [E] the screens it opens, once the scene has built them -- the
        /// construction panel for a station, the Workbench for a golem (G7) and the Management
        /// screen whose being open hides the prompt (G8). Null for one not built yet.
        /// </summary>
        public void ConfigureScreens(IConstructionScreen construction, IWorkbenchScreen workbench, IScreen management)
        {
            Interactor.Configure(InteractRange, Buffers, _stockpileBufferId, construction, workbench);
            Interactor.ConfigureAffordance(management, "E");
        }

        /// <summary>Unity PlayerInteractor's _interactRange in Sandbox.unity, in cells.</summary>
        public const float InteractRange = 1.5f;

        /// <summary>Everything [E] may reach: what the player built, and what the setup authored.</summary>
        public IEnumerable<PlaceableBuilding> InteractableBuildings() => Build.Buildings.Concat(_authoredBuildings);

        private void WireBuildMode()
        {
            Build.Configure(Grid, null);
            Build.ConfigureEconomy(Buffers, _stockpileBufferId, Placeables.Select(p => p.Prefab).ToArray());
            Build.ConfigureBelts(Belts, Endpoints, Conveyor);
            // "I can walk there" and "I can build there" are one boundary (§3.3): the same live
            // bounds object the player's clamp reads, so new floor is buildable the moment it is
            // walkable.
            Build.ConfigurePlacementBounds(Bounds, FloorLayout.StreetDepth);
            Build.ConfigureSteam(Steam);
            Build.ConfigureFreight(Masts);
            Build.ConfigureClockTower(ClockTower.Site);
            Build.ConfigureStationWiring(this);
            Build.ConfigureGolemRoster(() => _golems);
            Build.BuildingPlaced += OnBuildingPlaced;
        }

        /// <summary>A roster field: chassis names, comma-separated, or "all".</summary>
        private ChassisDefinition[] RosterFor(string roster)
        {
            List<string> list = (roster ?? "").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            return list.Contains("all")
                ? Definitions.Chassis.Values.OrderBy(c => c.maxAppendageSlots).ThenBy(c => c.name).ToArray()
                : list.Select(n => Definitions.Chassis[n]).ToArray();
        }

        /// <summary>Wires <paramref name="station"/> into the world and starts hosting its golems.</summary>
        public void AddStation(GolemConstructionStation station)
        {
            if (_stations.Contains(station))
            {
                return;
            }
            _stations.Add(station);
            station.GolemSpawned += OnGolemSpawned;
            station.GolemDismantled += OnGolemDismantled;
            ConfigureStation(station);

            // The first station that can build anything is the template a placed one copies,
            // and the dismantler the wrecking bar asks: birth and death in one place.
            if (_stationTemplate == null && station.HasBuildRoster)
            {
                _stationTemplate = station;
                Build.ConfigureGolemDismantling(station);
            }
        }

        /// <summary>
        /// The ONE definition of "a construction station, fully wired" -- SandboxBootstrap's, for
        /// the starter station and for every one the player places. Only ever FILLS IN a roster,
        /// never overwrites one.
        /// </summary>
        public bool ConfigureStation(GolemConstructionStation station)
        {
            if (station == null)
            {
                return false;
            }

            if (!station.HasBuildRoster && _stationTemplate != null && _stationTemplate != station)
            {
                station.ConfigureBuildRoster(_stationTemplate.ChassisRoster, _stationTemplate.GolemSource);
            }

            station.ConfigureSceneServices(Conveyor, Nodes, Buffers, Clock, null, _stockpileBufferId);
            station.ConfigureSpatial(Endpoints, Grid);

            // THE ONE LINE THE SWITCH GATES: handing a station the steam network is what makes
            // its golems steam-gated.
            if (_requireSteamPower)
            {
                station.ConfigureSteam(Steam);
            }

            // Not behind a switch: neither cap consumes anything or can soft-lock.
            station.ConfigureNodeExtractorCap(ExtractorCap);
            station.ConfigureAssemblyBay(AssemblyBay);
            station.ConfigureFreight(Masts);
            return station.HasBuildRoster;
        }

        private void OnBuildingPlaced(PlaceableBuilding building)
        {
            // BuildModeController already asked ConfigureStation through the seam; hosting the
            // station's golems is the part only the world can do.
            GolemConstructionStation station = building.GetPart<GolemConstructionStation>();
            if (station != null)
            {
                AddStation(station);
            }
        }

        private void OnGolemSpawned(GolemEntity golem)
        {
            if (!_golems.Contains(golem))
            {
                _golems.Add(golem);
            }

            // The station publishes WorldInteractablesChanged from inside SpawnGolem, BEFORE it
            // raises GolemSpawned -- so the interactor's re-snapshot ran against a roster one
            // golem short. Unity's snapshot was a scene scan, which already saw the new object;
            // a roster kept by events has to refresh again once it has actually changed. Found
            // by the `interact` scenario: [G] next to a fresh golem said "no golem in range".
            Interactor.RefreshInteractables();
            GolemSpawned?.Invoke(golem);
        }

        private void OnGolemDismantled(GolemEntity golem)
        {
            _golems.Remove(golem);
            Interactor.RefreshInteractables(); // the same ordering, the other way round
            GolemDismantled?.Invoke(golem);
        }

        /// <summary>Advances the simulation by real seconds (WorldNode calls this every frame).</summary>
        public void Advance(float seconds) => Clock.Advance(seconds);

        private sealed class TickAdapter : ITickable
        {
            private readonly Action<long> _tick;
            public TickAdapter(Action<long> tick) => _tick = tick;
            public void Tick(long currentTick) => _tick(currentTick);
        }
    }
}
