using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// A construction station the player builds has to be wired to the scene, or it is a
    /// building they paid 25 Scrap + 5 Brass for that silently builds nothing. Stations were
    /// only ever configured by SandboxBootstrap's one-shot startup sweep, which by definition
    /// cannot see one placed afterwards.
    /// </summary>
    public class PlacedStationConfigurationTests
    {

        // Stands in for SandboxBootstrap, which is the real implementor. The seam is the point
        // of the test: BuildModeController must ASK someone to wire the station rather than
        // holding a second copy of the half-dozen holders a station needs.
        private sealed class RecordingConfigurator : IPlacedStationConfigurator
        {
            public GolemConstructionStation Configured;
            public int Calls;

            public bool ConfigureStation(GolemConstructionStation station)
            {
                Configured = station;
                Calls++;
                return true;
            }
        }


        private (BuildModeController controller, GridMap map, PlaceableBuilding prefab) BuildStationPlacer()
        {

            var map = new GridMap();

            var prefab = new PlaceableBuilding { name = "GolemConstructionStationPrefab" };
            prefab.AddPart(new GolemConstructionStation());

            var controller = new BuildModeController();
            controller.Configure(map, prefab);

            return (controller, map, prefab);
        }

        [Test]
        public void PlacingAStation_AsksTheConfiguratorToWireIt()
        {
            (BuildModeController controller, GridMap map, _) = BuildStationPlacer();
            var configurator = new RecordingConfigurator();
            controller.ConfigureStationWiring(configurator);
            var cell = new Vector2Int(3, -1);

            controller.PlaceOrRemove(cell);

            Assert.AreEqual(1, configurator.Calls);
            Assert.IsNotNull(configurator.Configured);
            map.TryGetOccupant(cell, out object occupant);
            Assert.AreSame(
                ((PlaceableBuilding)occupant).GetPart<GolemConstructionStation>(),
                configurator.Configured,
                "It must wire the INSTANCE that was placed, not the prefab it came from.");
        }

        [Test]
        public void RebuildingASavedStation_AlsoAsksTheConfigurator()
        {
            // A save reloads through the same endpoint registration a placement does, so a
            // restored station must come back working rather than as a box.
            (BuildModeController controller, _, PlaceableBuilding prefab) = BuildStationPlacer();
            controller.ConfigureEconomy(null, "FactoryStockpile", new[] { prefab });
            var configurator = new RecordingConfigurator();
            controller.ConfigureStationWiring(configurator);

            bool rebuilt = controller.TryRebuildSavedBuilding(
                prefab.name, new Vector2Int(-2, 4), Facing.South, out PlaceableBuilding instance);

            Assert.IsTrue(rebuilt);
            Assert.IsNotNull(instance);
            Assert.AreEqual(1, configurator.Calls);
        }

        [Test]
        public void PlacingANonStation_NeverCallsTheConfigurator()
        {

            var map = new GridMap();

            var prefab = new PlaceableBuilding { name = "DepotPrefab" };

            var controller = new BuildModeController();
            controller.Configure(map, prefab);

            var configurator = new RecordingConfigurator();
            controller.ConfigureStationWiring(configurator);

            controller.PlaceOrRemove(new Vector2Int(0, 0));

            Assert.AreEqual(0, configurator.Calls);
        }

        [Test]
        public void PlacingAStation_WithNoConfigurator_StillPlacesTheBuilding()
        {
            // Main.unity and every existing rig never wire one; placement there is unchanged.
            (BuildModeController controller, GridMap map, _) = BuildStationPlacer();
            var cell = new Vector2Int(1, 1);

            controller.PlaceOrRemove(cell);

            Assert.IsTrue(map.IsOccupied(cell));
        }
    }
}
