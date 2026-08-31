using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The gap these cover, in one sentence: a factory the player BUILT did not survive a session,
    // because RestoreState could only apply a program to a GolemEntity that already existed and
    // silently skipped every entry that had none.
    //
    // Driven through a fake IGolemRespawner rather than a real GolemConstructionStation, which is
    // the point of that interface existing: spawning needs a prefab and seven scene references,
    // and none of them are what these tests are about. StationGolemRespawner is the thin adapter
    // over the real thing; what is asserted here is the DECISION -- who gets rebuilt, who does
    // not, and that a rebuilt golem comes back through exactly the same restore path as a live one.
    public class GolemRespawnTests
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

        private GolemEntity MakeGolem(string golemId, bool runtimeSpawned)
        {
            if (_root == null)
            {
                _root = new GameObject("Root");
            }

            var go = new GameObject(golemId);
            go.transform.SetParent(_root.transform);
            var golem = go.AddComponent<GolemEntity>();
            golem.Configure(golemId, null);
            if (runtimeSpawned)
            {
                golem.MarkRuntimeSpawned();
            }
            return golem;
        }

        // Stands in for the construction station: records what it was asked for, and hands back a
        // bare golem placed where it was told, which is the whole contract.
        private sealed class FakeRespawner : IGolemRespawner
        {
            private readonly GolemRespawnTests _owner;
            public readonly List<string> RequestedIds = new List<string>();
            public bool Refuse;

            public FakeRespawner(GolemRespawnTests owner) => _owner = owner;

            public bool TryRespawn(
                string golemId, ChassisDefinition chassis, Vector2Int cell, Facing facing,
                out GolemEntity golem)
            {
                RequestedIds.Add(golemId);
                golem = null;
                if (Refuse)
                {
                    return false;
                }

                golem = _owner.MakeGolem(golemId, runtimeSpawned: true);
                golem.Program.TryAssignChassis(chassis);
                golem.ConfigureSpatial(null, cell, facing);
                return true;
            }
        }

        private static ChassisDefinition MakeChassis(string name, int slots = 4)
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.name = name;
            chassis.maxAppendageSlots = slots;
            return chassis;
        }

        private static SaveData Capture(params GolemEntity[] golems) =>
            SaveLoadService.CaptureState(
                new StorageBufferRegistry(),
                new PatentRegistry(), golems);

        private static SaveLoadService.RestoreReport Restore(
            SaveData data, IEnumerable<GolemEntity> live, DefinitionCatalog catalog,
            IGolemRespawner respawner) =>
            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(),
                new PatentRegistry(), live, catalog, respawner);

        [Test]
        public void CaptureState_RecordsWhetherEachGolemWasBuiltDuringPlay()
        {
            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            GolemEntity authored = MakeGolem("SceneGolem", runtimeSpawned: false);

            SaveData data = Capture(built, authored);

            Assert.IsTrue(data.golems[0].wasRuntimeSpawned, "a player-built golem must be marked");
            Assert.IsFalse(data.golems[1].wasRuntimeSpawned, "a scene golem must not be");
        }

        // THE HEADLINE: the player's factory comes back.
        [Test]
        public void RestoreState_PlayerBuiltGolemMissingFromScene_IsRebuiltWithItsProgram()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger", slots: 2);
            var appendage = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            appendage.name = "Extract";

            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            built.Program.TryAssignChassis(chassis);
            built.Program.TryAddAppendage(appendage);
            built.Program.SetQuantityAt(0, 5);
            built.Inventory.AddInput(ItemType.Scrap, 9);
            built.SetPlacement(new Vector2Int(4, -3), Facing.East);

            SaveData data = Capture(built);
            Object.DestroyImmediate(built.gameObject);

            var respawner = new FakeRespawner(this);
            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity>(),
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage }),
                respawner);

            Assert.AreEqual(1, report.Respawned, "the player's golem was not rebuilt");
            Assert.AreEqual(0, report.Skipped);
            CollectionAssert.AreEqual(new[] { "PlayerGolem-001" }, respawner.RequestedIds);

            GolemEntity rebuilt = _root.transform.Find("PlayerGolem-001").GetComponent<GolemEntity>();
            Assert.AreEqual(chassis, rebuilt.Program.chassis);
            Assert.AreEqual(1, rebuilt.Program.appendages.Count, "the program did not come back");
            Assert.AreEqual(5, rebuilt.Program.GetQuantityAt(0), "the batch size did not come back");
            Assert.AreEqual(9, rebuilt.Inventory.Input.Get(ItemType.Scrap), "held stock did not come back");
            Assert.AreEqual(new Vector2Int(4, -3), rebuilt.Cell, "it came back on the wrong tile");
            Assert.AreEqual(Facing.East, rebuilt.Facing, "it came back pointing the wrong way");
        }

        // The other half of the fork, and the reason the flag exists at all. Main.unity's demos
        // are hand-wired; rebuilding one from GolemPrefab would produce a different object
        // wearing its name.
        [Test]
        public void RestoreState_SceneGolemMissing_IsSkippedNotRebuilt()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger");
            GolemEntity authored = MakeGolem("SceneGolem", runtimeSpawned: false);
            authored.Program.TryAssignChassis(chassis);

            SaveData data = Capture(authored);
            Object.DestroyImmediate(authored.gameObject);

            var respawner = new FakeRespawner(this);
            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity>(),
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                respawner);

            Assert.AreEqual(0, report.Respawned);
            Assert.AreEqual(1, report.Skipped);
            CollectionAssert.IsEmpty(respawner.RequestedIds, "a scene golem must never be rebuilt");
        }

        [Test]
        public void RestoreState_GolemStillInScene_IsRestoredInPlaceAndNotDuplicated()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger");
            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            built.Program.TryAssignChassis(chassis);

            SaveData data = Capture(built);

            var respawner = new FakeRespawner(this);
            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity> { built },
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                respawner);

            Assert.AreEqual(1, report.Restored);
            Assert.AreEqual(0, report.Respawned);
            CollectionAssert.IsEmpty(respawner.RequestedIds,
                "a golem that is still standing must not be built a second time");
        }

        // Every pre-existing call site passes no respawner. Behaviour there must be byte for byte
        // what it was, which is the same opt-in fork spatial routing and steam both ride.
        [Test]
        public void RestoreState_NoRespawner_SkipsExactlyAsItAlwaysDid()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger");
            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            built.Program.TryAssignChassis(chassis);

            SaveData data = Capture(built);
            Object.DestroyImmediate(built.gameObject);

            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity>(),
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                null);

            Assert.AreEqual(0, report.Respawned);
            Assert.AreEqual(1, report.Skipped);
        }

        // An older save has no flag at all, so it deserializes false and restores the way it
        // always has, rather than resurrecting golems into a scene that never had them.
        [Test]
        public void RestoreState_SaveWrittenBeforeTheFlagExisted_RebuildsNothing()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger");
            var data = new SaveData();
            data.golems.Add(new GolemEntry
            {
                golemId = "PlayerGolem-001",
                chassisName = "Scavenger",
                // wasRuntimeSpawned deliberately left at its default -- this is what
                // JsonUtility produces for a field the file does not contain.
            });

            var respawner = new FakeRespawner(this);
            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity>(),
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                respawner);

            Assert.AreEqual(0, report.Respawned);
            Assert.AreEqual(1, report.Skipped);
        }

        // Caught before spawning, not after: a golem is placed on a cell and registered with the
        // clock at birth, so discovering the chassis is unresolvable afterwards would leave an
        // empty golem standing in the factory holding a tile.
        [Test]
        public void RestoreState_ChassisNoLongerInTheCatalog_DoesNotEvenAttemptASpawn()
        {
            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            built.Program.TryAssignChassis(MakeChassis("DeletedChassis"));

            SaveData data = Capture(built);
            Object.DestroyImmediate(built.gameObject);

            var respawner = new FakeRespawner(this);
            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity>(),
                new DefinitionCatalog(
                    new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                respawner);

            Assert.AreEqual(0, report.Respawned);
            Assert.AreEqual(1, report.Skipped);
            CollectionAssert.IsEmpty(respawner.RequestedIds,
                "a golem with no resolvable chassis must not be half-built into the scene");
        }

        [Test]
        public void RestoreState_RespawnerRefuses_CountsAsSkippedRatherThanFailingTheLoad()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger");
            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            built.Program.TryAssignChassis(chassis);
            GolemEntity survivor = MakeGolem("PlayerGolem-002", runtimeSpawned: true);
            survivor.Program.TryAssignChassis(chassis);

            SaveData data = Capture(built, survivor);
            Object.DestroyImmediate(built.gameObject);

            var respawner = new FakeRespawner(this) { Refuse = true };
            SaveLoadService.RestoreReport report = Restore(
                data, new List<GolemEntity> { survivor },
                new DefinitionCatalog(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                respawner);

            Assert.AreEqual(1, report.Skipped, "the refusal should be reported");
            Assert.AreEqual(1, report.Restored, "and must not abandon the rest of the load");
        }

        // A rebuilt golem is itself savable, or the factory survives exactly one reload.
        [Test]
        public void RestoreState_RebuiltGolem_IsStillMarkedAndSurvivesASecondRoundTrip()
        {
            ChassisDefinition chassis = MakeChassis("Scavenger");
            GolemEntity built = MakeGolem("PlayerGolem-001", runtimeSpawned: true);
            built.Program.TryAssignChassis(chassis);

            SaveData first = Capture(built);
            Object.DestroyImmediate(built.gameObject);

            var catalog = new DefinitionCatalog(
                new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            Restore(first, new List<GolemEntity>(), catalog, new FakeRespawner(this));

            GolemEntity rebuilt = _root.transform.Find("PlayerGolem-001").GetComponent<GolemEntity>();
            Assert.IsTrue(rebuilt.IsRuntimeSpawned, "a rebuilt golem must still know it is rebuildable");

            SaveData second = Capture(rebuilt);
            Object.DestroyImmediate(rebuilt.gameObject);

            SaveLoadService.RestoreReport report = Restore(
                second, new List<GolemEntity>(), catalog, new FakeRespawner(this));

            Assert.AreEqual(1, report.Respawned, "the factory survived one reload and not two");
        }
    }
}
