using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
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
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private GolemEntity MakeGolem(string golemId)
        {
            if (_root == null)
            {
                _root = new GameObject("Root");
            }

            var go = new GameObject(golemId);
            go.transform.SetParent(_root.transform);
            var golem = go.AddComponent<GolemEntity>();
            golem.Configure(golemId, null);
            return golem;
        }

        [Test]
        public void CaptureState_CapturesBufferContentsAndFocus()
        {
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("ScrapBuffer", ItemType.Scrap, 42);
            var focus = new ArtificerFocusMeter("LocalPlayer", 100f);
            focus.TryConsume(30f);
            var patents = new PatentRegistry();

            SaveData data = SaveLoadService.CaptureState(buffers, focus, patents, new List<GolemEntity>());

            Assert.AreEqual(1, data.buffers.Count);
            Assert.AreEqual("ScrapBuffer", data.buffers[0].bufferId);
            Assert.AreEqual(ItemType.Scrap, data.buffers[0].itemTypes[0]);
            Assert.AreEqual(42, data.buffers[0].quantities[0]);
            Assert.AreEqual(70f, data.focusCurrent);
        }

        [Test]
        public void RestoreState_RestoresBufferContentsAndFocus()
        {
            var sourceBuffers = new StorageBufferRegistry();
            sourceBuffers.Deposit("ScrapBuffer", ItemType.Scrap, 42);
            var sourceFocus = new ArtificerFocusMeter("LocalPlayer", 100f);
            sourceFocus.TryConsume(30f);
            SaveData data = SaveLoadService.CaptureState(sourceBuffers, sourceFocus, new PatentRegistry(), new List<GolemEntity>());

            var destBuffers = new StorageBufferRegistry();
            var destFocus = new ArtificerFocusMeter("LocalPlayer", 100f);
            var catalog = new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);

            SaveLoadService.RestoreState(data, destBuffers, destFocus, new PatentRegistry(), new List<GolemEntity>(), catalog);

            Assert.AreEqual(42, destBuffers.GetOrCreate("ScrapBuffer").GetQuantity(ItemType.Scrap));
            Assert.AreEqual(70f, destFocus.CurrentFocus);
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
                sourceBuffers, new ArtificerFocusMeter("LocalPlayer"), new PatentRegistry(), new List<GolemEntity>());

            var destBuffers = new StorageBufferRegistry();
            destBuffers.Deposit("ScrapBuffer", ItemType.Scrap, 9999);

            SaveLoadService.RestoreState(
                data, destBuffers, new ArtificerFocusMeter("LocalPlayer"), new PatentRegistry(), new List<GolemEntity>(),
                new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]));

            Assert.AreEqual(42, destBuffers.GetOrCreate("ScrapBuffer").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void RestoreState_FocusLowerThanCurrent_StillSetsExactly()
        {
            // Refund can only ever increase CurrentFocus -- restoring a save with a lower
            // value than what's live right now needs SetCurrent, not Refund.
            var data = new SaveData { focusCurrent = 10f };
            var destFocus = new ArtificerFocusMeter("LocalPlayer", 100f); // starts at 100

            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), destFocus, new PatentRegistry(), new List<GolemEntity>(),
                new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]));

            Assert.AreEqual(10f, destFocus.CurrentFocus);
        }

        [Test]
        public void CaptureThenRestore_Blueprint_RoundTripsViaDefinitionCatalog()
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.name = "TestChassis";
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.name = "TestLogicCore";
            var appendage = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            appendage.name = "TestAppendage";

            var sourcePatents = new PatentRegistry();
            sourcePatents.TryPatent(new Blueprint("BP-001", "LocalPlayer", chassis, logicCore, new List<AppendageActionDefinition> { appendage }));
            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"), sourcePatents, new List<GolemEntity>());

            var destPatents = new PatentRegistry();
            var catalog = new DefinitionCatalog(new[] { chassis }, new[] { logicCore }, new[] { appendage });

            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"), destPatents, new List<GolemEntity>(), catalog);

            Assert.IsTrue(destPatents.TryUseBlueprint("BP-001", "LocalPlayer", out Blueprint restored));
            Assert.AreEqual(chassis, restored.Chassis);
            Assert.AreEqual(logicCore, restored.LogicCore);
            Assert.AreEqual(appendage, restored.Appendages[0]);
        }

        [Test]
        public void CaptureThenRestore_GolemProgram_RoundTrips()
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.name = "TestChassis";
            chassis.maxAppendageSlots = 2;
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.name = "TestLogicCore";
            var appendage = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            appendage.name = "TestAppendage";

            GolemEntity sourceGolem = MakeGolem("Golem1");
            sourceGolem.Program.TryAssignChassis(chassis);
            sourceGolem.Program.TryAddAppendage(appendage);
            sourceGolem.Program.logicCore = logicCore;
            sourceGolem.Program.CurrentStepIndex = 0;
            sourceGolem.Program.State = GolemState.Running;

            SaveData data = SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"), new PatentRegistry(), new List<GolemEntity> { sourceGolem });

            GolemEntity destGolem = MakeGolem("Golem1");
            var catalog = new DefinitionCatalog(new[] { chassis }, new[] { logicCore }, new[] { appendage });

            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"), new PatentRegistry(),
                new List<GolemEntity> { destGolem }, catalog);

            Assert.AreEqual(chassis, destGolem.Program.chassis);
            Assert.AreEqual(logicCore, destGolem.Program.logicCore);
            Assert.AreEqual(1, destGolem.Program.appendages.Count);
            Assert.AreEqual(appendage, destGolem.Program.appendages[0]);
            Assert.AreEqual(GolemState.Running, destGolem.Program.State);
        }

        [Test]
        public void RestoreState_GolemNoLongerInScene_IsSkippedWithoutError()
        {
            var data = new SaveData
            {
                golems = new List<GolemEntry> { new GolemEntry { golemId = "GoneGolem" } }
            };

            Assert.DoesNotThrow(() => SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"), new PatentRegistry(),
                new List<GolemEntity>(), new DefinitionCatalog(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0])));
        }
    }
}
