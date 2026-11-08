using System.Linq;
using Godot;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.Supply;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The Docks (docs/region-design.md), with real keys and clicks:
    /// <list type="number">
    ///   <item>Walking east down the street stops at its end: the gate is roped.</item>
    ///   <item>A Zeppelin built, the gate opens and the Docks are paved; D walks down the lane onto
    ///   them.</item>
    ///   <item>E at a quay spot opens the cart panel, and a click adds a Coal barge.</item>
    ///   <item>The Assembly Line tab's Docks row: a click buys two rows north, paved at once.</item>
    ///   <item>Saved to Slot 1, a row and the barge removed, loaded with two clicks: back as saved.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class DocksScenario : StepScenario
    {
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private PlayerInteractor Hands => Sandbox.Interactor;
        private SandboxNode Shell => Tree.GetFirstNodeInGroup(SandboxNode.GroupName) as SandboxNode;
        private FloorLayer Floor => Tree.Root.FindChild("Floor", true, false) as FloorLayer;
        private SupplyScreen Carts => SupplyScreen.Find(Runner);
        private HudOverlay Hud => Tree.Root.FindChild("Hud", true, false) as HudOverlay;
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private static readonly CoreVector2Int Quay = Docks.QuaySpots().First();

        private Button DocksExtend() =>
            Management.AssemblyLine.RowList.FindChild("DocksRow", true, false)?.FindChild("Extend", true, false) as Button;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Wait(0.5);
            Do("answer the playtest's first question", () =>
            {
                var playtest = Tree.Root.FindChild("Playtest", true, false) as PlaytestNode;
                if (playtest?.Card.Visible == true)
                {
                    Click(playtest.Options.GetChildren().OfType<Button>().First());
                }
                return null;
            });
            Do("out on the street, near its east end", () =>
            {
                if ((Tree.Root.FindChild("Playtest", true, false) as PlaytestNode)?.Card.Visible == true)
                {
                    return "the card is still up";
                }
                Sandbox.Tutorial?.Dismiss();
                Sandbox.Clock.Play();
                Hands.Teleport(new CoreVector3(28f, -16f, 0f));
                return Sandbox.Bounds.DocksOpen ? "the Docks are open on a fresh game" : null;
            });
            Wait(0.3);
            PlanRoped();
            PlanOpen();
            PlanQuay();
            PlanRows();
            PlanSaveLoad();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        private void PlanRoped()
        {
            Do("D east; no region named on the street", () =>
            {
                Press(Key.D, true);
                return Hud.RegionLine == "" ? null : "region " + Hud.RegionLine;
            });
            Wait(1.5);
            Do("stopped at the street's end; the gate is roped", () =>
            {
                Press(Key.D, false);
                if (Hands.Position.x > FloorLayout.StreetHalfExtent + 0.01f)
                {
                    return $"walked to x {Hands.Position.x:0.00}";
                }
                if (Shell.Gate.Shown != DocksGateView.ClosedSprite)
                {
                    return "the gate shows " + Shell.Gate.Shown;
                }
                if (Hud.RegionLine != Docks.ShutLine)
                {
                    return $"at the gate the plate reads '{Hud.RegionLine}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "docks-gate");
                return null;
            });
        }

        private void PlanOpen()
        {
            Do("the factory builds its first Zeppelin", () =>
            {
                Sandbox.TechTree.Ledger.RecordChassis(SandboxWorld.ZeppelinChassis);
                return null;
            });
            Until("the city notes it", 2, () =>
                Sandbox.Alerts.Log.Entries.Any(e => e.IsNote && e.Text == Docks.OpenedNote) ? null : "no note");
            Until("the gate opens, the Docks paved", 2, () =>
                Shell.Gate.Shown == DocksGateView.OpenSprite && Floor.DocksPainted == Docks.StartNorth
                    ? null : $"gate {Shell.Gate.Shown}, painted {Floor.DocksPainted}");
            Do("D down the lane", () => { Press(Key.D, true); return null; });
            Until("on the Docks", 6, () => Hands.Position.x >= Docks.West + 2 ? null : $"at x {Hands.Position.x:0.0}");
            Do("stop", () => { Press(Key.D, false); return null; });
            Until("the HUD names the Docks", 1, () => Hud.RegionLine == Docks.Name ? null : $"region '{Hud.RegionLine}'");
            Do("look", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "docks-open");
                return null;
            });
        }

        private void PlanQuay()
        {
            Do("at a quay spot, the barge's price in the pack", () =>
            {
                Supply.CartLadder coal = Sandbox.Supply.TryGetLadder(ItemType.Coal, out Supply.CartLadder l) ? l : null;
                EndgameLoad.Grant(Sandbox, coal.AddPrice);
                Sandbox.TechTree.Poll(); // the Ledger sees the price's goods, so the cart is offered
                Hands.Teleport(new CoreVector3(Quay.x, Quay.y + 1, 0f));
                return null;
            });
            Wait(0.3);
            Do("the prompt offers a cart; E opens the panel", () =>
            {
                if (Hands.CurrentPick.Kind != InteractionKind.Supply || !Hands.CurrentPrompt.Contains("Add a cart at"))
                {
                    return $"pick {Hands.CurrentPick.Kind}, prompt '{Hands.CurrentPrompt}'";
                }
                Tap(Key.E);
                return null;
            });
            Do("click Coal cart", () =>
            {
                if (!Carts.IsOpen || Carts.Spot?.Cell != Quay)
                {
                    return $"panel open {Carts.IsOpen} on {Carts.Spot?.Cell}";
                }
                Button coal = Carts.Button(SupplyAction.Add, ItemType.Coal);
                if (coal == null)
                {
                    return "no Coal cart offered: " + string.Join(", ", Carts.Rows.Select(r => r.row.Action + " " + r.row.ItemType));
                }
                Click(coal);
                return null;
            });
            Do("a Coal barge on the quay", () =>
            {
                if (Sandbox.Supply.SpotAt(Quay).Cart?.ItemType != ItemType.Coal)
                {
                    return "no barge: " + Carts.Status;
                }
                Tap(Key.Escape);
                return null;
            });
            Wait(0.3);
            Do("look at the quay", () => { ScenarioCapture.Shot(Runner.GetViewport(), "docks-quay"); return null; });
        }

        private void PlanRows()
        {
            Do("the next rows' price in the pack; Tab", () =>
            {
                EndgameLoad.Grant(Sandbox, Sandbox.DocksExpansion.NextCost());
                Sandbox.TechTree.Poll();
                Tap(Key.Tab);
                return null;
            });
            Do("click Assembly Line", () => { Click(Management.TabButtons[ManagementTab.AssemblyLine]); return null; });
            Wait(0.1);
            Do("click Extend on the Docks row", () =>
            {
                Button extend = DocksExtend();
                if (extend == null || extend.Disabled)
                {
                    return "no live Extend on the Docks row";
                }
                Click(extend);
                return null;
            });
            Until("two rows north, paved", 2, () =>
                Sandbox.Bounds.DocksNorth == Docks.StartNorth + 2 && Floor.DocksPainted == Docks.StartNorth + 2
                    ? null : $"north {Sandbox.Bounds.DocksNorth}, painted {Floor.DocksPainted}: {Sandbox.AssemblyLineBoard.Status}");
            Do("Escape", () => { Tap(Key.Escape); return null; });
        }

        private void PlanSaveLoad()
        {
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Save on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Save)); return null; });
            Do("saved; the barge removed and the rows taken back", () =>
            {
                if (Management.SlotBoard.Row("slot-1")?.Empty != false)
                {
                    return "Slot 1 is empty";
                }
                Sandbox.Supply.Remove(Sandbox.Supply.SpotAt(Quay));
                Sandbox.Bounds.RestoreDocks(0);
                return null;
            });
            Do("click Load on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Do("click Load now", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Until("loaded: the rows and the barge as saved", 3, () =>
            {
                if (Sandbox.Bounds.DocksNorth != Docks.StartNorth + 2 || Sandbox.Supply.SpotAt(Quay).Cart?.ItemType != ItemType.Coal)
                {
                    return $"north {Sandbox.Bounds.DocksNorth}, quay {Sandbox.Supply.SpotAt(Quay).Cart?.ItemType}";
                }
                return Floor.DocksPainted == Docks.StartNorth + 2 ? null : "painted " + Floor.DocksPainted;
            });
            Do("Escape", () =>
            {
                Tap(Key.Escape);
                Log("the street ended at the roped gate; the first Zeppelin opened and paved the Docks; walked down the lane onto them, added a Coal barge on the quay with E and a click, bought two rows with the Docks row's Extend, saved and loaded them back");
                return null;
            });
        }
    }
}
