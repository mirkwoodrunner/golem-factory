using System.Linq;
using Godot;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The guide's program step over the Workbench, ahead of the guide (from playtest, 2026-11-08:
    /// "I couldn't tell what all the steps I should program" on the first Aether-Hauler). Real keys
    /// and clicks from chapter 5 on:
    /// <list type="number">
    ///   <item>F9 four times fast-forwards chapters 1 to 4; Iron Smelting is claimed (the kit's own
    ///   claim) and the Hauler's price put in the pack.</item>
    ///   <item>E at the station and a click on the Aether-Hauler's row build it; the Workbench opens
    ///   on it with the guide on "Depots for the smelter", two steps short of the program.</item>
    ///   <item>The plate shows "Two Hauls, two goods" docked over the Workbench, naming Coke, with
    ///   no goods line and no notice from the step behind.</item>
    ///   <item>Following the glow (drags, the good arrow, the dial, the lever) programs the smelter;
    ///   once it runs that program the plate steps aside again.</item>
    /// </list>
    /// Writes only <c>user://scenario-*</c> files.
    /// </summary>
    public sealed class GuideAheadScenario : StepScenario
    {
        private const string Hauler = "AetherHauler";

        private SandboxWorld Sandbox => WorldNode.Find(Runner).Sandbox;
        private TutorialPanel Panel => Tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;
        private WorkbenchScreen Bench => Tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
        private ConstructionPanelNode Station => Tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;

        private Golems.GolemEntity _hauler;
        private int _moves;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Wait(0.5);
            Do("the first question put by (the kit doesn't need it)", () =>
            {
                if (Sandbox.Tutorial?.Playtest == null)
                {
                    return "no guide in playtest mode";
                }
                Sandbox.Tutorial.Playtest.Skip(0f);
                return null;
            });
            foreach (string next in new[] { "gears", "coking-card", "patent", "r4-card" })
            {
                string expect = next;
                Do("F9", () => { Tap(Key.F9); return null; });
                Until("the kit stops at " + expect, 120, () =>
                    Sandbox.Tutorial.KitRunning ? "running" : Sandbox.Tutorial.Current?.Id == expect ? null : "on " + Sandbox.Tutorial.Current?.Id);
            }
            PlanBuild();
            PlanShown();
            PlanFollowTheGlow();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        private void PlanBuild()
        {
            Do("Iron Smelting claimed, the Hauler's price in the pack", () =>
            {
                ChassisDefinition chassis = Sandbox.Definitions.Chassis[Hauler];
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
                }
                if (!Sandbox.Tutorial.KitClaim("AssembleIronSmelting"))
                {
                    return "Iron Smelting couldn't be claimed";
                }
                SandboxSetup.Placement station = Sandbox.Setup.starterStation;
                Sandbox.Interactor.Teleport(new CoreVector3(station.x, station.y - 1f, 0f));
                return null;
            });
            Until("the guide asks for an Aether-Hauler", 3, () => Sandbox.Tutorial.Current?.Id == "hauler" ? null : "on " + Sandbox.Tutorial.Current?.Id);
            Wait(0.3);
            Do("E at the station", () => { Tap(Key.E); return null; });
            Until("Construct Golem is open", 2, () => Station.IsOpen ? null : "closed");
            Do("click the Aether-Hauler's row", () =>
            {
                Click(Station.Rows.Single(r => r.chassis.name == Hauler).row);
                return null;
            });
            Until("the Workbench opens on the new Hauler", 3, () =>
            {
                _hauler = Sandbox.Golems.FirstOrDefault(g => !g.IsRemoved && g.Program.chassis?.name == Hauler);
                if (_hauler == null)
                {
                    return "no Hauler built: " + Station.Status;
                }
                return Bench.IsOpen && Sandbox.Workbench.TargetGolem == _hauler ? null : "the Workbench is on " + Sandbox.Workbench.TargetGolem?.GolemId;
            });
        }

        private void PlanShown()
        {
            Until("the plate shows the smelter's program, docked over the Workbench", 2, () =>
            {
                if (Sandbox.Tutorial.Current?.Id == "program-smelter")
                {
                    return "the guide reached the step itself; this checks it ahead of the guide";
                }
                if (!Panel.Plate.Visible)
                {
                    return "no plate over the Workbench (guide on " + Sandbox.Tutorial.Current?.Id + ")";
                }
                if (Panel.ShownStepId != "program-smelter" || Panel.TitleText != "Two Hauls, two goods")
                {
                    return $"plate shows '{Panel.TitleText}' ({Panel.ShownStepId})";
                }
                if (!Panel.BodyText.Contains("Coke") || !Panel.BodyText.Contains("Haul, Haul, Assemble Iron Smelting and Push Output"))
                {
                    return "body '" + Panel.BodyText + "'";
                }
                if (Panel.ProgressText != "" || Panel.NoticeVisible)
                {
                    return $"the step behind leaks through: goods '{Panel.ProgressText}', notice {Panel.NoticeVisible}";
                }
                Rect2 plate = Panel.Plate.GetGlobalRect();
                if (plate.Intersects(Bench.Lever.GetGlobalRect()) || plate.Intersects(Bench.VaultView.GetGlobalRect()))
                {
                    return $"the plate {plate} covers the lever or the vault";
                }
                if (CoveredSocket() is string covered)
                {
                    return covered;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "guide-ahead");
                Log($"over the new Hauler's Workbench, with the guide on '{Sandbox.Tutorial.Current?.Id}': '{Panel.TitleText}' docked at {plate.Position}");
                return null;
            });
        }

        // --- Following the glow, as a player does -----------------------------------------------

        private void PlanFollowTheGlow()
        {
            for (int i = 0; i < 40; i++)
            {
                Do("follow the glow " + (i + 1) + ": press", () =>
                {
                    _dragTo = null;
                    if (_hauler.HasRunnableProgram && Sandbox.Tutorial.SmelterPlan.Matches(_hauler.Program))
                    {
                        return null; // engaged: the rest of the moves are spare
                    }
                    WorkbenchHintTarget hint = Bench.ShownHint;
                    Control target = Bench.HintedTargetControl;
                    if (hint.Kind == WorkbenchHintKind.None || target == null)
                    {
                        return "the glow points at nothing: " + hint;
                    }
                    _moves++;
                    if (hint.Zone.Kind == DropZoneKind.Appendage && hint.Zone.AppendageIndex == 3 && !_shotStep4)
                    {
                        _shotStep4 = true;
                        ScenarioCapture.Shot(Runner.GetViewport(), "guide-ahead-step4");
                        if (CoveredSocket() is string covered)
                        {
                            return covered;
                        }
                    }
                    if (hint.Kind != WorkbenchHintKind.VaultCard)
                    {
                        Click(target);
                        return null;
                    }
                    Control card = Bench.HintedCardControl;
                    if (card == null)
                    {
                        return "no vault card glows for " + hint;
                    }
                    // The glowing card must be in the vault's window: a player can't grab what it can't see.
                    if (!Bench.VaultView.GetGlobalRect().Grow(1f).Encloses(card.GetGlobalRect())) // flush with an edge is in
                    {
                        return $"the glowing {hint.Card} sits outside the vault's window ({card.GetGlobalRect()} in {Bench.VaultView.GetGlobalRect()})";
                    }
                    _dragTo = target;
                    _pointer = Window(card.GetGlobalRect().GetCenter());
                    Mouse(new InputEventMouseMotion { Position = _pointer, GlobalPosition = _pointer });
                    Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = _pointer, GlobalPosition = _pointer });
                    Vector2 lift = _pointer + new Vector2(24f, 0f); // past Godot's drag threshold
                    Mouse(new InputEventMouseMotion { Position = lift, GlobalPosition = lift, Relative = lift - _pointer, ButtonMask = MouseButtonMask.Left });
                    _pointer = lift;
                    return null;
                });
                Do("carry", () =>
                {
                    if (_dragTo != null && !Tree.Root.GuiIsDragging())
                    {
                        return $"the press didn't lift a card (move {_moves}); under the pointer {Tree.Root.GuiGetHoveredControl()?.GetPath()}";
                    }
                    if (_dragTo != null)
                    {
                        Vector2 to = Window(_dragTo.GetGlobalRect().GetCenter());
                        Mouse(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - _pointer, ButtonMask = MouseButtonMask.Left });
                        _pointer = to;
                    }
                    return null;
                });
                Do("release", () =>
                {
                    if (_dragTo != null)
                    {
                        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = _pointer, GlobalPosition = _pointer });
                    }
                    return null;
                });
            }
            Until("the smelter runs its program, and the plate has stepped aside", 2, () =>
            {
                if (!Sandbox.Tutorial.SmelterPlan.Matches(_hauler.Program))
                {
                    var w = Sandbox.Workbench;
                    string draft = string.Join(" | ", Enumerable.Range(0, w.SocketCount).Select(i => $"{w.DraftAppendageAt(i)?.name}:{w.DraftItemTypeAt(i)}x{w.DraftQuantityAt(i)}"));
                    return $"the program isn't the smelter's yet after {_moves} moves: core {w.DraftLogicCore?.name}, draft {draft}, hint {Bench.ShownHint}, open {Bench.IsOpen}";
                }
                if (Panel.Plate.Visible)
                {
                    return $"the plate still shows '{Panel.TitleText}' over the Workbench";
                }
                Log($"{_moves} moves along the glow engaged it; the plate stepped aside");
                return null;
            });
            Do("Escape closes the Workbench", () => { Tap(Key.Escape); return null; });
        }

        /// <summary>A visible step or trigger socket under the guide's plate, said; or null.</summary>
        private string CoveredSocket()
        {
            if (!Panel.Plate.Visible)
            {
                return null;
            }
            Rect2 plate = Panel.Plate.GetGlobalRect();
            foreach (WorkbenchDropRow row in Bench.StepRows.Prepend(Bench.TriggerRow))
            {
                if (row.Socket.IsVisibleInTree() && plate.Intersects(row.Socket.GetGlobalRect()))
                {
                    return $"the plate {plate} covers {row.Name}'s socket {row.Socket.GetGlobalRect()}";
                }
            }
            return null;
        }

        private Control _dragTo;
        private bool _shotStep4;
        private Vector2 _pointer;

        private Vector2 Window(Vector2 canvas) => canvas * Tree.Root.ContentScaleFactor;

        private void Mouse(InputEventMouse e)
        {
            Tree.Root.WarpMouse(e.Position);
            Input.ParseInputEvent(e);
        }
    }
}
