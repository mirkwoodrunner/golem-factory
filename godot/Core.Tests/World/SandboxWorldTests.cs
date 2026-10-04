using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    /// <summary>
    /// The composed Sandbox (G5): the real setup, the real build menu, wired the way
    /// SandboxBootstrap wired it -- so every placeable is exercised through the same
    /// BuildModeController the player's clicks reach.
    /// </summary>
    public class SandboxWorldTests
    {
        private static SandboxWorld Compose(bool creative = false)
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxSetup setup = SandboxSetupTests.LoadReal();
            setup.creativeMode = creative;
            return SandboxWorld.Compose(definitions, setup, PlaceableCatalogTests.LoadReal(definitions));
        }

        private static void Afford(SandboxWorld world, PlaceableBuilding prefab, int times = 1)
        {
            foreach (RecipeIngredient c in prefab.Cost)
            {
                world.Buffers.Deposit(world.StockpileBufferId, c.itemType, c.quantity * times);
            }
        }

        private static Dictionary<string, int> Stock(SandboxWorld world) =>
            new[] { ItemType.Scrap, ItemType.Brass, ItemType.IronPlate, ItemType.Casing }
                .ToDictionary(t => t, t => world.Buffers.GetQuantity(world.StockpileBufferId, t));

        [Test]
        public void TheBuildMenuIsTheCatalog()
        {
            SandboxWorld world = Compose();
            Assert.AreEqual(10, world.Build.AvailablePrefabs.Count);
        }

        [Test]
        public void EveryPlaceable_PlacesChargingItsCost_AndDemolishesWithAFullRefund()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(2, 2);

            foreach (PlaceableEntry entry in world.Placeables)
            {
                Afford(world, entry.Prefab);
                Dictionary<string, int> before = Stock(world);

                world.Build.SetActivePrefab(entry.Prefab);
                world.Build.PlaceOrRemove(cell);
                Assert.IsTrue(world.Grid.IsOccupied(cell), entry.Key + " placed");
                foreach (RecipeIngredient c in entry.Prefab.Cost)
                {
                    Assert.AreEqual(before[c.itemType] - c.quantity, Stock(world)[c.itemType], $"{entry.Key} charged {c.itemType}");
                }

                world.Build.EnterDemolishMode();
                world.Build.PlaceOrRemove(cell);
                Assert.IsFalse(world.Grid.IsOccupied(cell), entry.Key + " demolished");
                CollectionAssert.AreEqual(before, Stock(world), entry.Key + ": the refund is the whole cost");
            }
        }

        [Test]
        public void APlacedStation_IsWiredByTheWorld_AndBuildsGolems()
        {
            // The station case the late-wiring seam existed for: a station the player PAYS for
            // must build something, not stand there as a decorative box.
            SandboxWorld world = Compose();
            PlaceableEntry entry = world.Placeables.Single(p => p.Key == "GolemConstructionStationPrefab");
            Afford(world, entry.Prefab);
            world.Build.SetActivePrefab(entry.Prefab);
            var cell = new Vector2Int(-4, 0);
            world.Build.PlaceOrRemove(cell);

            var station = world.Build.Buildings.Single(b => b.Cell == cell).GetPart<GolemConstructionStation>();
            Assert.IsTrue(station.HasBuildRoster, "the template's roster was copied in");

            var spawned = new List<GolemEntity>();
            world.GolemSpawned += spawned.Add;
            ChassisDefinition scavenger = world.Definitions.Chassis["ClockworkScavenger"];
            foreach (RecipeIngredient c in scavenger.cost)
            {
                world.Buffers.Deposit(world.StockpileBufferId, c.itemType, c.quantity);
            }
            Assert.IsTrue(station.TryConstructGolem(scavenger, out GolemEntity golem));
            CollectionAssert.AreEqual(new[] { golem }, spawned);
            CollectionAssert.Contains(world.Golems, golem);
        }

        [Test]
        public void TheStarterStationBuildsEveryChassis()
        {
            SandboxWorld world = Compose();
            Assert.AreEqual(world.Definitions.Chassis.Count, world.StarterStation.ChassisRoster.Length);
            Assert.IsTrue(world.StarterStation.HasBuildRoster);
        }

        [Test]
        public void ABoilerAndPipesJoinTheSteamNetwork()
        {
            SandboxWorld world = Compose();
            PlaceableEntry boiler = world.Placeables.Single(p => p.Key == "BoilerPrefab");
            PlaceableEntry pipe = world.Placeables.Single(p => p.Key == "SteamPipePrefab");
            Afford(world, boiler.Prefab);
            Afford(world, pipe.Prefab, 3);

            world.Build.SetActivePrefab(boiler.Prefab);
            world.Build.PlaceOrRemove(new Vector2Int(0, 4));
            world.Build.SetActivePrefab(pipe.Prefab);
            world.Build.Click(new Vector2Int(1, 4), pointerOverUi: false);
            world.Build.Hover(new Vector2Int(3, 4));
            world.Build.Release();

            Assert.IsTrue(world.Steam.HasBoilerAt(new Vector2Int(0, 4)));
            for (int x = 1; x <= 3; x++)
            {
                Assert.IsTrue(world.Steam.HasPipe(new Vector2Int(x, 4)), $"pipe at x={x}");
            }
        }

        [Test]
        public void ADraggedBeltRun_PointsAlongItself()
        {
            SandboxWorld world = Compose();
            PlaceableEntry belt = world.Placeables.Single(p => p.Key == "BeltPrefab");
            Afford(world, belt.Prefab, 4);
            world.Build.SetActivePrefab(belt.Prefab);

            // The player's gesture: press (lays the anchor), move, release.
            world.Build.Click(new Vector2Int(0, -2), pointerOverUi: false);
            world.Build.Hover(new Vector2Int(3, -2));
            world.Build.Release();

            IEnumerable<PlaceableBuilding> run = world.Build.Buildings.Where(b => b.GetPart<PlaceableBelt>() != null);
            Assert.AreEqual(4, run.Count());
            Assert.That(run.Where(b => b.Cell.x < 3).All(b => b.GetPart<PlaceableBelt>().Facing == Facing.East));
        }

        [Test]
        public void BuildBoundsGrowWithTheFloor()
        {
            SandboxWorld world = Compose();
            var beyond = new Vector2Int(0, world.Bounds.NorthExtent + 1);
            Assert.IsFalse(world.Build.IsCellBuildable(beyond));

            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 1000);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.IronPlate, 1000);
            Assert.IsTrue(world.FloorExpansion.TryPurchaseExpansion(), world.FloorExpansion.LastStatusMessage);

            Assert.IsTrue(world.Build.IsCellBuildable(beyond), "the new rows are buildable as soon as they are bought");
        }

        [Test]
        public void TheWorldTicksTheMarketTheBenchAndTheSteam()
        {
            SandboxWorld world = Compose();
            Assert.IsNotNull(world.Market);
            Assert.IsNotNull(world.StarterBench);
            world.Clock.Play();
            world.Advance(1f);
            Assert.That(world.Clock.CurrentTick, Is.GreaterThan(0));
        }

        [Test]
        public void TheWorkbenchOffersSandboxUnitysRoster()
        {
            SandboxWorld world = Compose();
            Assert.AreEqual(SandboxWorld.WorkbenchSockets, world.Workbench.SocketCount);
            Assert.AreEqual(5, world.Workbench.RackChassis.Count());
            Assert.AreEqual(2, world.Workbench.VaultLogicCores.Count());
            Assert.AreEqual(24, world.Workbench.VaultAppendages.Count(),
                "WorkbenchCanvas.prefab's two verbs plus Sandbox.unity's 22-card override");
            Assert.IsFalse(world.Workbench.IsRosterGated, "ungated until the Assembly Line panel (G8) can grant claims");
        }

        [Test]
        public void AFreshlyBuiltGolem_BecomesTheWorkbenchsTarget()
        {
            // Unity's station retargeted the WorkbenchController on spawn; the session is the
            // target the world hands every station.
            SandboxWorld world = Compose();
            ChassisDefinition scavenger = world.Definitions.Chassis["ClockworkScavenger"];
            foreach (RecipeIngredient c in scavenger.cost)
            {
                world.Buffers.Deposit(world.StockpileBufferId, c.itemType, c.quantity);
            }

            Assert.IsTrue(world.StarterStation.TryConstructGolem(scavenger, out GolemEntity golem));

            Assert.AreSame(golem, world.Workbench.TargetGolem);
            Assert.AreEqual(scavenger, world.Workbench.DraftChassis, "the draft was re-read from the new golem");
        }
    }
}
