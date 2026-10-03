using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §13.2: a raw good is bought and arrives as a truckload, with Creative Mode bypassing the
    /// whole economy back to infinite, free and steady.
    /// </summary>
    public class TruckloadMarketTests
    {
        private const string Stall = "CopperOreNode";
        private const string Wallet = "FactoryStockpile";

        private static List<RecipeIngredient> Price(int scrap) =>
            new List<RecipeIngredient> { new RecipeIngredient(ItemType.Scrap, scrap) };

        private static (TruckloadMarket market, ResourceNodeRegistry nodes, StorageBufferRegistry buffers)
            Build(bool creative, int startingStock = 0, int price = 20, int truckload = 40, int ticks = 50)
        {
            var nodes = new ResourceNodeRegistry();
            nodes.Register(new ResourceNode(Stall, ItemType.CopperOre, startingStock));

            var market = new TruckloadMarket(nodes, new GameMode(creative));
            market.AddOffer(new MarketOffer(Stall, Price(price), truckload, ticks));

            var buffers = new StorageBufferRegistry();
            return (market, nodes, buffers);
        }

        [Test]
        public void AnOrder_ChargesThePriceAndDeliversNothingYet()
        {
            (TruckloadMarket market, ResourceNodeRegistry nodes, StorageBufferRegistry buffers) =
                Build(creative: false);
            buffers.Deposit(Wallet, ItemType.Scrap, 20);

            Assert.AreEqual(MarketOrderResult.Ordered, market.TryOrder(Stall, buffers, Wallet, 0));

            Assert.AreEqual(0, buffers.GetOrCreate(Wallet).GetQuantity(ItemType.Scrap));
            nodes.TryGetNode(Stall, out ResourceNode node);
            Assert.AreEqual(0, node.RemainingQuantity, "the cart is still on the road");
            Assert.AreEqual(1, market.InTransitCount);
        }

        [Test]
        public void TheTruckloadArrivesInOneLump_NotATrickle()
        {
            // The burst is the design point: a lump needs storage, a trickle does not, and
            // buffer chests and accumulator lines exist to smooth one into the other.
            (TruckloadMarket market, ResourceNodeRegistry nodes, StorageBufferRegistry buffers) =
                Build(creative: false, ticks: 50, truckload: 40);
            buffers.Deposit(Wallet, ItemType.Scrap, 20);
            market.TryOrder(Stall, buffers, Wallet, 0);

            nodes.TryGetNode(Stall, out ResourceNode node);
            for (long tick = 0; tick < 50; tick++)
            {
                market.Tick(tick);
                Assert.AreEqual(0, node.RemainingQuantity, "nothing arrives early, at tick " + tick);
            }

            market.Tick(50);

            Assert.AreEqual(40, node.RemainingQuantity);
            Assert.AreEqual(0, market.InTransitCount);
        }

        [Test]
        public void ARefusedOrder_ChargesNothing()
        {
            (TruckloadMarket market, _, StorageBufferRegistry buffers) = Build(creative: false, price: 20);
            buffers.Deposit(Wallet, ItemType.Scrap, 19);

            Assert.AreEqual(MarketOrderResult.CannotAfford, market.TryOrder(Stall, buffers, Wallet, 0));

            Assert.AreEqual(19, buffers.GetOrCreate(Wallet).GetQuantity(ItemType.Scrap),
                "an atomic bundle withdrawal refunds in full");
            Assert.AreEqual(0, market.InTransitCount);
        }

        [Test]
        public void OneCartPerStall()
        {
            // Queueing orders would let a player convert a wallet straight into a pipeline with
            // no storage, which is exactly the pressure the burst exists to create.
            (TruckloadMarket market, _, StorageBufferRegistry buffers) = Build(creative: false);
            buffers.Deposit(Wallet, ItemType.Scrap, 100);
            market.TryOrder(Stall, buffers, Wallet, 0);

            Assert.AreEqual(MarketOrderResult.AlreadyInTransit, market.TryOrder(Stall, buffers, Wallet, 1));
            Assert.AreEqual(1, market.InTransitCount);
        }

        [Test]
        public void OrderingAgainAfterDelivery_Works()
        {
            (TruckloadMarket market, _, StorageBufferRegistry buffers) = Build(creative: false, ticks: 10);
            buffers.Deposit(Wallet, ItemType.Scrap, 40);
            market.TryOrder(Stall, buffers, Wallet, 0);
            market.Tick(10);

            Assert.AreEqual(MarketOrderResult.Ordered, market.TryOrder(Stall, buffers, Wallet, 11));
        }

        [Test]
        public void AFreeStall_CostsNothingButStillDelivers()
        {
            // The Scrap stall. §10 forbids a soft-lock: if every stall cost Scrap, a player at
            // zero Scrap could buy nothing and make nothing.
            var nodes = new ResourceNodeRegistry();
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 0));
            var market = new TruckloadMarket(nodes, new GameMode(false));
            market.AddOffer(new MarketOffer("ScrapNode", new List<RecipeIngredient>(), 30, 5));
            var buffers = new StorageBufferRegistry();

            Assert.AreEqual(MarketOrderResult.Ordered, market.TryOrder("ScrapNode", buffers, Wallet, 0));
            market.Tick(5);

            nodes.TryGetNode("ScrapNode", out ResourceNode node);
            Assert.AreEqual(30, node.RemainingQuantity);
        }

        [Test]
        public void TicksUntilDelivery_IsWhatTheStallCanTellThePlayer()
        {
            (TruckloadMarket market, _, StorageBufferRegistry buffers) = Build(creative: false, ticks: 50);
            buffers.Deposit(Wallet, ItemType.Scrap, 20);

            Assert.AreEqual(-1, market.TicksUntilDelivery(Stall, 0), "nothing on the road yet");

            market.TryOrder(Stall, buffers, Wallet, 0);

            Assert.AreEqual(50, market.TicksUntilDelivery(Stall, 0));
            Assert.AreEqual(20, market.TicksUntilDelivery(Stall, 30));
        }

        // --- Creative Mode -------------------------------------------------------------------

        [Test]
        public void CreativeMode_LeavesTheStallInfiniteAndRefusesToChargeForIt()
        {
            (TruckloadMarket market, ResourceNodeRegistry nodes, StorageBufferRegistry buffers) =
                Build(creative: true, startingStock: ResourceNode.Infinite);
            buffers.Deposit(Wallet, ItemType.Scrap, 100);

            Assert.AreEqual(MarketOrderResult.NotNeeded, market.TryOrder(Stall, buffers, Wallet, 0));

            Assert.AreEqual(100, buffers.GetOrCreate(Wallet).GetQuantity(ItemType.Scrap),
                "creative mode must never take payment");
            nodes.TryGetNode(Stall, out ResourceNode node);
            Assert.AreEqual(ResourceNode.Infinite, node.RemainingQuantity);
            Assert.AreEqual(0, market.InTransitCount);
        }

        [Test]
        public void CreativeMode_StallNeverRunsDown()
        {
            // Byte for byte the boulder-era behaviour: extraction does not decrement an
            // infinite node, so the old steady stream is exactly what the player still gets.
            var nodes = new ResourceNodeRegistry();
            nodes.Register(new ResourceNode(Stall, ItemType.CopperOre, ResourceNode.Infinite));

            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(nodes.TryExtract(Stall, out _));
            }

            nodes.TryGetNode(Stall, out ResourceNode node);
            Assert.AreEqual(ResourceNode.Infinite, node.RemainingQuantity);
            Assert.IsFalse(node.IsDepleted);
        }

        [Test]
        public void DeliveringToAnInfiniteNode_IsANoOp()
        {
            // One code path for both modes: the market does not need a creative-only branch
            // around Deliver, because an infinite stall has nothing to top up.
            var node = new ResourceNode(Stall, ItemType.CopperOre, ResourceNode.Infinite);

            Assert.AreEqual(0, node.Deliver(40));
            Assert.AreEqual(ResourceNode.Infinite, node.RemainingQuantity);
        }

        [Test]
        public void AnUnknownStall_IsRefusedRatherThanCreated()
        {
            (TruckloadMarket market, _, StorageBufferRegistry buffers) = Build(creative: false);

            Assert.AreEqual(MarketOrderResult.NoSuchStall,
                market.TryOrder("NoSuchNode", buffers, Wallet, 0));
        }

        [Test]
        public void Clear_ForgetsCartsOnTheRoad_SoALoadCannotMintThem()
        {
            (TruckloadMarket market, ResourceNodeRegistry nodes, StorageBufferRegistry buffers) =
                Build(creative: false, ticks: 10);
            buffers.Deposit(Wallet, ItemType.Scrap, 20);
            market.TryOrder(Stall, buffers, Wallet, 0);

            market.Clear();
            market.Tick(100);

            nodes.TryGetNode(Stall, out ResourceNode node);
            Assert.AreEqual(0, node.RemainingQuantity);
            Assert.AreEqual(0, market.InTransitCount);
        }
    }
}
