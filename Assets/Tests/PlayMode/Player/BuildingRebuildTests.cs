using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.Save;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// Rebuilding a saved building through the REAL <see cref="BuildModeController"/>, rather
    /// than the fake <c>IBuildingRebuilder</c> that <c>BuildingPersistenceTests</c> drives.
    ///
    /// <para>
    /// What can only go wrong here is the difference between placing a building and rebuilding
    /// one: the cost must not be charged twice, the saved facing must win over the cursor's, and
    /// a restored belt has to reach the <c>BeltNetwork</c> -- a lane that exists in the scene but
    /// not in the simulation is one the player can see and items cannot use.
    /// </para>
    /// </summary>
    public class BuildingRebuildTests
    {
        private static readonly Vector2 CellSize = Vector2.one;

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            foreach (PlaceableBuilding building in
                     Object.FindObjectsByType<PlaceableBuilding>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(building.gameObject);
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private (BuildModeController build, GridMapHolder grid, StorageBufferRegistryHolder stockpile)
            Build(bool withBelt = false)
        {
            _root = new GameObject("Root");

            var grid = new GameObject("GridMap").AddComponent<GridMapHolder>();
            grid.transform.SetParent(_root.transform);

            var prefabGo = new GameObject("TestPrefab");
            prefabGo.transform.SetParent(_root.transform);
            PlaceableBuilding prefab = prefabGo.AddComponent<PlaceableBuilding>();
            if (withBelt)
            {
                prefabGo.AddComponent<PlaceableBelt>();
            }

            var stockpile = new GameObject("Stockpile").AddComponent<StorageBufferRegistryHolder>();
            stockpile.transform.SetParent(_root.transform);

            var build = new GameObject("BuildMode").AddComponent<BuildModeController>();
            build.transform.SetParent(_root.transform);
            build.Configure(null, grid, prefab, CellSize);
            build.ConfigureEconomy(stockpile, "FactoryStockpile", new[] { prefab });

            return (build, grid, stockpile);
        }

        [UnityTest]
        public IEnumerator TryRebuildSavedBuilding_PlacesItOnItsSavedCellAndFacing()
        {
            (BuildModeController build, GridMapHolder grid, _) = Build();
            var cell = new Vector2Int(4, -2);
            yield return null;

            bool ok = build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.West, out PlaceableBuilding instance);

            Assert.IsTrue(ok);
            Assert.AreEqual(cell, instance.Cell);
            Assert.AreEqual(Facing.West, instance.Facing,
                "the SAVED facing must win -- inheriting the cursor's would re-plumb the factory");
            Assert.IsTrue(grid.Map.IsOccupied(cell), "a rebuilt building must hold its cell");
            Assert.IsTrue(instance.IsRuntimePlaced, "or it would vanish from the next save");
        }

        [UnityTest]
        public IEnumerator TryRebuildSavedBuilding_DoesNotChargeTheCostAgain()
        {
            (BuildModeController build, _, StorageBufferRegistryHolder stockpile) = Build();
            build.ActivePrefab.ConfigureCost(30, 0);
            yield return null;

            // Empty stockpile: the player has since spent everything, which is the normal case
            // hours into a game and the one that would make a charging load unplayable.
            bool ok = build.TryRebuildSavedBuilding(
                "TestPrefab", new Vector2Int(1, 1), Facing.North, out PlaceableBuilding _);

            Assert.IsTrue(ok, "a rebuild must not be blocked by an empty stockpile");
            Assert.AreEqual(0, stockpile.Registry.GetQuantity("FactoryStockpile", ItemType.Scrap));
        }

        [UnityTest]
        public IEnumerator TryRebuildSavedBuilding_UnknownPrefab_RefusesRatherThanPlacingNothing()
        {
            (BuildModeController build, GridMapHolder grid, _) = Build();
            yield return null;

            bool ok = build.TryRebuildSavedBuilding(
                "APrefabThatWasDeleted", new Vector2Int(2, 2), Facing.North, out PlaceableBuilding instance);

            Assert.IsFalse(ok);
            Assert.IsNull(instance);
            Assert.IsFalse(grid.Map.IsOccupied(new Vector2Int(2, 2)),
                "a refused rebuild must not leave the cell occupied by nothing");
        }

        [UnityTest]
        public IEnumerator TryRebuildSavedBuilding_OccupiedCell_Refuses()
        {
            (BuildModeController build, _, _) = Build();
            var cell = new Vector2Int(3, 3);
            yield return null;

            Assert.IsTrue(build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.North, out _));
            Assert.IsFalse(build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.North, out _),
                "two buildings must never share a cell");
        }

        // A restored belt has to exist in the SIMULATION, not just the scene.
        [UnityTest]
        public IEnumerator ARebuiltBelt_IsRegisteredWithTheBeltNetwork()
        {
            (BuildModeController build, _, _) = Build(withBelt: true);

            var conveyor = new GameObject("Conveyor").AddComponent<ConveyorSystemHolder>();
            conveyor.transform.SetParent(_root.transform);
            var beltNetwork = new GameObject("Belts").AddComponent<BeltNetworkHolder>();
            beltNetwork.transform.SetParent(_root.transform);
            var endpoints = new GameObject("Endpoints").AddComponent<SpatialEndpointRegistryHolder>();
            endpoints.transform.SetParent(_root.transform);
            build.ConfigureBelts(beltNetwork, endpoints, conveyor);
            yield return null;

            var cell = new Vector2Int(2, 0);
            Assert.IsTrue(build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.East, out _));

            Assert.IsTrue(beltNetwork.Network.TryGetBelt(cell, out PlacedBelt placed),
                "a rebuilt belt that never reached BeltNetwork is a lane items cannot use");
            Assert.AreEqual(Facing.East, placed.Facing,
                "and it must point the way it was saved pointing");
        }

        // A load REPLACES the built world; scene-authored buildings are not the load's to remove.
        [UnityTest]
        public IEnumerator ClearRuntimePlacedBuildings_RemovesPlacedOnesAndLeavesSceneOnesAlone()
        {
            (BuildModeController build, GridMapHolder grid, _) = Build();
            yield return null;

            build.TryRebuildSavedBuilding("TestPrefab", new Vector2Int(1, 0), Facing.North, out _);
            build.TryRebuildSavedBuilding("TestPrefab", new Vector2Int(2, 0), Facing.North, out _);

            var sceneBuilding = new GameObject("SceneBuilding").AddComponent<PlaceableBuilding>();
            sceneBuilding.transform.SetParent(_root.transform);
            sceneBuilding.Cell = new Vector2Int(9, 9);

            int cleared = build.ClearRuntimePlacedBuildings();

            Assert.AreEqual(2, cleared);
            Assert.IsFalse(grid.Map.IsOccupied(new Vector2Int(1, 0)), "the cell must be freed too");
            Assert.IsFalse(grid.Map.IsOccupied(new Vector2Int(2, 0)));
            Assert.IsTrue(sceneBuilding != null,
                "a scene-authored building is not the load's to demolish");
        }
    }
}
