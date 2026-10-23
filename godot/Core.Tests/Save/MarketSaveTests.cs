using System.IO;
using GolemFactory.Economy;
using GolemFactory.Save;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The market street in a save (Phase 3 critique, tuning-data-spec §19): a stall's stock and
    /// the truckloads on the road come back with the pack that paid for them. Before, neither was
    /// saved: a cart paid for before a save vanished on a fresh launch, and a cart ordered after a
    /// save still arrived on a load that handed its price back.
    /// </summary>
    public class MarketSaveTests
    {
        private const string Coal = "CoalNode";
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "golem-factory-market-" + System.Guid.NewGuid() + ".json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static int Stall(SandboxWorld world, string id) =>
            world.Nodes.TryGetNode(id, out ResourceNode node) ? node.RemainingQuantity : -99;

        private static MarketOrderResult Order(SandboxWorld world, string stall) =>
            world.Market.TryOrder(stall, world.Buffers, world.StockpileBufferId, world.Clock.CurrentTick, SandboxWorld.PostBufferId);

        private static void Tick(SandboxWorld world, int ticks)
        {
            world.Clock.Play();
            for (int i = 0; i < ticks; i++)
            {
                world.Clock.Advance(1f / world.Clock.TicksPerSecond);
            }
        }

        private static MarketOffer Offer(SandboxWorld world, string stall)
        {
            Assert.IsTrue(world.Market.TryGetOffer(stall, out MarketOffer offer));
            return offer;
        }

        [Test]
        public void ATruckloadOnTheRoad_AtASave_ArrivesAfterLoadingIt_InAFreshGame()
        {
            SandboxWorld world = Compose();
            MarketOffer offer = Offer(world, Coal);
            Give(world, ItemType.Scrap, 100);
            Assert.AreEqual(MarketOrderResult.Ordered, Order(world, Coal));
            Tick(world, 10);
            int stockBefore = Stall(world, Coal);
            world.SaveTo(_path);

            SandboxWorld fresh = Compose();
            fresh.LoadFrom(_path);
            Assert.IsTrue(fresh.Market.IsInTransit(Coal), "the cart is still on the road");
            Assert.AreEqual(offer.DeliveryTicks - 10, fresh.Market.TicksUntilDelivery(Coal, fresh.Clock.CurrentTick),
                "with the ticks it had left");

            Tick(fresh, offer.DeliveryTicks);
            Assert.IsFalse(fresh.Market.IsInTransit(Coal));
            Assert.AreEqual(stockBefore + offer.TruckloadSize, Stall(fresh, Coal), "the paid-for truckload arrives");
        }

        [Test]
        public void ATruckloadOrderedAfterTheSave_DoesNotArrive_OnALoadThatRefundsItsPrice()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 100);
            int scrap = Pack(world, ItemType.Scrap);
            int stall = Stall(world, Coal);
            world.SaveTo(_path);

            Assert.AreEqual(MarketOrderResult.Ordered, Order(world, Coal));
            world.LoadFrom(_path);

            Assert.AreEqual(scrap, Pack(world, ItemType.Scrap), "the price is back in the pack");
            Assert.IsFalse(world.Market.IsInTransit(Coal), "so the cart is not on the road");
            Tick(world, Offer(world, Coal).DeliveryTicks + 1);
            Assert.AreEqual(stall, Stall(world, Coal), "and no Coal arrives: no duplicate");
        }

        [Test]
        public void AStallsStock_ComesBackAsSaved()
        {
            SandboxWorld world = Compose();
            MarketOffer offer = Offer(world, Coal);
            Give(world, ItemType.Scrap, 100);
            Order(world, Coal);
            Tick(world, offer.DeliveryTicks + 1);
            Assert.AreEqual(offer.TruckloadSize, Stall(world, Coal), "precondition: the stall is stocked");
            int scrapStall = Stall(world, "ScrapNode");
            world.SaveTo(_path);

            // A fresh game's Coal stall is empty; a load stocks it as the save left it.
            SandboxWorld fresh = Compose();
            Assert.AreEqual(0, Stall(fresh, Coal));
            fresh.LoadFrom(_path);
            Assert.AreEqual(offer.TruckloadSize, Stall(fresh, Coal));
            Assert.AreEqual(scrapStall, Stall(fresh, "ScrapNode"));

            // And in the same game, stock taken after the save goes back.
            while (world.Nodes.TryExtract(Coal, out _) && Stall(world, Coal) > 50)
            {
            }
            world.LoadFrom(_path);
            Assert.AreEqual(offer.TruckloadSize, Stall(world, Coal));
        }

        [Test]
        public void ASaveFromBeforeTheMarketWasSaved_LeavesTheStreetAsItIs()
        {
            SandboxWorld world = Compose();
            MarketOffer offer = Offer(world, Coal);
            world.SaveTo(_path);
            SaveData data = SaveFileIO.ReadFromFile(_path);
            data.market = null;
            SaveFileIO.WriteToFile(data, _path);

            Give(world, ItemType.Scrap, 100);
            Order(world, Coal);
            world.LoadFrom(_path);
            Assert.IsTrue(world.Market.IsInTransit(Coal), "an older save says nothing about the street");
            Tick(world, offer.DeliveryTicks + 1);
            Assert.AreEqual(offer.TruckloadSize, Stall(world, Coal));
        }
    }
}
