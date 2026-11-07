using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Save;
using GolemFactory.Supply;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// A save from before supply carts (docs/supply-design.md §5): its nine stalls come back as
    /// carts at rung 0 on their places, each with the stall's stock and any truckload it had on
    /// the road, unclamped. The game says so once. A save with carts, or with neither, isn't touched.
    /// </summary>
    public class SupplyMigrationTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "golem-factory-supply-migration-" + System.Guid.NewGuid() + ".json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static readonly Vector2Int ScrapHome = new Vector2Int(-8, -16);
        private static readonly Vector2Int CoalHome = new Vector2Int(-4, -16);

        /// <summary>A save as the truckload market wrote it: no carts, and the stalls by id.</summary>
        private void WriteOldSave(SandboxWorld world, MarketEntry market)
        {
            world.SaveTo(_path);
            SaveData data = SaveFileIO.ReadFromFile(_path);
            data.supply = null;
            data.market = market;
            SaveFileIO.WriteToFile(data, _path);
        }

        private static MarketEntry NineStalls()
        {
            var market = new MarketEntry();
            foreach (SandboxSetup.LegacyStall stall in World.SandboxSetupTests.LoadReal().supply.legacyStalls)
            {
                market.stallIds.Add(stall.id);
                market.stallStocks.Add(0);
            }
            return market;
        }

        [Test]
        public void TheData_NamesTheNineOldStalls_OnSpots()
        {
            SandboxSetup setup = World.SandboxSetupTests.LoadReal();
            CollectionAssert.AreEquivalent(
                new[] { "ScrapNodeWest", "CoalNodeWest", "ScrapNode", "CoalNode", "CopperOreNode", "ZincOreNode", "AetherNode", "CopperOreNodeEast", "ZincOreNodeEast" },
                setup.supply.legacyStalls.Select(s => s.id), "the ids old saves name, never renamed");
            foreach (SandboxSetup.LegacyStall stall in setup.supply.legacyStalls)
            {
                Assert.IsTrue(setup.supply.spots.Any(s => s.x == stall.x && s.y == stall.y), stall.id + " has a spot");
                Assert.IsTrue(setup.supply.carts.Any(c => c.itemType == stall.itemType), stall.id + "'s good has a cart");
            }
        }

        [Test]
        public void AnOldSave_LoadsNineCarts_WithTheirStock_AndTheTruckloadsOnTheRoad()
        {
            SandboxWorld world = Compose();
            MarketEntry market = NineStalls();
            market.stallStocks[market.stallIds.IndexOf("ScrapNode")] = 37;
            market.stallStocks[market.stallIds.IndexOf("CoalNode")] = 90;
            // A Coal truckload on the road arrives into the Coal cart: 90 + 120 is past its 100.
            market.cartStallIds.Add("CoalNode");
            market.cartQuantities.Add(120);
            market.cartTicksLeft.Add(40);
            market.cartStallIds.Add("ZincOreNodeEast");
            market.cartQuantities.Add(120);
            market.cartTicksLeft.Add(10);
            WriteOldSave(world, market);

            SandboxWorld fresh = Compose();
            string status = fresh.LoadFrom(_path);

            List<SupplyCart> carts = fresh.Supply.Carts.ToList();
            Assert.AreEqual(9, carts.Count, "one cart on each old stall's place");
            foreach (SandboxSetup.LegacyStall stall in fresh.Setup.supply.legacyStalls)
            {
                SupplyCart cart = fresh.Supply.SpotAt(new Vector2Int(stall.x, stall.y)).Cart;
                Assert.IsNotNull(cart, stall.id);
                Assert.AreEqual(stall.itemType, cart.ItemType, stall.id);
                Assert.AreEqual(0, cart.RateRung, stall.id);
                Assert.AreEqual(0, cart.CapacityRung, stall.id);
            }
            Assert.AreEqual(37, fresh.Supply.SpotAt(ScrapHome).Cart.Stock);
            Assert.AreEqual(210, fresh.Supply.SpotAt(CoalHome).Cart.Stock, "a load never clamps");
            Assert.AreEqual(120, fresh.Supply.SpotAt(new Vector2Int(16, -16)).Cart.Stock);
            Assert.AreEqual(0, fresh.Supply.SpotAt(new Vector2Int(0, -16)).Cart.Stock);
            StringAssert.Contains(SupplyMigration.Notice, status, "said once, in the load's line");
        }

        [Test]
        public void AMigratedCart_OverCapacity_HoldsUntilBelowIt()
        {
            SandboxWorld world = Compose();
            MarketEntry market = NineStalls();
            market.stallStocks[market.stallIds.IndexOf("CoalNode")] = 150;
            WriteOldSave(world, market);
            SandboxWorld fresh = Compose();
            fresh.LoadFrom(_path);
            SupplyCart coal = fresh.Supply.SpotAt(CoalHome).Cart;

            fresh.Supply.Tick(0);
            fresh.Supply.Tick(1);
            Assert.AreEqual(150, coal.Stock, "above capacity, it doesn't trickle");
            Assert.AreEqual(0, coal.Accumulator);
        }

        [Test]
        public void AMigratedCart_RefundsNothing_ButItsStock()
        {
            SandboxWorld world = Compose();
            MarketEntry market = NineStalls();
            market.stallStocks[market.stallIds.IndexOf("CoalNode")] = 30;
            WriteOldSave(world, market);
            SandboxWorld fresh = Compose();
            fresh.LoadFrom(_path);
            foreach (string good in fresh.Wallet.Pack.Quantities.Keys.ToList())
            {
                fresh.Wallet.Pack.TryWithdraw(good, fresh.Wallet.Pack.GetQuantity(good));
            }

            Assert.IsTrue(fresh.Supply.Remove(fresh.Supply.SpotAt(CoalHome)).Done);
            Assert.AreEqual(30, Pack(fresh, ItemType.Coal), "its stock comes back");
            Assert.AreEqual(0, Pack(fresh, ItemType.Scrap), "it was never paid for, so half of nothing");
        }

        [Test]
        public void AMigratedStreet_StillKeepsAScrapCart()
        {
            SandboxWorld world = Compose();
            WriteOldSave(world, NineStalls());
            SandboxWorld fresh = Compose();
            fresh.LoadFrom(_path);

            // Two Scrap carts came over: one may go, the last may not (invariant 9).
            Assert.IsTrue(fresh.Supply.Remove(fresh.Supply.SpotAt(new Vector2Int(-16, -16))).Done);
            Assert.IsFalse(fresh.Supply.Remove(fresh.Supply.SpotAt(ScrapHome)).Done);
        }

        [Test]
        public void ASaveWithCarts_IsNotMigrated_AndSaysNothing()
        {
            SandboxWorld world = Compose();
            Assert.IsTrue(world.GrantCart(CoalHome, ItemType.Coal).Done);
            world.SaveTo(_path);
            Assert.IsNull(SaveFileIO.ReadFromFile(_path).market, "the market is never written now");

            SandboxWorld fresh = Compose();
            string status = fresh.LoadFrom(_path);
            Assert.AreEqual(2, fresh.Supply.Carts.Count(), "the Scrap cart and the Coal cart, as saved");
            StringAssert.DoesNotContain(SupplyMigration.Notice, status);
        }

        [Test]
        public void ASaveWithNeitherCartsNorStalls_StartsTheStreetAsANewGame()
        {
            SandboxWorld world = Compose();
            WriteOldSave(world, null);
            SandboxWorld fresh = Compose();
            Assert.IsTrue(fresh.GrantCart(CoalHome, ItemType.Coal).Done);
            string status = fresh.LoadFrom(_path);

            Assert.AreEqual(1, fresh.Supply.Carts.Count());
            Assert.AreEqual(ItemType.Scrap, fresh.Supply.SpotAt(ScrapHome).Cart.ItemType);
            StringAssert.DoesNotContain(SupplyMigration.Notice, status);
        }
    }
}
