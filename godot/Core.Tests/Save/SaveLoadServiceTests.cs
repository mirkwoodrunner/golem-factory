using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;

namespace GolemFactory.Tests.EditMode
{
    public class SaveLoadServiceTests
    {


        private GolemEntity MakeGolem(string golemId)
        {

            var golem = new GolemEntity();
            golem.Configure(golemId, null);
            return golem;
        }

        [Test]
        public void CaptureState_CapturesBufferContents()
        {
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("ScrapBuffer", ItemType.Scrap, 42);
            var patents = new PatentRegistry();

            SaveData data = SaveLoadService.CaptureState(buffers, patents, new List<GolemEntity>());

            Assert.AreEqual(1, data.buffers.Count);
            Assert.AreEqual("ScrapBuffer", data.buffers[0].bufferId);
            Assert.AreEqual(ItemType.Scrap, data.buffers[0].itemTypes[0]);
            Assert.AreEqual(42, data.buffers[0].quantities[0]);
        }

        [Test]
        public void RestoreState_RestoresBufferContents()
        {
            var sourceBuffers = new StorageBufferRegistry();
            sourceBuffers.Deposit("ScrapBuffer", ItemType.Scrap, 42);
            SaveData data = SaveLoadService.CaptureState(sourceBuffers, new PatentRegistry(), new List<GolemEntity>());

            var destBuffers = new StorageBufferRegistry();
            var catalog = new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);

            SaveLoadService.RestoreState(data, destBuffers, new PatentRegistry(), new List<GolemEntity>(), catalog);

            Assert.AreEqual(42, destBuffers.GetOrCreate("ScrapBuffer").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void RestoreState_ReplacesExistingBufferState_DoesNotMergeWithIt()
        {
            // Deposit is additive -- a naive RestoreState that just replays Deposit calls
            // would double-count anything already sitting in the destination buffer at
            // load time. Loading a save should replace state, not merge into it.
            var sourceBuffers = new StorageBufferRegistry();
            sourceBuffers.Deposit("ScrapBuffer", ItemType.Scrap, 42);
            SaveData data = SaveLoadService.CaptureState(
                sourceBuffers, new PatentRegistry(), new List<GolemEntity>());

            var destBuffers = new StorageBufferRegistry();
            destBuffers.Deposit("ScrapBuffer", ItemType.Scrap, 9999);

            SaveLoadService.RestoreState(
                data, destBuffers, new PatentRegistry(), new List<GolemEntity>(),
                new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]));

            Assert.AreEqual(42, destBuffers.GetOrCreate("ScrapBuffer").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void CaptureThenRestore_Blueprint_RoundTripsViaDefinitionCatalog()
        {
            var chassis = new ChassisDefinition();
            chassis.name = "TestChassis";
            var logicCore = new LogicCoreDefinition();
            logicCore.name = "TestLogicCore";
            var appendage = new AppendageActionDefinition();
            appendage.name = "TestAppendage";

            var sourcePatents = new PatentRegistry();
            sourcePatents.TryPatent(new Blueprint("BP-001", "LocalPlayer", chassis, logicCore, new List<AppendageActionDefinition> { appendage }));
            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), sourcePatents, new List<GolemEntity>());

            var destPatents = new PatentRegistry();
            var catalog = new DefinitionCatalog(new[] { chassis }, new[] { logicCore }, new[] { appendage });

            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), destPatents, new List<GolemEntity>(), catalog);

            Assert.IsTrue(destPatents.TryUseBlueprint("BP-001", "LocalPlayer", out Blueprint restored));
            Assert.AreEqual(chassis, restored.Chassis);
            Assert.AreEqual(logicCore, restored.LogicCore);
            Assert.AreEqual(appendage, restored.Appendages[0]);
        }

        // A card renamed or removed between builds is skipped on load. Its slot's good and batch
        // size must go with it: compacting only the cards slid every later slot onto the one
        // before's settings, so a Haul set to Coke came back hauling nothing in particular.
        private static (AppendageActionDefinition gone, AppendageActionDefinition haul, AppendageActionDefinition push) ThreeCards()
        {
            var gone = new AppendageActionDefinition { name = "AssembleRenamed" };
            var haul = new AppendageActionDefinition { name = "TestHaul", actionType = AppendageActionType.Haul };
            var push = new AppendageActionDefinition { name = "TestPush" };
            return (gone, haul, push);
        }

        [Test]
        public void AnUnresolvedCard_TakesItsSlotsSettingsWithIt_InAPatent()
        {
            var (gone, haul, push) = ThreeCards();
            var chassis = new ChassisDefinition { name = "TestChassis", maxAppendageSlots = 4 };
            var core = new LogicCoreDefinition { name = "TestCore" };
            var sourcePatents = new PatentRegistry();
            sourcePatents.TryPatent(new Blueprint("BP-001", "LocalPlayer", chassis, core,
                new List<AppendageActionDefinition> { gone, haul, push },
                new List<string> { "", ItemType.Coke, "" }, new List<int> { 1, 3, 1 }));
            SaveData data = SaveLoadService.CaptureState(new StorageBufferRegistry(), sourcePatents, new List<GolemEntity>());

            var destPatents = new PatentRegistry();
            SaveLoadService.RestoreState(data, new StorageBufferRegistry(), destPatents, new List<GolemEntity>(),
                new DefinitionCatalog(new[] { chassis }, new[] { core }, new[] { haul, push })); // no AssembleRenamed

            Assert.IsTrue(destPatents.TryUseBlueprint("BP-001", "LocalPlayer", out Blueprint restored));
            Assert.AreEqual(haul, restored.Appendages[0]);
            Assert.AreEqual(ItemType.Coke, restored.ItemTypes[0], "the Haul keeps its good");
            Assert.AreEqual(3, restored.Quantities[0], "and its batch size");
        }

        [Test]
        public void AnUnresolvedCard_TakesItsSlotsSettingsWithIt_OnAGolem()
        {
            var (gone, haul, push) = ThreeCards();
            var chassis = new ChassisDefinition { name = "TestChassis", maxAppendageSlots = 4 };
            GolemEntity source = MakeGolem("Golem1");
            source.Program.TryAssignChassis(chassis);
            source.Program.TryAddAppendage(gone);
            source.Program.TryAddAppendage(haul);
            source.Program.TryAddAppendage(push);
            source.Program.SetItemTypeAt(1, ItemType.Coke);
            source.Program.SetQuantityAt(1, 3);
            SaveData data = SaveLoadService.CaptureState(new StorageBufferRegistry(), new PatentRegistry(), new List<GolemEntity> { source });

            GolemEntity dest = MakeGolem("Golem1");
            SaveLoadService.RestoreState(data, new StorageBufferRegistry(), new PatentRegistry(), new List<GolemEntity> { dest },
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new[] { haul, push }));

            Assert.AreEqual(2, dest.Program.appendages.Count);
            Assert.AreEqual(haul, dest.Program.appendages[0]);
            Assert.AreEqual(ItemType.Coke, dest.Program.GetItemTypeAt(0), "the Haul keeps its good");
            Assert.AreEqual(3, dest.Program.GetQuantityAt(0), "and its batch size");
        }

        [Test]
        public void CaptureThenRestore_GolemProgram_RoundTrips()
        {
            var chassis = new ChassisDefinition();
            chassis.name = "TestChassis";
            chassis.maxAppendageSlots = 2;
            var logicCore = new LogicCoreDefinition();
            logicCore.name = "TestLogicCore";
            var appendage = new AppendageActionDefinition();
            appendage.name = "TestAppendage";

            GolemEntity sourceGolem = MakeGolem("Golem1");
            sourceGolem.Program.TryAssignChassis(chassis);
            sourceGolem.Program.TryAddAppendage(appendage);
            sourceGolem.Program.logicCore = logicCore;
            sourceGolem.Program.CurrentStepIndex = 0;
            sourceGolem.Program.State = GolemState.Running;

            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new PatentRegistry(), new List<GolemEntity> { sourceGolem });

            GolemEntity destGolem = MakeGolem("Golem1");
            var catalog = new DefinitionCatalog(new[] { chassis }, new[] { logicCore }, new[] { appendage });

            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { destGolem }, catalog);

            Assert.AreEqual(chassis, destGolem.Program.chassis);
            Assert.AreEqual(logicCore, destGolem.Program.logicCore);
            Assert.AreEqual(1, destGolem.Program.appendages.Count);
            Assert.AreEqual(appendage, destGolem.Program.appendages[0]);
            Assert.AreEqual(GolemState.Running, destGolem.Program.State);
        }

        // --- The machine model's state (progression-design section 2) -------------------------

        [Test]
        public void CaptureThenRestore_PerSlotHaulQuantities_RoundTrip()
        {
            var chassis = new ChassisDefinition();
            chassis.name = "TestChassis";
            chassis.maxAppendageSlots = 2;
            var appendage = new AppendageActionDefinition();
            appendage.name = "TestAppendage";
            appendage.haulQuantity = 1;

            GolemEntity sourceGolem = MakeGolem("Golem1");
            sourceGolem.Program.TryAssignChassis(chassis);
            sourceGolem.Program.TryAddAppendage(appendage);
            sourceGolem.Program.TryAddAppendage(appendage);
            sourceGolem.Program.SetQuantityAt(0, 8);
            sourceGolem.Program.SetQuantityAt(1, 3);

            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { sourceGolem });

            GolemEntity destGolem = MakeGolem("Golem1");
            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { destGolem },
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage }));

            Assert.AreEqual(8, destGolem.Program.GetQuantityAt(0),
                "the player's batch size did not survive the save");
            Assert.AreEqual(3, destGolem.Program.GetQuantityAt(1));
        }

        [Test]
        public void CaptureThenRestore_InternalStock_RoundTrips()
        {
            GolemEntity sourceGolem = MakeGolem("Golem1");
            sourceGolem.Inventory.AddInput(ItemType.Scrap, 7);
            sourceGolem.Inventory.AddInput(ItemType.Brass, 2);
            sourceGolem.Inventory.AddOutput(ItemType.Aether, 4);

            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { sourceGolem });

            GolemEntity destGolem = MakeGolem("Golem1");
            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { destGolem },
                new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]));

            Assert.AreEqual(7, destGolem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(2, destGolem.Inventory.GetInput(ItemType.Brass));
            Assert.AreEqual(4, destGolem.Inventory.GetOutput(ItemType.Aether));
            // Push drains in this order, so it has to survive a round trip too.
            CollectionAssert.AreEqual(
                new[] { ItemType.Scrap, ItemType.Brass }, destGolem.Inventory.Input.TypesInOrder);
        }

        [Test]
        public void RestoreState_ReplacesInternalStock_DoesNotMergeIntoIt()
        {
            // Stock.Add is additive, the same hazard buffers.Clear() exists for.
            GolemEntity sourceGolem = MakeGolem("Golem1");
            sourceGolem.Inventory.AddInput(ItemType.Scrap, 3);
            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { sourceGolem });

            GolemEntity destGolem = MakeGolem("Golem1");
            destGolem.Inventory.AddInput(ItemType.Scrap, 9);
            destGolem.Inventory.AddOutput(ItemType.Brass, 5);

            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { destGolem },
                new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]));

            Assert.AreEqual(3, destGolem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(0, destGolem.Inventory.GetOutput(ItemType.Brass), "stale output stock survived a load");
        }

        [Test]
        public void RestoreState_ASaveWrittenBeforeTheMachineModel_LoadsWithoutError()
        {
            // Older saves have no quantity or stock lists at all. They must restore to the
            // authored defaults and an empty hold rather than throwing.
            var chassis = new ChassisDefinition();
            chassis.name = "TestChassis";
            chassis.maxAppendageSlots = 2;
            var appendage = new AppendageActionDefinition();
            appendage.name = "TestAppendage";
            appendage.haulQuantity = 4;

            var data = new SaveData
            {
                golems = new List<GolemEntry>
                {
                    new GolemEntry
                    {
                        golemId = "Golem1",
                        chassisName = "TestChassis",
                        appendageNames = new List<string> { "TestAppendage" }
                    }
                }
            };
            data.golems[0].appendageQuantities = null;
            data.golems[0].inputStockTypes = null;

            GolemEntity destGolem = MakeGolem("Golem1");

            Assert.DoesNotThrow(() => SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { destGolem },
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage })));

            Assert.AreEqual(4, destGolem.Program.GetQuantityAt(0), "it lost the card's authored default");
            Assert.AreEqual(0, destGolem.Inventory.Input.TotalUnits);
        }

        [Test]
        public void RestoreState_MismatchedStockLists_TakeOnlyTheOverlapRatherThanThrowing()
        {
            var data = new SaveData
            {
                golems = new List<GolemEntry>
                {
                    new GolemEntry
                    {
                        golemId = "Golem1",
                        inputStockTypes = new List<string> { ItemType.Scrap, ItemType.Brass },
                        inputStockQuantities = new List<int> { 2 }
                    }
                }
            };

            GolemEntity destGolem = MakeGolem("Golem1");

            Assert.DoesNotThrow(() => SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity> { destGolem },
                new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0])));

            Assert.AreEqual(2, destGolem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(0, destGolem.Inventory.GetInput(ItemType.Brass));
        }

        [Test]
        public void RestoreState_GolemNoLongerInScene_IsSkippedWithoutError()
        {
            var data = new SaveData
            {
                golems = new List<GolemEntry> { new GolemEntry { golemId = "GoneGolem" } }
            };

            Assert.DoesNotThrow(() => SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new PatentRegistry(),
                new List<GolemEntity>(), new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0])));
        }
    }
}
