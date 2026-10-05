using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// G6's exit check: every chassis built at the station, and a program through every
    /// <see cref="AppendageActionType"/>, running in the live Sandbox -- steam power and all
    /// (Sandbox.tscn has requireSteamPower on, so a golem with no boiler near it does nothing).
    ///
    /// <code>
    ///  y=1   D1 G1> = = = G2> D2        G5            D3 G6>    M        (all facing East)
    ///  y=0   ~ ~ ~ ~ ~ ~ ~ ~ ~ B1 ~ ~ ~ ~ ~ ~ ~ ~ ~                     pipes power row y=1
    ///  y=-14         D4              D5
    ///  y=-15      B2 G3^          B3 G4^                                (facing North)
    ///  y=-16         [Scrap]         [Coal]                             market stalls
    /// </code>
    ///
    /// <list type="bullet">
    ///   <item>G1 Scavenger: Haul Scrap from depot D1, Push onto the belt run.</item>
    ///   <item>G2 Brass Presser: Haul off the belt, LoadIntoBuffer into depot D2.</item>
    ///   <item>G3 Scavenger: ExtractFromNode at the Scrap stall, Push into D4.</item>
    ///   <item>G4 Mainspring Overclocker: ExtractFromNode at the Coal stall (stocked by a truckload
    ///   the player ordered), Assemble R1 Coking, Repeat, Push Coke into D5.</item>
    ///   <item>G5 Aether-Hauler: Refine (Scrap from the ScrapBuffer into the IronPlateBuffer).</item>
    ///   <item>G6 Zeppelin Freight Loader: Haul from D3, FreightLaunch to the mast M.</item>
    ///   <item>G7 Scavenger, deliberately out of steam: it must stall on NoSteam and its badge must
    ///   say so once the stall has been held for its dwell.</item>
    /// </list>
    ///
    /// The setup goes through the same Core calls the player's gestures reach (the build
    /// controller, the interactor's refuel and truckload order, the construction panel's build);
    /// the mouse and keys for those are the `build` and `interact` scenarios' job. Programs are
    /// written straight onto GolemProgram -- the Workbench that writes them is G7.
    /// </summary>
    public sealed class GolemsScenario : IScenario
    {
        private const double RunSeconds = 30.0;

        private SandboxWorld _world;
        private ConstructionPanelNode _panel;
        private string _failure;
        private double _elapsed;
        private bool _started;
        private readonly Dictionary<string, int> _cycles = new Dictionary<string, int>();
        private readonly Dictionary<string, GolemEntity> _golems = new Dictionary<string, GolemEntity>();
        private int _scrapStallBefore;
        private int _cokeBefore;
        private SceneTree _tree;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _panel = _tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
            if (_panel == null || _world?.Setup == null)
            {
                _failure = "Sandbox.tscn is missing its construction panel or setup";
                return;
            }

            // Near the y=1 row, so the frames show it and the nearest golem lights its routing tiles.
            (_tree.Root.FindChild("Player", true, false) as PlayerNode)?.TeleportTo(new Compat.Vector3(-1f, 3f, 0f));

            try
            {
                Setup();
            }
            catch (Exception e)
            {
                _failure = "setup: " + e.Message;
                return;
            }

            EventBus.GolemCompleted += OnCompleted;
            _started = true;
        }

        public ScenarioResult? Step(double delta)
        {
            if (_failure != null)
            {
                return new ScenarioResult(false, _failure);
            }
            if (!_started)
            {
                return null;
            }

            _elapsed += delta;
            if (_elapsed < RunSeconds)
            {
                return null;
            }
            EventBus.GolemCompleted -= OnCompleted;
            return Verdict();
        }

        // --- Setup -------------------------------------------------------------------------

        private void Setup()
        {
            DefinitionSet defs = _world.Definitions;

            // Goods for every building and every chassis, plus Coke for the boilers and the
            // ScrapBuffer the Refine card reads.
            foreach (PlaceableEntry entry in _world.Placeables)
            {
                foreach (RecipeIngredient c in entry.Prefab.Cost)
                {
                    Deposit(c.itemType, c.quantity * 8);
                }
            }
            foreach (ChassisDefinition chassis in defs.Chassis.Values)
            {
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Deposit(c.itemType, c.quantity * 3);
                }
            }
            Deposit(ItemType.Scrap, 200);
            Deposit(ItemType.Coke, 600);
            _world.Buffers.Deposit("ScrapBuffer", ItemType.Scrap, 20);

            // Steam: a boiler with a pipe main along y=0 for row y=1, and one boiler beside each
            // street golem.
            PlaceableBoiler b1 = Place<PlaceableBoiler>("BoilerPrefab", 0, 0);
            for (int x = -9; x <= 8; x++)
            {
                if (x != 0)
                {
                    Place<PlaceableSteamPipe>("SteamPipePrefab", x, 0);
                }
            }
            PlaceableBoiler b2 = Place<PlaceableBoiler>("BoilerPrefab", -9, -15);
            PlaceableBoiler b3 = Place<PlaceableBoiler>("BoilerPrefab", -5, -15);
            foreach (PlaceableBoiler boiler in new[] { b1, b2, b3 })
            {
                if (!_world.Interactor.TryRefuelBoiler(boiler))
                {
                    throw new InvalidOperationException("refuel: " + _world.Interactor.LastStatusMessage);
                }
            }

            // The machines' neighbours.
            Place<PlaceableDepot>("DepotPrefab", -9, 1);
            for (int x = -7; x <= -5; x++)
            {
                PlaceAt("BeltPrefab", x, 1, Facing.East);
            }
            Place<PlaceableDepot>("DepotPrefab", -3, 1);
            Place<PlaceableDepot>("DepotPrefab", 4, 1);
            Place<PlaceableFreightMast>("FreightMastPrefab", 7, 1);
            Place<PlaceableDepot>("DepotPrefab", -8, -14);
            Place<PlaceableDepot>("DepotPrefab", -4, -14);

            // The Coal stall starts empty in normal mode: order a truckload, as the player would.
            ResourceNodeMarker coal = _world.Markers.Single(m => m.NodeId == "CoalNode");
            if (!_world.Interactor.TryOrderTruckload(coal))
            {
                throw new InvalidOperationException("truckload: " + _world.Interactor.LastStatusMessage);
            }

            // Build every chassis at the starter station, through the panel's own build.
            _panel.Open(_world.StarterStation);
            GolemEntity Build(string chassis) =>
                _panel.TryConstruct(defs.Chassis[chassis]) ? _panel.LastBuilt : throw new InvalidOperationException($"build {chassis}: {_panel.Status}");
            GolemEntity g1 = Build("ClockworkScavenger");
            _panel.Open(_world.StarterStation);
            GolemEntity g2 = Build("BrassPresser");
            _panel.Open(_world.StarterStation);
            GolemEntity g3 = Build("ClockworkScavenger");
            _panel.Open(_world.StarterStation);
            GolemEntity g4 = Build("MainspringOverclocker");
            _panel.Open(_world.StarterStation);
            GolemEntity g5 = Build("AetherHauler");
            _panel.Open(_world.StarterStation);
            GolemEntity g6 = Build("ZeppelinFreightLoader");
            _panel.Open(_world.StarterStation);
            GolemEntity g7 = Build("ClockworkScavenger");

            Program(g1, "G1", -8, 1, Facing.East, ("HaulScrap", 2), ("PushOutput", 0));
            Program(g2, "G2", -4, 1, Facing.East, ("HaulScrap", 1), ("LoadIntoScrapBuffer", 0));
            Program(g3, "G3", -8, -15, Facing.North, ("ExtractScrap", 2), ("PushOutput", 0));
            Program(g4, "G4", -4, -15, Facing.North, ("ExtractScrap", 3), ("AssembleCoking", 0), ("RepeatAssembly", 2), ("PushOutput", 0));
            Program(g5, "G5", 2, 1, Facing.East, ("RefineIronPlate", 0));
            Program(g6, "G6", 5, 1, Facing.East, ("HaulScrap", 2), ("FreightLaunch", 0));
            Program(g7, "G7", 9, 6, Facing.East, ("HaulScrap", 1), ("PushOutput", 0)); // no steam here

            // Each build opened the Workbench on its golem (Unity's order). The programs above
            // were written directly, so put the screen away.
            (_world.WorkbenchScreen as WorkbenchScreen)?.Close();

            _scrapStallBefore = _world.Nodes.TryGetNode("ScrapNode", out ResourceNode node) ? node.RemainingQuantity : 0;
            _cokeBefore = Stock(ItemType.Coke);
        }

        private void Program(GolemEntity golem, string label, int x, int y, Facing facing, params (string card, int quantity)[] steps)
        {
            DefinitionSet defs = _world.Definitions;
            golem.SetPlacement(new CoreVector2Int(x, y), facing);
            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            foreach ((string card, int quantity) in steps)
            {
                if (!golem.Program.TryAddAppendage(defs.Appendages[card]))
                {
                    throw new InvalidOperationException($"{label} ({golem.Program.chassis.name}) refused {card}");
                }
                if (quantity > 0)
                {
                    golem.Program.SetQuantityAt(golem.Program.appendages.Count - 1, quantity);
                }
            }
            _golems[label] = golem;
            _cycles[golem.GolemId] = 0;
        }

        private T Place<T>(string key, int x, int y) where T : class => PlaceAt(key, x, y, Facing.North).GetPart<T>();

        private PlaceableBuilding PlaceAt(string key, int x, int y, Facing facing)
        {
            var cell = new CoreVector2Int(x, y);
            _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == key).Prefab);
            while (_world.Build.PlacementFacing != facing)
            {
                _world.Build.RotatePlacement();
            }
            _world.Build.PlaceOrRemove(cell);
            _world.Build.CancelPlacement();
            return _world.Build.Buildings.SingleOrDefault(b => b.Cell == cell && !b.IsRemoved)
                ?? throw new InvalidOperationException($"could not place {key} at {cell}: {_world.Build.LastStatusMessage}");
        }

        private void Deposit(string itemType, int quantity) => _world.Buffers.Deposit(_world.StockpileBufferId, itemType, quantity);

        private int Stock(string itemType) => _world.Buffers.GetQuantity(_world.StockpileBufferId, itemType);

        private void OnCompleted(GolemCompletedEvent e)
        {
            if (_cycles.ContainsKey(e.GolemId))
            {
                _cycles[e.GolemId]++;
            }
        }

        // --- Verdict -----------------------------------------------------------------------

        private ScenarioResult Verdict()
        {
            var problems = new List<string>();
            int Cycles(string label) => _cycles[_golems[label].GolemId];

            foreach (string label in new[] { "G1", "G2", "G3", "G4", "G5", "G6" })
            {
                GolemEntity g = _golems[label];
                if (Cycles(label) < 1)
                {
                    problems.Add($"{label} {g.Program.chassis.name} never completed a cycle ({g.Program.State}, {g.StallReason} {g.StallResourceId})");
                }
            }

            int stallNow = _world.Nodes.TryGetNode("ScrapNode", out ResourceNode node) ? node.RemainingQuantity : 0;
            if (stallNow >= _scrapStallBefore)
            {
                problems.Add($"G3 took nothing from the Scrap stall ({_scrapStallBefore} -> {stallNow})");
            }
            if (Stock(ItemType.Coke) < _cokeBefore + 3)
            {
                problems.Add($"G4 made no Coke ({_cokeBefore} -> {Stock(ItemType.Coke)})");
            }
            if (_world.Buffers.GetQuantity("IronPlateBuffer", ItemType.IronPlate) < 1)
            {
                problems.Add("G5 refined no Iron Plate");
            }

            GolemEntity g7 = _golems["G7"];
            if (g7.StallReason != StallReason.NoSteam)
            {
                problems.Add($"G7 should be out of steam, is {g7.Program.State}/{g7.StallReason}");
            }

            // Every chassis is drawn with its own art, and the stalled golem wears its badge.
            var nodes = _tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().ToDictionary(n => n.Entity);
            foreach ((string label, GolemEntity g) in _golems)
            {
                if (!nodes.TryGetValue(g, out GolemNode view))
                {
                    problems.Add($"{label} has no node");
                    continue;
                }
                if (view.BodySprite != g.Program.chassis.chassisSprite)
                {
                    problems.Add($"{label} draws {view.BodySprite}, not {g.Program.chassis.chassisSprite}");
                }
            }
            if (nodes.TryGetValue(g7, out GolemNode g7View) && !(g7View.BadgeVisible && g7View.BadgeText.StartsWith("[!] " + g7.GolemId)))
            {
                problems.Add($"G7's stall badge: visible={g7View.BadgeVisible} '{g7View.BadgeText}'");
            }

            if (problems.Count > 0)
            {
                return new ScenarioResult(false, string.Join("; ", problems));
            }

            string counts = string.Join(" ", new[] { "G1", "G2", "G3", "G4", "G5", "G6" }.Select(l => $"{l}={Cycles(l)}"));
            return new ScenarioResult(true,
                $"5 chassis built, 8 action types run on steam: cycles {counts}; Scrap stall {_scrapStallBefore}->{stallNow}; " +
                $"Coke +{Stock(ItemType.Coke) - _cokeBefore}; Iron Plate {_world.Buffers.GetQuantity("IronPlateBuffer", ItemType.IronPlate)}; " +
                $"G7 stalled NoSteam, badge '{g7View.BadgeText.Replace('\n', ' ')}'");
        }
    }
}
