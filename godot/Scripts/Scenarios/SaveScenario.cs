using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// G9's exit check: a save/load/save/load leaves the stockpile and the world identical, and a
    /// load really rebuilds what is gone. Driven through the SaveLoad tab's own buttons.
    ///
    /// <list type="number">
    ///   <item>Build a small factory: two depots and a belt run, and a Scavenger programmed to
    ///   extract, standing at the Scrap stall.</item>
    ///   <item>Save. Then wreck it: demolish everything (refunded), dismantle the golem (refunded),
    ///   spend some Scrap.</item>
    ///   <item>Load: the buildings stand again with their views drawn, the golem is rebuilt --
    ///   same id, program and place -- with its node hosted, and the stockpile is what was saved
    ///   (no refund on load: that is the save/load duplicator).</item>
    ///   <item>Save and load twice more: nothing changes.</item>
    /// </list>
    /// </summary>
    public sealed class SaveScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;
        private const string ScratchPath = "user://scenario-save.json";

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private ManagementScreen _screen;
        private BuildingsLayer _layer;
        private int _wait;
        private Snapshot _saved;
        private string _golemId;

        private sealed class Snapshot
        {
            public Dictionary<string, int> Stock;
            public List<string> Buildings;
            public List<string> Golems;

            public string Diff(Snapshot other)
            {
                var problems = new List<string>();
                foreach (KeyValuePair<string, int> s in Stock)
                {
                    if (other.Stock.GetValueOrDefault(s.Key) != s.Value)
                    {
                        problems.Add($"{s.Key} {s.Value}->{other.Stock.GetValueOrDefault(s.Key)}");
                    }
                }
                if (!Buildings.SequenceEqual(other.Buildings))
                {
                    problems.Add($"buildings [{string.Join(",", Buildings)}] -> [{string.Join(",", other.Buildings)}]");
                }
                if (!Golems.SequenceEqual(other.Golems))
                {
                    problems.Add($"golems [{string.Join(",", Golems)}] -> [{string.Join(",", other.Golems)}]");
                }
                return problems.Count == 0 ? null : string.Join("; ", problems);
            }
        }

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _screen = _tree.Root.FindChild("Management", true, false) as ManagementScreen;
            _layer = _tree.GetFirstNodeInGroup(BuildingsLayer.GroupName) as BuildingsLayer;
            if (_screen == null || _layer == null)
            {
                Do("scene", () => "Sandbox.tscn is missing Management or the buildings layer");
                return;
            }
            _screen.SavePath = ScratchPath; // never the player's own save

            Do("build a small factory", BuildFactory);
            Do("open Management on SaveLoad", () => { _screen.Open(); _screen.SelectTab(ManagementTab.SaveLoad); return null; });
            Do("click Save", () => { _saved = Take(); Click(_screen.SaveButton); return null; });
            Do("it saved", () =>
                _screen.SaveStatus.StartsWith("Saved 1 golems and 3 buildings") ? null : $"status '{_screen.SaveStatus}'");
            Do("wreck the factory", Wreck);
            Do("click Load", () => { Click(_screen.LoadButton); return null; });
            Do("everything is back, exactly", Restored);
            for (int round = 1; round <= 2; round++)
            {
                int captured = round;
                Do($"save again (round {round})", () => { Click(_screen.SaveButton); return null; });
                Do($"load again (round {round})", () => { Click(_screen.LoadButton); return null; });
                Do($"nothing moved (round {round})", () =>
                {
                    string diff = _saved.Diff(Take());
                    if (diff != null)
                    {
                        return diff;
                    }
                    if (captured == 2)
                    {
                        _log.Add("save/load twice more: stockpile, buildings and golems identical");
                    }
                    return null;
                });
            }
            // The floor is saved too (G10): extend after the save, and a load takes the bought
            // rows back out -- planks, back wall and all.
            int north = 0;
            Do("deposit and save", () =>
            {
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.Scrap, 500);
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.IronPlate, 500);
                Click(_screen.SaveButton); // lands next frame: extend in the step after
                _saved = Take();
                north = _world.Bounds.NorthExtent;
                return null;
            });
            Do("the save was taken before extending", () =>
                _screen.SaveStatus.StartsWith("Saved") && _world.Bounds.NorthExtent == north
                    ? null
                    : $"save not taken first ('{_screen.SaveStatus}')");
            Do("extend the workshop", () =>
            {
                return _world.AssemblyLineBoard.ExtendFloor() ? null : "Extend refused: " + _world.AssemblyLineBoard.Status;
            });
            Do("the new rows are planked", () =>
                Floor().GetCellSourceId(new Vector2I(0, -_world.Bounds.NorthExtent)) >= 0 ? null : "no planks on the new back row");
            Do("load the save from before the expansion", () => { Click(_screen.LoadButton); return null; });
            Do("the rows and the wall went back", () =>
            {
                if (_world.Bounds.NorthExtent != north)
                {
                    return $"north wall at {_world.Bounds.NorthExtent}, saved at {north}";
                }
                if (Floor().GetCellSourceId(new Vector2I(0, -(north + 1))) >= 0)
                {
                    return "planks left beyond the restored wall";
                }
                var shell = _tree.GetFirstNodeInGroup(ShellNode.GroupName) as ShellNode;
                Node2D backWall = shell?.Walls.OrderBy(w => w.Position.Y).FirstOrDefault();
                // Nothing may stand further north than the restored back wall's own row.
                float limitY = GridConversions.CellToWorld(new CoreVector2Int(0, north + 2)).Y;
                if (backWall == null || backWall.Position.Y <= limitY)
                {
                    return $"back wall at y={backWall?.Position.Y}, north of row {north + 1} (y={limitY})";
                }
                _log.Add($"floor extended after the save came back to row {north}: planks and back wall restored");
                return _saved.Diff(Take());
            });
            Do("close", () => { _screen.Close(); return null; });
        }

        public ScenarioResult? Step(double delta)
        {
            if (_wait-- > 0)
            {
                return null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Dequeue();
            string failure;
            try
            {
                failure = step();
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        private string BuildFactory()
        {
            DefinitionSet defs = _world.Definitions;
            _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.Scrap, 300);
            Place("DepotPrefab", 6, 4, Facing.North);
            Place("DepotPrefab", 8, 4, Facing.North);
            Place("BeltPrefab", 7, 2, Facing.East);

            ChassisDefinition scavenger = defs.Chassis["ClockworkScavenger"];
            if (!_world.StarterStation.TryConstructGolem(scavenger, out GolemEntity golem))
            {
                return "the station built nothing";
            }
            golem.SetPlacement(new CoreVector2Int(-8, -15), Facing.North);
            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            _golemId = golem.GolemId;
            (_world.WorkbenchScreen as WorkbenchScreen)?.Close(); // the build opened it
            return null;
        }

        private string Wreck()
        {
            foreach (PlaceableBuilding building in _world.Build.Buildings.Where(b => !b.IsRemoved).ToList())
            {
                _world.Build.EnterDemolishMode();
                _world.Build.PlaceOrRemove(building.Cell);
            }
            _world.Build.CancelPlacement();
            GolemEntity golem = _world.Golems.Single(g => g.GolemId == _golemId);
            if (!_world.StarterStation.TryDismantleGolem(golem, out _, out string why))
            {
                return "could not dismantle: " + why;
            }
            _world.Buffers.TryWithdraw(_world.StockpileBufferId, ItemType.Scrap, 50);
            if (_world.Build.Buildings.Any(b => !b.IsRemoved) || _world.Golems.Any(g => g.GolemId == _golemId && !g.IsRemoved))
            {
                return "the factory is still standing";
            }
            return null;
        }

        private string Restored()
        {
            if (!_screen.SaveStatus.StartsWith("Loaded 0 golems, rebuilt 1; 3 buildings"))
            {
                return $"status '{_screen.SaveStatus}'";
            }
            string diff = _saved.Diff(Take());
            if (diff != null)
            {
                return diff;
            }
            GolemEntity golem = _world.Golems.Single(g => g.GolemId == _golemId && !g.IsRemoved);
            string program = string.Join(",", golem.Program.appendages.Select(a => a.name));
            if (program != "ExtractScrap,PushOutput" || golem.Cell != new CoreVector2Int(-8, -15))
            {
                return $"golem came back as [{program}] at {golem.Cell}";
            }
            bool hosted = _tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().Any(n => n.Entity == golem);
            if (!hosted)
            {
                return "the rebuilt golem has no node";
            }
            if (_layer.PlayerViewCount != 3)
            {
                return $"{_layer.PlayerViewCount} building views for 3 buildings";
            }
            _log.Add($"load after wrecking: 3 buildings drawn again, {_golemId} rebuilt with [{program}] at {golem.Cell} and hosted, stockpile exactly as saved");
            return null;
        }

        private Snapshot Take() => new Snapshot
        {
            Stock = new[] { ItemType.Scrap, ItemType.Brass, ItemType.IronPlate }
                .ToDictionary(t => t, t => _world.Buffers.GetQuantity(_world.StockpileBufferId, t)),
            Buildings = _world.Build.Buildings.Where(b => !b.IsRemoved)
                .Select(b => $"{b.PrefabKey}@{b.Cell.x},{b.Cell.y}:{b.Facing}").OrderBy(x => x).ToList(),
            Golems = _world.Golems.Where(g => !g.IsRemoved)
                .Select(g => $"{g.GolemId}@{g.Cell.x},{g.Cell.y}:{g.Facing}:{string.Join("+", g.Program.appendages.Select(a => a.name))}")
                .OrderBy(x => x).ToList(),
        };

        private void Place(string key, int x, int y, Facing facing)
        {
            _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == key).Prefab);
            while (_world.Build.PlacementFacing != facing)
            {
                _world.Build.RotatePlacement();
            }
            _world.Build.PlaceOrRemove(new CoreVector2Int(x, y));
            _world.Build.CancelPlacement();
        }

        private FloorLayer Floor() => FindFloor(_tree.Root);

        private static FloorLayer FindFloor(Node node)
        {
            if (node is FloorLayer floor)
            {
                return floor;
            }
            foreach (Node child in node.GetChildren())
            {
                FloorLayer found = FindFloor(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Click(Control control)
        {
            Vector2 at = control.GetGlobalRect().GetCenter();
            _tree.Root.WarpMouse(at);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }
    }
}
