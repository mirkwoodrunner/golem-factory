using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Contracts;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Selling golems to the city (docs/golem-sales-design.md), with real keys and clicks:
    /// <list type="number">
    ///   <item>The office opens with the first smelter, and the first golem order posts: Scavengers.</item>
    ///   <item>A Scavenger built at the station, G picks it up, W walks it to the office; the prompt
    ///   offers the sale, E sells it: gone, the hands empty, the HUD counting one.</item>
    ///   <item>E at the office: the notice shows the order (no id drawn).</item>
    ///   <item>A Golem Foundry fed by a golem hauling Scrap from a depot builds Scavengers and ships
    ///   them; its prompt and I's Crate screen say what it is doing. The order completes from the
    ///   Foundry, its reward in the pack, and the next order posts.</item>
    ///   <item>Saved to Slot 1 mid-build, the Foundry demolished, loaded with two clicks: the
    ///   Foundry, its build and the order as saved.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class GolemSalesScenario : StepScenario
    {
        private const string Scavenger = "ClockworkScavenger";
        private static readonly CoreVector2Int Front = TownSquare.OfficeCell + new CoreVector2Int(0, -1);
        private static readonly CoreVector2Int DepotCell = new CoreVector2Int(-6, 6);
        private static readonly CoreVector2Int LineGolemCell = new CoreVector2Int(-5, 6);
        private static readonly CoreVector2Int FoundryCell = new CoreVector2Int(-4, 6);
        private static readonly CoreVector2Int BoilerCell = new CoreVector2Int(-5, 7);

        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private GolemOrderBoard Orders => Sandbox.GolemOrders;
        private PlayerInteractor Hands => Sandbox.Interactor;
        private ContractScreen Notice => ContractScreen.Find(Runner);
        private CrateScreen Crates => Tree.Root.FindChild("CrateScreen", true, false) as CrateScreen;
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private HudOverlay Hud => Tree.Root.FindChild("Hud", true, false) as HudOverlay;
        private int Pack(string item) => Sandbox.Wallet.Pack.GetQuantity(item);

        private PlaceableGolemFoundry Foundry =>
            Sandbox.Build.Buildings.Where(b => !b.IsRemoved).Select(b => b.GetPart<PlaceableGolemFoundry>()).FirstOrDefault(f => f != null);

        private GolemEntity _golem;
        private ICrate _depot;
        private GolemOrder _first;
        private int _rewardBefore;
        private int _savedDelivered;
        private long _savedProgress;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Wait(0.5);
            PlanStart();
            PlanByHand();
            PlanNotice();
            PlanFoundry();
            PlanSaveLoad();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        // --- 0. A fresh game; the office opens and the first order posts -----------------------

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
                return Orders.Current != null ? "an order on a fresh game" : null;
            });
            Do("the factory smelts and has built a Scavenger: Slag, Iron Plate and Gear seen", () =>
            {
                foreach (string good in Sandbox.Contracts.Sequence[0].postWhenSeen)
                {
                    Sandbox.TechTree.Ledger.RecordItem(good);
                }
                Sandbox.TechTree.Ledger.RecordItem(ItemType.Gear);
                Sandbox.TechTree.Ledger.RecordChassis(Scavenger);
                return null;
            });
            Until("the first golem order posts: Scavengers", 2, () =>
            {
                _first = Orders.Current;
                return _first?.Chassis == Scavenger ? null : "order " + _first?.Chassis;
            });
            Until("the HUD shows it", 1, () =>
                Hud.OrderLine == "Golem order · 0 of " + GolemOrderText.Golems(Scavenger, _first.Count) ? null : $"HUD '{Hud.OrderLine}'");
        }

        // --- 1. By hand: G, walk, E ------------------------------------------------------------

        private void PlanByHand()
        {
            Do("a Scavenger built at the station; stand beside it and press G", () =>
            {
                ChassisDefinition chassis = Sandbox.Definitions.Chassis[Scavenger];
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Deposit(c.itemType, c.quantity);
                }
                if (!Sandbox.StarterStation.TryConstructGolem(chassis, out _golem))
                {
                    return "no golem built";
                }
                (Sandbox.WorkbenchScreen as WorkbenchScreen)?.Close();
                Hands.Teleport(new CoreVector3(_golem.Cell.x, _golem.Cell.y - 1, 0f));
                return null;
            });
            Wait(0.2);
            Do("G", () => { Tap(Key.G); return null; });
            Do("carrying it; off to the square", () =>
            {
                if (Hands.CarriedGolem != _golem)
                {
                    return "carrying " + Hands.CarriedGolem?.GolemId + ": " + Hands.LastStatusMessage;
                }
                Hands.Teleport(new CoreVector3(Front.x, Front.y - 3, 0f));
                return null;
            });
            Do("W up to the office", () => { Press(Key.W, true); return null; });
            Until("at its door", 10, () => Hands.Position.y >= Front.y - 0.2f ? null : $"at y {Hands.Position.y:0.0}");
            Do("stop", () => { Press(Key.W, false); return null; });
            Wait(0.3);
            Do("the prompt offers the sale", () =>
            {
                string expected = "Sell " + GolemNames.Short(_golem) + "  -  to the " + _first.Requester + " · 0 of " + _first.Count;
                if (Hands.CurrentPick.Kind != InteractionKind.Office || !Hands.CurrentPrompt.Contains(expected))
                {
                    return $"pick {Hands.CurrentPick.Kind}, prompt '{Hands.CurrentPrompt}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "sales-office");
                Tap(Key.E);
                return null;
            });
            Do("sold: gone for good, the hands empty, the order counts one", () =>
            {
                if (!_golem.IsRemoved || Sandbox.Golems.Contains(_golem) || Hands.CarriedGolem != null)
                {
                    return $"removed {_golem.IsRemoved}, carried {Hands.CarriedGolem?.GolemId}: '{Hands.LastStatusMessage}'";
                }
                if (Orders.Delivered != 1 || Orders.GolemsSold != 1 || Notice.IsOpen)
                {
                    return $"delivered {Orders.Delivered}, sold {Orders.GolemsSold}, notice {Notice.IsOpen}";
                }
                return Hands.LastStatusMessage.StartsWith("Sold " + GolemNames.Short(_golem) + " to the ") ? null : Hands.LastStatusMessage;
            });
            Until("the HUD counts it", 1, () => Hud.OrderLine.StartsWith("Golem order · 1 of ") ? null : $"HUD '{Hud.OrderLine}'");
        }

        // --- 2. The notice ---------------------------------------------------------------------

        private void PlanNotice()
        {
            Wait(0.2);
            Do("E at the office, empty-handed", () => { Tap(Key.E); return null; });
            Do("the notice shows the order: who, what, delivered, the reward (no id drawn)", () =>
            {
                if (!Notice.IsOpen || Notice.OrderTitle != "Golem order · " + _first.Requester)
                {
                    return $"open {Notice.IsOpen}, '{Notice.OrderTitle}'";
                }
                string asks = "Wants " + GolemOrderText.Golems(Scavenger, _first.Count) + " · 1 delivered";
                if (Notice.OrderAsks != asks || !Notice.OrderReward.StartsWith("Reward: "))
                {
                    return $"'{Notice.OrderAsks}', '{Notice.OrderReward}'";
                }
                string id = Notice.DrawnText.Select(PlayerText.RawIdIn).FirstOrDefault(t => t != null);
                if (id != null)
                {
                    return "an id on the notice: " + id;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "sales-notice");
                Tap(Key.Escape);
                return null;
            });
        }

        // --- 3. The Golem Foundry --------------------------------------------------------------

        private void PlanFoundry()
        {
            Do("a Foundry, fed by a Scavenger hauling Scrap from a depot, on steam", () =>
            {
                PlaceableBoiler boiler = Place<PlaceableBoiler>("BoilerPrefab", BoilerCell);
                Deposit(ItemType.Coke, 40);
                if (!Hands.TryRefuelBoiler(boiler))
                {
                    return "refuel: " + Hands.LastStatusMessage;
                }
                _depot = Place<ICrate>("DepotPrefab", DepotCell);
                TopUp();
                Place<PlaceableGolemFoundry>("GolemFoundryPrefab", FoundryCell);

                ChassisDefinition chassis = Sandbox.Definitions.Chassis[Scavenger];
                foreach (RecipeIngredient c in chassis.cost)
                {
                    Deposit(c.itemType, c.quantity);
                }
                if (!Sandbox.StarterStation.TryConstructGolem(chassis, out GolemEntity feeder))
                {
                    return "no golem built";
                }
                (Sandbox.WorkbenchScreen as WorkbenchScreen)?.Close();
                feeder.SetPlacement(LineGolemCell, Facing.East);
                feeder.Program.logicCore = Sandbox.Definitions.LogicCores["AlwaysOnCore"];
                feeder.Program.TryAddAppendage(Sandbox.Definitions.Appendages["HaulScrap"]);
                feeder.Program.SetItemTypeAt(0, ItemType.Scrap);
                feeder.Program.SetQuantityAt(0, 6);
                feeder.Program.TryAddAppendage(Sandbox.Definitions.Appendages["PushOutput"]);
                // South-east of it, so the player stands clear of its picture.
                Hands.Teleport(new CoreVector3(FoundryCell.x + 1, FoundryCell.y - 1, 0f));
                _rewardBefore = Pack(_first.RewardGood);
                return null;
            });
            Until("the Foundry has a whole Scavenger's Scrap, and builds", 30, () =>
                Foundry.State == FoundryState.Building ? null : $"{Foundry.State}: {Foundry.CapLine}");
            Do("its prompt says what it builds; I opens its intake", () =>
            {
                if (!Hands.CurrentPrompt.Contains("Golem Foundry  -  Building a Clockwork Scavenger · "))
                {
                    return $"prompt '{Hands.CurrentPrompt}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "sales-foundry");
                Tap(Key.I);
                return null;
            });
            Do("the Crate screen: Golem Foundry, its line", () =>
            {
                if (!Crates.IsOpen || Crates.Crate != Foundry || Crates.CrateTitle != PlaceableGolemFoundry.Name
                    || Crates.LabelButtonText != CrateScreenReadout.FoundryLabel)
                {
                    return $"open {Crates.IsOpen}, '{Crates.CrateTitle}', label '{Crates.LabelButtonText}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "sales-foundry-intake");
                Tap(Key.Escape);
                return null;
            });
            Until("it ships one: the order counts two", 40, () => Orders.Delivered >= 2 ? null : $"delivered {Orders.Delivered}, {Foundry.CapLine}");
            Do("its golem never entered the world", () =>
                Sandbox.Golems.Count(g => !g.IsRemoved && g.Program?.chassis?.name == Scavenger) == 1
                    ? null : "golems in the world: " + Sandbox.Golems.Count(g => !g.IsRemoved));
            Until("the Foundry fills the order: done, the reward in the pack, the next posted", 300, () =>
            {
                TopUp();
                if (Orders.Done < 1)
                {
                    return $"delivered {Orders.Delivered} of {_first.Count}";
                }
                int paid = Pack(_first.RewardGood) - _rewardBefore;
                int expected = Orders.RewardFor(_first, Orders.ChassisRaw(Scavenger) * _first.Count).Single().quantity;
                if (paid != expected)
                {
                    return $"{_first.RewardGood} paid {paid}, expected {expected}";
                }
                return Orders.Current != null && Orders.Current != _first ? null : "no next order";
            });
            Do("the overview's running totals count them", () =>
                Sandbox.RunTotalsLine.EndsWith(" · " + Orders.GolemsSold + " golems sold") ? null : Sandbox.RunTotalsLine);
        }

        // --- 4. Saved and loaded ---------------------------------------------------------------

        private void PlanSaveLoad()
        {
            Until("the Foundry is mid-build", 60, () =>
                TopUpThen() && Foundry.State == FoundryState.Building && Foundry.ProgressTicks > 10 ? null : $"{Foundry.State}");
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Save on Slot 1", () =>
            {
                Sandbox.Clock.Pause();
                _savedDelivered = Orders.Delivered;
                _savedProgress = Foundry.ProgressTicks;
                Click(Management.Slots.Button("slot-1", SlotAction.Save));
                return null;
            });
            Do("saved; the Foundry demolished", () =>
            {
                if (Management.SlotBoard.Row("slot-1")?.Empty != false)
                {
                    return "Slot 1 is empty";
                }
                Sandbox.Build.EnterDemolishMode();
                Sandbox.Build.PlaceOrRemove(FoundryCell);
                Sandbox.Build.CancelPlacement();
                return Foundry == null ? null : "still standing: " + Sandbox.Build.LastStatusMessage;
            });
            Do("click Load on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Do("click Load now", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Until("loaded: the Foundry, its build and the order as saved", 3, () =>
            {
                if (Foundry == null)
                {
                    return "no Foundry";
                }
                if (Foundry.Chassis != Scavenger || Foundry.ProgressTicks != _savedProgress || Orders.Delivered != _savedDelivered)
                {
                    return $"{Foundry.Chassis} {Foundry.ProgressTicks} of {_savedProgress}, delivered {Orders.Delivered} of {_savedDelivered}";
                }
                return Orders.Done == 1 ? null : "orders done " + Orders.Done;
            });
            Do("Escape", () =>
            {
                Tap(Key.Escape);
                Log($"the first order ({_first.Count} Scavengers for the {_first.Requester}): one carried to the office and sold with E, the rest built and shipped by a Golem Foundry fed by a golem; paid in {_first.RewardGood}; saved mid-build and loaded back");
                return null;
            });
        }

        // A crate holds 50 of a good, four Scavengers' worth: the depot is kept stocked, as a
        // cart or a line upstream would.
        private bool TopUpThen()
        {
            TopUp();
            return true;
        }

        private void TopUp()
        {
            int room = 50 - _depot.Buffer.GetQuantity(ItemType.Scrap);
            if (room >= 12)
            {
                Deposit(ItemType.Scrap, room);
                CrateTransfer.Put(Sandbox.Wallet.Pack, _depot, ItemType.Scrap, room);
            }
        }

        private T Place<T>(string key, CoreVector2Int cell) where T : class
        {
            PlaceableBuilding prefab = Sandbox.Placeables.Single(p => p.Key == key).Prefab;
            foreach (RecipeIngredient c in prefab.Cost)
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
