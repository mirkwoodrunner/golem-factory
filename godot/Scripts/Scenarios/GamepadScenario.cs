using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The gamepad (docs/steam-deck-design.md D4), with joypad events only: from the bindings check
    /// on, every step under test sends <c>InputEventJoypadButton</c> and <c>InputEventJoypadMotion</c>
    /// through <see cref="Input.ParseInputEvent"/>, never a key or the mouse.
    /// <list type="number">
    ///   <item>Every verb has a pad binding beside its key.</item>
    ///   <item>The left stick walks the Artificer, no faster on a diagonal than straight.</item>
    ///   <item>A at the starting Scrap cart harvests; X picks a golem up and sets it down.</item>
    ///   <item>Facing the golem shows its hover card at the cell ahead; turning away hides it; the
    ///   right stick looks back at it, with the look-around tile drawn; a screen hides both; a walk
    ///   brings the cursor back to the cell ahead.</item>
    ///   <item>Y opens the pack and B closes it; Start opens the menu and B closes it.</item>
    ///   <item>LB picks up the belt; the cursor starts a cell ahead of the Artificer; the right stick
    ///   steps it; RT places one belt, RT held while the stick steps lays a run; RB turns it; B puts it down.</item>
    ///   <item>LT takes the wrecking bar and RT takes a belt back.</item>
    ///   <item>D-pad up zooms in, down zooms out; Back pauses and plays the clock.</item>
    ///   <item>A nudge of the mouse hands the build cursor back to the pointer.</item>
    /// </list>
    /// Writes only <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class GamepadScenario : StepScenario
    {
        private const string BeltKey = "BeltPrefab";
        private static readonly CoreVector3 BuildSpot = new CoreVector3(-5f, 8f, 0f);

        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private PlayerInteractor Hands => Sandbox.Interactor;
        private BuildModeController Build => Sandbox.Build;
        private PlayerNode Player => Tree.Root.FindChildren("*", "Node2D", true, false).OfType<PlayerNode>().First();
        private BuildCursorNode Cursor => Tree.Root.FindChildren("*", "Node2D", true, false).OfType<BuildCursorNode>().First();
        private CameraRigNode Camera => Tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>().First();
        private PackScreen Pack => Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<PackScreen>().First();
        private ManagementScreen Menu => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private BuildMenuNode BuildMenu => Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<BuildMenuNode>().First();
        private PlaceableBuilding BeltPrefab => Sandbox.Placeables.First(p => p.Key == BeltKey).Prefab;
        private int BeltsAlive => Build.Buildings.Count(b => !b.IsRemoved && b.PrefabKey == BeltKey);

        private float _startX;
        private float _straight;
        private int _scrapBefore;
        private CoreVector2Int _first;
        private int _beltsBefore;
        private Facing _facingBefore;
        private float _sizeBefore;
        private Golems.GolemEntity _golem;

        private GolemHoverNode Hover => Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<GolemHoverNode>().First();

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            InputDevices.Reset();
            Wait(0.5);
            PlanStart();
            PlanBindings();
            PlanWalk();
            PlanHands();
            PlanHover();
            PlanScreens();
            PlanBuild();
            PlanDemolish();
            PlanCameraAndClock();
            PlanNames();
            PlanBackgroundCap();
            PlanBackToMouse();
        }

        protected override void Cleanup()
        {
            ReleaseAll();
            InputDevices.Reset();
            BackgroundFrameCap.EvenUnderScenario = false;
        }

        // --- 0. A fresh game, the playtest card answered (a click: the screens are D6) --------

        private void PlanStart()
        {
            Do("answer the playtest's first question", () =>
            {
                var playtest = Tree.Root.FindChild("Playtest", true, false) as PlaytestNode;
                if (playtest?.Card.Visible == true)
                {
                    Click(playtest.Options.GetChildren().OfType<Button>().First());
                }
                return null;
            });
            Do("the card down, the guide put away, the clock running", () =>
            {
                if ((Tree.Root.FindChild("Playtest", true, false) as PlaytestNode)?.Card.Visible == true)
                {
                    return "the card is still up";
                }
                Sandbox.Tutorial?.Dismiss();
                Sandbox.Clock.Play();
                return null;
            });
        }

        // --- 1. Bindings ------------------------------------------------------------------------

        private void PlanBindings()
        {
            Do("every pad action has a joypad event, and the keys are still there", () =>
            {
                foreach (string action in PadBindings.Actions)
                {
                    if (!InputMap.HasAction(action))
                    {
                        return action + " isn't registered";
                    }
                    if (!InputMap.ActionGetEvents(action).Any(e => e is InputEventJoypadButton || e is InputEventJoypadMotion))
                    {
                        return action + " has no pad event";
                    }
                }
                foreach ((string action, Key key) in new[] { (PlayerNode.Interact, Key.E), (PlayerNode.MoveLeft, Key.A), (BuildCursorNode.CancelAction, Key.Escape), (ManagementScreen.ToggleAction, Key.Tab), (PadBindings.Pause, Key.Space) })
                {
                    if (!InputMap.ActionGetEvents(action).OfType<InputEventKey>().Any(k => k.PhysicalKeycode == key))
                    {
                        return action + " lost its key " + key;
                    }
                }
                Log($"{PadBindings.Actions.Count()} actions bound on the pad");
                return null;
            });
        }

        // --- 2. Walk ----------------------------------------------------------------------------

        private void PlanWalk()
        {
            Do("stand in the open", () => { Hands.Teleport(BuildSpot); return null; });
            Wait(0.2);
            Do("left stick right", () =>
            {
                _startX = Player.CorePosition.x;
                Axis(JoyAxis.LeftX, 1f);
                return null;
            });
            Wait(1.0);
            Do("let go: walked east, and the pad is the device in use", () =>
            {
                Axis(JoyAxis.LeftX, 0f);
                _straight = Player.CorePosition.x - _startX;
                if (_straight < 2f)
                {
                    return $"walked {_straight:0.00} cells";
                }
                return InputDevices.PadInUse ? null : "the stick didn't count as the pad";
            });
            Wait(0.3);
            Do("stopped when let go", () =>
            {
                float drift = Player.CorePosition.x - _startX - _straight;
                return drift < 0.15f ? null : $"kept walking {drift:0.00} cells";
            });
            Do("back, then the stick to a corner", () =>
            {
                Hands.Teleport(BuildSpot);
                return null;
            });
            Wait(0.2);
            Do("left stick up and right", () =>
            {
                _startX = Player.CorePosition.x;
                _sizeBefore = Player.CorePosition.y; // the start's y, for the distance
                Axis(JoyAxis.LeftX, 1f);
                Axis(JoyAxis.LeftY, -1f);
                return null;
            });
            Wait(1.0);
            Do("a diagonal is no faster than straight", () =>
            {
                Axis(JoyAxis.LeftX, 0f);
                Axis(JoyAxis.LeftY, 0f);
                float dx = Player.CorePosition.x - _startX, dy = Player.CorePosition.y - _sizeBefore;
                float diagonal = Mathf.Sqrt(dx * dx + dy * dy);
                if (dx <= 0f || dy <= 0f)
                {
                    return $"went ({dx:0.00}, {dy:0.00}), not north-east";
                }
                if (diagonal > _straight * 1.1f)
                {
                    return $"diagonal {diagonal:0.00} cells against straight {_straight:0.00}";
                }
                Log($"stick: {_straight:0.00} cells east in 1 s, {diagonal:0.00} north-east");
                return null;
            });
        }

        // --- 3. A and X -------------------------------------------------------------------------

        private void PlanHands()
        {
            Do("walk up to the starting Scrap cart", () =>
            {
                CoreVector2Int scrap = World.Setup.HomeSpot(ItemType.Scrap).Value;
                Hands.Teleport(new CoreVector3(scrap.x, scrap.y + 1f, 0f));
                return null;
            });
            Wait(0.3);
            Do("press A", () =>
            {
                _scrapBefore = Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, ItemType.Scrap);
                Button(JoyButton.A, true);
                return null;
            });
            Do("let go of A: one Scrap harvested", () =>
            {
                Button(JoyButton.A, false);
                int got = Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, ItemType.Scrap) - _scrapBefore;
                return got >= 1 ? null : $"A harvested {got}";
            });
            Do("a Scavenger built; stand beside it", () =>
            {
                ChassisDefinition chassis = Sandbox.Definitions.Chassis["ClockworkScavenger"];
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
                }
                if (!Sandbox.StarterStation.TryConstructGolem(chassis, out Golems.GolemEntity golem))
                {
                    return "no golem built";
                }
                (Sandbox.WorkbenchScreen as WorkbenchScreen)?.Close();
                _golem = golem;
                Hands.Teleport(new CoreVector3(golem.Cell.x, golem.Cell.y - 1, 0f));
                return null;
            });
            Wait(0.3);
            Do("press X", () => { Button(JoyButton.X, true); return null; });
            Do("carrying it", () =>
            {
                Button(JoyButton.X, false);
                return Hands.CarriedGolem != null ? null : "X picked nothing up: " + Hands.LastStatusMessage;
            });
            Do("X again", () => { Button(JoyButton.X, true); return null; });
            Do("set down", () =>
            {
                Button(JoyButton.X, false);
                return Hands.CarriedGolem == null ? null : "still carrying: " + Hands.LastStatusMessage;
            });
        }

        // --- 3b. The hover card at the pad's cursor (D5.2) ---------------------------------------

        private void PlanHover()
        {
            Do("stand a cell east of the Scavenger", () =>
            {
                Hands.Teleport(new CoreVector3(_golem.Cell.x + 1, _golem.Cell.y, 0f));
                return null;
            });
            Wait(0.2);
            Do("tap the left stick west, to face it", () => { Axis(JoyAxis.LeftX, -1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftX, 0f); return null; });
            Wait(0.4);
            Do("facing it: its card shows, hung from the cell ahead, with no tile drawn", () =>
            {
                if (Cursor.PadCell != _golem.Cell)
                {
                    return $"the cursor is at {Cursor.PadCell}, the Scavenger at {_golem.Cell} (player {PlayerCell()}, facing {Player.Facing})";
                }
                if (Cursor.PadAimed || Cursor.ReticleVisible)
                {
                    return "the look-around tile shows without the right stick";
                }
                string card = CardAt(_golem.Cell);
                if (card != null)
                {
                    return card;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "gamepad-hover-ahead");
                return null;
            });
            Do("tap the left stick east, to turn away", () => { Axis(JoyAxis.LeftX, 1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftX, 0f); return null; });
            Wait(0.4);
            Do("turned away: no card", () =>
                Hover.Hovered == null && !Hover.CardVisible ? null : $"still hovering {Hover.Hovered?.GolemId}, card shown {Hover.CardVisible}");
            Do("right stick east, onto open floor", () => { Axis(JoyAxis.RightX, 1f); return null; });
            Do("let go", () => { Axis(JoyAxis.RightX, 0f); return null; });
            Do("the tile marks the cell the pad looks at", () =>
            {
                CoreVector2Int expected = PlayerCell() + new CoreVector2Int(2, 0);
                if (!Cursor.PadAimed || !Cursor.ReticleVisible || Cursor.PadCell != expected)
                {
                    return $"aimed {Cursor.PadAimed}, tile shown {Cursor.ReticleVisible}, at {Cursor.PadCell} (expected {expected})";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "gamepad-hover-floor");
                return null;
            });
            Until("the right stick looks back at the Scavenger", 6, () =>
            {
                Axis(JoyAxis.RightX, 0f);
                Axis(JoyAxis.RightY, 0f);
                if (Cursor.PadCell == _golem.Cell)
                {
                    return null;
                }
                CoreVector2Int d = _golem.Cell - Cursor.PadCell;
                if (d.x != 0)
                {
                    Axis(JoyAxis.RightX, System.Math.Sign(d.x));
                }
                else
                {
                    Axis(JoyAxis.RightY, -System.Math.Sign(d.y)); // a stick's up is -y
                }
                return $"cursor at {Cursor.PadCell}";
            });
            Do("let go of the right stick", () =>
            {
                Axis(JoyAxis.RightX, 0f);
                Axis(JoyAxis.RightY, 0f);
                return null;
            });
            Wait(0.4);
            Do("looked at: the card shows, and the tile marks where the pad looks", () =>
            {
                if (!Cursor.PadAimed || !Cursor.ReticleVisible)
                {
                    return $"aimed {Cursor.PadAimed}, tile shown {Cursor.ReticleVisible}";
                }
                string card = CardAt(_golem.Cell);
                if (card != null)
                {
                    return card;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "gamepad-hover-look");
                return null;
            });
            Do("Y opens the pack over it", () => { Button(JoyButton.Y, true); return null; });
            Do("Y up", () => { Button(JoyButton.Y, false); return null; });
            Do("a screen open: no card, no tile", () =>
            {
                if (!Pack.IsOpen)
                {
                    return "Y didn't open the pack";
                }
                return Hover.CardVisible || Cursor.ReticleVisible ? $"card {Hover.CardVisible}, tile {Cursor.ReticleVisible} over the pack" : null;
            });
            Do("B closes it", () => { Button(JoyButton.B, true); return null; });
            Do("B up", () => { Button(JoyButton.B, false); return Pack.IsOpen ? "B didn't close the pack" : null; });
            Do("tap the left stick south", () => { Axis(JoyAxis.LeftY, 1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftY, 0f); return null; });
            Do("walking brings the cursor back to the cell ahead", () =>
            {
                if (Cursor.PadAimed || Cursor.ReticleVisible)
                {
                    return $"aimed {Cursor.PadAimed}, tile shown {Cursor.ReticleVisible} after a walk";
                }
                CoreVector2Int ahead = PlayerCell() + new CoreVector2Int(0, -1);
                if (Cursor.PadCell != ahead)
                {
                    return $"the cursor is at {Cursor.PadCell}, the cell ahead {ahead}";
                }
                Log("the card followed the pad: the cell ahead, the right stick's look, hidden under a screen");
                return null;
            });
        }

        /// <summary>Null if the Scavenger's card shows, hung from <paramref name="cell"/>'s top-right corner; else why not.</summary>
        private string CardAt(CoreVector2Int cell)
        {
            if (Hover.Hovered != _golem || !Hover.CardVisible)
            {
                return $"hovering {Hover.Hovered?.GolemId ?? "nothing"}, card shown {Hover.CardVisible}";
            }
            float half = GridConversions.CellPixels / 2f;
            Vector2 corner = Runner.GetViewport().GetCanvasTransform() * (GridConversions.CellToWorld(cell) + new Vector2(half, -half));
            Vector2 expected = corner + new Vector2(16f, 16f);
            return Hover.CardPosition.DistanceTo(expected) <= 2f ? null : $"the card is at {Hover.CardPosition}, the cell's corner at {corner}";
        }

        // --- 4. Y, Start, B ---------------------------------------------------------------------

        private void PlanScreens()
        {
            Do("stand in the open; press Y", () =>
            {
                Hands.Teleport(BuildSpot);
                Button(JoyButton.Y, true);
                return null;
            });
            Do("the pack is open", () =>
            {
                Button(JoyButton.Y, false);
                return Pack.IsOpen ? null : "Y didn't open the pack";
            });
            Do("press B", () => { Button(JoyButton.B, true); return null; });
            Do("the pack is closed", () =>
            {
                Button(JoyButton.B, false);
                return Pack.IsOpen ? "B didn't close the pack" : null;
            });
            Wait(0.1);
            Do("press Start", () => { Button(JoyButton.Start, true); return null; });
            Do("the menu is open", () =>
            {
                Button(JoyButton.Start, false);
                return Menu.IsOpen ? null : "Start didn't open the menu";
            });
            Do("press B", () => { Button(JoyButton.B, true); return null; });
            Do("the menu is closed", () =>
            {
                Button(JoyButton.B, false);
                Log("Y opened the pack and Start the menu; B closed each");
                return Menu.IsOpen ? "B didn't close the menu" : null;
            });
        }

        // --- 5. LB, the right stick, RT, RB, B --------------------------------------------------

        private void PlanBuild()
        {
            Do("Scrap for a run of belts", () =>
            {
                foreach (RecipeIngredient c in BeltPrefab.Cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity * 8);
                }
                Hands.Teleport(BuildSpot);
                _beltsBefore = BeltsAlive;
                return null;
            });
            Wait(0.2);
            int index = Sandbox.Placeables.ToList().FindIndex(p => p.Key == BeltKey);
            for (int i = 0; i <= index; i++)
            {
                Do($"LB ({i + 1})", () => { Button(JoyButton.LeftShoulder, true); return null; });
                Do($"LB up ({i + 1})", () => { Button(JoyButton.LeftShoulder, false); return null; });
            }
            Do("the belt is in hand, its card showing, the cursor a cell ahead", () =>
            {
                if (Build.ActivePrefab != BeltPrefab)
                {
                    return "holding " + (Build.ActivePrefab == null ? "nothing" : Sandbox.Placeables.First(p => p.Prefab == Build.ActivePrefab).Key);
                }
                if (!BuildMenu.HoverCard.Visible || !BuildMenu.HoverCardName.StartsWith(BuildMenuLabels.NameFor(BeltKey)))
                {
                    return $"card '{BuildMenu.HoverCardName}', shown {BuildMenu.HoverCard.Visible}";
                }
                if (!Cursor.UsingPad)
                {
                    return "the cursor follows the mouse";
                }
                if (InputDeviceNode.AppliedMode != Input.MouseModeEnum.Hidden)
                {
                    return "the pointer still shows with the pad in use";
                }
                CoreVector2Int player = PlayerCell();
                if (Distance2(Cursor.PadCell, player) != 1)
                {
                    return $"cursor at {Cursor.PadCell}, the player at {player}";
                }
                return Cursor.GhostVisible && Cursor.GhostCell == Cursor.PadCell ? null : $"ghost at {Cursor.GhostCell}, shown {Cursor.GhostVisible}";
            });
            Do("right stick right", () =>
            {
                _first = Cursor.PadCell;
                Axis(JoyAxis.RightX, 1f);
                return null;
            });
            Do("one step at once, then let go", () =>
            {
                Axis(JoyAxis.RightX, 0f);
                return Cursor.PadCell == _first + new CoreVector2Int(1, 0) ? null : $"cursor at {Cursor.PadCell}, from {_first}";
            });
            Do("right stick down, held", () =>
            {
                _first = Cursor.PadCell;
                Axis(JoyAxis.RightY, 1f);
                return null;
            });
            Wait(0.6);
            Do("it repeats while held, then let go", () =>
            {
                Axis(JoyAxis.RightY, 0f);
                int moved = _first.y - Cursor.PadCell.y;
                return moved >= 3 && Cursor.PadCell.x == _first.x ? null : $"moved {moved} south (cursor {Cursor.PadCell} from {_first})";
            });
            Do("the cell is clear: RT", () =>
            {
                _first = Cursor.PadCell;
                if (Build.GhostStateFor(_first) != BuildGhostState.Valid)
                {
                    return $"the ghost at {_first} reads {Build.GhostStateFor(_first)}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "gamepad-cursor");
                Axis(JoyAxis.TriggerRight, 1f);
                return null;
            });
            Do("let go of RT: one belt placed under the cursor", () =>
            {
                Axis(JoyAxis.TriggerRight, 0f);
                if (BeltsAlive != _beltsBefore + 1)
                {
                    return $"{BeltsAlive - _beltsBefore} belts placed";
                }
                return Build.Buildings.Any(b => !b.IsRemoved && b.Cell == _first) ? null : "nothing at " + _first;
            });
            Do("the drag ended with RT", () => Build.IsDragging ? "RT up left the drag going" : null);
            Do("RB turns the next one", () =>
            {
                _facingBefore = Build.PlacementFacing;
                Button(JoyButton.RightShoulder, true);
                return null;
            });
            Do("turned", () =>
            {
                Button(JoyButton.RightShoulder, false);
                return Build.PlacementFacing != _facingBefore ? null : "RB didn't turn it";
            });
            Do("the cursor a cell west of the first belt; hold RT", () =>
            {
                Axis(JoyAxis.RightX, -1f);
                return null;
            });
            Do("let go of the stick; hold RT there", () =>
            {
                Axis(JoyAxis.RightX, 0f);
                _first = Cursor.PadCell;
                _beltsBefore = BeltsAlive;
                Axis(JoyAxis.TriggerRight, 1f);
                return null;
            });
            Do("RT held, the stick west", () => { Axis(JoyAxis.RightX, -1f); return null; });
            Wait(0.5);
            Do("let go of the stick, then of RT", () =>
            {
                Axis(JoyAxis.RightX, 0f);
                return null;
            });
            Do("RT up: a run of belts laid", () =>
            {
                Axis(JoyAxis.TriggerRight, 0f);
                int laid = BeltsAlive - _beltsBefore;
                if (laid < 3)
                {
                    return $"the drag laid {laid} belts (from {_first} to {Cursor.PadCell})";
                }
                Log($"LB to the belt, RT placed one, RB turned it, RT held over the stick laid {laid}");
                return null;
            });
            Do("the run's drag ended with RT", () => Build.IsDragging ? "RT up left the drag going" : null);
            Do("press B", () => { Button(JoyButton.B, true); return null; });
            Do("the tool is put down", () =>
            {
                Button(JoyButton.B, false);
                return Build.IsBuildToolActive ? "B didn't put the belt down" : null;
            });
        }

        // --- 6. LT and RT -----------------------------------------------------------------------

        private void PlanDemolish()
        {
            Do("LT", () => { Axis(JoyAxis.TriggerLeft, 1f); return null; });
            Do("the wrecking bar in hand", () =>
            {
                Axis(JoyAxis.TriggerLeft, 0f);
                return Build.IsDemolishActive ? null : "LT didn't take the wrecking bar";
            });
            Do("the cursor over the run's last belt", () =>
            {
                // The bar starts a cell ahead too; step it onto a belt the drag laid.
                _beltsBefore = BeltsAlive;
                return null;
            });
            Until("the stick steps it onto a belt", 4, () =>
            {
                Axis(JoyAxis.RightX, 0f);
                if (Build.Buildings.Any(b => !b.IsRemoved && b.PrefabKey == BeltKey && b.Cell == Cursor.PadCell))
                {
                    return null;
                }
                CoreVector2Int target = Build.Buildings.Where(b => !b.IsRemoved && b.PrefabKey == BeltKey).Select(b => b.Cell)
                    .OrderBy(c => Distance2(c, Cursor.PadCell)).First();
                CoreVector2Int d = target - Cursor.PadCell;
                if (d.x != 0)
                {
                    Axis(JoyAxis.RightX, System.Math.Sign(d.x));
                }
                else
                {
                    Axis(JoyAxis.RightY, -System.Math.Sign(d.y)); // a stick's up is -y
                }
                return "stepping toward " + target;
            });
            Do("let go; RT", () =>
            {
                Axis(JoyAxis.RightX, 0f);
                Axis(JoyAxis.RightY, 0f);
                Axis(JoyAxis.TriggerRight, 1f);
                return null;
            });
            Do("RT up: the belt is taken back", () =>
            {
                Axis(JoyAxis.TriggerRight, 0f);
                return BeltsAlive == _beltsBefore - 1 ? null : $"{_beltsBefore - BeltsAlive} belts taken";
            });
            Do("LT again", () => { Axis(JoyAxis.TriggerLeft, 1f); return null; });
            Do("the bar put down", () =>
            {
                Axis(JoyAxis.TriggerLeft, 0f);
                Log("LT took the wrecking bar, RT took a belt back, LT put it down");
                return Build.IsBuildToolActive ? "LT didn't put the bar down" : null;
            });
        }

        // --- 7. The d-pad and Back --------------------------------------------------------------

        private void PlanCameraAndClock()
        {
            Do("d-pad up", () =>
            {
                _sizeBefore = Camera.OrthographicSize;
                Button(JoyButton.DpadUp, true);
                return null;
            });
            Do("d-pad up let go", () => { Button(JoyButton.DpadUp, false); return null; });
            Until("zoomed in", 3, () => Camera.OrthographicSize < _sizeBefore - 0.5f ? null : $"size {Camera.OrthographicSize:0.0} from {_sizeBefore:0.0}");
            Do("d-pad down", () =>
            {
                _sizeBefore = Camera.OrthographicSize;
                Button(JoyButton.DpadDown, true);
                return null;
            });
            Do("d-pad down let go", () => { Button(JoyButton.DpadDown, false); return null; });
            Until("zoomed out", 3, () => Camera.OrthographicSize > _sizeBefore + 0.5f ? null : $"size {Camera.OrthographicSize:0.0} from {_sizeBefore:0.0}");
            Do("Back", () => { Button(JoyButton.Back, true); return null; });
            Do("paused", () =>
            {
                Button(JoyButton.Back, false);
                return Sandbox.Clock.State == ClockState.Paused ? null : "Back didn't pause: " + Sandbox.Clock.State;
            });
            Do("Back again", () => { Button(JoyButton.Back, true); return null; });
            Do("playing", () =>
            {
                Button(JoyButton.Back, false);
                Log("d-pad up and down zoomed; Back paused and played");
                return Sandbox.Clock.State == ClockState.Paused ? "Back didn't play" : null;
            });
        }

        // --- 7b. The names are the pad's (D7) ---------------------------------------------------

        private string ScrapStep => Sandbox.Tutorial.Steps.First(s => s.Id == "scrap").Body;

        private void PlanNames()
        {
            Do("at the Scrap cart", () =>
            {
                CoreVector2Int scrap = World.Setup.HomeSpot(ItemType.Scrap).Value;
                Hands.Teleport(new CoreVector3(scrap.x, scrap.y + 1f, 0f));
                return null;
            });
            Wait(0.3);
            Do("the prompt, the guide and the build menu name the pad's buttons", () =>
            {
                if (!Settings.KeyNames.Pad)
                {
                    return "the names aren't the pad's with the pad in use";
                }
                if (!Hands.CurrentPrompt.StartsWith("[A]"))
                {
                    return $"the prompt reads '{Hands.CurrentPrompt}'";
                }
                if (!ScrapStep.Contains("Walk with the left stick") || !ScrapStep.Contains("Press A to take Scrap"))
                {
                    return $"the guide reads '{ScrapStep}'";
                }
                Button belt = BuildMenu.Rows.First(r => r.prefab == BeltPrefab).row;
                if (BuildMenu.KeyShownOn(belt) != "" || BuildMenu.KeyShownOn(BuildMenu.DemolishRow) != Settings.KeyNames.PadL2)
                {
                    return $"the build menu shows '{BuildMenu.KeyShownOn(belt)}' on Belt and '{BuildMenu.KeyShownOn(BuildMenu.DemolishRow)}' on Demolish";
                }
                return null;
            });
            Do("L3", () => { Button(JoyButton.LeftStick, true); return null; });
            Do("L3 up: the guide is back", () =>
            {
                Button(JoyButton.LeftStick, false);
                return Sandbox.Tutorial.IsShowing ? null : "L3 didn't bring the guide back";
            });
            Wait(0.3);
            Do("the plate names L3 and R3, its body the pad's words, and no shortcut digit", () =>
            {
                var panel = Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<TutorialPanel>().First();
                if (!panel.CounterText.Contains("L3 hides") || !panel.CounterText.Contains("R3 skips"))
                {
                    return $"the counter reads '{panel.CounterText}'";
                }
                if (!panel.BodyText.Contains("the left stick") || panel.BodyText.Contains("Shortcut"))
                {
                    return $"the plate reads '{panel.BodyText}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "gamepad-names");
                return null;
            });
            Do("L3 again", () => { Button(JoyButton.LeftStick, true); return null; });
            Do("L3 up: the guide is put away", () =>
            {
                Button(JoyButton.LeftStick, false);
                if (Sandbox.Tutorial.IsShowing)
                {
                    return "L3 didn't put the guide away";
                }
                Log("the prompt read [A], the guide 'the left stick' and 'Press A', the build menu L2 on Demolish; L3 brought the guide back and put it away");
                return null;
            });
        }

        // --- 7c. The window loses focus: the frame cap (D8) -------------------------------------

        private int _maxFpsBefore;
        private long _tickBefore;

        private void PlanBackgroundCap()
        {
            Do("the window loses focus (the Steam overlay over it)", () =>
            {
                BackgroundFrameCap.EvenUnderScenario = true;
                _maxFpsBefore = Engine.MaxFps;
                _tickBefore = Sandbox.Clock.CurrentTick;
                Tree.Root.PropagateNotification((int)Node.NotificationApplicationFocusOut);
                return null;
            });
            Do("drawn at the background rate", () =>
                Engine.MaxFps == BackgroundFrameCap.BackgroundFps ? null : $"max fps {Engine.MaxFps}");
            Wait(0.5);
            Do("the factory ran on; focus back", () =>
            {
                if (Sandbox.Clock.CurrentTick <= _tickBefore)
                {
                    return "the clock stopped under the cap";
                }
                Tree.Root.PropagateNotification((int)Node.NotificationApplicationFocusIn);
                return null;
            });
            Do("the cap is off", () =>
            {
                BackgroundFrameCap.EvenUnderScenario = false;
                if (Engine.MaxFps != _maxFpsBefore)
                {
                    return $"max fps {Engine.MaxFps}, was {_maxFpsBefore}";
                }
                Log($"unfocused, the game drew at {BackgroundFrameCap.BackgroundFps} fps and the clock ran on; focused, the cap went back to {_maxFpsBefore}");
                return null;
            });
        }

        // --- 8. The mouse takes over again ------------------------------------------------------

        private void PlanBackToMouse()
        {
            Do("the belt picked up with LB", () => { Button(JoyButton.LeftShoulder, true); return null; });
            Do("LB up", () => { Button(JoyButton.LeftShoulder, false); return Build.IsBuildToolActive ? null : "nothing in hand"; });
            Do("nudge the mouse", () =>
            {
                Vector2 at = new Vector2(600f, 300f) * Tree.Root.ContentScaleFactor;
                Tree.Root.WarpMouse(at);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = new Vector2(20f, 0f) });
                return null;
            });
            Do("the pointer aims the tool again", () =>
            {
                if (InputDevices.PadInUse || Cursor.UsingPad)
                {
                    return "still on the pad";
                }
                if (InputDeviceNode.AppliedMode != Input.MouseModeEnum.Visible)
                {
                    return "the pointer is still hidden";
                }
                Button belt = BuildMenu.Rows.First(r => r.prefab == BeltPrefab).row;
                if (Settings.KeyNames.Pad || !ScrapStep.Contains("Walk with WASD") || BuildMenu.KeyShownOn(belt) == "")
                {
                    return $"the names stayed the pad's: '{ScrapStep}', Belt shows '{BuildMenu.KeyShownOn(belt)}'";
                }
                Build.CancelPlacement();
                Log("a mouse nudge handed the cursor back");
                return null;
            });
        }

        // --- Pad input --------------------------------------------------------------------------

        private static void Button(JoyButton button, bool down) =>
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = down, Device = 0 });

        private static void Axis(JoyAxis axis, float value) =>
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = 0 });

        private static void ReleaseAll()
        {
            foreach (JoyAxis axis in new[] { JoyAxis.LeftX, JoyAxis.LeftY, JoyAxis.RightX, JoyAxis.RightY, JoyAxis.TriggerLeft, JoyAxis.TriggerRight })
            {
                Axis(axis, 0f);
            }
        }

        private static int Distance2(CoreVector2Int a, CoreVector2Int b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

        private CoreVector2Int PlayerCell() =>
            new CoreVector2Int(Mathf.RoundToInt(Player.CorePosition.x), Mathf.RoundToInt(Player.CorePosition.y));
    }
}
