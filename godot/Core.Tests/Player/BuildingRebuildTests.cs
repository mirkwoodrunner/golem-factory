using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.Save;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
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



        private (BuildModeController build, GridMap grid, StorageBufferRegistry stockpile)
            Build(bool withBelt = false)
        {

            var grid = new GridMap();

            PlaceableBuilding prefab = new PlaceableBuilding { name = "TestPrefab" };
            if (withBelt)
            {
                prefab.AddPart(new PlaceableBelt());
            }

            var stockpile = new StorageBufferRegistry();

            var build = new BuildModeController();
            build.Configure(grid, prefab);
            build.ConfigureEconomy(stockpile, "FactoryStockpile", new[] { prefab });

            return (build, grid, stockpile);
        }

        [Test]
        public void TryRebuildSavedBuilding_PlacesItOnItsSavedCellAndFacing()
        {
            (BuildModeController build, GridMap grid, _) = Build();
            var cell = new Vector2Int(4, -2);

            bool ok = build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.West, out PlaceableBuilding instance);

            Assert.IsTrue(ok);
            Assert.AreEqual(cell, instance.Cell);
            Assert.AreEqual(Facing.West, instance.Facing,
                "the SAVED facing must win -- inheriting the cursor's would re-plumb the factory");
            Assert.IsTrue(grid.IsOccupied(cell), "a rebuilt building must hold its cell");
            Assert.IsTrue(instance.IsRuntimePlaced, "or it would vanish from the next save");
        }

        [Test]
        public void TryRebuildSavedBuilding_DoesNotChargeTheCostAgain()
        {
            (BuildModeController build, _, StorageBufferRegistry stockpile) = Build();
            build.ActivePrefab.ConfigureCost(30, 0);

            // Empty stockpile: the player has since spent everything, which is the normal case
            // hours into a game and the one that would make a charging load unplayable.
            bool ok = build.TryRebuildSavedBuilding(
                "TestPrefab", new Vector2Int(1, 1), Facing.North, out PlaceableBuilding _);

            Assert.IsTrue(ok, "a rebuild must not be blocked by an empty stockpile");
            Assert.AreEqual(0, stockpile.GetQuantity("FactoryStockpile", ItemType.Scrap));
        }

        [Test]
        public void TryRebuildSavedBuilding_UnknownPrefab_RefusesRatherThanPlacingNothing()
        {
            (BuildModeController build, GridMap grid, _) = Build();

            bool ok = build.TryRebuildSavedBuilding(
                "APrefabThatWasDeleted", new Vector2Int(2, 2), Facing.North, out PlaceableBuilding instance);

            Assert.IsFalse(ok);
            Assert.IsNull(instance);
            Assert.IsFalse(grid.IsOccupied(new Vector2Int(2, 2)),
                "a refused rebuild must not leave the cell occupied by nothing");
        }

        [Test]
        public void TryRebuildSavedBuilding_OccupiedCell_Refuses()
        {
            (BuildModeController build, _, _) = Build();
            var cell = new Vector2Int(3, 3);

            Assert.IsTrue(build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.North, out _));
            Assert.IsFalse(build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.North, out _),
                "two buildings must never share a cell");
        }

        // A restored belt has to exist in the SIMULATION, not just the scene.
        [Test]
        public void ARebuiltBelt_IsRegisteredWithTheBeltNetwork()
        {
            (BuildModeController build, _, _) = Build(withBelt: true);

            var conveyor = new ConveyorSystem();
            var beltNetwork = new BeltNetwork();
            var endpoints = new SpatialEndpointRegistry();
            build.ConfigureBelts(beltNetwork, endpoints, conveyor);

            var cell = new Vector2Int(2, 0);
            Assert.IsTrue(build.TryRebuildSavedBuilding("TestPrefab", cell, Facing.East, out _));

            Assert.IsTrue(beltNetwork.TryGetBelt(cell, out PlacedBelt placed),
                "a rebuilt belt that never reached BeltNetwork is a lane items cannot use");
            Assert.AreEqual(Facing.East, placed.Facing,
                "and it must point the way it was saved pointing");
        }

        // A load REPLACES the built world; scene-authored buildings are not the load's to remove.
        [Test]
        public void ClearRuntimePlacedBuildings_RemovesPlacedOnesAndLeavesSceneOnesAlone()
        {
            (BuildModeController build, GridMap grid, _) = Build();

            build.TryRebuildSavedBuilding("TestPrefab", new Vector2Int(1, 0), Facing.North, out _);
            build.TryRebuildSavedBuilding("TestPrefab", new Vector2Int(2, 0), Facing.North, out _);

            // In the world the way scene furniture is: on the grid, known to build mode, never
            // marked runtime-placed. (Unity found it through FindObjectsByType.)
            var sceneBuilding = new PlaceableBuilding { name = "SceneBuilding" };
            build.RegisterExistingBuilding(sceneBuilding, new Vector2Int(9, 9));

            int cleared = build.ClearRuntimePlacedBuildings();

            Assert.AreEqual(2, cleared);
            Assert.IsFalse(grid.IsOccupied(new Vector2Int(1, 0)), "the cell must be freed too");
            Assert.IsFalse(grid.IsOccupied(new Vector2Int(2, 0)));
            Assert.IsTrue(!sceneBuilding.IsRemoved,
                "a scene-authored building is not the load's to demolish");
        }
    }
}
