using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §3.3: the buildable area is the ground that is drawn. Before this, BuildModeController
    /// bounded placement by GridMap occupancy alone, so the player could build out past the
    /// kerb into nothing -- always true, and finally visible once the map had an outside.
    /// </summary>
    public class BuildPlacementBoundsTests
    {


        private (BuildModeController controller, GridMap map) Build(bool bounded)
        {

            var map = new GridMap();

            var prefab = new PlaceableBuilding { name = "DepotPrefab" };

            var controller = new BuildModeController();
            controller.Configure(map, prefab);

            if (bounded)
            {
                controller.ConfigurePlacementBounds(FloorLayout.HalfExtent, FloorLayout.StreetDepth);
            }

            return (controller, map);
        }

        [Test]
        public void PlaceOrRemove_PastTheKerb_RefusesAndLeavesTheCellFree()
        {
            (BuildModeController controller, GridMap map) = Build(bounded: true);
            var offGround = new Vector2Int(0, FloorLayout.WorldMinY - 2);

            controller.PlaceOrRemove(offGround);

            Assert.IsFalse(map.IsOccupied(offGround));
            Assert.IsNotEmpty(controller.LastStatusMessage);
        }

        [Test]
        public void PlaceOrRemove_OnTheMarketStreet_IsAllowed()
        {
            // The traders stand at y = -16. If the street were unbuildable, no belt or depot
            // could ever reach them.
            (BuildModeController controller, GridMap map) = Build(bounded: true);
            var stallApproach = new Vector2Int(0, -15);

            controller.PlaceOrRemove(stallApproach);

            Assert.IsTrue(map.IsOccupied(stallApproach));
        }

        [Test]
        public void PlaceOrRemove_InsideTheWorkshop_IsAllowed()
        {
            (BuildModeController controller, GridMap map) = Build(bounded: true);
            var cell = new Vector2Int(3, 4);

            controller.PlaceOrRemove(cell);

            Assert.IsTrue(map.IsOccupied(cell));
        }

        [Test]
        public void Unbounded_ByDefault_SoMainSceneAndTestRigsAreUnchanged()
        {
            (BuildModeController controller, GridMap map) = Build(bounded: false);
            var faraway = new Vector2Int(200, 200);

            controller.PlaceOrRemove(faraway);

            Assert.IsTrue(map.IsOccupied(faraway));
        }

        [Test]
        public void RemovingABuildingOffTheGround_StillWorks()
        {
            // Placed while unbounded (as an old save's building effectively was), then bounded.
            // Litter the player cannot clear would be worse than the bug being fixed.
            (BuildModeController controller, GridMap map) = Build(bounded: false);
            var offGround = new Vector2Int(0, FloorLayout.WorldMinY - 2);
            controller.PlaceOrRemove(offGround);

            controller.ConfigurePlacementBounds(FloorLayout.HalfExtent, FloorLayout.StreetDepth);
            controller.PlaceOrRemove(offGround);

            Assert.IsFalse(map.IsOccupied(offGround));
        }
    }
}
