using System.IO;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Tests.Data;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.World
{
    /// <summary>
    /// The belt splitter in the build menu (G10, at the user's call). Core's splitter rules are
    /// pinned by BeltSplitterTests; these pin that the player can actually build one: the menu
    /// offers it, it places as a splitter, sends goods down both branches, refunds, and a save
    /// brings it back as a splitter rather than a plain belt.
    /// </summary>
    public class BeltSplitterPlacementTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 100);
            return world;
        }

        private static PlaceableBuilding Place(SandboxWorld world, string key, int x, int y, Facing facing)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == key).Prefab);
            while (world.Build.PlacementFacing != facing)
            {
                world.Build.RotatePlacement();
            }
            world.Build.PlaceOrRemove(new Vector2Int(x, y));
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == new Vector2Int(x, y));
        }

        /// <summary>A belt feeding a splitter at (1,0), with branches leading north and east.</summary>
        private static (PlaceableBuilding feed, PlaceableBuilding splitter, PlaceableBuilding north, PlaceableBuilding east) Junction(SandboxWorld world) =>
            (Place(world, "BeltPrefab", 0, 0, Facing.East),
             Place(world, "BeltSplitterPrefab", 1, 0, Facing.North),
             Place(world, "BeltPrefab", 1, 1, Facing.North),
             Place(world, "BeltPrefab", 2, 0, Facing.East));

        [Test]
        public void TheBuildMenuOffersTheSplitter_RightAfterTheBelt()
        {
            var keys = Compose().Placeables.Select(p => p.Key).ToList();
            Assert.AreEqual(keys.IndexOf("BeltPrefab") + 1, keys.IndexOf("BeltSplitterPrefab"));
        }

        [Test]
        public void APlacedSplitter_SendsGoodsDownBothBranches()
        {
            SandboxWorld world = Compose();
            var (feed, splitter, north, east) = Junction(world);
            BeltSegment feedLane = feed.GetPart<PlaceableBelt>().Segment;
            Assert.AreEqual(2, splitter.GetPart<PlaceableBelt>().Segment.Outputs.Count);

            int fed = 0;
            for (long tick = 1; tick <= 400; tick++)
            {
                if (fed < 6 && feedLane.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap }))
                {
                    fed++;
                }
                world.Conveyor.Tick(tick);
            }

            int toNorth = north.GetPart<PlaceableBelt>().Segment.Items.Count;
            int toEast = east.GetPart<PlaceableBelt>().Segment.Items.Count;
            Assert.AreEqual(6, fed, "precondition: the feed took six");
            Assert.That(toNorth, Is.GreaterThan(0), "north branch");
            Assert.That(toEast, Is.GreaterThan(0), "east branch");
        }

        [Test]
        public void ASplitter_IsRefundedInFull()
        {
            SandboxWorld world = Compose();
            int before = world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap);
            PlaceableBuilding splitter = Place(world, "BeltSplitterPrefab", 3, 3, Facing.North);
            Assert.Less(world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap), before);

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(splitter.Cell);
            world.Build.CancelPlacement();

            Assert.AreEqual(before, world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap));
        }

        [Test]
        public void ASavedSplitter_LoadsBackAsASplitter()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-splitter-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld before = Compose();
                Junction(before);
                before.SaveTo(path);

                SandboxWorld after = Compose();
                StringAssert.EndsWith("4 buildings.", after.LoadFrom(path));
                PlaceableBuilding splitter = after.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == new Vector2Int(1, 0));
                Assert.IsNotNull(splitter.GetPart<PlaceableBeltSplitter>());
                Assert.AreEqual(2, splitter.GetPart<PlaceableBelt>().Segment.Outputs.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
