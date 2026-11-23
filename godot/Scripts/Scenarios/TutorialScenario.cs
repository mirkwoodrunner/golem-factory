using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Tutorial;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The step-by-step guide's panel, against the real scene (G10). Core's TutorialGuideTests
    /// walk the steps themselves; this checks what the player sees:
    /// <list type="bullet">
    ///   <item>The guide opens on step 1, and its arrow points off screen toward the Scrap cart,
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

        private ManagementScreen _management;

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
            Do("capture the guide panel", () => { ScenarioCapture.Shot(_tree.Root, "screen-guide-panel"); return null; });
            Do("its arrow points at the Scrap cart from the screen's edge", () =>
            {
                if (!_panel.Arrow.Visible || !_panel.OffScreen)
                {
                    return $"arrow visible {_panel.Arrow.Visible}, off screen {_panel.OffScreen}";
                }
                CoreVector2Int stall = _world.Setup.HomeSpot(ItemType.Scrap).Value;
                Vector2 target = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(new CoreVector2Int(stall.x, stall.y));
                Vector2 centre = _tree.Root.GetVisibleRect().GetCenter();
                Vector2 pointing = Vector2.Right.Rotated(_panel.Arrow.Rotation);
                float aim = pointing.Dot((target - centre).Normalized());
                if (aim < 0.95f)
                {
                    return $"arrow points {pointing}, the cart is toward {(target - centre).Normalized()}";
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
                _log.Add("step 1 'Gather Scrap', arrow at the screen edge pointing at the Scrap cart");
                return null;
            });

            Do("gather Scrap", () => { Give(ItemType.Scrap, 20); return null; });
            Do("the guide moves on to the Coal cart", () =>
                _panel.TitleText == "Buy a Coal cart" && _panel.ProgressText == "Add the Coal cart" ? null : $"'{_panel.TitleText}', '{_panel.ProgressText}'");
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

            // Walk down to the market street: the Boiler step marks its tile beside the Scrap cart.
            Do("walk to the Scrap cart", () =>
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
                _log.Add($"Boiler tile {spot} marked beside the Scrap cart, arrow over it");
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
                PlaceAt("BoilerPrefab", _world.Tutorial.BoilerSpot); // the marked tile
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
                // The next-drag hint is on: Extract and STEP 1 are framed, and the plate is
                // clear of both frames too (onboarding-feedback-design.md §8).
                if (!bench.HintCardOutline.Visible || !bench.HintTargetOutline.Visible)
                {
                    return $"no hint frames on 'Program it' (hint {bench.ShownHint})";
                }
                foreach (Control control in bench.StepRows.Select(r => (Control)r.Socket).Append(bench.Lever)
                             .Append(bench.VaultList).Append(bench.HintCardOutline).Append(bench.HintTargetOutline))
                {
                    if (control != null && control.IsVisibleInTree() && plate.Intersects(control.GetGlobalRect()))
                    {
                        return $"the guide covers {control.Name} ({control.GetGlobalRect()}) at {plate}";
                    }
                }
                _log.Add("in the Workbench on 'Program it', the guide sits clear of the sockets, lever, vault and hint frames");
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
                PlaceAt("DepotPrefab", _world.Tutorial.DepotSpot);
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
            // Its Scrap lands in the depot's crate, not the pack: chapter 1 ends on collecting it.
            Do("its first cycle asks for a collect", () => _panel.TitleText == "Collect from the crate" ? null : Wait);
            Do("stand at the depot and press E", () =>
            {
                CoreVector2Int depot = _world.Tutorial.DepotSpot;
                _world.Interactor.Teleport(new Compat.Vector3(depot.x, depot.y + 1f, 0f));
                return null;
            });
            Do("tap E", () =>
            {
                if (!_panel.Plate.Visible || _panel.OffScreen)
                {
                    return Wait; // the player's teleport lands, and the plate comes back, a frame later
                }
                _log.Add($"frame {Engine.GetFramesDrawn()}: the collect step at the depot");
                Tap(Key.E);
                return null;
            });
            Do("collecting opens chapter 2", () =>
            {
                if (_panel.TitleText != "Cut Gears")
                {
                    return Wait;
                }
                _log.Add($"E at the depot collected ('{_world.Interactor.LastStatusMessage}'), and chapter 2 opened");
                return null;
            });
            Do("cut the Gears", () =>
            {
                Give(ItemType.Gear, 30);
                Give(ItemType.IronPlate, 60);
                Give(ItemType.Scrap, 200);
                return null;
            });
            // 2026-11-21: cards are claimed in the Ledger, which opens on the guide's card.
            Do("the claim step sends the player to the Ledger", () =>
            {
                if (_panel.TitleText != "Claim Scrap Reclamation")
                {
                    return Wait;
                }
                return _panel.BodyText.Contains("open the Ledger") ? null : $"body '{_panel.BodyText}'";
            });
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click the Ledger tab", () =>
            {
                _management ??= _tree.Root.FindChild("Management", true, false) as ManagementScreen;
                if (_management == null || !_management.TabButtons.TryGetValue(ManagementTab.TechTree, out Button tab))
                {
                    return "no Management screen with a Ledger tab";
                }
                Click(tab);
                return null;
            });
            Do("the Ledger opens on Scrap Reclamation's plaque, tagged, with a live Claim", () =>
            {
                LedgerTab ledger = _management.Ledger;
                if (ledger.Readout.SelectedNodeId != "r2.reclamation")
                {
                    return Wait; // the tab draws a frame after the click
                }
                if (!ledger.TagShown("r2.reclamation") || !ledger.PaneClaim.IsVisibleInTree() || ledger.PaneClaim.Disabled)
                {
                    return $"tag {ledger.TagShown("r2.reclamation")}, Claim visible {ledger.PaneClaim.IsVisibleInTree()} disabled {ledger.PaneClaim.Disabled}, '{ledger.PaneUnlock}'";
                }
                Button plaque = ledger.PlaqueFor("r2.reclamation");
                if (!plaque.GetGlobalRect().Intersects(ledger.Chart.GetParent<Control>().GetGlobalRect()))
                {
                    return "the plaque was not scrolled into view";
                }
                ScenarioCapture.Shot(_tree.Root, "tutorial-ledger-claim");
                Click(ledger.PaneClaim);
                return null;
            });
            Do("claimed from the Ledger", () =>
            {
                bool claimed = _world.AssemblyLine.GetClaimedCards("LocalPlayer").Any(c => c.appendage?.name == "AssembleScrapReclamation");
                if (!claimed)
                {
                    return "not claimed: " + _world.AssemblyLineBoard.Status;
                }
                _log.Add("chapter 2: the Ledger opened on Scrap Reclamation's tagged plaque, and its Claim claimed it");
                Tap(Key.Escape);
                return null;
            });
            Do("build the Presser", () =>
                _world.StarterStation.TryConstructGolem(_world.Definitions.Chassis["BrassPresser"], out _) ? null : "no Presser");
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

            // Finish chapter 2 for real, then into chapter 3 as far as its pipe run.
            Do("lay the pipes, the depot, and program and place the Presser", () =>
            {
                foreach (CoreVector2Int pipe in _world.Tutorial.PipeSpots)
                {
                    PlaceAt("SteamPipePrefab", pipe);
                }
                PlaceAt("DepotPrefab", _world.Tutorial.PresserDepotSpot);
                var presser = _world.Golems.First(g => !g.IsRemoved && g.Program.chassis?.name == "BrassPresser");
                presser.Program.logicCore = _world.Definitions.LogicCores["AlwaysOnCore"];
                presser.Program.TryAddAppendage(_world.Definitions.Appendages["HaulScrap"]);
                presser.Program.TryAddAppendage(_world.Definitions.Appendages["AssembleScrapReclamation"]);
                presser.Program.TryAddAppendage(_world.Definitions.Appendages["PushOutput"]);
                presser.SetPlacement(_world.Tutorial.PresserSpot, World.Facing.North);
                return null;
            });
            Do("its first Iron Plate opens chapter 3", () => _panel.TitleText == "Claim Coking" ? null : Wait);
            Do("claim Coking, build a second Presser and the coal line's boiler", () =>
            {
                // A missing Coking card must fail HERE, naming itself, rather than fail a step later
                // as "on 'Claim Coking'".
                var coking = _world.AssemblyLine.Offers.FirstOrDefault(c => c.appendage?.name == "AssembleCoking");
                if (coking == null)
                {
                    return "Coking is not offered";
                }
                if (!_world.AssemblyLineBoard.Claim(coking))
                {
                    return "the Coking claim was refused: " + _world.AssemblyLineBoard.Status;
                }
                Give(ItemType.Scrap, 200);
                Give(ItemType.IronPlate, 60);
                Give(ItemType.Gear, 20);
                if (!_world.StarterStation.TryConstructGolem(_world.Definitions.Chassis["BrassPresser"], out _))
                {
                    return "the station built no second Presser";
                }
                PlaceAt("BoilerPrefab", _world.Tutorial.Boiler2Spot);
                return null;
            });
            Do("chapter 3's pipe step marks its whole run", () =>
            {
                if (_panel.TitleText != "Join the boilers")
                {
                    return $"on '{_panel.TitleText}'";
                }
                var markers = _panel.SpotMarkers.Select(m => m.GetGlobalRect()).ToList();
                foreach (CoreVector2Int pipe in _world.Tutorial.Pipe2Spots)
                {
                    Vector2 tile = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(pipe);
                    if (!markers.Any(r => r.HasPoint(tile)))
                    {
                        return $"no marker on pipe tile {pipe}";
                    }
                }
                _log.Add($"chapter 3: Coking claimed, second Presser and boiler built; the pipe step marks {_world.Tutorial.Pipe2Spots.Length} tiles, '{_panel.ProgressText}'");
                return null;
            });

            // From playtest (2026-11-22): chapter 5's smelter ran its hand-fed crate dry and the guide
            // said nothing. Now the plate names the empty crate under the step.
            Golems.GolemEntity smelter = null;
            Do("chapter 5: a smelter on its guide tile, its crate short of Coke", () =>
            {
                Give(ItemType.Scrap, 200);
                Give(ItemType.IronPlate, 60);
                Give(ItemType.Coke, 60);
                Tutorial.TutorialGuide guide = _world.Tutorial;
                PlaceAt("DepotPrefab", guide.SmeltInSpot);
                PlaceAt("DepotPrefab", guide.SmeltOutSpot);
                if (!_world.Steam.Reaches(guide.SmelterSpot, _world.Clock.CurrentTick))
                {
                    CoreVector2Int side = new[] { new CoreVector2Int(1, 0), new CoreVector2Int(-1, 0), new CoreVector2Int(0, 1), new CoreVector2Int(0, -1) }
                        .Select(d => guide.SmelterSpot + d)
                        .First(c => !_world.Grid.TryGetOccupant(c, out _) && _world.Build.IsCellBuildable(c));
                    PlaceAt("BoilerPrefab", side);
                    _world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == side).GetPart<Buildings.PlaceableBoiler>().Boiler.AddCoke(50);
                }
                if (!_world.StarterStation.TryConstructGolem(_world.Definitions.Chassis["AetherHauler"], out smelter))
                {
                    return "the station built no Aether-Hauler";
                }
                var p = smelter.Program;
                p.logicCore = _world.Definitions.LogicCores["AlwaysOnCore"];
                p.TryAddAppendage(_world.Definitions.Appendages["HaulScrap"]);
                p.SetItemTypeAt(0, ItemType.Scrap);
                p.SetQuantityAt(0, 2);
                p.TryAddAppendage(_world.Definitions.Appendages["HaulScrap"]);
                p.SetItemTypeAt(1, ItemType.Coke);
                p.TryAddAppendage(_world.Definitions.Appendages["AssembleIronSmelting"]);
                p.TryAddAppendage(_world.Definitions.Appendages["PushOutput"]);
                Buildings.ICrate crate = _world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == guide.SmeltInSpot).GetPart<Buildings.ICrate>();
                CrateTransfer.Put(_world.Wallet.Pack, crate, ItemType.Scrap, 2);
                smelter.SetPlacement(guide.SmelterSpot, guide.SmelterFacing);
                guide.Restore("contract", 0, false);
                return null;
            });
            Do("the plate says the smelter's crate is out of Coke", () =>
            {
                if (!_panel.NoticeVisible || !_panel.NoticeText.Contains("out of Coke"))
                {
                    if (_waited < 1100)
                    {
                        return Wait;
                    }
                    return $"no notice: step '{_world.Tutorial.CurrentStepId}', smelter {smelter.Mood} {smelter.StallReason} step {smelter.Program.CurrentStepIndex} at {smelter.Cell} (spot {_world.Tutorial.SmelterSpot}), notice '{_world.Tutorial.Notice}', panel '{_panel.NoticeText}' {_panel.NoticeVisible}";
                }
                _log.Add($"chapter 5: the smelter red on an empty crate, the plate notes '{_panel.NoticeText}'");
                return null;
            });
            // A control has no size until the frame after it is shown (Scripts/UI/CLAUDE.md).
            int settle = 0;
            Do("capture the notice", () =>
            {
                if (++settle < 3)
                {
                    return Wait;
                }
                ScenarioCapture.Shot(_tree.Root, "tutorial-notice-crate-dry");
                return null;
            });
            Do("top up the crate", () =>
            {
                Buildings.ICrate crate = _world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == _world.Tutorial.SmeltInSpot).GetPart<Buildings.ICrate>();
                CrateTransfer.Put(_world.Wallet.Pack, crate, ItemType.Coke, 20);
                return null;
            });
            Do("the notice goes", () => _panel.NoticeVisible ? Wait : null);

            // Chapter 8 opens by taking the player's hands off that crate: a coker and a Scavenger
            // fill one on the street row, and the smelter moves onto it.
            Do("chapter 8: the fed line's coker and crate are marked", () =>
            {
                Tutorial.TutorialGuide guide = _world.Tutorial;
                guide.Restore("fed-coke", 0, false);
                return null;
            });
            int fedSettle = 0;
            Do("the marks stand on the coker's tile and the crate's", () =>
            {
                if (++fedSettle < 3)
                {
                    return Wait;
                }
                if (_panel.TitleText != "Coke into a crate")
                {
                    return $"on '{_panel.TitleText}'";
                }
                Tutorial.TutorialGuide guide = _world.Tutorial;
                var markers = _panel.SpotMarkers.Where(m => m.Visible).Select(m => m.GetGlobalRect()).ToList();
                foreach (CoreVector2Int cell in new[] { guide.FeedCokerSpot, guide.FeedCrateSpot })
                {
                    Vector2 tile = _tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(cell);
                    if (!markers.Any(r => r.HasPoint(tile)))
                    {
                        return $"no marker on {cell} (coker {guide.FeedCokerSpot}, crate {guide.FeedCrateSpot})";
                    }
                }
                ScenarioCapture.Shot(_tree.Root, "tutorial-fed-line");
                _log.Add($"chapter 8: the fed line marks the coker at {guide.FeedCokerSpot} facing {guide.FeedCokerFacing} and its crate at {guide.FeedCrateSpot}");
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

        private void PlaceAt(string key, CoreVector2Int cell)
        {
            _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == key).Prefab);
            _world.Build.PlaceOrRemove(cell);
            _world.Build.CancelPlacement();
        }

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
