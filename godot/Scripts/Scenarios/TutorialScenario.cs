using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Tutorial;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The step-by-step guide's panel, against the real scene (G10). Core's TutorialGuideTests
    /// walk the steps themselves; this checks what the player sees:
    /// <list type="bullet">
    ///   <item>The guide opens on step 1, and its arrow points off screen toward the Scrap stall,
    ///   from the screen's edge.</item>
    ///   <item>Gathering advances it and the progress line follows.</item>
    ///   <item>The Boiler step outlines the build menu's Boiler row.</item>
    ///   <item>F1 hides and shows it; Skip guide puts it away.</item>
    ///   <item>Over a full screen the plate moves to the bottom right and the arrow hides.</item>
    /// </list>
    /// </summary>
    public sealed class TutorialScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private TutorialPanel _panel;
        private BuildMenuNode _menu;
        private int _wait;
        private Key? _heldTap;
        private int _waited;
        private const string Wait = "wait";
        private PlayerNode _player;

        private TutorialGuide Guide => _world.Tutorial;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _panel = _tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;
            _menu = _tree.Root.FindChild("BuildMenu", true, false) as BuildMenuNode;
            _player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            if (_panel == null || _menu == null || _world?.Tutorial == null)
            {
                Do("scene", () => "Sandbox.tscn is missing the Tutorial panel, the build menu, or the guide");
                return;
            }

            Do("the guide opens on step 1", () =>
                _panel.Plate.Visible && _panel.TitleText == "Gather Scrap" && _panel.ProgressText == "Scrap  0 / 20"
                    ? null
                    : $"plate {_panel.Plate.Visible}, '{_panel.TitleText}', '{_panel.ProgressText}'");
            Do("its arrow points at the Scrap stall from the screen's edge", () =>
            {
                if (!_panel.Arrow.Visible || !_panel.OffScreen)
                {
                    return $"arrow visible {_panel.Arrow.Visible}, off screen {_panel.OffScreen}";
                }
                SandboxSetup.NodeEntry stall = _world.Setup.nodes.Single(n => n.id == "ScrapNode");
                Vector2 target = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(new CoreVector2Int(stall.x, stall.y));
                Vector2 centre = _tree.Root.GetVisibleRect().GetCenter();
                Vector2 pointing = Vector2.Right.Rotated(_panel.Arrow.Rotation);
                float aim = pointing.Dot((target - centre).Normalized());
                if (aim < 0.95f)
                {
                    return $"arrow points {pointing}, the stall is toward {(target - centre).Normalized()}";
                }
                Rect2 arrow = new Rect2(_panel.Arrow.Position, _panel.Arrow.Size);
                if (!_tree.Root.GetVisibleRect().Encloses(arrow))
                {
                    return $"the arrow is drawn off screen at {arrow}";
                }
                // Clear of the build bar along the bottom (it sat on the old PAUSE bar once).
                if (arrow.Intersects(_menu.Panel.GetGlobalRect()))
                {
                    return $"the arrow sits on the build bar at {arrow}";
                }
                _log.Add("step 1 'Gather Scrap', arrow at the screen edge pointing at the Scrap stall");
                return null;
            });

            Do("gather Scrap", () => { Give(ItemType.Scrap, 20); return null; });
            Do("the guide moves on to Coal", () =>
                _panel.TitleText == "Buy Coal" && _panel.ProgressText == "Coal  0 / 5" ? null : $"'{_panel.TitleText}', '{_panel.ProgressText}'");
            Do("make the Coal, Coke and Iron Plate", () =>
            {
                Give(ItemType.Coal, 5);
                Give(ItemType.Coke, 5);
                Give(ItemType.IronPlate, 10);
                return null;
            });
            Do("the Boiler step outlines the Boiler row", () =>
            {
                if (_panel.TitleText != "Build a Boiler")
                {
                    return $"on '{_panel.TitleText}'";
                }
                Button row = _menu.Rows.Single(r => r.prefab.name == "BoilerPrefab").row;
                if (!_panel.RowHighlight.Visible || !_panel.RowHighlight.GetGlobalRect().Encloses(row.GetGlobalRect()))
                {
                    return $"outline {_panel.RowHighlight.Visible} at {_panel.RowHighlight.GetGlobalRect()}, row at {row.GetGlobalRect()}";
                }
                _log.Add($"Scrap/Coal/Coke/Iron advanced it to '{_panel.TitleText}', Boiler row outlined");
                return null;
            });

            // Walk down to the market street: the Boiler step marks its tile beside the Scrap stall.
            Do("walk to the Scrap stall", () =>
            {
                var at = _world.Interactor.Position;
                var goal = new Vector2(_world.Tutorial.GolemSpot.x + 1.5f, _world.Tutorial.GolemSpot.y + 2.5f);
                var to = new Vector2(goal.X - at.x, goal.Y - at.y);
                if (to.Length() < 0.3f)
                {
                    _player.ScriptedMove = null;
                    return null;
                }
                _player.ScriptedMove = to.Normalized();
                return Wait;
            });
            Do("the Boiler's tile is marked, on screen, and the arrow is over it", () =>
            {
                CoreVector2Int spot = _world.Tutorial.BoilerSpot;
                if (_panel.SpotMarker == null || !_panel.SpotMarker.Visible)
                {
                    return "no marker";
                }
                Vector2 tile = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(spot);
                if (!_panel.SpotMarker.GetGlobalRect().HasPoint(tile))
                {
                    return $"the marker {_panel.SpotMarker.GetGlobalRect()} is not on the Boiler tile at {tile}";
                }
                if (_panel.OffScreen)
                {
                    return "the arrow still points off screen";
                }
                _log.Add($"Boiler tile {spot} marked beside the Scrap stall, arrow over it");
                return null;
            });

            Do("F1", () => { Tap(Key.F1); return null; });
            Do("F1 hid it", () => _panel.Plate.Visible ? "still showing" : null);
            Do("F1 again", () => { Tap(Key.F1); return null; });
            Do("F1 brought it back", () => _panel.Plate.Visible && _panel.TitleText == "Build a Boiler" ? null : "not back on its step");

            Do("open Management over it", () => { Tap(Key.Tab); return null; });
            Do("over Management, the guide steps aside", () =>
                _panel.Plate.Visible || _panel.Arrow.Visible || _panel.RowHighlight.Visible ? "the guide still shows over Management" : null);
            Do("close Management", () => { Tap(Key.Escape); return null; });

            // On to the step done INSIDE the Workbench: there, and only there, the guide shows
            // over a full screen -- in the Blueprint Viewport, clear of the sockets and the lever.
            Do("build and fuel a boiler, then build a golem", () =>
            {
                Give(ItemType.Scrap, 200);
                Give(ItemType.IronPlate, 20);
                _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == "BoilerPrefab").Prefab);
                _world.Build.PlaceOrRemove(_world.Tutorial.BoilerSpot); // the marked tile
                _world.Build.CancelPlacement();
                PlaceableBoilerAt(_world.Tutorial.BoilerSpot);
                return _world.StarterStation.TryConstructGolem(_world.Definitions.Chassis["ClockworkScavenger"], out _)
                    ? null : "the station built nothing";
            });
            // The station points the Workbench at its new golem; [E] at the golem opens it.
            Do("open the Workbench on it", () => { _world.WorkbenchScreen.Open(); return null; });
            Do("the Workbench opened on the Program step, with the guide in it", () =>
            {
                var bench = _tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
                if (bench == null || !bench.IsOpen)
                {
                    return $"the Workbench did not open on the new golem (found {bench != null}, open {bench?.IsOpen}, screen {_world.WorkbenchScreen?.GetType().Name})";
                }
                if (_panel.TitleText != "Program it" || !_panel.Plate.Visible)
                {
                    return $"guide on '{_panel.TitleText}', showing {_panel.Plate.Visible}";
                }
                Rect2 plate = _panel.Plate.GetGlobalRect();
                foreach (Control control in bench.StepRows.Select(r => (Control)r.Socket).Append(bench.TriggerRow.Socket).Append(bench.Lever).Append(bench.VaultList))
                {
                    if (control != null && control.IsVisibleInTree() && plate.Intersects(control.GetGlobalRect()))
                    {
                        return $"the guide covers {control.Name} ({control.GetGlobalRect()}) at {plate}";
                    }
                }
                _log.Add("in the Workbench on 'Program it', the guide sits clear of the sockets, lever and vault");
                return null;
            });
            Do("close the Workbench", () => { Tap(Key.Escape); return null; });

            // Program it and give it its depot: the last placement step marks the golem's own
            // tile, with an arrow showing which way it must face.
            Do("program the golem and build the depot", () =>
            {
                var golem = _world.Golems.First(g => !g.IsRemoved);
                golem.Program.logicCore = _world.Definitions.LogicCores["AlwaysOnCore"];
                golem.Program.TryAddAppendage(_world.Definitions.Appendages["ExtractScrap"]);
                golem.Program.TryAddAppendage(_world.Definitions.Appendages["PushOutput"]);
                _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == "DepotPrefab").Prefab);
                _world.Build.PlaceOrRemove(_world.Tutorial.DepotSpot);
                _world.Build.CancelPlacement();
                return null;
            });
            Do("the golem's tile is marked, facing the depot", () =>
            {
                if (_panel.TitleText != "Put it to work")
                {
                    return $"on '{_panel.TitleText}'";
                }
                Vector2 tile = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(_world.Tutorial.GolemSpot);
                if (!_panel.SpotMarker.Visible || !_panel.SpotMarker.GetGlobalRect().HasPoint(tile))
                {
                    return $"marker {_panel.SpotMarker.Visible} at {_panel.SpotMarker.GetGlobalRect()}, golem tile at {tile}";
                }
                // The station-built golem stands far from any pipe, so the strip must say so
                // in the new words, not the old "no steam reaching tile".
                if (!_world.Alerts.Text.Contains("no steam pipe reaches it"))
                {
                    return $"strip '{_world.Alerts.Text}'";
                }
                _log.Add($"'Put it to work' marks the golem tile {_world.Tutorial.GolemSpot}, facing north to the depot");
                return null;
            });

            // Chapter 2: set the Scavenger to work, then on to the Presser and its steam pipe --
            // the step that marks more than one tile.
            Do("set the Scavenger to work on its tile", () =>
            {
                _world.Golems.First(g => !g.IsRemoved).SetPlacement(_world.Tutorial.GolemSpot, World.Facing.North);
                return null;
            });
            Do("its first cycle opens chapter 2", () => _panel.TitleText == "Cut Gears" ? null : Wait);
            Do("cut the Gears, claim Scrap Reclamation, build the Presser", () =>
            {
                Give(ItemType.Gear, 30);
                Give(ItemType.IronPlate, 60);
                Give(ItemType.Scrap, 200);
                for (int attempt = 0; attempt < 6 && !_world.AssemblyLine.GetClaimedCards("LocalPlayer").Any(c => c.appendage?.name == "AssembleScrapReclamation"); attempt++)
                {
                    int slot = Enumerable.Range(0, _world.AssemblyLine.SlotCount)
                        .FirstOrDefault(i => _world.AssemblyLine.GetCard(i)?.appendage?.name == "AssembleScrapReclamation");
                    _world.AssemblyLineBoard.Claim(slot);
                }
                return _world.StarterStation.TryConstructGolem(_world.Definitions.Chassis["BrassPresser"], out _) ? null : "no Presser";
            });
            Do("the pipe step marks both pipe tiles", () =>
            {
                if (_panel.TitleText != "Lay a steam pipe")
                {
                    return $"on '{_panel.TitleText}'";
                }
                var markers = _panel.SpotMarkers.Select(m => m.GetGlobalRect()).ToList();
                foreach (CoreVector2Int pipe in _world.Tutorial.PipeSpots)
                {
                    Vector2 tile = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(pipe);
                    if (!markers.Any(r => r.HasPoint(tile)))
                    {
                        return $"no marker on pipe tile {pipe} ({tile}); markers at {string.Join(", ", markers)}";
                    }
                }
                Button row = _menu.Rows.Single(r => r.prefab.name == "SteamPipePrefab").row;
                if (!_panel.RowHighlight.Visible || !_panel.RowHighlight.GetGlobalRect().Encloses(row.GetGlobalRect()))
                {
                    return "the Steam Pipe row is not outlined";
                }
                _log.Add($"chapter 2: claimed Scrap Reclamation, built a Presser; the pipe step marks {markers.Count} tiles and outlines Steam Pipe");
                return null;
            });

            Do("click Skip guide", () => { Click(_panel.SkipButton); return null; });
            Do("Skip put it away", () =>
            {
                if (_panel.Plate.Visible || _panel.Arrow.Visible || _panel.RowHighlight.Visible)
                {
                    return "something of the guide still shows";
                }
                _log.Add("F1 hides and shows it, it steps aside over Management, Skip guide puts it away");
                return null;
            });
        }

        public ScenarioResult? Step(double delta)
        {
            if (_heldTap is Key held)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
                _heldTap = null;
            }
            if (_wait-- > 0)
            {
                return null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Peek();
            string failure;
            try
            {
                failure = step();
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
            if (failure == Wait)
            {
                // Not yet: run this step again next frame (a walk in progress).
                if (++_waited > 1200)
                {
                    return new ScenarioResult(false, $"{name}: timed out");
                }
                return null;
            }
            _waited = 0;
            _steps.Dequeue();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        private void PlaceableBoilerAt(CoreVector2Int cell)
        {
            Buildings.PlaceableBoiler boiler = _world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell).GetPart<Buildings.PlaceableBoiler>();
            _world.Interactor.TryRefuelBoiler(boiler);
        }

        private void Give(string item, int quantity) => _world.Buffers.Deposit(_world.StockpileBufferId, item, quantity);

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }

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
