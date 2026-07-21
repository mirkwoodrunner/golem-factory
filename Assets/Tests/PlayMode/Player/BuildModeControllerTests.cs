using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    public class BuildModeControllerTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 0.5f);

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            // PlaceOrRemove-spawned buildings aren't parented under _root, so sweep them too.
            foreach (PlaceableBuilding building in Object.FindObjectsByType<PlaceableBuilding>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(building.gameObject);
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_OnEmptyCell_SpawnsBuildingAndOccupiesCell()
        {
            (BuildModeController controller, GridMapHolder gridMapHolder) = Build();
            var cell = new Vector2Int(1, 1);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsTrue(gridMapHolder.Map.IsOccupied(cell));
            gridMapHolder.Map.TryGetOccupant(cell, out object occupant);
            Assert.IsInstanceOf<PlaceableBuilding>(occupant);
            Assert.AreEqual(cell, ((PlaceableBuilding)occupant).Cell);
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_OnOccupiedCell_RemovesBuildingAndFreesCell()
        {
            (BuildModeController controller, GridMapHolder gridMapHolder) = Build();
            var cell = new Vector2Int(2, -1);
            controller.PlaceOrRemove(cell);
            yield return null;

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsFalse(gridMapHolder.Map.IsOccupied(cell));
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_DifferentCells_BothOccupied()
        {
            (BuildModeController controller, GridMapHolder gridMapHolder) = Build();
            var cellA = new Vector2Int(0, 0);
            var cellB = new Vector2Int(1, 0);

            controller.PlaceOrRemove(cellA);
            controller.PlaceOrRemove(cellB);
            yield return null;

            Assert.IsTrue(gridMapHolder.Map.IsOccupied(cellA));
            Assert.IsTrue(gridMapHolder.Map.IsOccupied(cellB));
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_WithoutPrefab_DoesNotOccupyCell()
        {
            _root = new GameObject("Root");
            var gridMapHolder = new GameObject("GridMap").AddComponent<GridMapHolder>();
            gridMapHolder.transform.SetParent(_root.transform);

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, gridMapHolder, null, CellSize);

            controller.PlaceOrRemove(Vector2Int.zero);
            yield return null;

            Assert.IsFalse(gridMapHolder.Map.IsOccupied(Vector2Int.zero));
        }

        private (BuildModeController controller, GridMapHolder gridMapHolder) Build()
        {
            _root = new GameObject("Root");

            var gridMapHolder = new GameObject("GridMap").AddComponent<GridMapHolder>();
            gridMapHolder.transform.SetParent(_root.transform);

            var prefab = new GameObject("PlaceholderPrefab").AddComponent<PlaceableBuilding>();
            prefab.transform.SetParent(_root.transform);

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, gridMapHolder, prefab, CellSize);

            return (controller, gridMapHolder);
        }
    }
}
