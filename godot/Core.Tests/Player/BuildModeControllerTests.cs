using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class BuildModeControllerTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 0.5f);


        [TearDown]
        public void TearDown()
        {
            // PlaceOrRemove-spawned buildings aren't parented under _root, so sweep them too.

        }

        [Test]
        public void PlaceOrRemove_OnEmptyCell_SpawnsBuildingAndOccupiesCell()
        {
            (BuildModeController controller, GridMap gridMapHolder) = Build();
            var cell = new Vector2Int(1, 1);

            controller.PlaceOrRemove(cell);

            Assert.IsTrue(gridMapHolder.IsOccupied(cell));
            gridMapHolder.TryGetOccupant(cell, out object occupant);
            Assert.IsInstanceOf<PlaceableBuilding>(occupant);
            Assert.AreEqual(cell, ((PlaceableBuilding)occupant).Cell);
        }

        [Test]
        public void PlaceOrRemove_OnOccupiedCell_RemovesBuildingAndFreesCell()
        {
            (BuildModeController controller, GridMap gridMapHolder) = Build();
            var cell = new Vector2Int(2, -1);
            controller.PlaceOrRemove(cell);

            controller.PlaceOrRemove(cell);

            Assert.IsFalse(gridMapHolder.IsOccupied(cell));
        }

        [Test]
        public void PlaceOrRemove_DifferentCells_BothOccupied()
        {
            (BuildModeController controller, GridMap gridMapHolder) = Build();
            var cellA = new Vector2Int(0, 0);
            var cellB = new Vector2Int(1, 0);

            controller.PlaceOrRemove(cellA);
            controller.PlaceOrRemove(cellB);

            Assert.IsTrue(gridMapHolder.IsOccupied(cellA));
            Assert.IsTrue(gridMapHolder.IsOccupied(cellB));
        }

        [Test]
        public void PlaceOrRemove_WithoutPrefab_DoesNotOccupyCell()
        {
            var gridMapHolder = new GridMap();

            var controller = new BuildModeController();
            controller.Configure(gridMapHolder, null);

            controller.PlaceOrRemove(Vector2Int.zero);

            Assert.IsFalse(gridMapHolder.IsOccupied(Vector2Int.zero));
        }

        [Test]
        public void PlaceOrRemove_InsufficientFunds_DoesNotOccupyCellOrSpawnBuilding()
        {
            (BuildModeController controller, GridMap gridMapHolder, _) = BuildWithCost(scrapCost: 10, brassCost: 0);
            var cell = new Vector2Int(3, 3);

            controller.PlaceOrRemove(cell);

            Assert.IsFalse(gridMapHolder.IsOccupied(cell));
            Assert.IsNotEmpty(controller.LastStatusMessage);
        }

        [Test]
        public void PlaceOrRemove_SufficientFunds_WithdrawsExactCostAndOccupiesCell()
        {
            (BuildModeController controller, GridMap gridMapHolder, StorageBufferRegistry stockpile) =
                BuildWithCost(scrapCost: 10, brassCost: 4);
            stockpile.Deposit("FactoryStockpile", ItemType.Scrap, 10);
            stockpile.Deposit("FactoryStockpile", ItemType.Brass, 4);
            var cell = new Vector2Int(4, 4);

            controller.PlaceOrRemove(cell);

            Assert.IsTrue(gridMapHolder.IsOccupied(cell));
            stockpile.TryGetBuffer("FactoryStockpile", out StorageBuffer buffer);
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Brass));
        }

        [Test]
        public void PlaceOrRemove_UnconfiguredStockpile_StaysFreeEvenWithNonZeroCost()
        {
            (BuildModeController controller, GridMap gridMapHolder) = Build();
            controller.ActivePrefab.ConfigureCost(999, 999);
            var cell = new Vector2Int(5, 5);

            controller.PlaceOrRemove(cell);

            Assert.IsTrue(gridMapHolder.IsOccupied(cell));
        }

        [Test]
        public void PickingAPlaceableAfterDemolish_PlacesAgain()
        {
            // THE BUG, stated as the player met it: pick Demolish, then pick a placeable, then
            // click bare floor -- and nothing is built. Every mode question in BuildModeController
            // asks IsDemolishActive FIRST, so PlaceOrRemove answered an empty tile with "nothing
            // here" and returned before it ever reached PlaceInternal. The stale menu highlight
            // was the visible symptom; this was the behaviour.
            (BuildModeController controller, GridMap gridMapHolder) = Build();
            PlaceableBuilding prefab = controller.ActivePrefab;

            controller.EnterDemolishMode();
            Assert.IsNull(controller.ActivePrefab, "precondition: the bar put the placeable down");

            controller.SetActivePrefab(prefab);
            controller.PlaceOrRemove(new Vector2Int(3, 3));

            Assert.IsFalse(controller.IsDemolishActive);
            Assert.IsTrue(gridMapHolder.IsOccupied(new Vector2Int(3, 3)),
                "the wrecking bar was still in hand, so the click removed nothing and built nothing");
        }

        [Test]
        public void TheTwoToolsAreExclusiveInBothDirections()
        {
            // Pinned as one property rather than two tests, because the failure was precisely
            // that one direction was pinned and the other was not.
            (BuildModeController controller, GridMap _) = Build();
            PlaceableBuilding prefab = controller.ActivePrefab;

            controller.EnterDemolishMode();
            Assert.IsTrue(controller.IsDemolishActive);
            Assert.IsNull(controller.ActivePrefab);

            controller.SetActivePrefab(prefab);
            Assert.IsFalse(controller.IsDemolishActive);
            Assert.AreSame(prefab, controller.ActivePrefab);
        }

        private (BuildModeController controller, GridMap gridMapHolder) Build()
        {

            var gridMapHolder = new GridMap();

            var prefab = new PlaceableBuilding { name = "PlaceholderPrefab" };

            var controller = new BuildModeController();
            controller.Configure(gridMapHolder, prefab);

            return (controller, gridMapHolder);
        }

        private (BuildModeController controller, GridMap gridMapHolder, StorageBufferRegistry stockpile) BuildWithCost(int scrapCost, int brassCost)
        {
            (BuildModeController controller, GridMap gridMapHolder) = Build();
            controller.ActivePrefab.ConfigureCost(scrapCost, brassCost);

            var stockpile = new StorageBufferRegistry();
            controller.ConfigureEconomy(stockpile, "FactoryStockpile", new[] { controller.ActivePrefab });

            return (controller, gridMapHolder, stockpile);
        }
    }
}
