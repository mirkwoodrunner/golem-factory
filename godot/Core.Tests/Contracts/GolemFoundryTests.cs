using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Contracts;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.EditMode;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.Contracts
{
    /// <summary>docs/golem-sales-design.md §2.2: the Golem Foundry in a composed world.</summary>
    public class GolemFoundryTests
    {
        private const string Scavenger = "ClockworkScavenger";
        private const string Presser = "BrassPresser";
        private static readonly Vector2Int FoundryCell = new Vector2Int(4, 6);

        private SandboxWorld _world;
        private readonly List<FoundryShippedEvent> _shipped = new List<FoundryShippedEvent>();

        [SetUp]
        public void SetUp()
        {
            _world = CrateWorld.Compose();
            _world.Clock.Play();
            _shipped.Clear();
            EventBus.FoundryShipped += Heard;
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.FoundryShipped -= Heard;
            _world.Dispose();
        }

        private void Heard(FoundryShippedEvent e) => _shipped.Add(e);

        private GolemOrderBoard Orders => _world.GolemOrders;

        private void Seconds(float s)
        {
            for (float t = 0f; t < s; t += 0.5f)
            {
                _world.Advance(0.5f);
            }
        }

        private void OpenOffice(params string[] chassis)
        {
            foreach (string good in _world.Contracts.Sequence[0].postWhenSeen)
            {
                _world.TechTree.Ledger.RecordItem(good);
            }
            _world.TechTree.Ledger.RecordItem(ItemType.Gear);
            foreach (string c in chassis)
            {
                _world.TechTree.Ledger.RecordChassis(c);
            }
            _world.Advance(0.1f);
            Assert.IsNotNull(Orders.Current, "precondition: an order is open");
        }

        private PlaceableGolemFoundry PlaceFoundry()
        {
            CrateWorld.Give(_world, ItemType.Scrap, 40);
            CrateWorld.Give(_world, ItemType.IronPlate, 20);
            CrateWorld.Give(_world, ItemType.Gear, 10);
            CrateWorld.Give(_world, ItemType.Slag, 10);
            PlaceableBuilding building = CrateWorld.Place(_world, "GolemFoundryPrefab", FoundryCell);
            PlaceableGolemFoundry foundry = building.GetPart<PlaceableGolemFoundry>();
            Assert.IsNotNull(foundry);
            return foundry;
        }

        private IItemEndpoint Intake()
        {
            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(FoundryCell, out IItemEndpoint endpoint));
            return endpoint;
        }

        private int Give(string item, int n)
        {
            int taken = 0;
            for (int i = 0; i < n; i++)
            {
                if (Intake().TryGive(new ItemStack { ItemType = item }))
                {
                    taken++;
                }
            }
            return taken;
        }

        private GolemEntity BuildByStation(string chassis)
        {
            ChassisDefinition c = _world.Definitions.Chassis[chassis];
            foreach (RecipeIngredient line in c.cost)
            {
                CrateWorld.Give(_world, line.itemType, line.quantity);
            }
            Assert.IsTrue(_world.StarterStation.TryConstructGolem(c, out GolemEntity golem));
            return golem;
        }

        [Test]
        public void WithNoOrder_ItTakesNothing_AndWaits()
        {
            PlaceableGolemFoundry foundry = PlaceFoundry();
            Assert.AreEqual(0, Give(ItemType.Scrap, 1));
            Assert.AreEqual(FoundryState.NoOrder, foundry.State);
            Assert.AreEqual("Waiting for a golem order at the City Office", foundry.CapLine);
            Assert.AreEqual("There is no golem order", foundry.RefusalFor(ItemType.Scrap));
        }

        [Test]
        public void ItTakes_OnlyTheOrderedChassissGoods_UpToOneCost()
        {
            OpenOffice(Scavenger);
            PlaceableGolemFoundry foundry = PlaceFoundry();
            Assert.AreEqual(0, Give(ItemType.Coal, 1), "a Scavenger takes no Coal");
            Assert.AreEqual("A Clockwork Scavenger takes Scrap", foundry.RefusalFor(ItemType.Coal));

            Assert.AreEqual(11, Give(ItemType.Scrap, 11));
            Assert.AreEqual(FoundryState.Short, foundry.State);
            Assert.AreEqual("Clockwork Scavenger · needs 1 Scrap", foundry.CapLine);
        }

        [Test]
        public void AWholeCost_IsSpent_Built_AndShipped_WithNoGolemInTheWorld()
        {
            OpenOffice(Scavenger);
            PlaceableGolemFoundry foundry = PlaceFoundry();
            int golemsBefore = _world.Golems.Count;
            var spent = new ConsumptionTally();
            spent.Attach();
            try
            {
                Assert.AreEqual(12, Give(ItemType.Scrap, 13), "one cost, not one more");
                Seconds(0.5f);
                Assert.AreEqual(FoundryState.Building, foundry.State);
                Assert.AreEqual(0, foundry.Buffer.GetQuantity(ItemType.Scrap), "spent");
                Assert.AreEqual(12, spent.Totals[ItemType.Scrap]);
                StringAssert.StartsWith("Building a Clockwork Scavenger · ", foundry.CapLine);

                Seconds(Orders.FoundrySeconds(Scavenger) + 1f);
                Assert.AreEqual(1, Orders.Delivered);
                Assert.AreEqual(1, Orders.GolemsSold);
                Assert.AreEqual(Scavenger, _shipped.Single().Chassis);
                Assert.AreEqual(FoundryCell, _shipped.Single().Cell);
                Assert.AreEqual(golemsBefore, _world.Golems.Count, "its golem never enters the world");
                Assert.AreEqual(FoundryState.Short, foundry.State, "on to the next");
            }
            finally
            {
                spent.Detach();
            }
        }

        [Test]
        public void ItsBuildTime_IsItsChassissTier()
        {
            Assert.AreEqual(20, Orders.FoundrySeconds(Scavenger));
            Assert.AreEqual(45, Orders.FoundrySeconds("MainspringOverclocker"));
            Assert.AreEqual(90, Orders.FoundrySeconds("ZeppelinFreightLoader"));
        }

        [Test]
        public void AGolemPastItsOrder_WaitsInTheFoundry_ForAnOrderOfItsKind()
        {
            BuildByStation(Presser);
            OpenOffice(Scavenger, Presser);
            Assert.AreEqual(Scavenger, Orders.Current.Chassis, "precondition");
            PlaceableGolemFoundry foundry = PlaceFoundry();
            Give(ItemType.Scrap, 12);
            Seconds(0.5f);
            Assert.AreEqual(FoundryState.Building, foundry.State);

            // The order is filled by hand meanwhile; the next is for Pressers.
            int left = Orders.Remaining;
            for (int i = 0; i < left; i++)
            {
                Assert.IsTrue(_world.SellGolem(BuildByStation(Scavenger)).Done);
            }
            Assert.AreEqual(Presser, Orders.Current.Chassis);

            Seconds(Orders.FoundrySeconds(Scavenger) + 1f);
            Assert.AreEqual(FoundryState.Holding, foundry.State);
            Assert.AreEqual("A Clockwork Scavenger waits for an order of its kind", foundry.CapLine);
            Assert.AreEqual(0, Orders.Delivered, "a Scavenger doesn't count for Pressers");
            Assert.IsEmpty(_shipped);
        }

        [Test]
        public void Demolishing_HandsBackItsPrice_ItsIntake_AndTheGolemUnderWay()
        {
            OpenOffice(Scavenger);
            PlaceableGolemFoundry foundry = PlaceFoundry();
            Give(ItemType.Scrap, 12);
            Seconds(0.5f);
            Assert.AreEqual(FoundryState.Building, foundry.State);
            Give(ItemType.Scrap, 5);
            int scrap = CrateWorld.Pack(_world, ItemType.Scrap);
            int plate = CrateWorld.Pack(_world, ItemType.IronPlate);

            _world.Build.EnterDemolishMode();
            _world.Build.PlaceOrRemove(FoundryCell);
            _world.Build.CancelPlacement();

            Assert.AreEqual(scrap + 40 + 5 + 12, CrateWorld.Pack(_world, ItemType.Scrap), "price, intake and the Scavenger under way");
            Assert.AreEqual(plate + 20, CrateWorld.Pack(_world, ItemType.IronPlate));
            Assert.IsFalse(_world.Endpoints.TryGetEndpoint(FoundryCell, out _), "its tile no longer takes goods");
        }

        [Test]
        public void ASave_KeepsItsIntake_AndItsBuild()
        {
            OpenOffice(Scavenger);
            PlaceableGolemFoundry foundry = PlaceFoundry();
            Give(ItemType.Scrap, 12);
            Seconds(0.5f);
            Seconds(5f);
            Give(ItemType.Scrap, 4);
            long progress = foundry.ProgressTicks;
            Assert.Greater(progress, 0L);
            string path = Path.Combine(Path.GetTempPath(), "golem-foundry-" + System.Guid.NewGuid() + ".json");
            try
            {
                _world.SaveTo(path);
                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    PlaceableGolemFoundry back = loaded.Build.Buildings
                        .Select(b => b.GetPart<PlaceableGolemFoundry>()).Single(f => f != null);
                    Assert.AreEqual(Scavenger, back.Chassis);
                    Assert.AreEqual(progress, back.ProgressTicks);
                    Assert.IsFalse(back.Holding);
                    Assert.AreEqual(4, back.Buffer.GetQuantity(ItemType.Scrap));
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
        public void ItsPrice_FitsAChapterFivePack_AndOpensWithTheOffice()
        {
            PlaceableEntry entry = _world.Placeables.Single(p => p.Key == "GolemFoundryPrefab");
            foreach (RecipeIngredient line in entry.Prefab.Cost)
            {
                Assert.LessOrEqual(line.quantity, 100, line.itemType + ": the starting pack holds 100 of each");
                Assert.That(new[] { ItemType.Scrap, ItemType.IronPlate, ItemType.Gear, ItemType.Slag }, Does.Contain(line.itemType),
                    "goods a chapter 5 factory makes");
            }
            // Slag is what opens the office (its first contract's condition), so the Foundry's
            // price reads "Requires: Slag" until there can be golem orders.
            Assert.That(entry.Prefab.Cost.Select(c => c.itemType), Does.Contain(ItemType.Slag));
            CollectionAssert.Contains(_world.Contracts.Sequence[0].postWhenSeen, ItemType.Slag);
        }
    }
}
