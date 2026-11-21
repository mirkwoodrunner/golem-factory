using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The tick-synchronised drawing layer and the build grid, with real keys and clicks:
    /// the shaders and their global parameters exist; the grid shows around the build cursor
    /// in the ghost's colour and its brackets close; belts, a corner, a splitter, a boiler and
    /// pipes placed by clicks land in the three batches, one instance each, and the pipes only
    /// while a boiler with Coke feeds them; the shaders' clock stops with Space, runs again, and
    /// runs four times as fast at 4x; Reduced motion stills it. Then the stress: every free cell
    /// of the full room planked with belts (over a thousand), drawn by the same three draw
    /// calls, with no managed allocation in a frame. Captures <c>tick-grid</c>,
    /// <c>tick-grid-blocked</c>, <c>tick-near</c>, <c>tick-close</c>, <c>tick-batches</c>,
    /// <c>tick-paused</c>, <c>tick-reduced</c>, <c>tick-stress</c>, <c>tick-grid-debug</c>.
    /// </summary>
    public sealed class TickVisualsScenario : StepScenario
    {
        /// <summary>The stress step must draw at least this many animated tiles.</summary>
        public const int StressTiles = 1000;

        private static readonly string[] Shaders =
        {
            GridOverlayNode.ShaderPath, TickVisualsNode.ConveyorShader, TickVisualsNode.ConduitShader,
            TickVisualsNode.GearShader, ShaderRect.TapeShader, ShaderRect.CardShader,
        };

        private SandboxWorld _world;
        private TickVisualsNode _tick;
        private GridOverlayNode _grid;
        private BuildCursorNode _cursor;
        private HudOverlay _hud;
        private ManagementScreen _management;
        private PlayerNode _player;
        private CoreVector2Int _origin;
        private PlaceableBoiler _boiler;
        private float _phaseBefore;
        private float _stepAtOne;
        private int _frames;
        private double _drawsWith, _drawsWithout;
        private bool _headless;

        private CoreVector2Int At(int dx, int dy) => new CoreVector2Int(_origin.x + dx, _origin.y + dy);

        protected override void Plan()
        {
            Do("find the nodes", () =>
            {
                _world = Runner.World.Sandbox;
                _tick = TickVisualsNode.Find(Runner);
                _grid = Tree.Root.FindChild("GridOverlay", true, false) as GridOverlayNode;
                _cursor = Tree.Root.FindChild("BuildCursor", true, false) as BuildCursorNode;
                _hud = Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<HudOverlay>().FirstOrDefault();
                _management = Tree.Root.FindChild("Management", true, false) as ManagementScreen;
                _player = Tree.Root.FindChild("Player", true, false) as PlayerNode;
                _headless = DisplayServer.GetName() == "headless";
                if (_tick == null || _grid == null || _cursor == null || _hud == null || _management == null || _player == null)
                {
                    return "Sandbox.tscn is missing TickVisuals, GridOverlay, the build cursor, the HUD, Management or the player";
                }
                GameSettingsStore.ResetForScenario(Tree.Root);
                // The guide asks nothing here.
                _world.Tutorial?.Restore(GolemFactory.Tutorial.TutorialGuide.FinishedStepId, 0, true);
                _world.Buffers.SetCapacity(_world.StockpileBufferId, StorageBuffer.Unlimited);
                foreach (PlaceableEntry entry in _world.Placeables)
                {
                    EndgameLoad.Grant(_world, entry.Prefab.Cost.Select(c => new RecipeIngredient { itemType = c.itemType, quantity = c.quantity * 12 }));
                }
                return null;
            });

            PlanShaders();
            PlanGrid();
            PlanBatches();
            PlanClock();
            PlanReducedMotion();
            PlanStress();
        }

        protected override void Cleanup()
        {
            if (_tick != null)
            {
                _tick.CountAllocations = false;
            }
            if (_grid != null)
            {
                _grid.CountAllocations = false;
                _grid.DebugCells = false;
            }
        }

        // --- The shaders exist, and read the clock only ------------------------------------------

        private void PlanShaders()
        {
            Do("every shader loads, and none reads TIME", () =>
            {
                foreach (string path in Shaders)
                {
                    if (!ResourceLoader.Exists(path) || GD.Load<Shader>(path) is not Shader shader)
                    {
                        return path + " did not load";
                    }
                    // A shader on the wall clock runs on through a pause; these run on the factory's.
                    string code = string.Join("\n", shader.Code.Split('\n').Where(line => !line.TrimStart().StartsWith("//")));
                    if (code.Contains("TIME"))
                    {
                        return path + " reads TIME";
                    }
                }
                foreach (string name in TickShaderGlobals.Names)
                {
                    if (!ProjectSettings.HasSetting("shader_globals/" + name))
                    {
                        return $"project.godot declares no global shader parameter {name}";
                    }
                }
                Log($"{Shaders.Length} shaders load, none on TIME; {TickShaderGlobals.Names.Length} global parameters declared");
                return null;
            });
            Do("the batches are three MultiMeshes, each with its shader", () =>
            {
                foreach ((TileBatch batch, string shader) in new[]
                         {
                             (_tick.Belts, TickVisualsNode.ConveyorShader), (_tick.Gears, TickVisualsNode.GearShader),
                             (_tick.Pipes, TickVisualsNode.ConduitShader),
                         })
                {
                    if (!batch.Node.IsInsideTree() || batch.Node.Multimesh == null || !batch.Node.Multimesh.UseCustomData
                        || batch.Material.Shader?.ResourcePath != shader)
                    {
                        return $"{batch.Node.Name} is not a MultiMesh with custom data drawn by {shader}";
                    }
                }
                return _grid.Shown ? "the grid shows with no tool in hand" : null;
            });
        }

        // --- The build grid ---------------------------------------------------------------------

        private void PlanGrid()
        {
            Do("press 3: the belt", () => { Tap(Key.Key3); return null; });
            Until("the belt is in hand", 0.5, () => _world.Build.IsPlacementActive ? null : "3 picked nothing up");
            Do("find five clear cells square, north-east of the Artificer", () =>
            {
                var player = new CoreVector2Int(Mathf.RoundToInt(_player.CorePosition.x), Mathf.RoundToInt(_player.CorePosition.y));
                // Near enough to stay on screen and clear of the HUD's bar and the right-hand panels.
                for (int y = player.y + 1; y <= player.y + 2; y++)
                {
                    for (int x = player.x + 1; x <= player.x + 2; x++)
                    {
                        var corner = new CoreVector2Int(x, y);
                        if (Enumerable.Range(0, 25).All(i => _world.Build.GhostStateFor(new CoreVector2Int(x + i % 5, y + i / 5)) == BuildGhostState.Valid))
                        {
                            _origin = corner;
                            return null;
                        }
                    }
                }
                return "no clear 5x5 block of floor near the Artificer";
            });
            Do("point at the first cell", () => { PointAtCell(At(0, 0)); return null; });
            Until("the grid shows around the cursor, its brackets out", 0.5, () =>
            {
                if (!_grid.Shown || !_cursor.GhostVisible)
                {
                    return "no grid, or no ghost";
                }
                if (_grid.Cell != At(0, 0) || _cursor.GhostCell != At(0, 0))
                {
                    return $"grid on {_grid.Cell}, ghost on {_cursor.GhostCell}, pointer on {At(0, 0)}";
                }
                return _grid.DrawnRect.HasPoint(GridConversions.CellToWorld(At(0, 0))) ? null : "the grid's quad is not under the cursor";
            });
            Until("the brackets close onto the cell", 1.0, () =>
                _grid.DrawnSnap <= 0f ? null : $"still {_grid.DrawnSnap} px out");
            Do("the grid wears the ghost's valid colour", () =>
            {
                Compat.Color valid = BuildGhostVisuals.ValidTint;
                return Near(_grid.DrawnColor, new Color(valid.r, valid.g, valid.b, valid.a)) && Near(_grid.DrawnColor, _cursor.GhostTint)
                    ? null : $"grid {_grid.DrawnColor}, ghost {_cursor.GhostTint}";
            });
            Do("move a cell east", () => { PointAtCell(At(1, 0)); return null; });
            Until("the brackets start out again on the new cell, then close", 1.0, () =>
            {
                if (_grid.Cell != At(1, 0))
                {
                    return "the grid did not follow";
                }
                _sawSnap |= _grid.DrawnSnap > 0f;
                return _sawSnap && _grid.DrawnSnap <= 0f ? null : $"snap {_grid.DrawnSnap}, seen out: {_sawSnap}";
            });
        }

        private bool _sawSnap;

        // --- Belts, a splitter, a boiler and pipes, by clicks ---------------------------------------

        private void PlanBatches()
        {
            TurnTo(Facing.East);
            foreach (int dx in new[] { 0, 1, 2 })
            {
                int x = dx;
                Do($"click a belt east at +{x}", () => { ClickCell(At(x, 0)); return null; });
            }
            TurnTo(Facing.North);
            foreach (int dy in new[] { 0, 1 })
            {
                int y = dy;
                Do($"click a belt north at +3,+{y}", () => { ClickCell(At(3, y)); return null; });
            }
            Until("five belts, each an instance of the belt batch; the turn a corner", 1.0, () =>
            {
                List<PlaceableBelt> belts = BeltsPlaced();
                if (belts.Count != 5)
                {
                    return $"{belts.Count} belts placed: {_world.Build.LastStatusMessage}";
                }
                if (_tick.Belts.Count != 5 || _tick.Gears.Count != 0)
                {
                    return $"{_tick.Belts.Count} belt instances, {_tick.Gears.Count} gears";
                }
                return belts.Count(b => b.Shape != BeltShape.Straight) == 1 ? null : "no belt took the corner";
            });
            Do("capture the grid", () => { ScenarioCapture.Shot(Tree.Root, "tick-grid"); return null; });

            Do("point at a belt: the cell is taken", () => { PointAtCell(At(1, 0)); return null; });
            Until("the grid turns the blocked red with the ghost", 1.0, () =>
            {
                if (_world.Build.GhostStateFor(At(1, 0)) != BuildGhostState.Blocked)
                {
                    return "a belt on a belt was not blocked";
                }
                Compat.Color blocked = BuildGhostVisuals.BlockedTint;
                Color drawn = _grid.DrawnColor;
                return Mathf.Abs(drawn.R - blocked.r) < 0.02f && Mathf.Abs(drawn.G - blocked.g) < 0.02f && Mathf.Abs(drawn.B - blocked.b) < 0.02f
                    ? null : $"grid {drawn}";
            });
            Do("capture the blocked grid", () => { ScenarioCapture.Shot(Tree.Root, "tick-grid-blocked"); return null; });

            Do("press 4: the splitter", () => { Tap(Key.Key4); return null; });
            Do("click it two rows up", () => { ClickCell(At(0, 2)); return null; });
            Until("its gear is in the gear batch", 1.0, () =>
                _tick.Gears.Count == 1 && _tick.Belts.Count == 5 ? null : $"{_tick.Gears.Count} gears, {_tick.Belts.Count} belts: {_world.Build.LastStatusMessage}");

            Do("press 5: the boiler", () => { Tap(Key.Key5); return null; });
            Do("click it in the far corner", () => { ClickCell(At(0, 4)); return null; });
            Do("press 6: the pipe", () => { Tap(Key.Key6); return null; });
            foreach ((int dx, int dy) in new[] { (1, 4), (2, 4), (3, 4), (3, 3), (4, 4) })
            {
                int x = dx, y = dy;
                Do($"click a pipe at +{x},+{y}", () => { ClickCell(At(x, y)); return null; });
            }
            Until("a boiler and five pipes stand", 1.0, () =>
            {
                _boiler = _world.Build.Buildings.Select(b => b.GetPart<PlaceableBoiler>()).FirstOrDefault(b => b?.Boiler != null);
                int pipes = _world.Build.Buildings.Count(b => b.GetPart<PlaceableSteamPipe>() != null);
                return _boiler != null && pipes == 5 ? null : $"boiler {_boiler != null}, {pipes} pipes: {_world.Build.LastStatusMessage}";
            });
            Until("a pipe glints exactly while a fuelled boiler feeds it", 1.0, () =>
                _tick.Pipes.Count == HotPipes() ? null : $"{_tick.Pipes.Count} pipe instances, {HotPipes()} pipes carry steam");
            Do("stoke the boiler if it is cold", () =>
            {
                if (_boiler.Boiler.CokeStock == 0)
                {
                    _boiler.Boiler.AddCoke(5);
                }
                return null;
            });
            Until("every pipe joined to it glints", 1.0, () =>
                HotPipes() == 5 && _tick.Pipes.Count == 5 ? null : $"{_tick.Pipes.Count} pipe instances, {HotPipes()} of 5 pipes carry steam");

            // The same, close enough to see a pixel: Near is one art pixel to one screen pixel.
            Do("stand among it and zoom to Near", () =>
            {
                CoreVector2Int stand = At(2, 2);
                _player.TeleportTo(new Compat.Vector3(stand.x, stand.y, 0f));
                _world.Camera.ZoomStep(1);
                return null;
            });
            Wait(1.5);
            Do("point at a clear cell", () => { PointAtCell(At(4, 1)); return null; });
            Wait(0.5);
            Do("capture it at Near", () => { ScenarioCapture.Shot(Tree.Root, "tick-near"); return null; });
            Do("zoom to Close", () => { _world.Camera.ZoomStep(1); return null; });
            Wait(1.5);
            Do("point at the clear cell again", () => { PointAtCell(At(4, 1)); return null; });
            Wait(0.5);
            Do("capture it at Close", () => { ScenarioCapture.Shot(Tree.Root, "tick-close"); return null; });
            Do("zoom back out", () => { _world.Camera.ZoomStep(-1); _world.Camera.ZoomStep(-1); return null; });
            Wait(1.5);

            Do("press Escape: the tool goes down", () => { Tap(Key.Escape); return null; });
            Until("the grid goes with it", 0.5, () => !_world.Build.IsBuildToolActive && !_grid.Shown ? null : "the grid still shows");
            Do("capture the batches", () => { ScenarioCapture.Shot(Tree.Root, "tick-batches"); return null; });
            Do("log", () =>
            {
                Log($"grid follows the ghost and closes; by clicks: {_tick.Belts.Count} belts (1 corner), {_tick.Gears.Count} gear, {_tick.Pipes.Count} hot pipes, {_tick.Refills} refills");
                return null;
            });
        }

        private List<PlaceableBelt> BeltsPlaced() => _world.Build.Buildings
            .Where(b => b.GetPart<PlaceableBeltSplitter>() == null)
            .Select(b => b.GetPart<PlaceableBelt>()).Where(b => b != null).ToList();

        private int HotPipes() => _world.Build.Buildings.Select(b => b.GetPart<PlaceableSteamPipe>())
            .Count(p => p != null && p.IsRegistered && _world.Steam.CarriesSteam(p.Cell, _world.Clock.CurrentTick));

        // --- The shaders' clock is the factory's -----------------------------------------------

        private void PlanClock()
        {
            Do("the clock runs at 1x", () =>
            {
                _world.Clock.Speed = 1f;
                _world.Clock.Play();
                _frames = 0;
                return null;
            });
            Until("the shaders' phase is the clock's, and moves", 1.0, () =>
            {
                if (Mathf.Abs(TickShaderGlobals.TickPhase - TickVisuals.TickPhase(_world.Clock)) > 0.0001f)
                {
                    return $"shaders told {TickShaderGlobals.TickPhase}, the clock is at {TickVisuals.TickPhase(_world.Clock)}";
                }
                if (!Watched(WatchFrames))
                {
                    return "watching";
                }
                _stepAtOne = Wrapped(TickShaderGlobals.TickPhase - _phaseBefore) / WatchFrames;
                return _stepAtOne > 0f ? null : "the phase did not move";
            });

            Do("press Space", () => { Tap(Key.Space); return null; });
            Until("the factory pauses", 0.5, () => _world.Clock.State == ClockState.Paused ? null : "Space did not pause");
            Do("note the phase", () => { _phaseBefore = TickShaderGlobals.TickPhase; _frames = 0; return null; });
            Until("for half a second nothing is sent to the shaders", 1.0, () =>
            {
                _sentWhilePaused |= TickShaderGlobals.TickPhase != _phaseBefore || TickShaderGlobals.LastSent != 0;
                if (++_frames < 30)
                {
                    return "watching";
                }
                return _sentWhilePaused ? "paused, and the shaders' clock was still being sent" : null;
            });
            Do("the HUD's gear is the gear shader's, and dimmed while paused", () =>
            {
                ColorRect gear = _hud.ClockGear;
                if (gear == null || !gear.IsVisibleInTree() || (gear.Material as ShaderMaterial)?.Shader?.ResourcePath != ShaderRect.GearShader)
                {
                    return "the clock bar has no gear drawn by " + ShaderRect.GearShader;
                }
                if (((ShaderMaterial)gear.Material).GetShaderParameter("rect_px").AsVector2() != gear.Size)
                {
                    return $"the gear's shader works in {((ShaderMaterial)gear.Material).GetShaderParameter("rect_px")}, the control is {gear.Size}";
                }
                return gear.SelfModulate != Colors.White ? null : "paused, the gear is as bright as running";
            });
            Do("the paused frame", () => { ScenarioCapture.Shot(Tree.Root, "tick-paused"); return null; });

            Do("press Space again", () => { Tap(Key.Space); return null; });
            Until("the factory runs, the gear bright again", 0.5, () =>
                _world.Clock.State != ClockState.Running ? "Space did not play"
                : _hud.ClockGear.SelfModulate == Colors.White ? null : "running, the gear is still dimmed");
            Do("click 4x", () => { Click(_hud.SpeedButton(ClockReadout.SpeedPresets.Length - 1)); return null; });
            Until("the clock is at its fastest", 0.5, () =>
                Mathf.IsEqualApprox(_world.Clock.Speed, ClockReadout.SpeedPresets.Max()) ? null : $"speed {_world.Clock.Speed}");
            Do("watch the phase at speed", () => { _frames = 0; return null; });
            Until("the phase runs as many times faster", 1.0, () =>
            {
                if (!Watched(WatchFrames))
                {
                    return "watching";
                }
                float step = Wrapped(TickShaderGlobals.TickPhase - _phaseBefore) / WatchFrames;
                float times = ClockReadout.SpeedPresets.Max();
                return Mathf.Abs(step / _stepAtOne - times) < 0.05f * times ? null : $"{step / _stepAtOne:F2} times as fast, the clock {times}";
            });
            Do("click 1x", () => { Click(_hud.SpeedButton(ClockReadout.IndexOfSpeed(1f))); return null; });
            Do("log the clock", () =>
            {
                Log($"shaders' clock: {_stepAtOne * 60f:F1} ticks/s at 1x, nothing sent while paused, {ClockReadout.SpeedPresets.Max():F0} times at {ClockReadout.SpeedPresets.Max():F0}x");
                return null;
            });
        }

        private const int WatchFrames = 24;
        private bool _sentWhilePaused;

        /// <summary>
        /// Called once a frame by a waiting step: notes the shaders' phase on the first call, and
        /// is true on the call <paramref name="frames"/> frames after it.
        /// </summary>
        private bool Watched(int frames)
        {
            if (_frames == 0)
            {
                _phaseBefore = TickShaderGlobals.TickPhase;
            }
            return _frames++ >= frames;
        }

        private static float Wrapped(float phase) => phase < 0f ? phase + TickVisuals.PhaseWrapTicks : phase;

        // --- Reduced motion --------------------------------------------------------------------

        private void PlanReducedMotion()
        {
            SwitchReducedMotion(on: true);
            Until("the shaders are told to hold still", 0.5, () =>
                !_world.Motion.TickMotion && TickShaderGlobals.Motion == 0f ? null : $"sim_motion {TickShaderGlobals.Motion}");
            Do("capture it still", () => { ScenarioCapture.Shot(Tree.Root, "tick-reduced"); return null; });
            SwitchReducedMotion(on: false);
            Until("and to move again", 0.5, () =>
                _world.Motion.TickMotion && TickShaderGlobals.Motion == 1f ? null : $"sim_motion {TickShaderGlobals.Motion}");
            Do("log reduced motion", () => { Log("Reduced motion stills the shaders, and gives them back"); return null; });
        }

        private void SwitchReducedMotion(bool on)
        {
            string state = on ? "on" : "off";
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Until("Management opens", 0.5, () => _management.IsOpen ? null : "Tab did not open Management");
            Do("click the Settings tab", () => { Click(_management.TabButtons[UI.ManagementTab.Settings]); return null; });
            Until("Settings shows", 0.5, () => _management.TabContent[UI.ManagementTab.Settings].Visible ? null : "not showing");
            Do($"click Reduced motion {state}", () => { Click(_management.Settings.ReducedMotion); return null; });
            Until($"Reduced motion is {state}", 0.5, () => _world.Motion.Reduced == on ? null : $"Reduced motion is {_world.Motion.Reduced}");
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Until("Management closes", 0.5, () => !_management.IsOpen ? null : "still open");
        }

        // --- The stress: a thousand belts, three draw calls, nothing allocated ---------------------

        private void PlanStress()
        {
            Do("buy the whole room and plank every free cell with belt", () =>
            {
                while (_world.Bounds.CanExpand)
                {
                    EndgameLoad.Grant(_world, _world.FloorExpansion.NextCost());
                    if (!_world.FloorExpansion.TryPurchaseExpansion())
                    {
                        return "floor: " + _world.FloorExpansion.LastStatusMessage;
                    }
                }
                PlaceableBuilding belt = _world.Placeables.First(p => p.Key == "BeltPrefab").Prefab;
                PlaceableBuilding splitter = _world.Placeables.First(p => p.Key == "BeltSplitterPrefab").Prefab;
                int rows = 0;
                for (int y = FloorLayout.WorldMinY; y <= _world.Bounds.NorthExtent; y++, rows++)
                {
                    // Rows run east and west by turns, with a splitter every eighth cell of every
                    // fourth row: a gear batch of some size too.
                    for (int x = -FloorLayout.HalfExtent; x <= FloorLayout.HalfExtent; x++)
                    {
                        var cell = new CoreVector2Int(x, y);
                        bool gear = rows % 4 == 0 && Mathf.PosMod(x, 8) == 0;
                        PlaceableBuilding prefab = gear ? splitter : belt;
                        _world.Build.SetActivePrefab(prefab);
                        if (_world.Build.GhostStateFor(cell) == BuildGhostState.Unaffordable)
                        {
                            EndgameLoad.Grant(_world, prefab.Cost.Select(c => new RecipeIngredient { itemType = c.itemType, quantity = c.quantity * 64 }));
                        }
                        if (_world.Build.GhostStateFor(cell) != BuildGhostState.Valid)
                        {
                            continue;
                        }
                        Facing want = rows % 2 == 0 ? Facing.East : Facing.West;
                        while (_world.Build.PlacementFacing != want)
                        {
                            _world.Build.RotatePlacement();
                        }
                        _world.Build.PlaceOrRemove(cell);
                    }
                }
                _world.Build.CancelPlacement();
                return null;
            });
            Until($"at least {StressTiles} tiles animate, each an instance, in three batches", 2.0, () =>
            {
                int tiles = _tick.Belts.Count + _tick.Gears.Count + _tick.Pipes.Count;
                int belts = BeltsPlaced().Count;
                if (_tick.Belts.Count != belts)
                {
                    return $"{_tick.Belts.Count} belt instances for {belts} belts";
                }
                return tiles >= StressTiles ? null : $"only {tiles} tiles fit the room";
            });
            Do("zoom out to see the most of it", () =>
            {
                while (_world.Camera.ZoomStep(-1))
                {
                }
                return null;
            });
            Wait(0.5);

            // Nothing changes in the factory from here, so a frame's work is the clock alone.
            Do("count what a frame allocates", () =>
            {
                _refillsBefore = _tick.Refills;
                _commitsBefore = _tick.Belts.Commits + _tick.Gears.Commits + _tick.Pipes.Commits;
                _tick.AllocatedBytes = 0;
                _tick.CountAllocations = true;
                _frames = 0;
                return null;
            });
            Until("300 frames of a running factory", 8.0, () => ++_frames >= 300 ? null : "running");
            Do("the layer allocated nothing, and sent no instance again", () =>
            {
                _tick.CountAllocations = false;
                int commits = _tick.Belts.Commits + _tick.Gears.Commits + _tick.Pipes.Commits;
                if (_tick.AllocatedBytes != 0)
                {
                    return $"{_tick.AllocatedBytes} bytes allocated over the frames";
                }
                return _tick.Refills == _refillsBefore && commits == _commitsBefore
                    ? null : $"{_tick.Refills - _refillsBefore} refills and {commits - _commitsBefore} buffers sent with nothing built";
            });

            Do("pick the belt up again over the belts", () => { Tap(Key.Key3); return null; });
            Do("point at a cell", () => { PointAtCell(At(1, 1)); _grid.AllocatedBytes = 0; _grid.CountAllocations = true; _frames = 0; return null; });
            Until("120 frames of the grid, the pointer moving", 6.0, () =>
            {
                PointAtCell(At(1 + (_frames / 16) % 3, 1));
                return ++_frames >= 120 ? null : "running";
            });
            Do("the grid allocated nothing", () =>
            {
                _grid.CountAllocations = false;
                return _grid.AllocatedBytes == 0 ? null : $"{_grid.AllocatedBytes} bytes allocated by the grid";
            });
            Do("capture the stress", () => { ScenarioCapture.Shot(Tree.Root, "tick-stress"); return null; });
            Do("press Escape", () => { Tap(Key.Escape); return null; });

            // What the layer costs the renderer: the frame with it, and without.
            Wait(0.2);
            Do("count the draw calls with the layer", () => { _drawsWith = Draws(); return null; });
            Do("hide the layer", () => { _tick.Visible = false; return null; });
            Wait(0.2);
            Do("count them without", () => { _drawsWithout = Draws(); _tick.Visible = true; return null; });
            Do("the layer is at most three draw calls", () =>
            {
                int tiles = _tick.Belts.Count + _tick.Gears.Count + _tick.Pipes.Count;
                if (_headless)
                {
                    Log($"stress: {tiles} tiles ({_tick.Belts.Count} belts, {_tick.Gears.Count} gears, {_tick.Pipes.Count} hot pipes), 0 bytes allocated over 300 frames; headless, so no draw calls to count");
                    return null;
                }
                double cost = _drawsWith - _drawsWithout;
                Log($"stress: {tiles} tiles ({_tick.Belts.Count} belts, {_tick.Gears.Count} gears, {_tick.Pipes.Count} hot pipes), 0 bytes allocated over 300 frames, {cost:F0} draw calls ({_drawsWith:F0} with the layer, {_drawsWithout:F0} without)");
                return cost >= 1 && cost <= 3 ? null : $"the layer costs {cost:F0} draw calls ({_drawsWith:F0} with, {_drawsWithout:F0} without)";
            });

            Do("show every cell boundary", () => { _grid.DebugCells = true; return null; });
            Wait(0.1);
            Do("the debug grid covers the view", () =>
            {
                Rect2 view = ViewRect.World;
                if (!_grid.Shown || !_grid.DrawnRect.Encloses(view.Grow(-1f)))
                {
                    return $"grid {_grid.DrawnRect}, view {view}";
                }
                ScenarioCapture.Shot(Tree.Root, "tick-grid-debug");
                _grid.DebugCells = false;
                return null;
            });
        }

        private int _refillsBefore, _commitsBefore;

        private static double Draws() => Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);

        // --- Input ------------------------------------------------------------------------------

        private Vector2 ScreenOf(CoreVector2Int cell) => Tree.Root.GetCanvasTransform() * GridConversions.CellToWorld(cell);

        private void PointAtCell(CoreVector2Int cell) => PointAt(ScreenOf(cell));

        private void ClickCell(CoreVector2Int cell) => Click(ScreenOf(cell));

        /// <summary>R, as many times as it takes to face <paramref name="facing"/>.</summary>
        private void TurnTo(Facing facing)
        {
            for (int i = 0; i < 3; i++)
            {
                Do($"press R toward {facing}", () =>
                {
                    if (_world.Build.PlacementFacing != facing)
                    {
                        Tap(Key.R);
                    }
                    return null;
                });
            }
            Until($"the belt faces {facing}", 0.5, () => _world.Build.PlacementFacing == facing ? null : $"facing {_world.Build.PlacementFacing}");
        }

        private static bool Near(Color a, Color b) =>
            Mathf.Abs(a.R - b.R) < 0.01f && Mathf.Abs(a.G - b.G) < 0.01f && Mathf.Abs(a.B - b.B) < 0.01f && Mathf.Abs(a.A - b.A) < 0.01f;
    }
}
