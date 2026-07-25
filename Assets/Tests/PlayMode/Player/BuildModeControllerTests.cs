using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Economy;
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

        [UnityTest]
        public IEnumerator PlaceOrRemove_InsufficientFunds_DoesNotOccupyCellOrSpawnBuilding()
        {
            (BuildModeController controller, GridMapHolder gridMapHolder, _) = BuildWithCost(scrapCost: 10, brassCost: 0);
            var cell = new Vector2Int(3, 3);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsFalse(gridMapHolder.Map.IsOccupied(cell));
            Assert.IsNotEmpty(controller.LastStatusMessage);
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_SufficientFunds_WithdrawsExactCostAndOccupiesCell()
        {
            (BuildModeController controller, GridMapHolder gridMapHolder, StorageBufferRegistryHolder stockpile) =
                BuildWithCost(scrapCost: 10, brassCost: 4);
            stockpile.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 10);
            stockpile.Registry.Deposit("FactoryStockpile", ItemType.Brass, 4);
            var cell = new Vector2Int(4, 4);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsTrue(gridMapHolder.Map.IsOccupied(cell));
            stockpile.Registry.TryGetBuffer("FactoryStockpile", out StorageBuffer buffer);
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Brass));
        }

        [UnityTest]
        public IEnumerator PlaceOrRemove_UnconfiguredStockpile_StaysFreeEvenWithNonZeroCost()
        {
            (BuildModeController controller, GridMapHolder gridMapHolder) = Build();
            controller.ActivePrefab.ConfigureCost(999, 999);
            var cell = new Vector2Int(5, 5);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsTrue(gridMapHolder.Map.IsOccupied(cell));
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

        private (BuildModeController controller, GridMapHolder gridMapHolder, StorageBufferRegistryHolder stockpile) BuildWithCost(int scrapCost, int brassCost)
        {
            (BuildModeController controller, GridMapHolder gridMapHolder) = Build();
            controller.ActivePrefab.ConfigureCost(scrapCost, brassCost);

            var stockpile = new GameObject("Stockpile").AddComponent<StorageBufferRegistryHolder>();
            stockpile.transform.SetParent(_root.transform);
            controller.ConfigureEconomy(stockpile, "FactoryStockpile", new[] { controller.ActivePrefab });

            return (controller, gridMapHolder, stockpile);
        }
    }
}
