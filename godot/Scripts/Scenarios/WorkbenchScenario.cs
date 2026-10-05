using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// G7's exit check: program a golem end to end through the Workbench, with real mouse and
    /// key input -- and the nine WorkbenchControllerTests that were about UGUI plumbing, asked of
    /// the Godot screen instead (docs/godot-test-ledger.md).
    ///
    /// <list type="number">
    ///   <item>A Scavenger is built at the station; the Workbench opens on it (Unity's order) and
    ///   the construction panel is closed (Open_ClosesManagementPanelAndConstructionPanel).</item>
    ///   <item>Picking up a vault appendage lights the sockets that would take it.</item>
    ///   <item>Real drags: AlwaysOnCore onto TRIGGER, ExtractScrap onto STEP 1, PushOutput onto
    ///   STEP 2 (RealDrag_ReleasedOverAnAppendageSocket_CommitsThroughTheNormalPath).</item>
    ///   <item>The STEP 1 dial's + raises the batch size; vault cards carry no dial
    ///   (VaultCards_HaveNoStepper).</item>
    ///   <item>A vault card released over nothing changes nothing and leaves no orphan card or
    ///   ghost behind, twice over (FailedDrag_*, RepeatedFailedDrags_*).</item>
    ///   <item>A socketed card released over nothing leaves its socket
    ///   (RealDrag_SlotCardReleasedOverNothing_*), and is put back.</item>
    ///   <item>The golem's program is untouched until ENGAGE GEARS is clicked; then it is the
    ///   draft, and the lever throws.</item>
    ///   <item>PATENT stamps BP-001.</item>
    ///   <item>CLOSE hides the screen (Open_ActivatesCanvasRoot_Close_Deactivates); [E] at the
    ///   golem opens it again, re-read from the committed program, with no orphans.</item>
    ///   <item>With no target the lever goes dead (EngageButton_GoesNonInteractable...).</item>
    /// </list>
    /// </summary>
    public sealed class WorkbenchScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private WorkbenchScreen _screen;
        private ConstructionPanelNode _panel;
        private PlayerNode _player;
        private GolemEntity _golem;
        private int _wait;
        private Key? _heldTap;
        private int _vaultCardCount;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _screen = _tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
            _panel = _tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
            _player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            if (_screen == null || _panel == null || _player == null)
            {
                Do("scene", () => "Sandbox.tscn is missing the Workbench, the construction panel or the player");
                return;
            }

            DefinitionsCheck();
            PlanBuildAndOpen();
            PlanDrags();
            PlanFailedDrags();
            PlanEngageAndPatent();
            PlanCloseAndReopen();
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

        // --- Plan ---------------------------------------------------------------------------

        private void DefinitionsCheck()
        {
            Do("the Workbench is closed at start", () => _screen.IsOpen ? "open before anyone asked" : null);
        }

        private void PlanBuildAndOpen()
        {
            ChassisDefinition scavenger = _world.Definitions.Chassis["ClockworkScavenger"];
            Do("build a Scavenger at the station", () =>
            {
                foreach (RecipeIngredient c in scavenger.cost)
                {
                    _world.Buffers.Deposit(_world.StockpileBufferId, c.itemType, c.quantity);
                }
                _panel.Open(_world.StarterStation);
                return null;
            });
            // A separate step: controls are laid out on the frame after they are built, and a
            // click aimed at a row that has no size yet lands somewhere else.
            Do("click the Scavenger's row", () => { Click(_panel.Rows.Single(r => r.chassis == scavenger).row); return null; });
            Do("the Workbench opened on it, and the panel closed", () =>
            {
                _golem = _panel.LastBuilt;
                if (_golem == null)
                {
                    return "nothing was built: " + _panel.Status;
                }
                if (!_screen.IsOpen || _panel.IsOpen)
                {
                    return $"workbench open={_screen.IsOpen}, panel open={_panel.IsOpen}";
                }
                if (_screen.TargetGolem != _golem || !_screen.TargetText.Contains(_golem.GolemId))
                {
                    return $"target header '{_screen.TargetText}'";
                }
                _vaultCardCount = VaultCards().Count;
                return _vaultCardCount == 26 ? null : $"vault shows {_vaultCardCount} cards, expected 2 cores + 24 appendages";
            });
            Do("vault cards carry no dial", () =>
                VaultCards().Any(c => c.FindChild("Quantity", true, false) != null) ? "a vault card offers a batch size" : null);
        }

        private void PlanDrags()
        {
            Do("press on ExtractScrap in the vault and lift it", () => { DragStart(VaultCard("ExtractScrap")); return null; });
            Do("hold it high: the sockets that would take it are lit", () =>
            {
                DragMoveTo(_screen.StepRows[0].Socket);
                return null;
            });
            Do("STEP 1 and 2 lit valid, TRIGGER and STEP 3 lit invalid", () =>
            {
                if (_world.Workbench.AppendageHighlights[0] != DropZoneHighlight.Valid ||
                    _world.Workbench.AppendageHighlights[1] != DropZoneHighlight.Valid ||
                    _world.Workbench.AppendageHighlights[2] != DropZoneHighlight.Invalid ||
                    _world.Workbench.LogicHighlight != DropZoneHighlight.Invalid)
                {
                    return $"highlights trigger={_world.Workbench.LogicHighlight} steps={string.Join(",", _world.Workbench.AppendageHighlights)}";
                }
                return null;
            });
            Do("release over STEP 1", () => { DragRelease(_screen.StepRows[0].Socket); return null; });
            Do("ExtractScrap sits in STEP 1", () => Draft(0) == "ExtractScrap" ? null : $"STEP 1 holds {Draft(0)}");

            DragCard("PushOutput", () => _screen.StepRows[1].Socket);
            Do("PushOutput sits in STEP 2", () => Draft(1) == "PushOutput" ? null : $"STEP 2 holds {Draft(1)}");
            DragCard("AlwaysOnCore", () => _screen.TriggerRow.Socket);
            Do("AlwaysOnCore sits in TRIGGER", () => _world.Workbench.DraftLogicCore?.name == "AlwaysOnCore" ? null : "the trigger is empty");

            Do("click + on STEP 1's dial", () =>
            {
                Button plus = _screen.StepRows[0].Card?.FindChild("Increase", true, false) as Button;
                if (plus == null)
                {
                    return "STEP 1's Extract card has no dial";
                }
                Click(plus);
                return null;
            });
            Do("the batch size rose", () => _world.Workbench.DraftQuantityAt(0) == 2 ? null : $"quantity {_world.Workbench.DraftQuantityAt(0)}");
            Do("and nothing has touched the golem yet", () =>
                _golem.Program.appendages.Count == 0 && _golem.Program.logicCore == null ? null : "the draft leaked onto the golem before Engage");
            Do("drags", () => { _log.Add("real drags filled TRIGGER, STEP 1, STEP 2; dial +1; golem untouched until Engage"); return null; });
        }

        private void PlanFailedDrags()
        {
            for (int i = 0; i < 2; i++)
            {
                DragCard("HaulScrap", () => (Control)_screen.TargetLabelControl);
                Do("a vault card dropped on nothing changes nothing, and leaves nothing behind", () =>
                {
                    if (Draft(0) != "ExtractScrap" || Draft(1) != "PushOutput" || Draft(2) != null)
                    {
                        return "the draft changed";
                    }
                    int cards = VaultCards().Count;
                    int stray = StrayCards();
                    return cards == _vaultCardCount && stray == 0 ? null : $"vault {cards} cards (was {_vaultCardCount}), {stray} stray";
                });
            }

            DragCard(() => _screen.StepRows[1].Card, () => (Control)_screen.TargetLabelControl);
            Do("a socketed card dropped on nothing leaves its socket", () =>
                Draft(1) == null && StrayCards() == 0 ? null : $"STEP 2 still holds {Draft(1)}, {StrayCards()} stray");
            DragCard("PushOutput", () => _screen.StepRows[1].Socket);
            Do("put it back", () => Draft(1) == "PushOutput" ? null : "PushOutput did not go back");
            Do("failed", () => { _log.Add("failed drags: no orphans, no ghosts; a socket card dropped on nothing leaves"); return null; });
        }

        private void PlanEngageAndPatent()
        {
            Do("pull ENGAGE GEARS", () => { Click(_screen.Lever); return null; });
            Do("the golem runs the draft, and the lever threw", () =>
            {
                GolemProgram p = _golem.Program;
                if (p.logicCore?.name != "AlwaysOnCore" || p.appendages.Count != 2 ||
                    p.appendages[0].name != "ExtractScrap" || p.appendages[1].name != "PushOutput")
                {
                    return $"program core={p.logicCore?.name} steps={string.Join(",", p.appendages.Select(a => a.name))}";
                }
                if (p.GetQuantityAt(0) != 2)
                {
                    return $"batch size {p.GetQuantityAt(0)}, expected 2";
                }
                if (!_screen.StatusText.StartsWith("Gears engaged"))
                {
                    return $"status '{_screen.StatusText}'";
                }
                return null;
            });
            Do("PATENT", () => { Click(_screen.PatentButton); return null; });
            Do("BP-001 stamped", () =>
                _world.Patents.Blueprints.ContainsKey("BP-001") ? null : "no blueprint in the registry");
            Do("engage", () => { _log.Add("ENGAGE committed core + 2 steps (batch 2); PATENT stamped BP-001"); return null; });
        }

        private void PlanCloseAndReopen()
        {
            Do("click CLOSE", () => { Click(_screen.CloseButtonControl); return null; });
            Do("the screen is hidden", () => _screen.IsOpen || _screen.RootControl.Visible ? "still showing" : null);
            Do("walk to the golem and press E", () =>
            {
                Compat.Vector2Int cell = _golem.Cell;
                // From the NORTH: the golem stands on the station's door tile, so from the south
                // the station is the nearer thing and [E] (rightly) opens the station instead.
                _player.TeleportTo(new CoreVector3(cell.x, cell.y + 1f, 0f));
                return null;
            });
            Do("press E", () => { Tap(Key.E); return null; });
            Do("it reopens on the golem, re-read from its committed program, with no orphans", () =>
            {
                if (!_screen.IsOpen || _screen.TargetGolem != _golem)
                {
                    return "E at the golem did not open the Workbench on it";
                }
                if (Draft(0) != "ExtractScrap" || Draft(1) != "PushOutput" || _world.Workbench.DraftQuantityAt(0) != 2)
                {
                    return "the reopened draft is not the committed program";
                }
                return StrayCards() == 0 ? null : $"{StrayCards()} stray cards after close/open";
            });
            Do("with no target the lever goes dead", () =>
            {
                _world.Workbench.ConfigureGolem(null);
                return null;
            });
            Do("the lever is disabled", () => _screen.Lever.Disabled ? null : "a lever with nothing to program is still live");
            Do("reopen", () => { _log.Add("CLOSE hid it; E at the golem reopened it re-read; no target, dead lever"); return null; });
        }

        // --- Gestures -----------------------------------------------------------------------

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void DragCard(string vaultCard, Func<Control> target) => DragCard(() => VaultCard(vaultCard), target);

        /// <summary>A whole drag over several steps: press, lift past the threshold, travel, release.</summary>
        private void DragCard(Func<Control> source, Func<Control> target)
        {
            Do("pick up", () => { Control c = source(); if (c == null) return "no such card"; DragStart(c); return null; });
            Do("carry", () => { DragMoveTo(target()); return null; });
            Do("release", () => { DragRelease(target()); return null; });
        }

        private Vector2 _pointer;

        private void DragStart(Control card)
        {
            _pointer = card.GetGlobalRect().GetCenter();
            Mouse(new InputEventMouseMotion { Position = _pointer, GlobalPosition = _pointer });
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = _pointer, GlobalPosition = _pointer });
            // Past Godot's drag threshold, so the press becomes a drag.
            Vector2 lift = _pointer + new Vector2(24f, 0f);
            Mouse(new InputEventMouseMotion { Position = lift, GlobalPosition = lift, Relative = lift - _pointer, ButtonMask = MouseButtonMask.Left });
            _pointer = lift;
        }

        private void DragMoveTo(Control target)
        {
            Vector2 to = target.GetGlobalRect().GetCenter();
            Mouse(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - _pointer, ButtonMask = MouseButtonMask.Left });
            _pointer = to;
        }

        private void DragRelease(Control target)
        {
            Vector2 at = target.GetGlobalRect().GetCenter();
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }

        private void Click(Control control)
        {
            Vector2 at = control.GetGlobalRect().GetCenter();
            Mouse(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }

        private void Mouse(InputEventMouse e)
        {
            _tree.Root.WarpMouse(e.Position);
            Input.ParseInputEvent(e);
        }

        private void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }

        // --- Reading the screen --------------------------------------------------------------

        private List<WorkbenchCardControl> VaultCards() =>
            _screen.VaultList.GetChildren().OfType<WorkbenchCardControl>().Where(c => !c.IsQueuedForDeletion()).ToList();

        private WorkbenchCardControl VaultCard(string name) =>
            VaultCards().FirstOrDefault(c => (c.CardRef.Appendage?.name ?? c.CardRef.LogicCore?.name) == name);

        private string Draft(int socket) => _world.Workbench.DraftAppendageAt(socket)?.name;

        /// <summary>Cards that are neither in the vault nor in a socket: what a failed drag used to orphan.</summary>
        private int StrayCards()
        {
            int stray = 0;
            var stack = new Stack<Node>();
            stack.Push(_screen);
            while (stack.Count > 0)
            {
                Node node = stack.Pop();
                foreach (Node child in node.GetChildren())
                {
                    stack.Push(child);
                }
                if (node is WorkbenchCardControl card && !card.IsQueuedForDeletion())
                {
                    bool inVault = card.GetParent() == _screen.VaultList;
                    bool inSocket = card.GetParent() is Panel p && p.Name == "Socket";
                    if (!inVault && !inSocket)
                    {
                        stray++;
                    }
                    else if (card.Modulate.A < 0.99f)
                    {
                        stray++; // a ghost left at drag alpha
                    }
                }
            }
            return stray;
        }
    }
}
