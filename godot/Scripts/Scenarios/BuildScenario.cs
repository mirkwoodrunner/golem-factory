using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Data;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// G5's exit check on Sandbox.tscn, driven the way a player drives it: synthetic mouse and
    /// key events pushed through the viewport, so a click reaches the build menu or the world by
    /// Godot's own routing -- including the rule that a click a menu row consumed never builds
    /// on the tile underneath.
    ///
    /// <list type="number">
    ///   <item>Every placeable: pick its row, click a tile -> it stands there, drawn, its cost
    ///   charged. The depot is turned with R first and must face the new way.</item>
    ///   <item>Selecting a row over a tile places nothing there.</item>
    ///   <item>A belt run dragged across four tiles points along itself; a pipe run off a boiler
    ///   joins the steam network and draws the right pieces (end, straight).</item>
    ///   <item>Escape puts the tool down and the row's highlight goes with it.</item>
    ///   <item>Demolish: clicks take buildings, a drag sweeps the belt run, and the stockpile ends
    ///   exactly where it started -- the full refund.</item>
    /// </list>
    /// </summary>
    public sealed class BuildScenario : IScenario
    {
        private const int FramesBetweenSteps = 3;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private ScenarioRunner _runner;
        private SandboxWorld _world;
        private BuildMenuNode _menu;
        private BuildingsLayer _layer;
        private Viewport _viewport;
        private Dictionary<string, int> _funded;
        private int _wait;

        private static readonly string[] Tracked = { "Scrap", "Brass", "IronPlate", "Casing" };

        public void Begin(ScenarioRunner runner)
        {
            _runner = runner;
            SceneTree tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _menu = tree.Root.FindChild("BuildMenu", true, false) as BuildMenuNode;
            _layer = tree.GetFirstNodeInGroup(BuildingsLayer.GroupName) as BuildingsLayer;
            _viewport = tree.Root;

            if (_menu == null || _layer == null || _world?.Setup == null)
            {
                _steps.Enqueue(("scene", () => "Sandbox.tscn is missing its build menu, buildings layer or setup"));
                return;
            }

            // Fund every placeable twice over, and remember the books: the run must end here.
            foreach (PlaceableEntry entry in _world.Placeables)
            {
                foreach (RecipeIngredient c in entry.Prefab.Cost)
                {
                    _world.Buffers.Deposit(_world.StockpileBufferId, c.itemType, c.quantity * 6);
                }
            }
            _funded = Stock();

            PlanMenu();
            PlanPlacements();
            PlanDrags();
            PlanEscape();
            PlanDemolition();
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
            string failure = step();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        // --- The plan ----------------------------------------------------------------------

        /// <summary>Unity's BuildMenuDemolishRowTests, as the player sees the menu.</summary>
        private void PlanMenu()
        {
            Do("the menu offers Demolish below every placeable", () =>
            {
                Node list = _menu.DemolishRow.GetParent();
                if (list.GetChildCount() != _world.Placeables.Count + 1)
                {
                    return $"{list.GetChildCount()} rows for {_world.Placeables.Count} placeables";
                }
                return list.GetChild(list.GetChildCount() - 1) == _menu.DemolishRow ? null : "Demolish is not the last row";
            });
            Do("the panel is tall enough for every row", () =>
            {
                Rect2 panel = _menu.Panel.GetGlobalRect();
                Rect2 last = _menu.DemolishRow.GetGlobalRect();
                return panel.Encloses(last) ? null : $"the Demolish row {last} hangs outside the panel {panel}";
            });
            Do("pick up the bar", () => { ClickRow(null); return null; });
            Do("the bar is in hand", () =>
                _world.Build.IsDemolishActive && _world.Build.ActivePrefab == null ? null : "the Demolish row did not pick up the bar");
            Do("pick a placeable", () => { ClickRow(_world.Placeables[0].Prefab); return null; });
            Do("picking a placeable put the bar down", () =>
                !_world.Build.IsDemolishActive && _world.Build.ActivePrefab == _world.Placeables[0].Prefab ? null : "both tools in hand");
            Do("pick up the bar, then put it down from its own row", () => { ClickRow(null); ClickRow(null); return null; });
            Do("the Demolish row toggles", () =>
                !_world.Build.IsBuildToolActive ? null : "a second click on Demolish left a tool in hand");
            // G10's compact menu: a row is its key and name; the cost is in a hover card, and the
            // number keys do what clicking a row does.
            Do("the menu is compact", () =>
                _menu.Panel.Size.X <= 180f ? null : $"the menu is {_menu.Panel.Size.X}px wide");
            Do("hover the Boiler row", () =>
            {
                Vector2 at = _menu.Rows.Single(r => r.prefab.name == "BoilerPrefab").row.GetGlobalRect().GetCenter();
                Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                return null;
            });
            Do("its card shows the name, key and cost", () =>
            {
                if (!_menu.HoverCard.Visible)
                {
                    return $"no hover card (hovered: {_viewport.GuiGetHoveredControl()?.Name} {_viewport.GuiGetHoveredControl()?.GetType().Name}, mouse {_viewport.GetMousePosition()})";
                }
                if (!_menu.HoverCardName.StartsWith("Boiler") || !_menu.HoverCardName.Contains("[5]"))
                {
                    return $"card says '{_menu.HoverCardName}'";
                }
                if (!_menu.HoverCardCost.Contains("30 Scrap") || !_menu.HoverCardCost.Contains("10 Iron Plate"))
                {
                    return $"card cost '{_menu.HoverCardCost}'";
                }
                return _menu.HoverCard.GetGlobalRect().Position.X >= _menu.Panel.GetGlobalRect().End.X
                    ? null : "the card overlaps the menu";
            });
            Do("press 1", () => { PressKey(Key.Key1); return null; });
            Do("1 picked up the first placeable", () =>
                _world.Build.ActivePrefab == _world.Placeables[0].Prefab ? null : $"holding {_world.Build.ActivePrefab?.name ?? "nothing"}");
            Do("press 1 again", () => { PressKey(Key.Key1); return null; });
            Do("1 again put it down", () => _world.Build.IsBuildToolActive ? "still holding a tool" : null);
            Do("press X", () => { PressKey(Key.X); return null; });
            Do("X picked up the wrecking bar", () => _world.Build.IsDemolishActive ? null : "no wrecking bar");
            Do("press 3 over the bar", () => { PressKey(Key.Key3); return null; });
            Do("3 swapped the bar for the third placeable", () =>
                !_world.Build.IsDemolishActive && _world.Build.ActivePrefab == _world.Placeables[2].Prefab ? null : "tools not swapped");
            Do("put it down", () => { PressKey(Key.Escape); return null; });
            Do("menu", () => { _log.Add($"menu: 12 rows, {_menu.Panel.Size.X}px wide, Demolish last, toggles, tools exclusive; hover card '{_menu.HoverCardName}' / '{_menu.HoverCardCost}'; keys 1, 3, X select and toggle"); return null; });
        }

        private void PlanPlacements()
        {
            int i = 0;
            foreach (PlaceableEntry entry in _world.Placeables.Where(p => !p.Prefab.IsDragPlaceable))
            {
                var cell = new CoreVector2Int(-8 + 2 * i++, 5);
                PlaceableEntry captured = entry;
                Dictionary<string, int> before = null;

                Do($"pick {entry.DisplayName}", () =>
                {
                    before = Stock();
                    ClickRow(captured.Prefab);
                    return null;
                });
                Do($"{entry.DisplayName} row is lit", () =>
                    _world.Build.ActivePrefab == captured.Prefab ? null : "the row click did not pick it up");

                if (captured.Key == "DepotPrefab")
                {
                    Do("turn the depot", () => { PressKey(Key.R); return null; });
                    Do("the depot turned", () =>
                        _world.Build.PlacementFacing == Facing.East ? null : $"facing {_world.Build.PlacementFacing}, expected East");
                }

                if (captured.Key == "DepotPrefab")
                {
                    Do("hover an empty tile", () => { Hover(cell); return null; });
                    Do("the ghost shows there, valid, with its arrow", () =>
                    {
                        var cursor = _runner.GetTree().Root.FindChild("BuildCursor", true, false) as BuildCursorNode;
                        if (cursor == null || !cursor.GhostVisible)
                        {
                            return "no ghost";
                        }
                        if (cursor.GhostCell != cell)
                        {
                            return $"ghost at {cursor.GhostCell}, pointer at {cell}";
                        }
                        Compat.Color valid = Player.BuildGhostVisuals.ValidTint;
                        return Mathf.IsEqualApprox(cursor.GhostTint.G, valid.g) ? null : $"ghost tinted {cursor.GhostTint}, expected the valid tint";
                    });
                }

                Do($"place {entry.DisplayName}", () => { ClickCell(cell); return null; });
                Do($"{entry.DisplayName} stands", () =>
                {
                    PlaceableBuilding placed = BuildingAt(cell);
                    if (placed == null || placed.PrefabKey != captured.Key)
                    {
                        return $"nothing of that kind at {cell}";
                    }
                    if (!_layer.TryGetView(placed, out BuildingView view) || view.SpriteName == null)
                    {
                        return "placed but not drawn";
                    }
                    foreach (RecipeIngredient c in captured.Prefab.Cost)
                    {
                        if (Stock()[c.itemType] != before[c.itemType] - c.quantity)
                        {
                            return $"charged wrongly for {c.itemType}";
                        }
                    }
                    if (captured.Key == "DepotPrefab" && placed.Facing != Facing.East)
                    {
                        return $"the depot was placed facing {placed.Facing}";
                    }
                    return null;
                });
            }

            Do("selecting a row over a tile builds nothing there", () =>
            {
                int count = _world.Build.Buildings.Count;
                ClickRow(_world.Placeables[0].Prefab); // put the depot down again (toggle)...
                ClickRow(_world.Placeables[0].Prefab); // ...and back up
                return _world.Build.Buildings.Count == count ? null : "a row click also placed a building";
            });
            Do("count", () =>
            {
                _log.Add($"{_world.Placeables.Count(p => !p.Prefab.IsDragPlaceable)} placeables placed by menu and click");
                return null;
            });
        }

        private void PlanDrags()
        {
            PlaceableEntry belt = _world.Placeables.Single(p => p.Key == "BeltPrefab");
            PlaceableEntry pipe = _world.Placeables.Single(p => p.Key == "SteamPipePrefab");
            PlaceableEntry boiler = _world.Placeables.Single(p => p.Key == "BoilerPrefab");

            Do("pick the belt", () => { ClickRow(belt.Prefab); return null; });
            Do("drag a belt run", () => { DragCells(new CoreVector2Int(-6, -3), new CoreVector2Int(-3, -3)); return null; });
            Do("the run points along itself", () =>
            {
                for (int x = -6; x <= -3; x++)
                {
                    PlaceableBelt part = BuildingAt(new CoreVector2Int(x, -3))?.GetPart<PlaceableBelt>();
                    if (part == null)
                    {
                        return $"no belt at x={x}";
                    }
                    if (x < -3 && part.Facing != Facing.East)
                    {
                        return $"belt at x={x} faces {part.Facing}";
                    }
                }
                _log.Add("belt run of 4 laid East");
                return null;
            });

            // The belt splitter (G10): clicked onto the run's end, it takes the run's goods and
            // fans them out to every belt leading away from it.
            PlaceableEntry splitterEntry = _world.Placeables.Single(p => p.Key == "BeltSplitterPrefab");
            Do("pick the splitter", () => { ClickRow(splitterEntry.Prefab); return null; });
            Do("click it onto the end of the run", () => { ClickCell(new CoreVector2Int(-2, -3)); return null; });
            Do("lay two branches off it", () =>
            {
                foreach ((CoreVector2Int cell, Facing facing) in new[] { (new CoreVector2Int(-2, -2), Facing.North), (new CoreVector2Int(-1, -3), Facing.East) })
                {
                    _world.Build.SetActivePrefab(belt.Prefab);
                    while (_world.Build.PlacementFacing != facing)
                    {
                        _world.Build.RotatePlacement();
                    }
                    _world.Build.PlaceOrRemove(cell);
                }
                _world.Build.SetActivePrefab(splitterEntry.Prefab); // the arrow check below
                return null;
            });
            Do("the run feeds the splitter, which feeds both branches", () =>
            {
                PlaceableBuilding splitter = BuildingAt(new CoreVector2Int(-2, -3));
                if (splitter?.GetPart<PlaceableBeltSplitter>() == null)
                {
                    return "no splitter at the run's end";
                }
                BeltSegment lane = splitter.GetPart<PlaceableBelt>().Segment;
                if (BuildingAt(new CoreVector2Int(-3, -3)).GetPart<PlaceableBelt>().Segment.Next != lane)
                {
                    return "the run does not feed the splitter";
                }
                if (lane.Outputs.Count != 2)
                {
                    return $"the splitter has {lane.Outputs.Count} outputs";
                }
                _layer.TryGetView(splitter, out BuildingView view);
                if (view?.SpriteName != "belt_splitter")
                {
                    return $"the splitter draws {view?.SpriteName}";
                }
                _log.Add("splitter on the run's end feeds 2 branches and draws its own tile");
                return null;
            });
            Do("put the splitter down", () => { _world.Build.CancelPlacement(); return null; });

            // Cargo through the junction, at the Sandbox's belt speed: six Scrap fed onto the
            // run's tail must reach both dead-end branches (and is what the frames are for).
            for (int k = 0; k < 90; k++)
            {
                int beat = k;
                Do("feed the run and let it flow", () =>
                {
                    if (beat % 10 == 0 && beat < 60)
                    {
                        BuildingAt(new CoreVector2Int(-6, -3)).GetPart<PlaceableBelt>().Segment
                            .TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
                    }
                    return null;
                });
            }
            Do("both branches received Scrap", () =>
            {
                int north = BuildingAt(new CoreVector2Int(-2, -2)).GetPart<PlaceableBelt>().Segment.Items.Count;
                int east = BuildingAt(new CoreVector2Int(-1, -3)).GetPart<PlaceableBelt>().Segment.Items.Count;
                if (north == 0 || east == 0)
                {
                    return $"branches hold {north} (north) and {east} (east)";
                }
                _log.Add($"Scrap through the splitter: {north} north, {east} east");
                return null;
            });

            // The boiler from the placement pass stands at one of the y=5 cells; lay a pipe run
            // off its east side.
            Do("pick the pipe", () => { ClickRow(pipe.Prefab); return null; });
            Do("drag a pipe run off the boiler", () =>
            {
                CoreVector2Int b = _world.Build.Buildings.Single(x => x.PrefabKey == boiler.Key).Cell;
                DragCells(new CoreVector2Int(b.x, b.y - 1), new CoreVector2Int(b.x, b.y - 3));
                return null;
            });
            Do("the pipes joined the network and drew their pieces", () =>
            {
                CoreVector2Int b = _world.Build.Buildings.Single(x => x.PrefabKey == boiler.Key).Cell;
                for (int dy = 1; dy <= 3; dy++)
                {
                    if (!_world.Steam.HasPipe(new CoreVector2Int(b.x, b.y - dy)))
                    {
                        return $"no pipe {dy} below the boiler";
                    }
                }
                PlaceableBuilding tail = BuildingAt(new CoreVector2Int(b.x, b.y - 3));
                PlaceableBuilding middle = BuildingAt(new CoreVector2Int(b.x, b.y - 2));
                if (tail.GetPart<PlaceableSteamPipe>().Shape != PipeShape.End || middle.GetPart<PlaceableSteamPipe>().Shape != PipeShape.Straight)
                {
                    return $"shapes {middle.GetPart<PlaceableSteamPipe>().Shape}/{tail.GetPart<PlaceableSteamPipe>().Shape}";
                }
                _layer.TryGetView(tail, out BuildingView view);
                if (view?.SpriteName != "steam_pipe_end")
                {
                    return $"the open end draws {view?.SpriteName}";
                }
                _log.Add("pipe run of 3 joined the boiler's network");
                return null;
            });
        }

        private void PlanEscape()
        {
            Do("Escape puts the tool down", () => { PressKey(Key.Escape); return null; });
            Do("nothing in hand, nothing lit", () =>
            {
                if (_world.Build.IsBuildToolActive)
                {
                    return "still holding a tool";
                }
                return _menu.Rows.Any(r => r.row.SelfModulate.R > 0.5f) ? "a row is still lit" : null;
            });
        }

        private void PlanDemolition()
        {
            Do("pick the wrecking bar", () => { ClickRow(null); return null; });
            Do("sweep the belt run", () => { DragCells(new CoreVector2Int(-6, -3), new CoreVector2Int(-3, -3)); return null; });
            Do("the sweep took the run", () =>
                Enumerable.Range(-6, 4).Any(x => BuildingAt(new CoreVector2Int(x, -3)) != null) ? "a belt survived the sweep" : null);
            Do("demolish the rest by click", () =>
            {
                foreach (CoreVector2Int cell in _world.Build.Buildings.Select(b => b.Cell).ToList())
                {
                    ClickCell(cell);
                }
                return null;
            });
            Do("everything is down and the books balance", () =>
            {
                if (_world.Build.Buildings.Count > 0)
                {
                    return $"{_world.Build.Buildings.Count} buildings still stand";
                }
                if (_layer.ViewCount > 0)
                {
                    return $"{_layer.ViewCount} views outlived their buildings";
                }
                Dictionary<string, int> now = Stock();
                string drift = string.Join(", ", Tracked.Where(t => now[t] != _funded[t]).Select(t => $"{t} {_funded[t]}->{now[t]}"));
                if (drift.Length > 0)
                {
                    return "refund did not restore the stockpile: " + drift;
                }
                _log.Add("all demolished, stockpile restored exactly");
                return null;
            });
        }

        // --- Input, the player's way --------------------------------------------------------

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        /// <summary>Clicks a build-menu row by its screen position; null = the Demolish row.</summary>
        private void ClickRow(PlaceableBuilding prefab)
        {
            Button row = prefab == null ? _menu.DemolishRow : _menu.Rows.Single(r => r.prefab == prefab).row;
            Vector2 at = row.GetGlobalRect().GetCenter();
            Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }

        private void Hover(CoreVector2Int cell)
        {
            Vector2 at = ScreenOf(cell);
            Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        }

        private void ClickCell(CoreVector2Int cell)
        {
            Vector2 at = ScreenOf(cell);
            Push(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }

        /// <summary>
        /// Press at <paramref name="from"/>, move to <paramref name="to"/>, release. The motion
        /// reaches Core as <c>Hover</c>, which extends the live drag -- here driven directly,
        /// because the cursor node polls the pointer once a frame and a scenario step is one call.
        /// </summary>
        private void DragCells(CoreVector2Int from, CoreVector2Int to)
        {
            Vector2 a = ScreenOf(from);
            Vector2 b = ScreenOf(to);
            Push(new InputEventMouseMotion { Position = a, GlobalPosition = a });
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
            Push(new InputEventMouseMotion { Position = b, GlobalPosition = b });
            _world.Build.Hover(to);
            Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
        }

        private void PressKey(Key key)
        {
            Push(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            Push(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        }

        private void Push(InputEvent e)
        {
            if (e is InputEventMouse mouse)
            {
                // Headless has no real pointer: warp it, so GetGlobalMousePosition agrees with
                // the event the cursor node is about to read.
                _viewport.WarpMouse(mouse.Position);
            }
            Input.ParseInputEvent(e);
            Input.FlushBufferedEvents();
        }

        private Vector2 ScreenOf(CoreVector2Int cell) =>
            _viewport.GetCanvasTransform() * GridConversions.CellToWorld(cell);

        private PlaceableBuilding BuildingAt(CoreVector2Int cell) =>
            _world.Build.Buildings.FirstOrDefault(b => b.Cell == cell && !b.IsRemoved);

        private Dictionary<string, int> Stock() =>
            Tracked.ToDictionary(t => t, t => _world.Buffers.GetQuantity(_world.StockpileBufferId, t));
    }
}
