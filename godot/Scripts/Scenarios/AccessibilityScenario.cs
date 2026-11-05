using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.Settings;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Reduced motion and the colour-blind marks (docs/menus-design.md §5), on the `moods`
    /// street, switched on and off with real clicks in the Settings tab:
    /// <list type="bullet">
    ///   <item>Before: the golems bob (so "still" below is not true of a still street).</item>
    ///   <item>On: for two seconds no golem's body moves, scales or skews, while any frame that
    ///   changed before still changes. The wheel cuts to a zoom level. A depot placed, filled and
    ///   demolished has no landing, bump, dust or pop (the dust and pop are withheld, counted) and
    ///   is gone at once. A click on the alert strip cuts the camera to the stopped golem; hovered,
    ///   its source tile wears the stripes. A card dropped in the Workbench is settled at once and
    ///   the tape shows its whole line. A tower stage cuts the camera to the tower, no building
    ///   swells or flashes, and the banner still shows; the clock runs throughout.</item>
    ///   <item>Off again: the golems bob, a depot lands in dust, and the stripes are gone.</item>
    /// </list>
    /// Settings are the scenario's scratch file (GameSettingsStore), never the player's.
    /// Captures <c>access-settings</c>, <c>access-stripes</c>, <c>access-tower</c>.
    /// </summary>
    public sealed class AccessibilityScenario : StreetFixture
    {
        private ManagementScreen _screen;
        private BuildingsLayer _buildings;
        private HudOverlay _hud;
        private CameraRigNode _camera;
        private GolemHoverNode _hover;
        private string _violation;
        private readonly Dictionary<GolemNode, GolemFrameKey> _firstFrame = new Dictionary<GolemNode, GolemFrameKey>();
        private int _framesChangedMoving;
        private int _framesChangedStill;
        private Dictionary<FxKind, int> _started;
        private Dictionary<FxKind, int> _withheld;
        private PlaceableDepot _depot;

        private static readonly CoreVector2Int DepotCell = new CoreVector2Int(-1, 4);

        protected override void Plan()
        {
            Do("find the screens", () =>
            {
                _screen = Tree.Root.FindChild("Management", true, false) as ManagementScreen;
                _buildings = Tree.GetFirstNodeInGroup(BuildingsLayer.GroupName) as BuildingsLayer;
                _hud = Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<HudOverlay>().FirstOrDefault();
                _camera = Tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>().FirstOrDefault();
                _hover = Tree.Root.FindChild("GolemHover", true, false) as GolemHoverNode;
                if (_screen == null || _buildings == null || _hud == null || _camera == null || _hover == null)
                {
                    return "Sandbox.tscn is missing Management, the buildings, the HUD, the camera or the hover";
                }
                GameSettingsStore.ResetForScenario(Tree.Root);
                return null;
            });
            Do("stand away; program the stuck", () => { Stand(-2f, 6f); ProgramTheStuck(); return null; });
            Wait(2.0);

            PlanBefore();
            PlanSwitch(on: true);
            PlanStill();
            PlanZoom();
            PlanDepot();
            PlanAlertAndStripes();
            PlanWorkbench();
            PlanTower();
            PlanSwitch(on: false);
            PlanAfter();
        }

        // --- Before: the street moves --------------------------------------------------------

        private void PlanBefore()
        {
            Do("note each golem's frame", () => { NoteFrames(); return null; });
            Until("watch the street move for 2 s", 2.0, () =>
            {
                _framesChangedMoving = Math.Max(_framesChangedMoving, FramesChanged());
                return "watching";
            }, mayTimeOut: true);
            Until("some golem bobs", 1.0, () =>
                Nodes().Any(n => Mathf.Abs(n.DrawnOffset.Y) > 0.01f) ? null : "no golem moved: the 'still' check below would prove nothing");
        }

        // --- The Settings tab, by clicks ----------------------------------------------------

        private void PlanSwitch(bool on)
        {
            string state = on ? "on" : "off";
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Until("Management opens", 0.5, () => _screen.IsOpen ? null : "Tab did not open Management");
            Do("click the Settings tab", () => { Click(_screen.TabButtons[UI.ManagementTab.Settings]); return null; });
            Until("Settings shows", 0.5, () => _screen.TabContent[UI.ManagementTab.Settings].Visible ? null : "not showing");
            Do($"click Reduced motion {state}", () => { Click(_screen.Settings.ReducedMotion); return null; });
            Do($"click Colour-blind marks {state}", () => { Click(_screen.Settings.ColorBlindMarks); return null; });
            Do($"both are {state}, in the world and the text", () =>
                World.Motion.Reduced == on && ColourMarks.Enabled == on && GameSettingsStore.Current.ReducedMotion == on
                && GameSettingsStore.Current.ColorBlindMarks == on
                && TextTokens.Fill(ColourMarks.SourceToken) == (on ? "striped teal" : "teal")
                    ? null
                    : $"world {World.Motion.Reduced}, marks {ColourMarks.Enabled}, settings {GameSettingsStore.Current.ReducedMotion}/{GameSettingsStore.Current.ColorBlindMarks}");
            if (on)
            {
                Do("capture the settings", () => { ScenarioCapture.Shot(Tree.Root, "access-settings"); return null; });
            }
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Until("Management closes", 0.5, () => !_screen.IsOpen ? null : "still open");
        }

        // --- On: nothing decorative moves -------------------------------------------------------

        private void PlanStill()
        {
            Do("note each golem's frame again", () => { NoteFrames(); _violation = null; return null; });
            Until("watch the street for 2 s", 2.0, () =>
            {
                foreach (GolemNode n in Nodes())
                {
                    if (n.DrawnOffset != Vector2.Zero || n.DrawnScale != Vector2.One || n.DrawnSkew != 0f)
                    {
                        _violation ??= $"{n.Entity.GolemId} moved: offset {n.DrawnOffset}, scale {n.DrawnScale}, skew {n.DrawnSkew}";
                    }
                }
                _framesChangedStill = Math.Max(_framesChangedStill, FramesChanged());
                return "watching";
            }, mayTimeOut: true);
            Do("no golem's body moved", () => _violation);
            Do("its frames still play", () =>
                _framesChangedMoving == 0 || _framesChangedStill > 0
                    ? null
                    : $"{_framesChangedMoving} golems changed frame with motion, none without");
            Do("still", () =>
            {
                Log.Add($"Reduced: no golem moved for 2 s ({_framesChangedStill} changed frame, {_framesChangedMoving} with motion)");
                return null;
            });
        }

        private void PlanZoom()
        {
            Do("wheel in a notch", () => { Wheel(MouseButton.WheelUp); return null; });
            Do("the zoom cut to Near", () =>
                Mathf.Abs(_camera.OrthographicSize - CameraDirector.ZoomLevels[1]) < 0.001f
                    ? null : $"size {_camera.OrthographicSize:0.000} three frames after the notch (a glide)");
            Do("wheel back out", () => { Wheel(MouseButton.WheelDown); return null; });
            Do("the zoom cut back", () =>
                Mathf.Abs(_camera.OrthographicSize - CameraDirector.ZoomLevels[CameraDirector.DefaultZoomLevel]) < 0.001f
                    ? null : $"size {_camera.OrthographicSize:0.000}");
        }

        private void PlanDepot()
        {
            Do("place a depot", () =>
            {
                NoteFx();
                _depot = Place<PlaceableDepot>("DepotPrefab", DepotCell.x, DepotCell.y);
                return null;
            });
            Do("no landing, no dust", () =>
            {
                BuildingView view = ViewAt(DepotCell);
                if (view == null)
                {
                    return "no view for the depot";
                }
                if (view.Landing || view.DrawnScale != Vector2.One)
                {
                    return $"it landed: scale {view.DrawnScale}";
                }
                return Grew(_started, _buildings.Fx.Started, FxKind.Dust) ? "dust started"
                    : !Grew(_withheld, _buildings.Fx.Withheld, FxKind.Dust) ? "no dust was asked for" : null;
            });
            Do("fill it", () =>
            {
                NoteFx();
                Feed(_depot, ItemType.Scrap, 1);
                return null;
            });
            Do("no bump, no pop", () =>
            {
                BuildingView view = ViewAt(DepotCell);
                if (view.DrawnScale != Vector2.One)
                {
                    return $"it bumped: scale {view.DrawnScale}";
                }
                return Grew(_started, _buildings.Fx.Started, FxKind.GoodPop) ? "the good popped"
                    : !Grew(_withheld, _buildings.Fx.Withheld, FxKind.GoodPop) ? "no pop was asked for" : null;
            });
            Do("take the Scrap back and demolish it", () =>
            {
                CrateTransfer.Take(_depot, World.Wallet.Pack, ItemType.Scrap, 1);
                PlaceableBuilding prefab = World.Placeables.Single(p => p.Key == "DepotPrefab").Prefab;
                World.Build.SetActivePrefab(prefab);
                World.Build.PlaceOrRemove(DepotCell);
                World.Build.CancelPlacement();
                return null;
            });
            Do("gone at once", () => ViewAt(DepotCell) == null ? null : "the demolished depot is still drawn three frames later");
            Do("depot", () => { Log.Add("a depot placed, filled and demolished with no landing, bump, dust or pop, and gone at once"); return null; });
        }

        private void PlanAlertAndStripes()
        {
            Do("click the alert strip", () => { Click(_hud.AlertsStrip.GetGlobalRect().GetCenter()); return null; });
            Do("the camera cut to the stopped golem", () =>
            {
                GolemEntity golem = World.Golems.FirstOrDefault(g => g.GolemId == World.Camera.AlertGolemId);
                if (World.Camera.Focus != CameraFocus.Alert || golem == null)
                {
                    return $"focus {World.Camera.Focus}";
                }
                float off = _camera.GlobalPosition.DistanceTo(GridConversions.CellToWorld(golem.Cell));
                return off < 1f ? null : $"the camera is {off:0} px from the golem three frames after the click (a glide)";
            });
            Do("point at it", () =>
            {
                PointAtCell(World.Golems.First(g => g.GolemId == World.Camera.AlertGolemId).Cell);
                return null;
            });
            Until("its tiles show, the source striped", 0.5, () =>
            {
                GolemNode node = Nodes().FirstOrDefault(n => n.Entity.GolemId == World.Camera.AlertGolemId);
                if (_hover.Hovered?.GolemId != World.Camera.AlertGolemId || node == null || !node.TilesVisible)
                {
                    return $"hovered {_hover.Hovered?.GolemId ?? "nothing"}";
                }
                return node.SourceStripesVisible ? null : "the source tile has no stripes";
            });
            Do("capture the stripes", () => { ScenarioCapture.Shot(Tree.Root, "access-stripes"); return null; });
            Do("point away and walk", () =>
            {
                PointAt(new Vector2(20f, 300f));
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.D, Keycode = Key.D, Pressed = true });
                return null;
            });
            Wait(0.2);
            Do("stop", () =>
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.D, Keycode = Key.D, Pressed = false });
                return World.Camera.Focus == CameraFocus.Player ? null : "walking did not bring the camera back";
            });
            Do("the camera cut back to the player", () =>
                _camera.GlobalPosition.DistanceTo(Player.GlobalPosition) < 1f ? null : $"{_camera.GlobalPosition.DistanceTo(Player.GlobalPosition):0} px from the player");
            Do("alert", () => { Log.Add("the alert strip cut the camera to the golem and back; the hovered golem's source tile was striped"); return null; });
        }

        private void PlanWorkbench()
        {
            Do("open the Workbench on P", () =>
            {
                var screen = (WorkbenchScreen)World.WorkbenchScreen;
                screen.RetargetGolem(P);
                screen.Open();
                return null;
            });
            Do("drop a card", () =>
            {
                var screen = (WorkbenchScreen)World.WorkbenchScreen;
                int id = screen.VaultCardId(World.Workbench.VaultAppendages.FirstOrDefault());
                if (id < 0)
                {
                    return "no card in the vault";
                }
                screen.Drop(id, UI.WorkbenchZone.Socket(0));
                return null;
            });
            Do("settled at once, the tape's line whole", () =>
            {
                var screen = (WorkbenchScreen)World.WorkbenchScreen;
                if (screen.CardLanding)
                {
                    return "the card is still landing";
                }
                return screen.TapePrintedText == screen.TapeText ? null : $"the tape is typing: '{screen.TapePrintedText}' of '{screen.TapeText}'";
            });
            Do("close it", () => { ((WorkbenchScreen)World.WorkbenchScreen).Close(); return null; });
        }

        private void PlanTower()
        {
            Do("a stage completes", () =>
            {
                _violation = null;
                EventBus.Publish(new ClockTowerStageCompletedEvent(1, "Foundation", false, World.Clock.CurrentTick));
                return World.Camera.TowerPhase == TowerEventPhase.GlideIn ? null : "no tower event";
            });
            Do("the camera cut to the tower", () =>
            {
                Vector2 tower = GridConversions.FootprintFeet(TownSquare.TowerCentre, TownSquare.TowerSize);
                return Mathf.Abs(_camera.GlobalPosition.X - tower.X) < 1f
                    ? null : $"the camera is at x {_camera.GlobalPosition.X:0}, the tower at {tower.X:0} (a glide)";
            });
            Until("watch the reveal: nothing swells or flashes", CameraDirector.TowerGlideInSeconds + CameraDirector.TowerRevealAt + 0.6, () =>
            {
                foreach (BuildingView v in _buildings.GetChildren().OfType<BuildingView>())
                {
                    if (v.DrawnScale.X > 1.001f || v.DrawnModulate.R > 1.001f)
                    {
                        _violation ??= $"{v.SpriteName} swelled or flashed: scale {v.DrawnScale}, tint {v.DrawnModulate}";
                    }
                }
                return "watching";
            }, mayTimeOut: true);
            Do("no swell or flash, the banner shows, the clock runs", () =>
                _violation ?? (World.Camera.TowerRevealed && _hud.BannerText == "Stage 1 complete · Foundation"
                               && World.Clock.State == Simulation.ClockState.Running
                    ? null : $"revealed {World.Camera.TowerRevealed}, banner '{_hud.BannerText}'"));
            Do("capture the tower", () => { ScenarioCapture.Shot(Tree.Root, "access-tower"); return null; });
            Until("the event ends", CameraDirector.TowerRevealSeconds + CameraDirector.TowerGlideBackSeconds + 1.0, () =>
                World.Camera.TowerPhase == TowerEventPhase.None ? null : World.Camera.TowerPhase.ToString());
            Do("tower", () => { Log.Add("the tower's stage cut the camera there and back, no swell or flash, the banner shown"); return null; });
        }

        // --- Off again: it all comes back ------------------------------------------------------

        private void PlanAfter()
        {
            Until("the golems bob again", 1.5, () =>
                Nodes().Any(n => Mathf.Abs(n.DrawnOffset.Y) > 0.01f) ? null : "no golem moves with Reduced motion off");
            Do("place a depot", () =>
            {
                NoteFx();
                _depot = Place<PlaceableDepot>("DepotPrefab", DepotCell.x, DepotCell.y);
                return null;
            });
            Do("it lands in dust", () =>
            {
                BuildingView view = ViewAt(DepotCell);
                return view != null && view.Landing && Grew(_started, _buildings.Fx.Started, FxKind.Dust)
                    ? null : $"landing {view?.Landing}, dust {Grew(_started, _buildings.Fx.Started, FxKind.Dust)}";
            });
            Do("point at N", () => { PointAtCell(N.Cell); return null; });
            Until("N's tiles show, unstriped", 0.5, () =>
            {
                GolemNode node = NodeOf(N);
                if (_hover.Hovered != N || !node.TilesVisible)
                {
                    return $"hovered {_hover.Hovered?.GolemId ?? "nothing"}";
                }
                return node.SourceStripesVisible ? "the stripes stayed with the marks off" : null;
            });
            Do("after", () => { Log.Add("off again: the golems bob, a depot lands in dust, no stripes"); return null; });
        }

        // --- Helpers ------------------------------------------------------------------------

        private IEnumerable<GolemNode> Nodes() =>
            Tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().Where(n => n.Entity != null && !n.Entity.IsRemoved);

        private void NoteFrames()
        {
            _firstFrame.Clear();
            foreach (GolemNode n in Nodes())
            {
                _firstFrame[n] = n.DrawnFrame;
            }
        }

        private int FramesChanged() => _firstFrame.Count(e => GodotObject.IsInstanceValid(e.Key) && !e.Key.DrawnFrame.Equals(e.Value));

        private void NoteFx()
        {
            _started = new Dictionary<FxKind, int>(_buildings.Fx.Started);
            _withheld = new Dictionary<FxKind, int>(_buildings.Fx.Withheld);
        }

        private static bool Grew(Dictionary<FxKind, int> before, Dictionary<FxKind, int> now, FxKind kind) =>
            (now.TryGetValue(kind, out int n) ? n : 0) > (before != null && before.TryGetValue(kind, out int b) ? b : 0);

        private BuildingView ViewAt(CoreVector2Int cell)
        {
            PlaceableBuilding building = World.Build.Buildings.FirstOrDefault(b => b.Cell == cell && !b.IsRemoved);
            if (building != null)
            {
                return _buildings.TryGetView(building, out BuildingView view) ? view : null;
            }
            // Removed from the world: is its view still drawn?
            return _buildings.GetChildren().OfType<BuildingView>()
                .FirstOrDefault(v => v.Building.Cell == cell && v.Building.IsRemoved && !v.IsQueuedForDeletion());
        }

        private void Wheel(MouseButton button)
        {
            Vector2 at = Tree.Root.GetVisibleRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        }

        private void Click(Control control) => Click(control.GetGlobalRect().GetCenter());

        private void Click(Vector2 at)
        {
            PointAt(at);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }
    }
}
