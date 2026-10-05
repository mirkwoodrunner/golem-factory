using System.IO;
using System.Linq;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode SaveLoadPanelTests, ported onto <see cref="SandboxWorld.SaveTo"/> and
    /// <see cref="SandboxWorld.LoadFrom"/> -- what the SaveLoad tab's buttons call (G9) -- with
    /// the same status lines, plus the two load regressions the conversion plan names.
    /// </summary>
    public class SaveLoadPanelTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "golem-factory-save-" + System.Guid.NewGuid() + ".json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        /// <summary>A golem no station built: the shape Unity's test used (a scene golem).</summary>
        private static GolemEntity AdoptSceneGolem(SandboxWorld world)
        {
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            world.AdoptGolem(golem);
            return golem;
        }

        private static GolemEntity BuildScavenger(SandboxWorld world)
        {
            ChassisDefinition scavenger = world.Definitions.Chassis["ClockworkScavenger"];
            foreach (RecipeIngredient c in scavenger.cost)
            {
                world.Buffers.Deposit(world.StockpileBufferId, c.itemType, c.quantity);
            }
            Assert.IsTrue(world.StarterStation.TryConstructGolem(scavenger, out GolemEntity golem));
            return golem;
        }

        [Test]
        public void SaveButton_CapturesState()
        {
            SandboxWorld world = Compose();
            AdoptSceneGolem(world);

            Assert.AreEqual("Saved 1 golems and 0 buildings.", world.SaveTo(_path));
            Assert.IsTrue(File.Exists(_path));
        }

        [Test]
        public void LoadButton_MissingFile_ShowsStatusMessage()
        {
            Assert.AreEqual("No save file found.", Compose().LoadFrom(_path));
        }

        [Test]
        public void LoadButton_RestoresState()
        {
            SandboxWorld world = Compose();
            AdoptSceneGolem(world);
            world.SaveTo(_path);

            // The golem is still in the world, so it is restored in place, not rebuilt -- and
            // the line says what the load DID, not how many entries the file held.
            Assert.AreEqual("Loaded 1 golems, rebuilt 0; 0 buildings.", world.LoadFrom(_path));
        }

        [Test]
        public void LoadButton_GolemGoneFromScene_ReportsItAsSkippedRatherThanSilently()
        {
            // A golem no station built cannot be rebuilt by one; the load must say so.
            SandboxWorld world = Compose();
            GolemEntity golem = AdoptSceneGolem(world);
            world.SaveTo(_path);
            golem.Remove();

            Assert.AreEqual("Loaded 0 golems, rebuilt 0, skipped 1; 0 buildings.", world.LoadFrom(_path));
        }

        [Test]
        public void APlayerBuiltGolemThatIsGone_IsRebuiltWithItsProgramAndPlace()
        {
            // The case the respawner exists for: a fresh session has none of the golems the
            // player built, so every one of them must come back from the save.
            SandboxWorld world = Compose();
            GolemEntity golem = BuildScavenger(world);
            golem.Program.logicCore = world.Definitions.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(world.Definitions.Appendages["ExtractScrap"]);
            golem.SetPlacement(new Compat.Vector2Int(-8, -15), Facing.North);
            string id = golem.GolemId;
            world.SaveTo(_path);
            Assert.IsTrue(world.StarterStation.TryDismantleGolem(golem, out _, out _), "precondition: dismantled");

            StringAssert.StartsWith("Loaded 0 golems, rebuilt 1", world.LoadFrom(_path));

            GolemEntity back = world.Golems.Single(g => g.GolemId == id && !g.IsRemoved);
            Assert.AreEqual("ExtractScrap", back.Program.appendages.Single().name);
            Assert.AreEqual(new Compat.Vector2Int(-8, -15), back.Cell);
        }

        [Test]
        public void SaveLoadSaveLoad_DoesNotDuplicateGoods()
        {
            // Root CLAUDE.md: a load replaces the built world without refunding it. Refund the
            // swept buildings on top of the restored buffers and every load mints a factory.
            SandboxWorld world = Compose();
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 15 * 3);
            var depot = world.Placeables.Single(p => p.Key == "DepotPrefab");
            world.Build.SetActivePrefab(depot.Prefab);
            world.Build.PlaceOrRemove(new Compat.Vector2Int(2, 2));
            world.Build.PlaceOrRemove(new Compat.Vector2Int(4, 2));
            world.Build.CancelPlacement();
            int scrap = world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap);

            for (int round = 0; round < 2; round++)
            {
                world.SaveTo(_path);
                StringAssert.EndsWith("2 buildings.", world.LoadFrom(_path));
                Assert.AreEqual(scrap, world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Scrap), "round " + round);
                Assert.AreEqual(2, world.Build.Buildings.Count(b => !b.IsRemoved), "round " + round);
            }
        }
    }
}
