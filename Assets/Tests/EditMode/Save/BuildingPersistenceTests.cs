using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The player's BUILT world across a save round-trip.
    ///
    /// <para>
    /// <c>SaveData</c> had no concept of a placed building at all -- belts, depots, boilers, steam
    /// pipes and the Clock Tower were simply gone on reload. That made the golem-respawn pass half
    /// a fix: golems came back and stalled on their first tick, because the belts they push into
    /// and the depots they fill did not.
    /// </para>
    ///
    /// <para>
    /// Driven through a fake <see cref="IBuildingRebuilder"/>, which is the point of that
    /// interface: placing a building means grid occupancy plus up to three registrations that
    /// only a live scene has, and none of that is what these tests are about. The real
    /// BuildModeController path is covered in the PlayMode suite.
    /// </para>
    /// </summary>
    public class BuildingPersistenceTests
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

        private PlaceableBuilding MakeBuilding(string prefabKey, Vector2Int cell, Facing facing, bool placed)
        {
            if (_root == null)
            {
                _root = new GameObject("Root");
            }

            var go = new GameObject(prefabKey);
            go.transform.SetParent(_root.transform);
            var building = go.AddComponent<PlaceableBuilding>();
            building.Cell = cell;
            building.Facing = facing;
            if (placed)
            {
                building.MarkRuntimePlaced(prefabKey);
            }

            return building;
        }

        private sealed class FakeRebuilder : IBuildingRebuilder
        {
            private readonly BuildingPersistenceTests _owner;
            public readonly List<string> Rebuilt = new List<string>();
            public int ClearCalls;
            public bool Refuse;

            public FakeRebuilder(BuildingPersistenceTests owner) => _owner = owner;

            public int ClearPlacedBuildings()
            {
                ClearCalls++;
                return 0;
            }

            public bool TryRebuild(
                string prefabKey, Vector2Int cell, Facing facing, out PlaceableBuilding building)
            {
                building = null;
                if (Refuse)
                {
                    return false;
                }

                Rebuilt.Add($"{prefabKey}@{cell.x},{cell.y}:{facing}");
                building = _owner.MakeBuilding(prefabKey, cell, facing, placed: true);
                return true;
            }
        }

        private static SaveData Capture(params PlaceableBuilding[] buildings) =>
            SaveLoadService.CaptureState(
                new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"),
                new PatentRegistry(), new List<GolemEntity>(), buildings);

        private static SaveLoadService.RestoreReport Restore(SaveData data, IBuildingRebuilder rebuilder) =>
            SaveLoadService.RestoreState(
                data, new StorageBufferRegistry(), new ArtificerFocusMeter("LocalPlayer"),
                new PatentRegistry(), new List<GolemEntity>(),
                new DefinitionCatalog(
                    new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]),
                null, rebuilder);

        [Test]
        public void CaptureState_RecordsPlacedBuildingsWithTheirCellAndFacing()
        {
            PlaceableBuilding belt = MakeBuilding("BeltPrefab", new Vector2Int(3, -4), Facing.East, placed: true);

            SaveData data = Capture(belt);

            Assert.AreEqual(1, data.buildings.Count);
            Assert.AreEqual("BeltPrefab", data.buildings[0].prefabKey);
            Assert.AreEqual(3, data.buildings[0].cellX);
            Assert.AreEqual(-4, data.buildings[0].cellY);
            Assert.AreEqual((int)Facing.East, data.buildings[0].facing,
                "a belt's facing IS its routing -- a factory restored facing North is a different factory");
        }

        // Sandbox authors two buildings straight into the scene. They come back with the scene, so
        // capturing them would rebuild a duplicate onto a cell the original already holds.
        [Test]
        public void CaptureState_IgnoresBuildingsAuthoredIntoTheScene()
        {
            PlaceableBuilding starter = MakeBuilding("StarterBench", Vector2Int.zero, Facing.North, placed: false);

            SaveData data = Capture(starter);

            Assert.IsEmpty(data.buildings);
        }

        // THE HEADLINE: the factory's structures come back.
        [Test]
        public void RestoreState_RebuildsEveryPlacedBuilding()
        {
            PlaceableBuilding belt = MakeBuilding("BeltPrefab", new Vector2Int(1, 2), Facing.South, placed: true);
            PlaceableBuilding depot = MakeBuilding("DepotPrefab", new Vector2Int(5, 5), Facing.West, placed: true);

            SaveData data = Capture(belt, depot);
            Object.DestroyImmediate(belt.gameObject);
            Object.DestroyImmediate(depot.gameObject);

            var rebuilder = new FakeRebuilder(this);
            SaveLoadService.RestoreReport report = Restore(data, rebuilder);

            Assert.AreEqual(2, report.BuildingsRebuilt);
            Assert.AreEqual(0, report.BuildingsSkipped);
            CollectionAssert.AreEqual(
                new[] { "BeltPrefab@1,2:South", "DepotPrefab@5,5:West" }, rebuilder.Rebuilt);
        }

        // A load REPLACES the built world. Without this, loading twice stacks two factories on one
        // floor and the second silently fails cell by cell against the first.
        [Test]
        public void RestoreState_ClearsThePlacedWorldBeforeRebuilding()
        {
            SaveData data = Capture(MakeBuilding("BeltPrefab", Vector2Int.one, Facing.North, placed: true));

            var rebuilder = new FakeRebuilder(this);
            Restore(data, rebuilder);

            Assert.AreEqual(1, rebuilder.ClearCalls);
        }

        [Test]
        public void RestoreState_NoRebuilder_ReportsEveryBuildingAsSkippedRatherThanSilently()
        {
            SaveData data = Capture(
                MakeBuilding("BeltPrefab", Vector2Int.zero, Facing.North, placed: true),
                MakeBuilding("DepotPrefab", Vector2Int.one, Facing.North, placed: true));

            SaveLoadService.RestoreReport report = Restore(data, null);

            Assert.AreEqual(0, report.BuildingsRebuilt);
            Assert.AreEqual(2, report.BuildingsSkipped,
                "silence here is how the whole built world went missing without anyone noticing");
        }

        [Test]
        public void RestoreState_RebuilderRefuses_CountsAsSkippedAndDoesNotAbandonTheLoad()
        {
            SaveData data = Capture(MakeBuilding("GonePrefab", Vector2Int.zero, Facing.North, placed: true));

            SaveLoadService.RestoreReport report = Restore(data, new FakeRebuilder(this) { Refuse = true });

            Assert.AreEqual(0, report.BuildingsRebuilt);
            Assert.AreEqual(1, report.BuildingsSkipped);
        }

        // An older save has no buildings array at all; it must restore as it always did rather
        // than clearing a world it knows nothing about.
        [Test]
        public void RestoreState_SaveWrittenBeforeBuildingsExisted_RebuildsNothingAndReportsNothing()
        {
            var data = new SaveData();

            var rebuilder = new FakeRebuilder(this);
            SaveLoadService.RestoreReport report = Restore(data, rebuilder);

            Assert.AreEqual(0, report.BuildingsRebuilt);
            Assert.AreEqual(0, report.BuildingsSkipped);
            CollectionAssert.IsEmpty(rebuilder.Rebuilt);
        }
    }
}
