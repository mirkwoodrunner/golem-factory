using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// G8's exit check: every screen opens and works, and only one is ever up. Real keys and
    /// clicks throughout.
    ///
    /// <list type="number">
    ///   <item>The HUD: the alerts strip says all golems are running; PAUSE stops the clock and
    ///   PLAY restarts it.</item>
    ///   <item>Tab opens Management; the world HUD and the build menu hide while it is up
    ///   (HudScreenExclusivityTests' build-menu case).</item>
    ///   <item>Inventory: a header and one row per good, each with an icon slot.</item>
    ///   <item>Every tab button shows only its own content and lights only itself.</item>
    ///   <item>Assembly Line: claim an unlocked card from the stockpile -- it reaches the
    ///   Workbench's vault; Extend buys two rows of workshop, which are planked and walled.</item>
    ///   <item>Patents: a patented program is listed; Load opens the Workbench with it in the
    ///   draft and closes Management.</item>
    ///   <item>Ledger: one clickable plaque per node; clicking R4 opens its recipe; redrawing the
    ///   chart keeps the selection and the pane (the two TechTreeRecipeReadoutTests that were
    ///   about the drawn chart).</item>
    ///   <item>Escape closes; Tab while the Workbench is up does not stack Management on it.</item>
    /// </list>
    /// </summary>
    public sealed class ManagementScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private ManagementScreen _screen;
        private HudOverlay _hud;
        private BuildMenuNode _menu;
        private WorkbenchScreen _workbench;
        private int _wait;
        private Key? _heldTap;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _screen = _tree.Root.FindChild("Management", true, false) as ManagementScreen;
            _hud = _tree.Root.FindChild("Hud", true, false) as HudOverlay;
            _menu = _tree.Root.FindChild("BuildMenu", true, false) as BuildMenuNode;
            _workbench = _tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
            if (_screen == null || _hud == null || _menu == null || _workbench == null)
            {
                Do("scene", () => "Sandbox.tscn is missing Management, the HUD, the build menu or the Workbench");
                return;
            }

            PlanHud();
            PlanOpenAndInventory();
            PlanTabs();
            PlanAssemblyLine();
            PlanPatents();
            PlanLedger();
            PlanCloseAndExclusivity();
        }

        public ScenarioResult? Step(double delta)
        {
            if (_wait-- > 0)
            {
                return null;
            }
            if (_heldTap is Key held)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
                _heldTap = null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Dequeue();
            string failure = step();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        private void PlanHud()
        {
            Do("let the alerts strip reconcile", () => null);
            Do("the strip says all golems are running", () =>
                _hud.AlertsText.Contains("running") ? null : $"strip reads '{_hud.AlertsText}'");
            Do("click PAUSE", () => { Click(_hud.PlayPauseButton); return null; });
            Do("the clock is paused", () => _world.Clock.State == ClockState.Paused ? null : "PAUSE did not pause the clock");
            Do("click PLAY", () => { Click(_hud.PlayPauseButton); return null; });
            Do("the clock runs", () =>
            {
                if (_world.Clock.State != ClockState.Running)
                {
                    return "PLAY did not restart the clock";
                }
                _log.Add($"HUD: strip '{_hud.AlertsText}', PAUSE/PLAY drive the clock, tape '{_hud.TickText}'");
                return null;
            });
        }

        private void PlanOpenAndInventory()
        {
            Do("stock the stockpile", () =>
            {
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.Scrap, 500);
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.IronPlate, 200);
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.Coal, 5);
                return null;
            });
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("Management is up, the HUD and build menu are not", () =>
            {
                if (!_screen.IsOpen)
                {
                    return "Tab did not open Management";
                }
                if (_hud.Showing || _menu.IsBodyVisible)
                {
                    return $"HUD showing={_hud.Showing}, build menu showing={_menu.IsBodyVisible} under a full screen";
                }
                return null;
            });
            Do("Inventory lists the stockpile, every good with an icon slot", () =>
            {
                // Rows are found by their quantity column (Godot renames duplicate sibling names, so
                // "Item" is only the first one's name), and must each also carry the icon slot.
                List<Control> items = _screen.InventoryList.GetChildren().OfType<Control>()
                    .Where(c => c.FindChild("Quantity", true, false) != null).ToList();
                if (items.Count < 3)
                {
                    return $"{items.Count} item rows";
                }
                if (items.Any(r => r.FindChild("Icon", true, false) == null))
                {
                    return "an item row without its icon slot";
                }
                _log.Add($"Inventory: {items.Count} item rows with icons");
                return null;
            });
        }

        private void PlanTabs()
        {
            foreach (ManagementTab tab in (ManagementTab[])Enum.GetValues(typeof(ManagementTab)))
            {
                ManagementTab captured = tab;
                Do($"click the {tab} tab", () => { Click(_screen.TabButtons[captured]); return null; });
                Do($"only {tab} shows and only it is lit", () =>
                {
                    var shown = _screen.TabContent.Where(e => e.Value.Visible).Select(e => e.Key).ToList();
                    if (shown.Count != 1 || shown[0] != captured)
                    {
                        return $"showing {string.Join(",", shown)}";
                    }
                    Color lit = _screen.TabButtons[captured].SelfModulate;
                    int sameAsLit = _screen.TabButtons.Values.Count(b => b.SelfModulate == lit);
                    return sameAsLit == 1 ? null : $"{sameAsLit} tab buttons wear the lit plate";
                });
            }
            Do("tabs", () => { _log.Add("each tab shows only itself, lit alone"); return null; });
        }

        private void PlanAssemblyLine()
        {
            Do("open the Assembly Line tab", () => { Click(_screen.TabButtons[ManagementTab.AssemblyLine]); return null; });
            int vaultBefore = 0;
            string claimed = null;
            // The line opens on the three opening-hand verbs the player already owns (Unity seeds
            // the slots before granting the hand, and only a REFILL skips owned cards). Claiming
            // them is free and buys nothing; clear them so a card that matters comes up.
            Do("clear the owned opening verbs off the line", () =>
            {
                var owned = new HashSet<DraftableCardDefinition>(_world.AssemblyLine.GetClaimedCards("LocalPlayer"));
                for (int pass = 0; pass < 6; pass++)
                {
                    for (int i = 0; i < _world.AssemblyLine.SlotCount; i++)
                    {
                        DraftableCardDefinition card = _world.AssemblyLine.GetCard(i);
                        if (card != null && owned.Contains(card))
                        {
                            _world.AssemblyLineBoard.Claim(i);
                        }
                    }
                }
                return null;
            });
            Do("click Claim on an affordable card", () =>
            {
                vaultBefore = _world.Workbench.VaultAppendages.Count() + _world.Workbench.RackChassis.Count();
                Button claim = _screen.AssemblyLine.RowList.GetChildren().OfType<Control>()
                    .Select(r => r.FindChild("Claim", true, false) as Button).FirstOrDefault(b => b != null && !b.Disabled);
                if (claim == null)
                {
                    return "no affordable card on the line";
                }
                claimed = (claim.GetParent().GetChild(0) as Label)?.Text;
                Click(claim);
                return null;
            });
            Do("the card is claimed and reaches the Workbench", () =>
            {
                if (!_world.AssemblyLineBoard.Status.StartsWith("Claimed"))
                {
                    return $"status '{_world.AssemblyLineBoard.Status}'";
                }
                int after = _world.Workbench.VaultAppendages.Count() + _world.Workbench.RackChassis.Count();
                return after == vaultBefore + 1 ? null : $"claimed '{claimed}' ({_world.AssemblyLineBoard.Status}); Workbench offers {after} cards/chassis, was {vaultBefore}";
            });

            int northBefore = 0;
            Do("click Extend", () =>
            {
                northBefore = _world.Bounds.NorthExtent;
                Button extend = _screen.AssemblyLine.RowList.FindChild("Extend", true, false) as Button;
                if (extend == null || extend.Disabled)
                {
                    return "no live Extend button";
                }
                Click(extend);
                return null;
            });
            Do("two rows of workshop were bought, planked and walled", () =>
            {
                int north = _world.Bounds.NorthExtent;
                if (north != northBefore + 2)
                {
                    return $"north wall {northBefore} -> {north}";
                }
                var floor = _tree.Root.FindChild("Floor", true, false) as TileMapLayer;
                if (floor == null || floor.GetCellSourceId(new Vector2I(0, -north)) < 0)
                {
                    return "the new rows were not planked";
                }
                var wall = _tree.Root.FindChild("WallNorth_0", true, false) as Node2D;
                float expectedY = -(north + 0.5f) * GridConversions.CellPixels;
                if (wall == null || Mathf.Abs(wall.Position.Y - expectedY) > 1f)
                {
                    return $"the north wall stands at y={wall?.Position.Y}, expected {expectedY}";
                }
                _log.Add($"Assembly Line: claimed '{claimed}', it reached the Workbench; Extend {northBefore}->{north}, planked and walled");
                return null;
            });
        }

        private void PlanPatents()
        {
            AppendageActionDefinition push = _world.Definitions.Appendages["PushOutput"];
            Do("patent a program", () =>
            {
                _world.Workbench.LoadBlueprintIntoDraft(new Blueprints.Blueprint("BP-x", "LocalPlayer", null, null, new List<AppendageActionDefinition> { push }));
                _world.Workbench.Patent();
                return null;
            });
            Do("open the Patents tab", () => { Click(_screen.TabButtons[ManagementTab.Patents]); return null; });
            Do("it is listed", () =>
                _screen.PatentList.GetChildren().OfType<Control>().Count(c => c.FindChild("Load", true, false) != null) == 1 ? null : "no blueprint row");
            Do("click Load", () =>
            {
                Button load = _screen.PatentList.FindChild("Load", true, false) as Button;
                if (load == null)
                {
                    return "no Load button";
                }
                Click(load);
                return null;
            });
            Do("the Workbench opens with it in the draft, Management closed", () =>
            {
                if (!_workbench.IsOpen || _screen.IsOpen)
                {
                    return $"workbench open={_workbench.IsOpen}, management open={_screen.IsOpen}";
                }
                if (_world.Workbench.DraftAppendageAt(0) != push)
                {
                    return "the loaded blueprint is not in the draft";
                }
                _workbench.Close();
                _log.Add("Patents: listed, Load opened the Workbench with the program");
                return null;
            });
        }

        private void PlanLedger()
        {
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("open the Ledger", () => { Click(_screen.TabButtons[ManagementTab.TechTree]); return null; });
            Do("one clickable plaque per node", () =>
            {
                int plaques = _screen.Ledger.Plaques.Count(p => p.MouseFilter != Control.MouseFilterEnum.Ignore && !p.Disabled);
                return plaques == TechTreeCatalog.Nodes.Count ? null : $"{plaques} clickable plaques for {TechTreeCatalog.Nodes.Count} nodes";
            });
            Do("bring R4's plaque into view", () =>
            {
                Button r4 = _screen.Ledger.PlaqueFor("r4.ironsmelting");
                var scroll = (ScrollContainer)_screen.Ledger.Chart.GetParent();
                scroll.EnsureControlVisible(r4);
                return null;
            });
            Do("click R4", () => { Click(_screen.Ledger.PlaqueFor("r4.ironsmelting")); return null; });
            Do("its recipe opens", () =>
                _screen.Ledger.PaneBody.Contains("2 Scrap + 1 Coke") ? null : $"pane reads '{_screen.Ledger.PaneBody}'");
            Do("redraw the chart", () => { _screen.Ledger.RebuildChart(); return null; });
            Do("the selection and the pane survive", () =>
            {
                if (_screen.Ledger.Readout.SelectedNodeId != "r4.ironsmelting" || !_screen.Ledger.PaneBody.Contains("2 Scrap + 1 Coke"))
                {
                    return "the rebuild lost the selection or the pane";
                }
                _log.Add($"Ledger: {TechTreeCatalog.Nodes.Count} clickable plaques; R4's recipe opened and survived a rebuild; '{_screen.Ledger.HeaderText.Split("·")[0].Trim()}'");
                return null;
            });
        }

        private void PlanCloseAndExclusivity()
        {
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("Management closed, the HUD is back", () =>
                !_screen.IsOpen && _hud.Showing && _menu.IsBodyVisible ? null : $"open={_screen.IsOpen} hud={_hud.Showing} menu={_menu.IsBodyVisible}");
            Do("open the Workbench, then press Tab", () => { _workbench.Open(); return null; });
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("Tab does not stack Management on the Workbench", () =>
            {
                if (_world.Screens.OpenCount != 1)
                {
                    return $"{_world.Screens.OpenCount} screens up";
                }
                _workbench.Close();
                _log.Add("Escape closed it and the HUD came back; one screen at a time");
                return null;
            });
        }

        // --- Input --------------------------------------------------------------------------

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Click(Control control)
        {
            Vector2 at = control.GetGlobalRect().GetCenter();
            _tree.Root.WarpMouse(at);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }

        private void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }
    }
}
