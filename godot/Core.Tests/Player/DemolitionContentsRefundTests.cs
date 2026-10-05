using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Steam;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.Player
{
    /// <summary>
    /// A demolished building hands back what it holds as well as what it cost (G10, from
    /// playtest: "the coke is lost if you demolish the boiler"), on the same argument that
    /// refunds a dismantled golem's cargo. Plus the boiler's "how long does it last" readout.
    /// </summary>
    public class DemolitionContentsRefundTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 30);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.IronPlate, 10);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Coke, 20);
            return world;
        }

        private static PlaceableBuilding PlaceBoiler(SandboxWorld world, Vector2Int cell)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "BoilerPrefab").Prefab);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell);
        }

        private static void Demolish(SandboxWorld world, Vector2Int cell)
        {
            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
        }

        private static int Stock(SandboxWorld world, string item) => world.Buffers.GetQuantity(world.StockpileBufferId, item);

        [Test]
        public void DemolishingAFuelledBoiler_HandsBackItsCoke_AsWellAsItsCost()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(4, 4);
            PlaceableBuilding boiler = PlaceBoiler(world, cell);
            Assert.IsTrue(world.Interactor.TryRefuelBoiler(boiler.GetPart<PlaceableBoiler>()));
            Assert.AreEqual(0, Stock(world, ItemType.Coke), "precondition: all 20 Coke in the firebox");

            Demolish(world, cell);

            Assert.IsTrue(boiler.IsRemoved);
            Assert.AreEqual(20, Stock(world, ItemType.Coke), "the Coke came back");
            Assert.AreEqual(30, Stock(world, ItemType.Scrap));
            Assert.AreEqual(10, Stock(world, ItemType.IronPlate));
        }

        [Test]
        public void TheRefundBundle_AddsContentsToTheCost()
        {
            SandboxWorld world = Compose();
            PlaceableBuilding boiler = PlaceBoiler(world, new Vector2Int(4, 4));
            world.Interactor.TryRefuelBoiler(boiler.GetPart<PlaceableBoiler>());

            string bundle = string.Join(" ", GolemFactory.Player.BuildModeController.RefundFor(boiler).Select(i => i.itemType + ":" + i.quantity));

            Assert.AreEqual("Scrap:30 IronPlate:10 Coke:20", bundle);
        }

        [Test]
        public void TheBoilerCaption_SaysHowLongItsCokeLasts()
        {
            Assert.AreEqual("18 Coke · 2 golems working · 3:00 left", SteamGaugeUtility.FormatBoiler(18, 2));
            Assert.AreEqual("18 Coke · no golems working", SteamGaugeUtility.FormatBoiler(18, 0));
            Assert.AreEqual("6:40", SteamGaugeUtility.FormatLastsOneGolem(20));
        }
    }
}
