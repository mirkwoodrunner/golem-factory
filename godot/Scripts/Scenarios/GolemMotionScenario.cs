using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Every chassis at its signature verb on steam, side by side on the street and held in view
    /// (<see cref="GolemWorkMotion"/>). Stocks are topped up and drained every frame, so each
    /// golem works for the whole run. Passes when each body was seen making its motion. Also the
    /// fixture for capturing the motions with <c>--write-movie</c>.
    ///
    /// <code>
    ///  y=-14       Pr>   Oc>   D Ha> D   Ze>     M        (all facing East)
    ///  y=-15   B ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ~                pipe main
    ///  y=-16  [Scrap] Sc>
    /// </code>
    /// </summary>
    public sealed class GolemMotionScenario : IScenario
    {
        private const double RunSeconds = 8.0;

        private sealed class Watch
        {
            public GolemEntity Golem;
            public GolemNode View;
            public float MinScaleY = 1f, MaxScaleY = 1f, MaxSkew, MinOffsetX, MaxOffsetX, MaxLift, MaxRed;
        }

        private SandboxWorld _world;
        private SceneTree _tree;
        private string _failure;
        private double _elapsed;
        private readonly Dictionary<string, Watch> _watch = new Dictionary<string, Watch>();

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            var panel = _tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
            if (panel == null || _world?.Setup == null)
            {
                _failure = "Sandbox.tscn is missing its construction panel or setup";
                return;
            }

            // Park the player out of reach of every prompt, out of sight, and aim the camera at
            // the row from there.
            var player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            var parked = new Compat.Vector3(-2f, -19f, 0f);
            player?.TeleportTo(parked);
            if (player != null)
            {
                player.Visible = false;
            }
            var centre = new Compat.Vector2(0f, -13f); // the row sits below the playtest panel
            foreach (CameraRigNode camera in _tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>())
            {
                camera.SetOrthographicSize(4.5f);
                camera.Offset = new Vector2(centre.x - parked.x, parked.y - centre.y) * GridConversions.CellPixels;
            }

            try
            {
                Setup(panel);
            }
            catch (Exception e)
            {
                _failure = "setup: " + e.Message;
            }
        }

        private void Setup(ConstructionPanelNode panel)
        {
            DefinitionSet defs = _world.Definitions;
            foreach (ChassisDefinition chassis in defs.Chassis.Values)
            {
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Deposit(c.itemType, c.quantity);
                }
            }
            foreach (PlaceableEntry entry in _world.Placeables)
            {
                foreach (RecipeIngredient c in entry.Prefab.Cost)
                {
                    Deposit(c.itemType, c.quantity * 16);
                }
            }
            Deposit(ItemType.Coke, 100);
            Deposit(ItemType.Scrap, 200);

            PlaceableBoiler boiler = Place("BoilerPrefab", -7, -15).GetPart<PlaceableBoiler>();
            for (int x = -6; x <= 4; x++)
            {
                Place("SteamPipePrefab", x, -15);
            }
            if (!_world.Interactor.TryRefuelBoiler(boiler))
            {
                throw new InvalidOperationException("refuel: " + _world.Interactor.LastStatusMessage);
            }
            Place("DepotPrefab", -1, -14);
            Place("DepotPrefab", 1, -14);
            Place("FreightMastPrefab", 6, -14);

            Build(panel, "BrassPresser", -5, -14, ("AssembleCoking", 0));
            Build(panel, "MainspringOverclocker", -3, -14, ("AssembleCoking", 0), ("RepeatAssembly", 2));
            Build(panel, "AetherHauler", 0, -14, ("HaulScrap", 6), ("PushOutput", 0));
            Build(panel, "ZeppelinFreightLoader", 3, -14, ("FreightLaunch", 0));
            Build(panel, "ClockworkScavenger", -7, -16, ("ExtractScrap", 6));
            (_world.WorkbenchScreen as WorkbenchScreen)?.Close();
        }

        private void Build(ConstructionPanelNode panel, string chassis, int x, int y, params (string card, int quantity)[] steps)
        {
            DefinitionSet defs = _world.Definitions;
            panel.Open(_world.StarterStation);
            GolemEntity golem = panel.TryConstruct(defs.Chassis[chassis])
                ? panel.LastBuilt
                : throw new InvalidOperationException($"build {chassis}: {panel.Status}");
            golem.SetPlacement(new CoreVector2Int(x, y), Facing.East);
            golem.BindToNearestMast();
            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            foreach ((string card, int quantity) in steps)
            {
                if (!golem.Program.TryAddAppendage(defs.Appendages[card]))
                {
                    throw new InvalidOperationException($"{chassis} refused {card}");
                }
                if (quantity > 0)
                {
                    golem.Program.SetQuantityAt(golem.Program.appendages.Count - 1, quantity);
                }
            }
            _watch[chassis] = new Watch { Golem = golem };
        }

        public ScenarioResult? Step(double delta)
        {
            if (_failure != null)
            {
                return new ScenarioResult(false, _failure);
            }

            Feed();
            foreach (Watch w in _watch.Values)
            {
                w.View ??= _tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().FirstOrDefault(n => n.Entity == w.Golem);
                if (w.View == null)
                {
                    continue;
                }
                float cell = GridConversions.CellPixels;
                w.MinScaleY = Math.Min(w.MinScaleY, w.View.DrawnScale.Y);
                w.MaxScaleY = Math.Max(w.MaxScaleY, w.View.DrawnScale.Y);
                w.MaxSkew = Math.Max(w.MaxSkew, Math.Abs(w.View.DrawnSkew));
                w.MinOffsetX = Math.Min(w.MinOffsetX, w.View.DrawnOffset.X / cell);
                w.MaxOffsetX = Math.Max(w.MaxOffsetX, w.View.DrawnOffset.X / cell);
                w.MaxLift = Math.Max(w.MaxLift, -w.View.DrawnOffset.Y / cell);
                w.MaxRed = Math.Max(w.MaxRed, w.View.DrawnModulate.R);
            }

            _elapsed += delta;
            if (_elapsed < RunSeconds)
            {
                return null;
            }
            return Verdict();
        }

        // Keep every golem busy: the stampers never run out of Coal or fill with Coke, the
        // Scavenger's stall never empties, and its haul and the Zeppelin's cargo are cleared.
        private void Feed()
        {
            if (_world.Nodes.TryGetNode("ScrapNode", out ResourceNode scrap) && scrap.RemainingQuantity < 20)
            {
                scrap.Deliver(20);
            }
            foreach (string chassis in new[] { "BrassPresser", "MainspringOverclocker" })
            {
                GolemInventory stock = _watch[chassis].Golem.Inventory;
                stock.AddInput(ItemType.Coal, stock.InputRoomFor(ItemType.Coal));
                int made = stock.GetOutput(ItemType.Coke);
                if (made > 0)
                {
                    stock.TryConsumeOutput(ItemType.Coke, made);
                }
            }
            GolemInventory scavenged = _watch["ClockworkScavenger"].Golem.Inventory;
            int held = scavenged.GetInput(ItemType.Scrap);
            if (held > 6)
            {
                scavenged.TryConsumeInput(ItemType.Scrap, held);
            }
            GolemInventory cargo = _watch["ZeppelinFreightLoader"].Golem.Inventory;
            cargo.AddInput(ItemType.Scrap, Math.Max(0, 4 - cargo.GetInput(ItemType.Scrap)));
        }

        private ScenarioResult Verdict()
        {
            var problems = new List<string>();
            string Seen(Watch w) =>
                $"scaleY {w.MinScaleY:0.00}..{w.MaxScaleY:0.00} skew {w.MaxSkew:0.00} x {w.MinOffsetX:0.00}..{w.MaxOffsetX:0.00} lift {w.MaxLift:0.00} red {w.MaxRed:0.00}";
            void Expect(string chassis, bool ok, string what)
            {
                Watch w = _watch[chassis];
                if (w.View == null)
                {
                    problems.Add($"{chassis} has no node");
                }
                else if (!ok)
                {
                    problems.Add($"{chassis} should {what} ({Seen(w)}; {w.Golem.Program.State}/{w.Golem.StallReason} {w.Golem.StallResourceId})");
                }
            }

            Watch pr = _watch["BrassPresser"], oc = _watch["MainspringOverclocker"], sc = _watch["ClockworkScavenger"],
                ha = _watch["AetherHauler"], ze = _watch["ZeppelinFreightLoader"];
            Expect("BrassPresser", pr.MinScaleY <= 0.9f && pr.MaxScaleY >= 1.03f, "stamp");
            Expect("MainspringOverclocker", oc.MinScaleY <= 0.9f && oc.MaxRed > 1.05f, "stamp and glow");
            Expect("ClockworkScavenger", sc.MinOffsetX < -0.04f && sc.MaxSkew > 0.1f, "reach back to the stall");
            Expect("AetherHauler", ha.MinOffsetX < -0.04f && ha.MaxOffsetX > 0.08f && ha.MinScaleY < 0.93f, "heave and lunge");
            Expect("ZeppelinFreightLoader", ze.MinScaleY < 0.9f && ze.MaxLift > 0.2f, "crouch and hop");

            if (problems.Count > 0)
            {
                return new ScenarioResult(false, string.Join("; ", problems));
            }
            return new ScenarioResult(true, string.Join("; ", _watch.Select(kv => $"{kv.Key}: {Seen(kv.Value)}")));
        }

        private PlaceableBuilding Place(string key, int x, int y)
        {
            var cell = new CoreVector2Int(x, y);
            _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == key).Prefab);
            _world.Build.PlaceOrRemove(cell);
            _world.Build.CancelPlacement();
            return _world.Build.Buildings.SingleOrDefault(b => b.Cell == cell && !b.IsRemoved)
                ?? throw new InvalidOperationException($"could not place {key} at {cell}: {_world.Build.LastStatusMessage}");
        }

        private void Deposit(string itemType, int quantity) => _world.Buffers.Deposit(_world.StockpileBufferId, itemType, quantity);
    }
}
