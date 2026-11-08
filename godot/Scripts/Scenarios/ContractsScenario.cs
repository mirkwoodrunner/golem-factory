using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Contracts;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Supply;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// City contracts (docs/contracts-design.md §9), with real keys and clicks:
    /// <list type="number">
    ///   <item>W walks up to the City Office in the town square: roped off, its prompt greyed, E
    ///   refused with when it opens.</item>
    ///   <item>The factory smelts (Slag seen): the first contract posts, the rope comes down, and E
    ///   opens the notice; a click takes it, its bonus timer running in plain ink.</item>
    ///   <item>I opens the office's crate: a click puts one Iron Plate in, a Shift-click all the
    ///   pack holds; the rest comes from a golem hauling from a depot and pushing into the office.</item>
    ///   <item>Done within par: the reward is in the pack, the bonus rung on the shelf; a Coal cart
    ///   added, E and a click put the rung on it for nothing. The next contract posts.</item>
    ///   <item>Taken and part filled, saved to Slot 1, filled further, and loaded with two clicks:
    ///   the board is as saved.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class ContractsScenario : StepScenario
    {
        private static readonly CoreVector2Int Front = TownSquare.OfficeCell + new CoreVector2Int(0, -1);
        private static readonly int Row = TownSquare.OfficeOrigin.y;
        private static readonly CoreVector2Int GolemCell = new CoreVector2Int(TownSquare.OfficeOrigin.x - 1, Row);
        private static readonly CoreVector2Int DepotCell = new CoreVector2Int(TownSquare.OfficeOrigin.x - 2, Row);
        private static readonly CoreVector2Int BoilerCell = new CoreVector2Int(TownSquare.OfficeOrigin.x - 1, Row + 1);

        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private ContractBoard Board => Sandbox.Contracts;
        private PlayerInteractor Hands => Sandbox.Interactor;
        private ContractScreen Notice => ContractScreen.Find(Runner);
        private CrateScreen Crates => Tree.Root.FindChild("CrateScreen", true, false) as CrateScreen;
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private HudOverlay Hud => Tree.Root.FindChild("Hud", true, false) as HudOverlay;
        private CityOfficeView View => (Tree.GetFirstNodeInGroup(BuildingsLayer.GroupName) as BuildingsLayer)?.Office;
        private int Pack(string item) => Sandbox.Wallet.Pack.GetQuantity(item);

        private ContractDefinition _first;
        private ContractDefinition _second;
        private int _gear;
        private CoreVector2Int _coalSpot;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Wait(0.5);
            PlanStart();
            PlanRopedOff();
            PlanTake();
            PlanByHand();
            PlanByGolem();
            PlanReward();
            PlanSaveLoad();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        // --- 0. A fresh game, a step short of the square ---------------------------------------

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
            Do("the card is down; the guide put away; out in the square", () =>
            {
                if ((Tree.Root.FindChild("Playtest", true, false) as PlaytestNode)?.Card.Visible == true)
                {
                    return "the card is still up";
                }
                Sandbox.Tutorial?.Dismiss();
                Sandbox.Clock.Play();
                _first = Board.Sequence[0];
                _second = Board.Sequence[1];
                Hands.Teleport(new CoreVector3(Front.x, Front.y - 3, 0f));
                return Board.IsOpen ? "the office is open on a fresh game" : null;
            });
            Wait(0.3);
        }

        // --- 1. Roped off ---------------------------------------------------------------------

        private void PlanRopedOff()
        {
            Do("W up to the office", () => { Press(Key.W, true); return null; });
            Until("at its door", 10, () => Hands.Position.y >= Front.y - 0.2f ? null : $"at y {Hands.Position.y:0.0}");
            Do("stop: roped off", () => { Press(Key.W, false); return null; });
            Wait(0.3);
            Do("the prompt is greyed and says so; the rope is drawn", () =>
            {
                if (Hands.CurrentPick.Kind != InteractionKind.Office || Hands.CurrentAffordance != InteractionAffordance.Unavailable
                    || !Hands.CurrentPrompt.Contains("City Office  -  roped off"))
                {
                    return $"pick {Hands.CurrentPick.Kind} {Hands.CurrentAffordance}, prompt '{Hands.CurrentPrompt}'";
                }
                if (View == null || View.SpritePath != CityOfficeView.ClosedSprite)
                {
                    return "drawn as " + View?.SpritePath;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "contracts-roped");
                Tap(Key.E);
                return null;
            });
            Do("E is refused, saying when it opens", () =>
                !Notice.IsOpen && Hands.LastStatusMessage.StartsWith("The City Office opens once you make ")
                    ? null : $"notice open {Notice.IsOpen}, '{Hands.LastStatusMessage}'");
        }

        // --- 2. The first contract posts, and is taken --------------------------------------------

        private void PlanTake()
        {
            Do("the factory smelts iron: Slag and Iron Plate seen", () =>
            {
                foreach (string good in _first.postWhenSeen)
                {
                    Sandbox.TechTree.Ledger.RecordItem(good);
                }
                return null;
            });
            Until("the first contract posts", 2, () => Board.Current == _first && Board.State == ContractState.Posted ? null : $"current {Board.Current?.name}");
            Until("the rope comes down", 2, () => View.SpritePath == CityOfficeView.OpenSprite ? null : "still " + View.SpritePath);
            Do("the prompt names who asks", () =>
            {
                if (Hands.CurrentAffordance != InteractionAffordance.Ready || !Hands.CurrentPrompt.Contains(_first.requester + " has work"))
                {
                    return $"{Hands.CurrentAffordance}, '{Hands.CurrentPrompt}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "contracts-office");
                Tap(Key.E);
                return null;
            });
            Do("the notice: who asks, what, the reward, the bonus, Take", () =>
            {
                if (!Notice.IsOpen || Notice.Title != _first.requester || Notice.Words != _first.words)
                {
                    return $"open {Notice.IsOpen}, '{Notice.Title}', '{Notice.Words}'";
                }
                string asked = $"0 of {_first.asks[0].quantity} {ItemTiers.DisplayName(_first.asks[0].itemType)}";
                if (Notice.Asks != asked || !Notice.Reward.StartsWith("Reward: ") || !Notice.Bonus.StartsWith("Bonus within "))
                {
                    return $"'{Notice.Asks}', '{Notice.Reward}', '{Notice.Bonus}'";
                }
                if (Notice.Button(ContractAction.Accept) == null)
                {
                    return "no Take row";
                }
                string id = Notice.DrawnText.Select(PlayerText.RawIdIn).FirstOrDefault(t => t != null);
                return id == null ? null : "an id on the notice: " + id;
            });
            Do("click Take", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "contracts-notice");
                Click(Notice.Button(ContractAction.Accept));
                return null;
            });
            Do("taken: the bonus counts down in plain words", () =>
            {
                if (Board.State != ContractState.Accepted || !Notice.Bonus.StartsWith("Bonus if done in ") || Notice.Button(ContractAction.Accept) != null)
                {
                    return $"{Board.State}, '{Notice.Bonus}', status '{Notice.Status}'";
                }
                Tap(Key.Escape);
                return null;
            });
            Until("the HUD shows it", 1, () =>
                Hud.ContractLine.StartsWith("Contract · " + _first.requester + " · 0 of ") ? null : $"HUD '{Hud.ContractLine}'");
            Do("the prompt says how far it has got, and offers its crate", () =>
            {
                if (!Hands.CurrentPrompt.Contains(_first.requester + " · 0 of ") || !Hands.CurrentPrompt.Contains("[I] open"))
                {
                    return $"prompt '{Hands.CurrentPrompt}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "contracts-taken");
                return null;
            });
        }

        // --- 3. By hand ---------------------------------------------------------------------------

        private void PlanByHand()
        {
            string plate = _first?.asks[0].itemType ?? ItemType.IronPlate;
            Do("25 Iron Plate in the pack; I at the office", () =>
            {
                plate = _first.asks[0].itemType;
                Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, plate, 25 - Pack(plate));
                Tap(Key.I);
                return null;
            });
            Do("the office's crate screen; click Iron Plate once", () =>
            {
                if (!Crates.IsOpen || Crates.Crate != Sandbox.OfficeCrate || Crates.CrateTitle != ContractText.OfficeName)
                {
                    return $"open {Crates.IsOpen}, '{Crates.CrateTitle}'";
                }
                return ClickRow(Crates.PackRows, plate, shift: false);
            });
            Until("one counted", 2, () => Board.Delivered(plate) == 1 && Pack(plate) == 24 ? null : $"delivered {Board.Delivered(plate)}, pack {Pack(plate)}");
            Do("Shift-click Iron Plate", () => ClickRow(Crates.PackRows, plate, shift: true));
            Until("all the pack held, counted and spent", 2, () =>
                Board.Delivered(plate) == 25 && Pack(plate) == 0 && Board.OfficeBuffer.GetQuantity(plate) == 0
                    ? null : $"delivered {Board.Delivered(plate)}, pack {Pack(plate)}, crate {Board.OfficeBuffer.GetQuantity(plate)}");
            Do("Escape", () => { Tap(Key.Escape); return null; });
        }

        private string ClickRow(IReadOnlyDictionary<string, Button> rows, string good, bool shift)
        {
            if (!rows.TryGetValue(good, out Button row))
            {
                return $"no {good} row";
            }
            Vector2 at = row.GetGlobalRect().GetCenter();
            Tree.Root.WarpMouse(at);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ShiftPressed = shift });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at, ShiftPressed = shift });
            return null;
        }

        // --- 4. By golem ------------------------------------------------------------------------

        private void PlanByGolem()
        {
            Do("a delivery line: a fuelled boiler, a depot of Iron Plate, a Scavenger between, facing the office", () =>
            {
                string plate = _first.asks[0].itemType;
                PlaceableBoiler boiler = Place<PlaceableBoiler>("BoilerPrefab", BoilerCell);
                Deposit(ItemType.Coke, 40);
                if (!Hands.TryRefuelBoiler(boiler))
                {
                    return "refuel: " + Hands.LastStatusMessage;
                }
                ICrate depot = Place<ICrate>("DepotPrefab", DepotCell);
                Deposit(plate, 30);
                CrateTransfer.Put(Sandbox.Wallet.Pack, depot, plate, 30);

                ChassisDefinition chassis = Sandbox.Definitions.Chassis["ClockworkScavenger"];
                foreach (PunchCards.RecipeIngredient c in chassis.cost)
                {
                    Deposit(c.itemType, c.quantity);
                }
                if (!Sandbox.StarterStation.TryConstructGolem(chassis, out GolemEntity golem))
                {
                    return "no golem built";
                }
                (Sandbox.WorkbenchScreen as WorkbenchScreen)?.Close();
                golem.SetPlacement(GolemCell, Facing.East);
                golem.Program.logicCore = Sandbox.Definitions.LogicCores["AlwaysOnCore"];
                golem.Program.TryAddAppendage(Sandbox.Definitions.Appendages["HaulScrap"]);
                golem.Program.SetItemTypeAt(0, plate);
                golem.Program.SetQuantityAt(0, 5);
                golem.Program.TryAddAppendage(Sandbox.Definitions.Appendages["PushOutput"]);
                _gear = Pack(_first.reward.goods[0].itemType);
                return null;
            });
            Until("the golem fills the rest: done, within par", 30, () =>
                Board.Done.Contains(_first.name) ? null : $"delivered {Board.Delivered(_first.asks[0].itemType)} of {_first.asks[0].quantity}");
        }

        // --- 5. The reward, the rung, the next contract --------------------------------------------

        private void PlanReward()
        {
            Do("the reward in the pack, the bonus earned, its rung on the shelf", () =>
            {
                PunchCards.RecipeIngredient reward = _first.reward.goods[0];
                if (Pack(reward.itemType) != _gear + reward.quantity)
                {
                    return $"{reward.itemType} {_gear} -> {Pack(reward.itemType)}";
                }
                if (!Board.EarnedBonus(_first.name) || Board.ShelfRungs.Count != 1 || Board.ShelfRungs[0].ItemType != _first.bonus.rungGood)
                {
                    return $"bonus {Board.EarnedBonus(_first.name)}, shelf {Board.ShelfRungs.Count}";
                }
                return null;
            });
            Until("the HUD's contract line clears", 1, () => Hud.ContractLine == "" ? null : $"HUD still '{Hud.ContractLine}'");
            Do("a Coal cart on the street; E at the office", () =>
            {
                CartSpot spot = Sandbox.Supply.Spots.First(s => s.IsEmpty);
                _coalSpot = spot.Cell;
                SupplyResult added = Sandbox.GrantCart(spot.Cell, _first.bonus.rungGood);
                if (!added.Done)
                {
                    return added.Message;
                }
                Tap(Key.E);
                return null;
            });
            Do("the notice offers the rung on that cart", () =>
            {
                if (!Notice.IsOpen || Notice.Rows.Count(r => r.row.Action == ContractAction.Rung && r.row.Enabled) != 1)
                {
                    return $"open {Notice.IsOpen}, rows {string.Join(" | ", Notice.Rows.Select(r => r.row.Label + " " + r.row.Detail))}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "contracts-reward");
                Click(Notice.Button(ContractAction.Rung));
                return null;
            });
            Do("the cart has its rung, for nothing", () =>
            {
                SupplyCart cart = Sandbox.Supply.SpotAt(_coalSpot).Cart;
                bool rate = _first.bonus.rungKind == ContractRungKind.Rate;
                if ((rate ? cart.RateRung : cart.CapacityRung) != 1 || Board.ShelfRungs.Count != 0)
                {
                    return $"rate {cart.RateRung} capacity {cart.CapacityRung}, shelf {Board.ShelfRungs.Count}: '{Notice.Status}'";
                }
                return cart.RungsPaid.Count == 0 ? null : "a given rung was recorded as paid";
            });
            Do("the next contract's goods are seen", () =>
            {
                foreach (string good in _second.postWhenSeen)
                {
                    Sandbox.TechTree.Ledger.RecordItem(good);
                }
                return null;
            });
            Until("it posts", 2, () => Notice.Title == _second.requester ? null : $"notice '{Notice.Title}'");
            Do("Take", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "contracts-next");
                Click(Notice.Button(ContractAction.Accept));
                return null;
            });
            Do("taken", () => Board.Current == _second && Board.State == ContractState.Accepted ? null : $"{Board.Current?.name} {Board.State}");
            Do("Escape", () => { Tap(Key.Escape); return null; });
        }

        // --- 6. Saved and loaded ------------------------------------------------------------------

        private void PlanSaveLoad()
        {
            string good = null;
            Do("five delivered through the office's endpoint", () =>
            {
                good = _second.asks[0].itemType;
                return Deliver(good, 5);
            });
            Until("counted", 2, () => Board.Delivered(good) == 5 ? null : $"delivered {Board.Delivered(good)}");
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Save on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Save)); return null; });
            Do("saved; five more after", () =>
            {
                if (Management.SlotBoard.Row("slot-1")?.Empty != false)
                {
                    return "Slot 1 is empty";
                }
                return Deliver(good, 5);
            });
            Until("ten counted", 2, () => Board.Delivered(good) == 10 ? null : $"delivered {Board.Delivered(good)}");
            Do("click Load on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Do("click Load now", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Until("loaded: the board as saved", 3, () =>
            {
                if (Board.Current?.name != _second.name || Board.State != ContractState.Accepted || Board.Delivered(good) != 5)
                {
                    return $"{Board.Current?.name} {Board.State}, delivered {Board.Delivered(good)}";
                }
                if (!Board.Done.Contains(_first.name) || !Board.EarnedBonus(_first.name))
                {
                    return "the first contract's record is gone";
                }
                return null;
            });
            Do("Escape; the HUD shows the loaded contract", () =>
            {
                Tap(Key.Escape);
                Log($"the office opened with the first smelter; {_first.requester}'s contract taken with a click, filled by hand (one, then Shift) and by a golem, its bonus rung put on a Coal cart; {_second.requester}'s taken, saved part filled and loaded back");
                return null;
            });
            Until("the HUD line", 1, () => Hud.ContractLine.Contains("5 of ") ? null : $"HUD '{Hud.ContractLine}'");
        }

        // A golem's push or a belt, as the office's cells take one: its endpoint.
        private string Deliver(string good, int n)
        {
            if (!Sandbox.Endpoints.TryGetEndpoint(TownSquare.OfficeCell, out IItemEndpoint office))
            {
                return "no endpoint on the office";
            }
            for (int i = 0; i < n; i++)
            {
                if (!office.TryGive(new Belts.ItemStack { ItemType = good }))
                {
                    return $"the office refused {good} {i + 1}";
                }
            }
            return null;
        }

        private T Place<T>(string key, CoreVector2Int cell) where T : class
        {
            PlaceableBuilding prefab = Sandbox.Placeables.Single(p => p.Key == key).Prefab;
            foreach (PunchCards.RecipeIngredient c in prefab.Cost)
            {
                Deposit(c.itemType, c.quantity);
            }
            Sandbox.Build.SetActivePrefab(prefab);
            Sandbox.Build.PlaceOrRemove(cell);
            Sandbox.Build.CancelPlacement();
            PlaceableBuilding placed = Sandbox.Build.Buildings.SingleOrDefault(b => b.Cell == cell && !b.IsRemoved)
                ?? throw new System.InvalidOperationException($"could not place {key} at {cell}: {Sandbox.Build.LastStatusMessage}");
            return placed.GetPart<T>();
        }

        private void Deposit(string itemType, int quantity) => Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, itemType, quantity);
    }
}
