using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Contracts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Tests.EditMode;
using GolemFactory.World;
using GolemFactory.Tests.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;
using Vector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Tests.Contracts
{
    /// <summary>docs/golem-sales-design.md §0–§3: the City Office's golem orders in a composed world.</summary>
    public class GolemOrderTests
    {
        private const string Scavenger = "ClockworkScavenger";
        private const string Presser = "BrassPresser";

        private SandboxWorld _world;
        private readonly List<ContractEvent> _events = new List<ContractEvent>();

        [SetUp]
        public void SetUp()
        {
            _world = CrateWorld.Compose();
            _events.Clear();
            EventBus.Contract += Heard;
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Contract -= Heard;
            _world.Dispose();
        }

        private void Heard(ContractEvent e) => _events.Add(e);

        private GolemOrderBoard Orders => _world.GolemOrders;

        private void Frame() => _world.Advance(0.1f);

        private void OpenOffice()
        {
            foreach (string good in _world.Contracts.Sequence[0].postWhenSeen)
            {
                _world.TechTree.Ledger.RecordItem(good);
            }
            _world.TechTree.Ledger.RecordItem(ItemType.Gear);
            Frame();
            Assert.IsTrue(_world.Contracts.IsOpen, "precondition: the office is open");
        }

        private GolemEntity Build(string chassis)
        {
            ChassisDefinition c = _world.Definitions.Chassis[chassis];
            foreach (RecipeIngredient line in c.cost)
            {
                CrateWorld.Give(_world, line.itemType, line.quantity);
            }
            Assert.IsTrue(_world.StarterStation.TryConstructGolem(c, out GolemEntity golem), "built a " + chassis);
            _world.TechTree.Ledger.RecordChassis(chassis);
            return golem;
        }

        private void StandAt(Vector2Int cell)
        {
            _world.Interactor.Position = new Vector3(cell.x, cell.y, 0f);
            _world.Interactor.RefreshInteractables();
            _world.Interactor.RefreshAffordance();
        }

        private void CarryToOffice(GolemEntity golem)
        {
            StandAt(golem.Cell);
            Assert.IsTrue(_world.Interactor.ToggleCarryGolem(), "picked up");
            StandAt(TownSquare.OfficeCell + new Vector2Int(0, -1));
            Assert.AreEqual(InteractionKind.Office, _world.Interactor.CurrentPick.Kind, "precondition: at the office");
        }

        // --- When orders come (§1, §7 Q2: with the office) --------------------------------------

        [Test]
        public void NoOrder_UntilTheOfficeOpens_ThenScavengers()
        {
            Build(Scavenger);
            _world.TechTree.Ledger.RecordItem(ItemType.Gear);
            Assert.IsNotNull(Orders.Generate(0), "precondition: an order could be named, so only the office waits");
            Frame();
            Assert.IsNull(Orders.Current, "the office is roped off");

            OpenOffice();
            Assert.IsNotNull(Orders.Current);
            Assert.AreEqual(Scavenger, Orders.Current.Chassis);
            Assert.AreEqual(ContractEventKind.OrderPosted, _events.Single(e => e.Kind == ContractEventKind.OrderPosted).Kind);
        }

        [Test]
        public void Orders_RotateThroughTheChassisBuilt_CheapestFirst_SizedByTheirBudget()
        {
            Build(Presser);
            Build(Scavenger);
            _world.TechTree.Ledger.RecordItem(ItemType.Gear);
            CollectionAssert.AreEqual(new[] { Scavenger, Presser }, Orders.Rotation());

            GolemOrder first = Orders.Generate(0);
            Assert.AreEqual(Scavenger, first.Chassis);
            Assert.AreEqual(Orders.BudgetRaw(0) / 12, first.Count, "80 raw goods buys six 12-raw Scavengers");

            GolemOrder second = Orders.Generate(1);
            Assert.AreEqual(Presser, second.Chassis);
            Assert.AreEqual(2, second.Count, "160 raw goods buys two 80-raw Pressers");

            GolemOrder late = Orders.Generate(40);
            Assert.AreEqual(Scavenger, late.Chassis);
            Assert.AreEqual(Orders.Setup.maxCount, late.Count, "never more than maxCount");
            Assert.AreEqual(Orders.Setup.plateauRaw, Orders.BudgetRaw(40), "the budget levels off");
        }

        [Test]
        public void AnOrder_NamesItsRequester_AndTheGolems_InItsWords()
        {
            Build(Scavenger);
            OpenOffice();
            GolemOrder order = Orders.Current;
            Assert.AreEqual(Orders.Setup.requesters[0].name, order.Requester);
            StringAssert.Contains(GolemOrderText.Golems(Scavenger, order.Count), order.Words);
            StringAssert.DoesNotContain("{", order.Words);
            Assert.IsNull(GolemFactory.UI.PlayerText.RawIdIn(order.Words));
        }

        // --- By hand (§2.1) ----------------------------------------------------------------------

        [Test]
        public void E_AtTheOffice_WithTheOrderedGolem_SellsIt_AndGivesItsCargoBack()
        {
            GolemEntity golem = Build(Scavenger);
            OpenOffice();
            golem.Inventory.Input.Add(ItemType.Scrap, 3);
            int scrapBefore = CrateWorld.Pack(_world, ItemType.Scrap);

            CarryToOffice(golem);
            StringAssert.StartsWith("[E]  Sell Scavenger #", _world.Interactor.CurrentPrompt);
            StringAssert.Contains("to the " + Orders.Current.Requester + " · 0 of " + Orders.Current.Count, _world.Interactor.CurrentPrompt);

            Assert.IsTrue(_world.Interactor.Interact());
            Assert.IsTrue(golem.IsRemoved, "gone for good");
            Assert.IsFalse(_world.Golems.Contains(golem));
            Assert.IsNull(_world.Interactor.CarriedGolem, "the hands are empty");
            Assert.AreEqual(1, Orders.Delivered);
            Assert.AreEqual(1, Orders.GolemsSold);
            Assert.AreEqual(scrapBefore + 3, CrateWorld.Pack(_world, ItemType.Scrap), "its cargo, never its chassis");
        }

        [Test]
        public void AGolemOfAnotherChassis_IsNotSold_AndEOpensTheNotice()
        {
            GolemEntity scavenger = Build(Scavenger);
            scavenger.SetPlacement(scavenger.Cell + new Vector2Int(6, 0), scavenger.Facing); // out of reach
            OpenOffice();
            GolemEntity presser = Build(Presser);
            Assert.AreEqual(Scavenger, Orders.Current.Chassis, "precondition");

            CarryToOffice(presser);
            Assert.IsFalse(_world.Interactor.OfficeWouldSell);
            StringAssert.DoesNotContain("Sell", _world.Interactor.CurrentPrompt);
            _world.Interactor.Interact();
            Assert.IsFalse(presser.IsRemoved);
            Assert.AreSame(presser, _world.Interactor.CarriedGolem);

            GolemSaleResult refused = _world.SellGolem(presser);
            Assert.IsFalse(refused.Done);
            Assert.AreEqual("The order is for Clockwork Scavengers.", refused.Refusal);
            Assert.AreEqual(0, Orders.Delivered);
        }

        [Test]
        public void ASale_WhoseCargoThePackCantHold_IsRefused_AndNothingMoves()
        {
            GolemEntity golem = Build(Scavenger);
            OpenOffice();
            golem.Inventory.Input.Add(ItemType.Scrap, 5);
            CrateWorld.Give(_world, ItemType.Scrap, 1000);

            GolemSaleResult sale = _world.SellGolem(golem);
            Assert.IsFalse(sale.Done);
            Assert.AreEqual("Not sold: pack full of Scrap.", sale.Refusal);
            Assert.IsFalse(golem.IsRemoved);
            Assert.AreEqual(0, Orders.Delivered);
        }

        // --- The reward (§3) ---------------------------------------------------------------------

        [Test]
        public void TheLastGolem_CompletesTheOrder_PaysHalfTheWorth_AndTheNextPosts()
        {
            Build(Scavenger);
            OpenOffice();
            GolemOrder order = Orders.Current;
            int gearBefore = CrateWorld.Pack(_world, order.RewardGood);
            for (int i = 0; i < order.Count; i++)
            {
                Assert.IsTrue(_world.SellGolem(Build(Scavenger)).Done, "golem " + i);
            }

            Assert.AreEqual(1, Orders.Done);
            int paid = CrateWorld.Pack(_world, order.RewardGood) - gearBefore;
            double worth = 12.0 * order.Count;
            Assert.AreEqual((int)(worth * 0.5 / Orders.Raw(order.RewardGood)), paid);
            Assert.LessOrEqual(paid * Orders.Raw(order.RewardGood), worth, "never worth more than the golems");
            Assert.IsNotNull(Orders.Current, "the next order posts");
            Assert.AreNotSame(order, Orders.Current);
            Assert.AreEqual(1, _events.Count(e => e.Kind == ContractEventKind.OrderCompleted));
        }

        [Test]
        public void AGolemWithCards_IsWorthATenthMore_PerFilledSlot()
        {
            Assert.AreEqual(12.0, Orders.Worth(Scavenger, 0), 1e-9);
            Assert.AreEqual(14.4, Orders.Worth(Scavenger, 2), 1e-9);
            Assert.AreEqual(645.0 * 1.6, Orders.Worth("ZeppelinFreightLoader", 6), 1e-9);
        }

        [Test]
        public void NoReward_IsEverWorthMoreThanTheGolems_ForAnyOrder()
        {
            foreach (string chassis in _world.Definitions.Chassis.Keys)
            {
                _world.TechTree.Ledger.RecordChassis(chassis);
            }
            foreach (string good in Orders.Setup.rewardGoods)
            {
                _world.TechTree.Ledger.RecordItem(good);
            }
            for (int k = 0; k < 60; k++)
            {
                GolemOrder order = Orders.Generate(k);
                double bare = Orders.ChassisRaw(order.Chassis) * order.Count;
                double full = Orders.Worth(order.Chassis, _world.Definitions.Chassis[order.Chassis].maxAppendageSlots) * order.Count;
                foreach (double worth in new[] { bare, full })
                {
                    double paid = Orders.RewardFor(order, worth).Sum(g => g.quantity * Orders.Raw(g.itemType));
                    Assert.LessOrEqual(paid, worth, "order " + k);
                    Assert.Greater(paid, 0.0, "order " + k + " pays something");
                }
            }
        }

        [Test]
        public void ARewardThePackCantHold_WaitsOnTheOfficesShelf()
        {
            Build(Scavenger);
            OpenOffice();
            GolemOrder order = Orders.Current;
            CrateWorld.Give(_world, order.RewardGood, 1000);
            for (int i = 0; i < order.Count; i++)
            {
                _world.SellGolem(Build(Scavenger));
            }
            Assert.AreEqual(1, Orders.Done);
            RecipeIngredient shelved = _world.Contracts.ShelfGoods.Single();
            Assert.AreEqual(order.RewardGood, shelved.itemType);
            Assert.AreEqual(Orders.RewardFor(order, 12.0 * order.Count).Single().quantity, shelved.quantity);
        }

        // --- Saves, Creative, text (§0) ----------------------------------------------------------

        [Test]
        public void ASave_RestoresTheOrder_ItsCount_AndTheGolemsSold()
        {
            Build(Scavenger);
            OpenOffice();
            _world.SellGolem(Build(Scavenger));
            _world.SellGolem(Build(Scavenger));
            GolemOrder order = Orders.Current;
            string path = Path.Combine(Path.GetTempPath(), "golem-orders-" + System.Guid.NewGuid() + ".json");
            try
            {
                _world.SaveTo(path);
                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    GolemOrderBoard board = loaded.GolemOrders;
                    Assert.AreEqual(order.Chassis, board.Current.Chassis);
                    Assert.AreEqual(order.Count, board.Current.Count);
                    Assert.AreEqual(order.RewardGood, board.Current.RewardGood);
                    Assert.AreEqual(order.Words, board.Current.Words);
                    Assert.AreEqual(2, board.Delivered);
                    Assert.AreEqual(2, board.GolemsSold);
                    Assert.AreEqual(24.0, board.WorthDelivered, 1e-9);
                }
                finally
                {
                    loaded.Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void AnOldSave_StartsAtTheFirstOrder_Quietly()
        {
            Build(Scavenger);
            OpenOffice();
            _events.Clear();
            Orders.Restore(null);
            Assert.AreEqual(0, Orders.Done);
            Assert.AreEqual(Scavenger, Orders.Current.Chassis);
            Assert.IsEmpty(_events, "a load isn't the city posting work");
        }

        [Test]
        public void ACreativeWorld_HasNoOrders()
        {
            SandboxWorld creative = CreativeTests.Compose(creative: true);
            try
            {
                creative.Advance(0.1f);
                Assert.IsFalse(creative.GolemOrders.Enabled);
                Assert.IsNull(creative.GolemOrders.Current);
            }
            finally
            {
                creative.Dispose();
            }
        }

        [Test]
        public void TheRunTotals_CountGolemsSold_OnceTheOfficeIsOpen()
        {
            Build(Scavenger);
            StringAssert.DoesNotContain("sold", _world.RunTotalsLine);
            OpenOffice();
            _world.SellGolem(Build(Scavenger));
            StringAssert.EndsWith(" · 1 golem sold", _world.RunTotalsLine);
        }
    }
}
