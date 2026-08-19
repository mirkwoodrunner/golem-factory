using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// §3.3: the buildable area is the ground that is drawn. Before this, BuildModeController
    /// bounded placement by GridMap occupancy alone, so the player could build out past the
    /// kerb into nothing -- always true, and finally visible once the map had an outside.
    /// </summary>
    public class BuildPlacementBoundsTests
    {
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

        private (BuildModeController controller, GridMapHolder map) Build(bool bounded)
        {
            _root = new GameObject("Root");

            var map = new GameObject("GridMap").AddComponent<GridMapHolder>();
            map.transform.SetParent(_root.transform);

            var prefab = new GameObject("DepotPrefab").AddComponent<PlaceableBuilding>();
            prefab.transform.SetParent(_root.transform);

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, map, prefab, new Vector2(1f, 1f));

            if (bounded)
            {
                controller.ConfigurePlacementBounds(FloorLayout.HalfExtent, FloorLayout.StreetDepth);
            }

            return (controller, map);
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_PastTheKerb_RefusesAndLeavesTheCellFree()
        {
            (BuildModeController controller, GridMapHolder map) = Build(bounded: true);
            var offGround = new Vector2Int(0, FloorLayout.WorldMinY - 2);

            controller.PlaceOrRemove(offGround);
            yield return null;

            Assert.IsFalse(map.Map.IsOccupied(offGround));
            Assert.IsNotEmpty(controller.LastStatusMessage);
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_OnTheMarketStreet_IsAllowed()
        {
            // The traders stand at y = -16. If the street were unbuildable, no belt or depot
            // could ever reach them.
            (BuildModeController controller, GridMapHolder map) = Build(bounded: true);
            var stallApproach = new Vector2Int(0, -15);

            controller.PlaceOrRemove(stallApproach);
            yield return null;

            Assert.IsTrue(map.Map.IsOccupied(stallApproach));
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_InsideTheWorkshop_IsAllowed()
        {
            (BuildModeController controller, GridMapHolder map) = Build(bounded: true);
            var cell = new Vector2Int(3, 4);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsTrue(map.Map.IsOccupied(cell));
        }

        [UnityTest]
        public IEnumerator Unbounded_ByDefault_SoMainSceneAndTestRigsAreUnchanged()
        {
            (BuildModeController controller, GridMapHolder map) = Build(bounded: false);
            var faraway = new Vector2Int(200, 200);

            controller.PlaceOrRemove(faraway);
            yield return null;

            Assert.IsTrue(map.Map.IsOccupied(faraway));
        }

        [UnityTest]
        public IEnumerator RemovingABuildingOffTheGround_StillWorks()
        {
            // Placed while unbounded (as an old save's building effectively was), then bounded.
            // Litter the player cannot clear would be worse than the bug being fixed.
            (BuildModeController controller, GridMapHolder map) = Build(bounded: false);
            var offGround = new Vector2Int(0, FloorLayout.WorldMinY - 2);
            controller.PlaceOrRemove(offGround);
            yield return null;

            controller.ConfigurePlacementBounds(FloorLayout.HalfExtent, FloorLayout.StreetDepth);
            controller.PlaceOrRemove(offGround);
            yield return null;

            Assert.IsFalse(map.Map.IsOccupied(offGround));
        }
    }
}
