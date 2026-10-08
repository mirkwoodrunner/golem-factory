using System.IO;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Data;
using GolemFactory.Simulation;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Save;
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
            private static void Fund(SandboxWorld world)
        {
            foreach (string item in new[] { ItemType.Scrap, ItemType.Brass, ItemType.IronPlate, ItemType.Gear, ItemType.Coke })
            {
                world.Buffers.Deposit(world.StockpileBufferId, item, 5000);
            }
        }

        private static string ClaimFirstAffordable(SandboxWorld world)
        {
            for (int i = 0; i < world.AssemblyLine.SlotCount; i++)
            {
                DraftableCardDefinition card = world.AssemblyLine.GetCard(i);
                if (card != null && world.AssemblyLineBoard.Claim(i))
                {
                    return card.name;
                }
            }
            Assert.Fail("nothing on the line could be claimed: " + world.AssemblyLineBoard.Status);
            return null;
        }

        private static string Claims(SandboxWorld world) =>
            string.Join(",", world.AssemblyLine.GetClaimedCards("LocalPlayer").Select(c => c.name).OrderBy(n => n));

        private static string Offered(SandboxWorld world) =>
            string.Join(",", Enumerable.Range(0, world.AssemblyLine.SlotCount).Select(i => world.AssemblyLine.GetCard(i)?.name));

        [Test]
        public void Progress_RoundTrips_AndALoadReplacesWhatCameAfterTheSave()
        {
            // G10, at the user's call: claims, the ledger, the floor, the bay tier and the clock
            // are saved. A load REPLACES them, or a card bought after the save would survive a
            // load that also hands back the goods it cost.
            SandboxWorld world = Compose();
            Fund(world);
            ClaimFirstAffordable(world);
            Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor(), world.AssemblyLineBoard.Status);
            Assert.IsTrue(world.AssemblyLineBoard.UpgradeBays(), world.AssemblyLineBoard.Status);
            world.TechTree.Ledger.RecordItem(ItemType.Coke);
            world.Clock.Speed = 2f;
            world.Clock.Pause();
            string claims = Claims(world);
            int north = world.Bounds.NorthExtent, tier = world.AssemblyBay.Tier, slots = world.AssemblyBay.MaxGolemSlots;
            world.SaveTo(_path);

            ClaimFirstAffordable(world);
            Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor());
            Assert.IsTrue(world.AssemblyLineBoard.UpgradeBays());
            world.Clock.Speed = 4f;
            world.Clock.Play();
            Assert.AreNotEqual(claims, Claims(world), "precondition: progress moved on");

            world.LoadFrom(_path);

            Assert.AreEqual(claims, Claims(world));
            // The line is rebuilt as a fresh session holding those claims would build it -- not
            // in the exact queue order the session had reached -- so it offers no gap and
            // nothing the player owns.
            var owned = world.AssemblyLine.GetClaimedCards("LocalPlayer");
            for (int i = 0; i < world.AssemblyLine.SlotCount; i++)
            {
                Assert.IsNotNull(world.AssemblyLine.GetCard(i), "slot " + i);
                CollectionAssert.DoesNotContain(owned, world.AssemblyLine.GetCard(i), "slot " + i);
            }
            Assert.AreEqual(north, world.Bounds.NorthExtent);
            Assert.AreEqual(tier, world.AssemblyBay.Tier);
            Assert.AreEqual(slots, world.AssemblyBay.MaxGolemSlots);
            Assert.IsTrue(world.TechTree.Ledger.HasItem(ItemType.Coke));
            Assert.AreEqual(2f, world.Clock.Speed);
            Assert.AreEqual(ClockState.Paused, world.Clock.State);
        }

        [Test]
        public void AFreshSession_LoadsTheSavedProgress_AndABuildingBeyondTheOriginalWall()
        {
            // The case that made the floor worth saving: a fresh session starts at the authored
            // back wall, so a building on a bought row had nowhere to be rebuilt.
            SandboxWorld before = Compose();
            Fund(before);
            ClaimFirstAffordable(before);
            Assert.IsTrue(before.AssemblyLineBoard.ExtendFloor());
            int row = before.Bounds.NorthExtent - 1;
            before.Build.SetActivePrefab(before.Placeables.Single(p => p.Key == "DepotPrefab").Prefab);
            before.Build.PlaceOrRemove(new Compat.Vector2Int(0, row));
            before.Build.CancelPlacement();
            Assert.AreEqual(1, before.Build.Buildings.Count(b => !b.IsRemoved), "precondition: placed on the new row");
            string claims = Claims(before);
            before.SaveTo(_path);

            SandboxWorld after = Compose();
            int rowsAdded = 0;
            after.FloorExpansion.RowsAdded += (from, to) => rowsAdded += to - from + 1;
            StringAssert.EndsWith("1 buildings.", after.LoadFrom(_path));

            Assert.AreEqual(before.Bounds.NorthExtent, after.Bounds.NorthExtent);
            Assert.AreEqual(before.Bounds.NorthExtent - after.Bounds.MinNorthExtent, rowsAdded, "the floor heard about its rows");
            Assert.AreEqual(new Compat.Vector2Int(0, row), after.Build.Buildings.Single(b => !b.IsRemoved).Cell);
            Assert.AreEqual(claims, Claims(after));
        }

        [Test]
        public void LoadingASaveFromBeforeAnExpansion_TakesTheRowsBackOut()
        {
            SandboxWorld world = Compose();
            Fund(world);
            int north = world.Bounds.NorthExtent;
            world.SaveTo(_path);
            Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor());
            int removed = 0;
            world.FloorExpansion.RowsRemoved += (from, to) => removed += to - from + 1;

            world.LoadFrom(_path);

            Assert.AreEqual(north, world.Bounds.NorthExtent);
            Assert.AreEqual(world.FloorExpansion.RowsPerPurchase, removed);
        }

        [Test]
        public void ASaveWithNoProgress_LeavesProgressAlone()
        {
            // A G9 save has no progress entry; loading it must not wipe the session's claims.
            SandboxWorld world = Compose();
            Fund(world);
            ClaimFirstAffordable(world);
            string claims = Claims(world);
            world.SaveTo(_path);
            SaveData data = SaveFileIO.ReadFromFile(_path);
            data.progress = null;
            SaveFileIO.WriteToFile(data, _path);

            world.LoadFrom(_path);

            Assert.AreEqual(claims, Claims(world));
        }
    }
}
