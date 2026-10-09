using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.Golems
{
    /// <summary>
    /// One Haul card, the player picks the good (G10, at the user's call). Before this the deck's
    /// only Haul was typed to Scrap, so no golem could load Coke, ore or plate, and every
    /// two-input recipe -- the Aether-Hauler's whole reason to exist -- could not be fed.
    /// </summary>
    public class HaulItemTypeTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        [Test]
        public void AHaulSlot_StartsOnItsCardsGood_AndKeepsThePlayersPick()
        {
            SandboxWorld world = Compose();
            var program = new GolemProgram();
            program.TryAssignChassis(world.Definitions.Chassis["AetherHauler"]);
            program.TryAddAppendage(world.Definitions.Appendages["HaulScrap"]);
            program.TryAddAppendage(world.Definitions.Appendages["HaulScrap"]);

            Assert.AreEqual(ItemType.Scrap, program.GetItemTypeAt(1));
            program.SetItemTypeAt(1, ItemType.Coke);
            Assert.AreEqual(ItemType.Coke, program.GetItemTypeAt(1));
            Assert.AreEqual(ItemType.Scrap, program.GetItemTypeAt(0), "per slot, not per card");
            Assert.AreEqual(ItemType.Scrap, world.Definitions.Appendages["HaulScrap"].inputItemType, "the shared card is untouched");

            program.RemoveAppendageAt(0);
            Assert.AreEqual(ItemType.Coke, program.GetItemTypeAt(0), "the pick moves with its slot");
        }

        [Test]
        public void AnAetherHauler_FeedsIronSmelting_TwoInputs_FromOneDepot()
        {
            // The case that was impossible: Haul Scrap, Haul Coke, Assemble Iron Smelting, Push.
            SandboxWorld world = Compose();
            var defs = world.Definitions;
            foreach (var c in defs.Chassis["AetherHauler"].cost)
            {
                world.Buffers.Deposit(world.StockpileBufferId, c.itemType, c.quantity);
            }
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 200);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Coke, 100);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.IronPlate, 40);
            Place(world, "BoilerPrefab", new Vector2Int(5, 5));
            world.Interactor.TryRefuelBoiler(world.Build.Buildings.Single(b => b.Cell == new Vector2Int(5, 5)).GetPart<PlaceableBoiler>());
            Place(world, "DepotPrefab", new Vector2Int(4, 4)); // behind
            Place(world, "DepotPrefab", new Vector2Int(4, 6)); // in front

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["AetherHauler"], out GolemEntity smelter));
            smelter.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            smelter.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            smelter.Program.SetQuantityAt(0, 2);
            smelter.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            smelter.Program.SetItemTypeAt(1, ItemType.Coke);
            Assert.IsTrue(smelter.Program.TryAddAppendage(defs.Appendages["AssembleIronSmelting"]));
            Assert.IsTrue(smelter.Program.TryAddAppendage(defs.Appendages["PushOutput"]));
            smelter.SetPlacement(new Vector2Int(4, 5), Facing.North);

            int ironBefore = world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.IronPlate);
            int slagBefore = world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Slag);
            world.Clock.Play();
            for (int frame = 0; frame < 600; frame++)
            {
                world.Advance(1f / 30f);
            }

            Assert.Greater(world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.IronPlate), ironBefore,
                $"Iron Plate smelted ({smelter.Program.State} {smelter.StallReason} {smelter.StallResourceId})");
            Assert.Greater(world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Slag), slagBefore, "with its Slag");
        }

        [Test]
        public void TheWorkbench_PicksTheGood_EngagesIt_PatentsIt_AndLoadsItBack()
        {
            SandboxWorld world = Compose();
            var defs = world.Definitions;
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 12);
            world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity golem);
            golem.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            var bench = world.Workbench;
            bench.HaulableGood = null; // offer every good
            bench.RetargetGolem(golem);
            bench.Open();

            Assert.IsTrue(bench.IsDraftHaul(0));
            Assert.AreEqual(ItemType.Scrap, bench.DraftItemTypeAt(0));
            bench.CycleDraftItemType(0, +1);
            string next = bench.DraftItemTypeAt(0);
            Assert.AreNotEqual(ItemType.Scrap, next);
            bench.CycleDraftItemType(0, -1);
            Assert.AreEqual(ItemType.Scrap, bench.DraftItemTypeAt(0), "the arrows go both ways");
            bench.CycleDraftItemType(0, +1);

            Assert.AreEqual(ItemType.Scrap, golem.Program.GetItemTypeAt(0), "nothing commits before the lever");
            Assert.IsTrue(bench.Engage());
            Assert.AreEqual(next, golem.Program.GetItemTypeAt(0), "ENGAGE commits the good");

            var blueprint = bench.Patent();
            Assert.AreEqual(next, blueprint.ItemTypes[0], "a patent keeps the good");
            bench.CycleDraftItemType(0, +1);
            bench.LoadBlueprintIntoDraft(blueprint);
            Assert.AreEqual(next, bench.DraftItemTypeAt(0), "and loading it back restores it");
        }

        [Test]
        public void TheGoodSurvivesASaveAndLoad_OnGolemsAndPatents()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-haul-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld world = Compose();
                var defs = world.Definitions;
                world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 12);
                world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity golem);
                golem.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
                golem.Program.SetItemTypeAt(0, ItemType.Coke);
                world.Patents.TryPatent(new Blueprints.Blueprint("BP-001", "LocalPlayer", golem.Program.chassis, null,
                    golem.Program.appendages.ToList(), new[] { ItemType.Coke }, new[] { 5 }));
                string id = golem.GolemId;
                world.SaveTo(path);

                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);

                GolemEntity back = fresh.Golems.Single(g => g.GolemId == id && !g.IsRemoved);
                Assert.AreEqual(ItemType.Coke, back.Program.GetItemTypeAt(0));
                var patent = fresh.Patents.Blueprints["BP-001"];
                Assert.AreEqual(ItemType.Coke, patent.ItemTypes[0]);
                Assert.AreEqual(5, patent.Quantities[0]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void Place(SandboxWorld world, string key, Vector2Int cell)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == key).Prefab);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
        }
    }
}
