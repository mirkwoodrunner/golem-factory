using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using Stop = GolemFactory.Nodes.WorkbenchScreen.PadStopKind;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The screens by pad (docs/steam-deck-design.md D6), with joypad events only once the
    /// playtest card is answered. So far the Workbench (D6.W, the owner's answers):
    /// <list type="number">
    ///   <item>The Workbench opens on a new Scavenger with the ring on the hinted card.</item>
    ///   <item>A sends Always On, Extract and Push Output to the next socket each, the ring
    ///   following the hint to the lever; LB and the d-pad do nothing behind the screen.</item>
    ///   <item>The d-pad walks the ring to STEP 1; RT and LT change its batch size, and the dial
    ///   names them; X takes the card out and A sends it back.</item>
    ///   <item>With every step full, A moves nothing and the status says why.</item>
    ///   <item>A Haul in STEP 2: RB and LB change its good.</item>
    ///   <item>A on the lever engages the program; B closes the screen.</item>
    /// </list>
    /// </summary>
    public sealed class GamepadUiScenario : StepScenario
    {
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private WorkbenchSession Session => Sandbox.Workbench;
        private WorkbenchScreen Bench => Tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
        private CameraRigNode Camera => Tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>().First();

        private static readonly Compat.Vector3 OpenFloor = new Compat.Vector3(-5f, 8f, 0f);

        private GolemEntity _golem;
        private float _zoom;
        private string _good;
        private bool _dpadDown;
        private JoyButton _dpadHeld;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            InputDevices.Reset();
            Wait(0.5);
            PlanStart();
            PlanOpen();
            PlanSend();
            PlanDial();
            PlanFull();
            PlanHaul();
            PlanEngage();
            PlanVaultScroll();
        }

        // --- 7. A long vault scrolls to the ring ------------------------------------------------

        private void PlanVaultScroll()
        {
            Do("press A at the golem again", () => { Button(JoyButton.A, true); return null; });
            Do("let go of A", () => { Button(JoyButton.A, false); return null; });
            Wait(0.1);
            Do("open; every card offered, so the vault is longer than its window", () =>
            {
                if (!Bench.IsOpen)
                {
                    return "A didn't reopen the Workbench: " + Sandbox.Interactor.LastStatusMessage;
                }
                Session.ConfigureCardGating(null, null);
                return null;
            });
            Until("the vault overflows", 1, () =>
            {
                WorkbenchCardControl last = Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().LastOrDefault(c => !c.IsQueuedForDeletion());
                return last != null && last.GetGlobalRect().End.Y > Bench.VaultView.GetGlobalRect().End.Y + 1f ? null : "the whole vault fits its window";
            });
            NavigateTo("the vault", Stop.Vault, () => 0);
            NavigateTo("the last card", Stop.Vault, () => Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Count(c => !c.IsQueuedForDeletion()) - 1);
            Wait(0.1);
            Do("the last card is scrolled into the window, the ring around it", () =>
            {
                WorkbenchCardControl last = Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Last(c => !c.IsQueuedForDeletion());
                string name = last.CardRef.Appendage?.name ?? last.CardRef.LogicCore?.name;
                string ring = RingOn(name);
                if (ring != null)
                {
                    return ring;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-scroll");
                return null;
            });
            PressButton("B", JoyButton.B);
            Do("closed again", () =>
            {
                if (Bench.IsOpen)
                {
                    return "B didn't close it";
                }
                Log("the d-pad walked a long vault to its last card, scrolled into view");
                return null;
            });
        }

        protected override void Cleanup()
        {
            foreach (JoyAxis axis in new[] { JoyAxis.LeftX, JoyAxis.LeftY, JoyAxis.TriggerLeft, JoyAxis.TriggerRight })
            {
                Axis(axis, 0f);
            }
            InputDevices.Reset();
        }

        // --- 0. A fresh game, the playtest card answered (a click), the pad picked up ----------

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
            Do("the card down, the clock running", () =>
            {
                if ((Tree.Root.FindChild("Playtest", true, false) as PlaytestNode)?.Card.Visible == true)
                {
                    return "the card is still up";
                }
                Sandbox.Clock.Play();
                return null;
            });
            Do("nudge the left stick", () => { Axis(JoyAxis.LeftX, 0.6f); return null; });
            Do("let go: the pad is in use", () =>
            {
                Axis(JoyAxis.LeftX, 0f);
                return InputDevices.PadInUse ? null : "the stick didn't count as the pad";
            });
        }

        // --- 1. The Workbench opens with the ring on the hinted card ----------------------------

        private void PlanOpen()
        {
            Do("build a Scavenger at the station", () =>
            {
                ChassisDefinition chassis = Sandbox.Definitions.Chassis["ClockworkScavenger"];
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
                }
                if (!Sandbox.StarterStation.TryConstructGolem(chassis, out _golem))
                {
                    return "no golem built";
                }
                (Sandbox.WorkbenchScreen as WorkbenchScreen)?.Close();
                return null;
            });
            // It stands on the station, where A builds: X carries it off to open floor first.
            Do("stand south of it", () =>
            {
                Sandbox.Interactor.Teleport(new Compat.Vector3(_golem.Cell.x, _golem.Cell.y - 1, 0f));
                return null;
            });
            Wait(0.2);
            PressButton("X picks it up", JoyButton.X);
            Do("carrying it; walk to open floor", () =>
            {
                if (Sandbox.Interactor.CarriedGolem != _golem)
                {
                    return "X picked nothing up: " + Sandbox.Interactor.LastStatusMessage;
                }
                Sandbox.Interactor.Teleport(OpenFloor);
                return null;
            });
            Wait(0.2);
            PressButton("X sets it down", JoyButton.X);
            Do("set down; step south of it", () =>
            {
                if (Sandbox.Interactor.CarriedGolem != null)
                {
                    return "still carrying: " + Sandbox.Interactor.LastStatusMessage;
                }
                Sandbox.Interactor.Teleport(new Compat.Vector3(_golem.Cell.x, _golem.Cell.y - 1, 0f));
                return null;
            });
            Wait(0.2);
            Do("tap the left stick up, to face it", () => { Axis(JoyAxis.LeftY, -1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftY, 0f); return null; });
            Wait(0.2);
            Do("press A at the golem", () =>
            {
                _zoom = Camera.OrthographicSize;
                Button(JoyButton.A, true);
                return null;
            });
            Do("let go of A", () => { Button(JoyButton.A, false); return null; });
            Wait(0.1);
            Do("the Workbench is open on it, nothing sent by the A that opened it, the ring on Always On, the card the guide lights", () =>
            {
                if (!Bench.IsOpen || Bench.TargetGolem != _golem)
                {
                    return $"open {Bench.IsOpen} on {Bench.TargetGolem?.GolemId}: {Sandbox.Interactor.LastStatusMessage}";
                }
                if (Session.DraftLogicCore != null || Session.AssignedAppendageCount != 0)
                {
                    return "the A that opened the Workbench also sent a card";
                }
                string ring = RingOn("AlwaysOnCore");
                if (ring != null)
                {
                    return ring;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-open");
                return null;
            });
        }

        // --- 2. A sends each card; the ring follows the hint ------------------------------------

        private void PlanSend()
        {
            PressButton("A on Always On", JoyButton.A);
            Do("Always On in TRIGGER, landing; the ring on Extract", () =>
            {
                if (Session.DraftLogicCore?.name != "AlwaysOnCore")
                {
                    return "the trigger holds " + (Session.DraftLogicCore?.name ?? "nothing");
                }
                return Bench.CardLanding ? RingOn("ExtractScrap") : "no landing";
            });
            PressButton("A on Extract", JoyButton.A);
            Do("Extract in STEP 1; the ring on Push Output", () => Draft(0) != "ExtractScrap" ? $"STEP 1 holds {Draft(0)}" : RingOn("PushOutput"));
            PressButton("A on Push Output", JoyButton.A);
            Do("Push Output in STEP 2; the ring on the lever; the golem untouched", () =>
            {
                if (Draft(1) != "PushOutput")
                {
                    return $"STEP 2 holds {Draft(1)}";
                }
                if (_golem.Program.appendages.Count != 0)
                {
                    return "a send wrote to the golem before the lever";
                }
                if (Bench.PadFocusKind != Stop.Lever || !Encloses(Bench.PadRingRect, Bench.Lever))
                {
                    return $"the ring is on {Bench.PadFocusKind} {Bench.PadFocusIndex}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-lever");
                return null;
            });
            PressButton("LB behind the screen", JoyButton.LeftShoulder);
            PressButton("d-pad up behind the screen", JoyButton.DpadUp);
            Do("the build menu and the camera didn't hear them", () =>
            {
                if (Sandbox.Build.IsBuildToolActive)
                {
                    return "LB picked up a build tool behind the Workbench";
                }
                return Mathf.Abs(Camera.OrthographicSize - _zoom) > 0.01f ? "the d-pad zoomed the camera behind the Workbench" : null;
            });
            Log("A sent Always On, Extract and Push Output, the ring following the hint to the lever");
        }

        // --- 3. The d-pad to STEP 1; the triggers on its dial; X and A --------------------------

        private void PlanDial()
        {
            NavigateTo("STEP 1", Stop.Step, () => 0);
            PressAxis("RT", JoyAxis.TriggerRight);
            Do("the batch size rose, and the dial names the triggers", () =>
            {
                if (Session.DraftQuantityAt(0) != 2)
                {
                    return $"batch {Session.DraftQuantityAt(0)}";
                }
                string minus = Glyph(0, "Decrease"), plus = Glyph(0, "Increase");
                if (minus != "LT" || plus != "RT")
                {
                    return $"the dial reads '{minus}' and '{plus}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-dial");
                return null;
            });
            PressAxis("LT", JoyAxis.TriggerLeft);
            Do("back to one", () => Session.DraftQuantityAt(0) == 1 ? null : $"batch {Session.DraftQuantityAt(0)}");
            PressButton("X on STEP 1", JoyButton.X);
            Do("STEP 1 empty, STEP 2 kept; the ring on Extract again", () =>
            {
                if (Draft(0) != null || Draft(1) != "PushOutput")
                {
                    return $"steps {Draft(0)}, {Draft(1)}";
                }
                return RingOn("ExtractScrap");
            });
            PressButton("A on Extract", JoyButton.A);
            Do("Extract back in STEP 1, the first empty step", () => Draft(0) == "ExtractScrap" ? null : $"STEP 1 holds {Draft(0)}");
            Log("the d-pad reached STEP 1; RT and LT stepped its dial; X took it out and A sent it back");
        }

        // --- 4. Every step full -----------------------------------------------------------------

        private void PlanFull()
        {
            NavigateTo("Haul Scrap in the vault", Stop.Vault, () => VaultIndex("HaulScrap"));
            PressButton("A on Haul Scrap", JoyButton.A);
            Do("nothing moved, and the status says why", () =>
            {
                if (Draft(0) != "ExtractScrap" || Draft(1) != "PushOutput")
                {
                    return $"steps {Draft(0)}, {Draft(1)}";
                }
                return Bench.StatusText == WorkbenchSession.StepsFullMessage ? null : $"status '{Bench.StatusText}'";
            });
        }

        // --- 5. A Haul's good by the shoulders --------------------------------------------------

        private void PlanHaul()
        {
            Do("the factory has made Coke, so a Haul can take it", () =>
            {
                Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, ItemType.Coke, 5);
                Sandbox.TechTree.Poll(); // the ledger sweeps once a second; don't depend on where the steps fall
                return null;
            });
            NavigateTo("STEP 2", Stop.Step, () => 1);
            PressButton("X on STEP 2", JoyButton.X);
            NavigateTo("Haul Scrap", Stop.Vault, () => VaultIndex("HaulScrap"));
            PressButton("A on Haul Scrap", JoyButton.A);
            Do("Haul in STEP 2", () => Draft(1) == "HaulScrap" ? null : $"STEP 2 holds {Draft(1)}");
            NavigateTo("STEP 2", Stop.Step, () => 1);
            Do("the picker names the shoulders", () =>
            {
                _good = Session.DraftItemTypeAt(1);
                string left = Glyph(1, "PreviousGood"), right = Glyph(1, "NextGood");
                return left == "LB" && right == "RB" ? null : $"the picker reads '{left}' and '{right}'";
            });
            PressButton("RB", JoyButton.RightShoulder);
            Do("the good changed", () =>
            {
                if (Session.DraftItemTypeAt(1) == _good)
                {
                    return $"still {_good}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-haul");
                return null;
            });
            PressButton("LB", JoyButton.LeftShoulder);
            Do("and back", () => Session.DraftItemTypeAt(1) == _good ? null : $"{Session.DraftItemTypeAt(1)}, not {_good}");
            PressButton("X on the Haul", JoyButton.X);
            Do("the ring follows the hint to Push Output", () => Draft(1) != null ? $"STEP 2 holds {Draft(1)}" : RingOn("PushOutput"));
            PressButton("A on Push Output", JoyButton.A);
            Do("Push Output back in STEP 2", () => Draft(1) == "PushOutput" ? null : $"STEP 2 holds {Draft(1)}");
            Log("RB and LB stepped a Haul's good");
        }

        // --- 6. The lever and B -----------------------------------------------------------------

        private void PlanEngage()
        {
            Do("the ring on the lever", () => Bench.PadFocusKind == Stop.Lever ? null : $"the ring is on {Bench.PadFocusKind} {Bench.PadFocusIndex}");
            PressButton("A on the lever", JoyButton.A);
            Do("the program is the golem's", () =>
            {
                List<string> steps = _golem.Program.appendages.Select(a => a.name).ToList();
                return _golem.Program.logicCore?.name == "AlwaysOnCore" && steps.SequenceEqual(new[] { "ExtractScrap", "PushOutput" })
                    ? null : $"the golem runs {_golem.Program.logicCore?.name}: {string.Join(", ", steps)}";
            });
            PressButton("B", JoyButton.B);
            Do("the Workbench is closed", () =>
            {
                if (Bench.IsOpen)
                {
                    return "B didn't close it";
                }
                Log("A on the lever engaged it; B closed the Workbench");
                return null;
            });
        }

        // --- Helpers ----------------------------------------------------------------------------

        private string Draft(int slot) => Session.DraftAppendageAt(slot)?.name;

        private int VaultIndex(string card) =>
            Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Where(c => !c.IsQueuedForDeletion())
                .Select((c, i) => (c, i)).FirstOrDefault(p => p.c.CardRef.Appendage?.name == card || p.c.CardRef.LogicCore?.name == card).i;

        private WorkbenchCardControl VaultCard(string card) =>
            Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().FirstOrDefault(c => !c.IsQueuedForDeletion()
                && (c.CardRef.Appendage?.name == card || c.CardRef.LogicCore?.name == card));

        /// <summary>Null if the ring rests on <paramref name="card"/> in the vault, drawn around it, in the vault's window.</summary>
        private string RingOn(string card)
        {
            WorkbenchCardControl control = VaultCard(card);
            if (control == null)
            {
                return card + " isn't in the vault";
            }
            if (!Bench.PadRingVisible || Bench.PadFocusControl != control)
            {
                return $"the ring ({Bench.PadRingVisible}) is on {Bench.PadFocusKind} {Bench.PadFocusIndex}, not {card}";
            }
            if (!Encloses(Bench.PadRingRect, control))
            {
                return $"the ring {Bench.PadRingRect} doesn't surround {card} {control.GetGlobalRect()}";
            }
            Rect2 view = Bench.VaultView.GetGlobalRect().Grow(1f);
            return view.Encloses(control.GetGlobalRect()) ? null : $"{card} is outside the vault's window";
        }

        private static bool Encloses(Rect2 ring, Control control) => ring.Encloses(control.GetGlobalRect());

        private string Glyph(int slot, string button) =>
            ((Bench.StepRows[slot].Card?.FindChild(button, true, false) as Control)?.FindChild("Label", false, false) as Label)?.Text;

        private void PressButton(string name, JoyButton button)
        {
            Do(name, () => { Button(button, true); return null; });
            Do(name + " up", () => { Button(button, false); return null; });
        }

        private void PressAxis(string name, JoyAxis axis)
        {
            Do(name, () => { Axis(axis, 1f); return null; });
            Do(name + " up", () => { Axis(axis, 0f); return null; });
        }

        /// <summary>The d-pad, a press and a release at a time, toward the stop until the ring is on it.</summary>
        private void NavigateTo(string name, Stop kind, System.Func<int> index)
        {
            Until("the d-pad walks the ring to " + name, 6, () =>
            {
                if (_dpadDown)
                {
                    Button(_dpadHeld, false);
                    _dpadDown = false;
                    return "letting go";
                }
                if (Bench.PadFocusKind == kind && Bench.PadFocusIndex == index())
                {
                    return null;
                }
                Control target = kind switch
                {
                    Stop.Step => Bench.StepRows[index()].Socket,
                    Stop.Vault => Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Where(c => !c.IsQueuedForDeletion()).ElementAt(index()),
                    Stop.Lever => Bench.Lever,
                    _ => null,
                };
                Vector2 d = target.GetGlobalRect().GetCenter() - Bench.PadRingRect.GetCenter();
                bool inVault = Bench.PadFocusKind == Stop.Vault && kind == Stop.Vault;
                _dpadHeld = inVault
                    ? (index() > Bench.PadFocusIndex ? JoyButton.DpadDown : JoyButton.DpadUp)
                    : Mathf.Abs(d.X) > Mathf.Abs(d.Y) ? (d.X > 0 ? JoyButton.DpadRight : JoyButton.DpadLeft) : (d.Y > 0 ? JoyButton.DpadDown : JoyButton.DpadUp);
                Button(_dpadHeld, true);
                _dpadDown = true;
                return $"the ring on {Bench.PadFocusKind} {Bench.PadFocusIndex}";
            });
            Do("let go of the d-pad", () =>
            {
                if (_dpadDown)
                {
                    Button(_dpadHeld, false);
                    _dpadDown = false;
                }
                return null;
            });
        }

        private static void Button(JoyButton button, bool down) =>
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = down, Device = 0 });

        private static void Axis(JoyAxis axis, float value) =>
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = 0 });
    }
}
