using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Game feel, end to end (docs/game-feel-design.md §6): the wheel steps a zoom level and the
    /// camera glides there; a depot lands with a squash and dust, a good popping out of it when
    /// it fills, and comes down in a squash and dust; a card dropped in the Workbench lands with
    /// weight and the tape prints its new line; a click on the alert strip glides the camera to
    /// the stopped golem and walking brings it back; and a Clock Tower stage glides the camera to
    /// the tower, shows the banner after the reveal, and glides back. Windowed with
    /// <c>--capture-dir</c>, it saves <c>feel-*.png</c> at each.
    /// </summary>
    public sealed class FeelScenario : IScenario
    {
        private readonly List<(string What, Func<string> Act, float Wait)> _steps = new List<(string, Func<string>, float)>();
        private readonly List<string> _passed = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private CameraRigNode _camera;
        private BuildingsLayer _buildings;
        private HudOverlay _hud;
        private int _step;
        private float _waited;
        private PlaceableBuilding _depot;
        private GolemEntity _golem;
        private CoreVector2Int _depotCell;
        private Dictionary<FxKind, int> _fxBefore;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _camera = _tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>().FirstOrDefault();
            _buildings = BuildingsLayerOf(_tree);
            _hud = _tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<HudOverlay>().FirstOrDefault();
            // Beside the player, so a windowed run's captures show it.
            var player = _tree.Root.FindChild("Player", true, false) as Node2D;
            _depotCell = GridConversions.WorldToCell(player?.GlobalPosition ?? Vector2.Zero) + new CoreVector2Int(2, 1);

            // The zoom levels (decision 3).
            Do("wheel in a notch", () => { Wheel(MouseButton.WheelUp); return null; }, 2f);
            Do("glide to Near", () => Near(_camera.OrthographicSize, CameraDirector.ZoomLevels[1], "zoom after a notch in"), 0f);
            Do("wheel back out", () => { Wheel(MouseButton.WheelDown); return null; }, 2f);
            Do("back at Normal", () => Near(_camera.OrthographicSize, CameraRigRules.DefaultOrthographicSize, "zoom back out"), 0f);

            // A building lands, fills and comes down (§4).
            Do("place a depot", PlaceDepot, 0.05f);
            Do("it lands", () =>
            {
                BuildingView view = ViewOf(_depot);
                if (view == null || !view.Landing)
                {
                    return "the depot didn't land";
                }
                return Grew(FxKind.Dust, "dust as it landed");
            }, 0.05f);
            Do("capture the landing", () => { Shot("feel-place"); return null; }, 0.4f);
            Do("fill it", () =>
            {
                _fxBefore = Counts();
                _depot.GetPart<ICrate>().Buffer.Deposit(ItemType.Coke, 1);
                return null;
            }, 0.08f);
            Do("a good pops out", () => Grew(FxKind.GoodPop, "the good's pop") ?? Grew(FxKind.Glint, "the glint"), 0f);
            Do("capture the pop", () => { Shot("feel-pop"); return null; }, 0.5f);
            Do("demolish it", () =>
            {
                _fxBefore = Counts();
                PlaceableBuilding prefab = _world.Placeables.Single(p => p.Key == "DepotPrefab").Prefab;
                _world.Build.SetActivePrefab(prefab);
                _world.Build.PlaceOrRemove(_depotCell);
                _world.Build.CancelPlacement();
                return _depot.IsRemoved ? null : "the depot wasn't removed: " + _world.Build.LastStatusMessage;
            }, 0.05f);
            Do("it comes down", () =>
            {
                BuildingView view = _buildings.GetChildren().OfType<BuildingView>().FirstOrDefault(v => v.Building == _depot);
                return view != null && view.Demolishing ? Grew(FxKind.Dust, "dust as it came down") : "the depot didn't squash down";
            }, 0.5f);
            Do("and is gone", () =>
                _buildings.GetChildren().OfType<BuildingView>().Any(v => v.Building == _depot) ? "the demolished depot's view stayed" : null, 0f);

            // The Workbench (§4): a card lands with weight, the tape prints.
            Do("build a golem", BuildGolem, 0.1f);
            Do("drop a card", () =>
            {
                var screen = _world.WorkbenchScreen as WorkbenchScreen;
                int id = screen?.VaultCardId(_world.Workbench.VaultAppendages.FirstOrDefault()) ?? -1;
                if (id < 0)
                {
                    return "no card in the vault";
                }
                screen.Drop(id, UI.WorkbenchZone.Socket(0));
                return null;
            }, 0.05f);
            Do("it lands and the tape prints", () =>
            {
                var screen = (WorkbenchScreen)_world.WorkbenchScreen;
                if (!screen.CardLanding)
                {
                    return "the card didn't land";
                }
                return screen.TapePrintedText.Length < screen.TapeText.Length && screen.TapeText.StartsWith(screen.TapePrintedText)
                    ? null
                    : $"the tape didn't print: '{screen.TapePrintedText}' of '{screen.TapeText}'";
            }, 1.5f);
            Do("the tape finishes its line", () =>
            {
                var screen = (WorkbenchScreen)_world.WorkbenchScreen;
                string printed = screen.TapePrintedText;
                screen.Close();
                return printed == screen.TapeText ? null : $"the tape stopped at '{printed}'";
            }, 0.1f);

            // The pan to alerts (decision 2).
            Do("stall the golem", StallGolem, 1.2f);
            Do("click the alert strip", () =>
            {
                Rect2 strip = _hud.AlertsStrip.GetGlobalRect();
                Click(strip.GetCenter());
                return null;
            }, 2.5f);
            Do("the camera is on the golem", () =>
            {
                if (_world.Camera.Focus != CameraFocus.Alert || _world.Camera.AlertGolemId != _golem.GolemId)
                {
                    return $"focus {_world.Camera.Focus} on {_world.Camera.AlertGolemId}, not the stopped golem ({_golem.Program.State}, {_golem.Mood})";
                }
                float off = _camera.GlobalPosition.DistanceTo(GridConversions.CellToWorld(_golem.Cell));
                return off < 4f ? null : $"the camera is {off:0} px from the golem";
            }, 0f);
            Do("capture the golem", () => { Shot("feel-alert"); return null; }, 0f);
            Do("walk", () => { Key(Godot.Key.D, true); return null; }, 0.2f);
            Do("the camera is back", () =>
            {
                Key(Godot.Key.D, false);
                return _world.Camera.Focus == CameraFocus.Player ? null : "walking didn't bring the camera back";
            }, 0.1f);

            // The Clock Tower's stage (decision 4).
            Do("a stage completes", () =>
            {
                EventBus.Publish(new ClockTowerStageCompletedEvent(1, "Foundation", false, _world.Clock.CurrentTick));
                return _world.Camera.TowerPhase == TowerEventPhase.GlideIn ? null : "no tower event";
            }, CameraDirector.TowerGlideInSeconds + CameraDirector.TowerRevealAt + 0.2f);
            Do("the camera is at the tower, the stage shown", () =>
            {
                Vector2 tower = GridConversions.FootprintFeet(TownSquare.TowerCentre, TownSquare.TowerSize);
                if (Math.Abs(_camera.GlobalPosition.X - tower.X) > 12f)
                {
                    return $"the camera is at x {_camera.GlobalPosition.X:0}, the tower at {tower.X:0}";
                }
                if (_world.Clock.State != Simulation.ClockState.Running)
                {
                    return "the clock stopped for the event";
                }
                return _hud.BannerText == "Stage 1 complete · Foundation" ? null : $"banner '{_hud.BannerText}'";
            }, 0f);
            Do("capture the tower", () => { Shot("feel-tower"); return null; }, 3f);
            Do("and back", () =>
                _world.Camera.TowerPhase == TowerEventPhase.None && _world.Camera.Focus == CameraFocus.Player && _hud.BannerText == ""
                    ? null
                    : $"still {_world.Camera.TowerPhase}, banner '{_hud.BannerText}'", 0f);
        }

        public ScenarioResult? Step(double delta)
        {
            if (_step >= _steps.Count)
            {
                return new ScenarioResult(true, string.Join("; ", _passed));
            }
            if (_waited > 0f)
            {
                _waited -= (float)delta;
                return null;
            }
            (string what, Func<string> act, float wait) = _steps[_step++];
            string failure;
            try
            {
                failure = act();
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
            if (failure != null)
            {
                return new ScenarioResult(false, $"{what}: {failure}");
            }
            _passed.Add(what);
            _waited = wait;
            return null;
        }

        private void Do(string what, Func<string> act, float wait) => _steps.Add((what, act, wait));

        private string PlaceDepot()
        {
            PlaceableBuilding prefab = _world.Placeables.Single(p => p.Key == "DepotPrefab").Prefab;
            EndgameLoad.Grant(_world, prefab.Cost);
            _fxBefore = Counts();
            _world.Build.SetActivePrefab(prefab);
            _world.Build.PlaceOrRemove(_depotCell);
            _world.Build.CancelPlacement();
            _depot = _world.Build.Buildings.SingleOrDefault(b => b.Cell == _depotCell && !b.IsRemoved);
            return _depot == null ? "could not place: " + _world.Build.LastStatusMessage : null;
        }

        private string BuildGolem()
        {
            var panel = _tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
            ChassisDefinition presser = _world.Definitions.Chassis["BrassPresser"];
            EndgameLoad.Grant(_world, presser.cost);
            panel.Open(_world.StarterStation);
            if (!panel.TryConstruct(presser))
            {
                return "build: " + panel.Status;
            }
            _golem = panel.LastBuilt;
            _golem.SetPlacement(new CoreVector2Int(4, -12), Facing.East);
            return _world.WorkbenchScreen is WorkbenchScreen { Visible: true } ? null : "the Workbench didn't open on the new golem";
        }

        /// <summary>An Assemble with nothing to assemble from: a golem that has stopped.</summary>
        private string StallGolem()
        {
            _golem.Program.logicCore = _world.Definitions.LogicCores["AlwaysOnCore"];
            _golem.Program.appendages.Clear();
            return _golem.Program.TryAddAppendage(_world.Definitions.Appendages["AssembleCoking"]) ? null : "the Presser refused Assemble";
        }

        private BuildingView ViewOf(PlaceableBuilding building) =>
            _buildings.TryGetView(building, out BuildingView view) ? view : null;

        private Dictionary<FxKind, int> Counts() => new Dictionary<FxKind, int>(_buildings.Fx.Started);

        private string Grew(FxKind kind, string what)
        {
            int before = _fxBefore != null && _fxBefore.TryGetValue(kind, out int b) ? b : 0;
            int now = _buildings.Fx.Started.TryGetValue(kind, out int n) ? n : 0;
            return now > before ? null : "no " + what;
        }

        private static string Near(float actual, float wanted, string what) =>
            Math.Abs(actual - wanted) < 0.05f ? null : $"{what}: size {actual:0.00}, wanted {wanted:0.00}";

        private static BuildingsLayer BuildingsLayerOf(SceneTree tree) =>
            tree.GetFirstNodeInGroup(BuildingsLayer.GroupName) as BuildingsLayer;

        private void Wheel(MouseButton button)
        {
            Vector2 at = _tree.Root.GetVisibleRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        }

        private static void Click(Vector2 at)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }

        private static void Key(Key key, bool down) =>
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down });

        private void Shot(string name) => ScenarioCapture.Shot(_tree.Root, name);
    }
}
