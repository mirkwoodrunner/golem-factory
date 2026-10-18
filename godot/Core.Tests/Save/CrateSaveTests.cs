using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Save;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Saves with crates (docs/crates-design.md §7, §9 step 3): version 1, a crate's goods on its
    /// own building entry, restores that never clamp, and a save from before crates migrated --
    /// the stockpile becomes the pack whole, above its cap if need be, and every crate starts empty.
    /// </summary>
    public class CrateSaveTests
    {
        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "golem-factory-crates-" + System.Guid.NewGuid() + ".json");

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

        private static ICrate PlaceDepot(SandboxWorld world, Vector2Int cell)
        {
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 15);
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "DepotPrefab").Prefab);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
            return CrateAt(world, cell);
        }

        private static ICrate CrateAt(SandboxWorld world, Vector2Int cell) =>
            world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell).GetPart<ICrate>();

        private static int Pack(SandboxWorld world, string item) => world.Buffers.GetQuantity(world.StockpileBufferId, item);

        [Test]
        public void ASave_CarriesEachCratesGoodsOnItsBuilding_AndALoadRestoresThemExactly()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(6, 6);
            ICrate crate = PlaceDepot(world, cell);
            crate.Buffer.Deposit(ItemType.Scrap, 20);
            crate.Buffer.Deposit(ItemType.Coke, 7);
            crate.SetFilter(ItemType.Scrap);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Gear, 9);
            world.SaveTo(_path);

            SaveData data = SaveFileIO.ReadFromFile(_path);
            Assert.AreEqual(SaveData.CurrentVersion, data.version);
            Assert.IsFalse(data.buffers.Any(b => b.bufferId == "Crate(6,6)"), "a crate rides on its building, not in buffers");
            BuildingEntry entry = data.buildings.Single(b => b.cellX == 6 && b.cellY == 6);
            CollectionAssert.AreEquivalent(new[] { ItemType.Scrap, ItemType.Coke }, entry.crateItemTypes);

            // Wreck it after the save: the crate emptied, the pack spent.
            crate.Buffer.TryWithdraw(ItemType.Scrap, 20);
            crate.Buffer.Deposit(ItemType.Brass, 3);
            world.Buffers.TryWithdraw(world.StockpileBufferId, ItemType.Gear, 9);

            for (int round = 0; round < 3; round++)
            {
                world.LoadFrom(_path);
                ICrate back = CrateAt(world, cell);
                Assert.AreEqual(20, back.Buffer.GetQuantity(ItemType.Scrap), "round " + round);
                Assert.AreEqual(7, back.Buffer.GetQuantity(ItemType.Coke));
                Assert.AreEqual(0, back.Buffer.GetQuantity(ItemType.Brass), "a load replaces, never merges");
                Assert.AreEqual(ItemType.Scrap, back.FilterItemType, "the label after it");
                Assert.AreEqual(9, Pack(world, ItemType.Gear));
                Assert.IsTrue(world.Endpoints.TryGetEndpoint(cell, out IItemEndpoint endpoint));
                Assert.IsInstanceOf<FilteredBufferEndpoint>(endpoint);
                world.SaveTo(_path); // save/load/save/load duplicates nothing
            }
        }

        [Test]
        public void ALoad_RestoresAPackAndACrateAboveTheirCaps_WithoutClamping()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(6, 6);
            PlaceDepot(world, cell);
            world.SaveTo(_path);

            SaveData data = SaveFileIO.ReadFromFile(_path);
            BufferEntry pack = data.buffers.Single(b => b.bufferId == world.StockpileBufferId);
            pack.itemTypes.Add(ItemType.Brass);
            pack.quantities.Add(140);
            BuildingEntry depot = data.buildings.Single(b => b.cellX == 6 && b.cellY == 6);
            depot.crateItemTypes.Add(ItemType.Coal);
            depot.crateQuantities.Add(75);
            SaveFileIO.WriteToFile(data, _path);

            world.LoadFrom(_path);
            Assert.AreEqual(140, Pack(world, ItemType.Brass), "the pack, above its 100");
            Assert.AreEqual(75, CrateAt(world, cell).Buffer.GetQuantity(ItemType.Coal), "the crate, above its 50");
            Assert.AreEqual(0, world.Wallet.RoomFor(ItemType.Brass));
        }

        [Test]
        public void ASaveFromBeforeCrates_KeepsTheStockpileAsThePack_AndItsDepotsStartEmpty()
        {
            // A version-0 save, as the game wrote it before crates: no version, one unlimited
            // stockpile holding everything, a labelled depot with no goods of its own.
            File.WriteAllText(_path, @"{
  ""buffers"": [ { ""bufferId"": ""FactoryStockpile"", ""itemTypes"": [""Scrap"", ""IronPlate""], ""quantities"": [340, 12] } ],
  ""blueprints"": [],
  ""golems"": [],
  ""buildings"": [ { ""prefabKey"": ""DepotPrefab"", ""cellX"": 6, ""cellY"": 6, ""facing"": 0, ""depotFilterItemType"": ""IronPlate"" } ]
}");
            SandboxWorld world = Compose();
            string notice = null;
            world.Interactor.PopupRaised += p => notice = p.Text;

            string status = world.LoadFrom(_path);

            Assert.AreEqual(340, Pack(world, ItemType.Scrap), "intact, above the cap");
            Assert.AreEqual(12, Pack(world, ItemType.IronPlate));
            ICrate crate = CrateAt(world, new Vector2Int(6, 6));
            Assert.AreEqual(0, crate.Buffer.Quantities.Values.Sum(), "the crate starts empty");
            Assert.AreEqual(ItemType.IronPlate, crate.FilterItemType, "its label kept");
            StringAssert.Contains(SandboxWorld.CratesMigrationNotice, status, "said in the status line");
            Assert.AreEqual(SandboxWorld.CratesMigrationNotice, notice, "and at the player");

            // Spendable above the cap; closed to more until under it.
            Assert.IsTrue(world.Wallet.TryCharge(new[] { new PunchCards.RecipeIngredient(ItemType.Scrap, 300) }));
            Assert.AreEqual(40, Pack(world, ItemType.Scrap));

            // A save made now is version 1, and loading it says nothing more.
            world.SaveTo(_path);
            notice = null;
            StringAssert.DoesNotContain(SandboxWorld.CratesMigrationNotice, world.LoadFrom(_path));
            Assert.IsNull(notice);
        }
    }
}
