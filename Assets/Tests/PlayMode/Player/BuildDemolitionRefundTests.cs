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
    /// <summary>
    /// Demolition gives the goods back, and the wrecking bar is reachable without a placeable
    /// in hand.
    ///
    /// <para>
    /// <b>Found in playtest as "the ability to pick up depots goes away at some point."</b>
    /// Removal was only ever reachable from a click that passed
    /// <c>BuildClickPolicy.ShouldPlace</c>, which demands a placeable in hand -- invisible while
    /// build mode had no exit, and broken the moment Escape / right-click shipped. And when it
    /// did work it paid nothing back, so every misplaced Depot was 15 Scrap burned.
    /// </para>
    ///
    /// <para>
    /// PlayMode rather than EditMode: placement Instantiates real GameObjects and demolition
    /// Destroys them, which needs a frame to land -- the same reason
    /// <c>BuildModeControllerTests</c> lives here.
    /// </para>
    /// </summary>
    public class BuildDemolitionRefundTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 1f);
        private const string Stockpile = "FactoryStockpile";

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
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
        public IEnumerator Demolishing_APlacedBuilding_RefundsItsWholeCost()
        {
            (BuildModeController controller, _, StorageBufferRegistryHolder stockpile) = Build(scrap: 10, brass: 4);
            stockpile.Registry.Deposit(Stockpile, ItemType.Scrap, 10);
            stockpile.Registry.Deposit(Stockpile, ItemType.Brass, 4);
            var cell = new Vector2Int(2, 2);

            controller.PlaceOrRemove(cell);
            yield return null;
            Assume.That(stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap), Is.EqualTo(0));

            controller.PlaceOrRemove(cell);
            yield return null;

            // FULL, not a salvage fraction: a percentage is a tax on changing your mind, and
            // this is a game whose loop is laying something down and moving it.
            Assert.AreEqual(10, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap));
            Assert.AreEqual(4, stockpile.Registry.GetQuantity(Stockpile, ItemType.Brass));
        }

        [UnityTest]
        public IEnumerator PlaceThenDemolish_LeavesTheStockpileExactlyWhereItStarted()
        {
            // The property that makes "move a building" free: remove and re-place is a no-op on
            // the books, so there is no separate pick-up-and-carry mode to build.
            (BuildModeController controller, _, StorageBufferRegistryHolder stockpile) = Build(scrap: 15, brass: 0);
            stockpile.Registry.Deposit(Stockpile, ItemType.Scrap, 15);

            var from = new Vector2Int(1, 1);
            var to = new Vector2Int(4, 1);

            controller.PlaceOrRemove(from);
            yield return null;
            controller.PlaceOrRemove(from);
            yield return null;
            controller.PlaceOrRemove(to);
            yield return null;

            Assert.AreEqual(0, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap),
                "the building has moved, and cost exactly one building");
        }

        [UnityTest]
        public IEnumerator Demolishing_ASceneAuthoredBuilding_RefundsNothing()
        {
            // Refunding furniture nobody bought would mint goods: demolish the workshop's own
            // authored depots and the stockpile grows out of the scenery.
            (BuildModeController controller, GridMapHolder grid, StorageBufferRegistryHolder stockpile) =
                Build(scrap: 15, brass: 0);

            var authored = new GameObject("AuthoredDepot").AddComponent<PlaceableBuilding>();
            authored.ConfigureCost(15, 0);
            var cell = new Vector2Int(6, 6);
            authored.Cell = cell;
            grid.Map.TryOccupy(cell, authored);
            Assume.That(authored.IsRuntimePlaced, Is.False);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsFalse(grid.Map.IsOccupied(cell), "it still comes down");
            Assert.AreEqual(0, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap));
        }

        [UnityTest]
        public IEnumerator DemolishMode_RemovesWithNoPlaceableInHand_AndPlacesNothing()
        {
            (BuildModeController controller, GridMapHolder grid, StorageBufferRegistryHolder stockpile) =
                Build(scrap: 15, brass: 0);
            stockpile.Registry.Deposit(Stockpile, ItemType.Scrap, 15);
            var cell = new Vector2Int(3, 0);

            controller.PlaceOrRemove(cell);
            yield return null;

            controller.EnterDemolishMode();
            Assert.IsFalse(controller.IsPlacementActive, "the wrecking bar is not a placeable");
            Assert.IsTrue(controller.IsBuildToolActive, "but clicks still belong to build mode");

            controller.PlaceOrRemove(cell);
            yield return null;
            Assert.IsFalse(grid.Map.IsOccupied(cell));
            Assert.AreEqual(15, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap));

            // And a second click on the now-empty tile must not build one back.
            controller.PlaceOrRemove(cell);
            yield return null;
            Assert.IsFalse(grid.Map.IsOccupied(cell), "demolish mode never places");
            Assert.AreEqual(15, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap));
        }

        [UnityTest]
        public IEnumerator ClearRuntimePlacedBuildings_RefundsNothing()
        {
            // A load REPLACES the built world, and it sweeps exactly the buildings a refund pays
            // out on. Paying them back would hand the player their whole factory's cost on every
            // load, on top of the buffers the save then restores -- save, load, save, load is an
            // infinite resource duplicator.
            (BuildModeController controller, _, StorageBufferRegistryHolder stockpile) = Build(scrap: 15, brass: 0);
            stockpile.Registry.Deposit(Stockpile, ItemType.Scrap, 30);

            controller.PlaceOrRemove(new Vector2Int(1, 1));
            controller.PlaceOrRemove(new Vector2Int(2, 1));
            yield return null;
            Assume.That(stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap), Is.EqualTo(0));

            Assert.AreEqual(2, controller.ClearRuntimePlacedBuildings());
            yield return null;

            Assert.AreEqual(0, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap),
                "a load must not mint the factory's cost");
        }

        [UnityTest]
        public IEnumerator WithNoRoomForTheRefund_TheBuildingStaysStanding()
        {
            // The cozy rule: never take goods away as the price of tidying up. A demolition that
            // destroyed what it could not hand back is exactly the punishment a full refund
            // exists to remove, so it refuses instead -- and refuses BEFORE tearing anything
            // down, so the world is untouched rather than half-dismantled.
            (BuildModeController controller, GridMapHolder grid, StorageBufferRegistryHolder stockpile) =
                Build(scrap: 15, brass: 0);
            stockpile.Registry.SetCapacity(Stockpile, 20);
            stockpile.Registry.Deposit(Stockpile, ItemType.Scrap, 15);
            var cell = new Vector2Int(1, 2);

            controller.PlaceOrRemove(cell);
            yield return null;
            // 20-cap, 0 held after paying 15: the 15-Scrap refund fits. Fill it so it cannot.
            stockpile.Registry.Deposit(Stockpile, ItemType.Scrap, 20);
            Assume.That(stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap), Is.EqualTo(20));

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsTrue(grid.Map.IsOccupied(cell), "nothing is destroyed that cannot be paid for");
            Assert.AreEqual(20, stockpile.Registry.GetQuantity(Stockpile, ItemType.Scrap));
            Assert.IsNotEmpty(controller.LastStatusMessage, "and it says why");
        }

        [UnityTest]
        public IEnumerator CancelPlacement_PutsTheWreckingBarDownToo()
        {
            // The three exits from build mode have to cover the destructive tool as well, or
            // the trap CancelPlacement was written to close reopens with worse consequences.
            (BuildModeController controller, _, _) = Build(scrap: 0, brass: 0);
            controller.EnterDemolishMode();

            Assert.IsTrue(controller.CancelPlacement());
            Assert.IsFalse(controller.IsDemolishActive);
            Assert.IsFalse(controller.IsBuildToolActive);
            Assert.IsFalse(controller.CancelPlacement(), "nothing in hand, nothing to put down");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EnterDemolishMode_DropsTheHeldPlaceable()
        {
            (BuildModeController controller, _, _) = Build(scrap: 0, brass: 0);
            Assume.That(controller.IsPlacementActive, Is.True);

            controller.EnterDemolishMode();

            Assert.IsNull(controller.ActivePrefab, "one cursor, one tool");
            Assert.IsTrue(controller.IsDemolishActive);
            yield return null;
        }

        private (BuildModeController controller, GridMapHolder grid, StorageBufferRegistryHolder stockpile)
            Build(int scrap, int brass)
        {
            _root = new GameObject("Root");

            var grid = new GameObject("GridMap").AddComponent<GridMapHolder>();
            grid.transform.SetParent(_root.transform);

            var prefab = new GameObject("PlaceholderPrefab").AddComponent<PlaceableBuilding>();
            prefab.transform.SetParent(_root.transform);
            prefab.ConfigureCost(scrap, brass);

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, grid, prefab, CellSize);

            var stockpile = new GameObject("Stockpile").AddComponent<StorageBufferRegistryHolder>();
            stockpile.transform.SetParent(_root.transform);
            controller.ConfigureEconomy(stockpile, Stockpile, new[] { prefab });

            return (controller, grid, stockpile);
        }
    }
}
