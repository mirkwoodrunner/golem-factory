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
    /// A construction station the player builds has to be wired to the scene, or it is a
    /// building they paid 25 Scrap + 5 Brass for that silently builds nothing. Stations were
    /// only ever configured by SandboxBootstrap's one-shot startup sweep, which by definition
    /// cannot see one placed afterwards.
    /// </summary>
    public class PlacedStationConfigurationTests
    {
        private GameObject _root;

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

        private (BuildModeController controller, GridMapHolder map, PlaceableBuilding prefab) BuildStationPlacer()
        {
            _root = new GameObject("Root");

            var map = new GameObject("GridMap").AddComponent<GridMapHolder>();
            map.transform.SetParent(_root.transform);

            var prefabGo = new GameObject("GolemConstructionStationPrefab", typeof(PlaceableBuilding));
            prefabGo.transform.SetParent(_root.transform);
            prefabGo.AddComponent<GolemConstructionStation>();
            var prefab = prefabGo.GetComponent<PlaceableBuilding>();

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, map, prefab, new Vector2(1f, 1f));

            return (controller, map, prefab);
        }

        [UnityTest]
        public IEnumerator PlacingAStation_AsksTheConfiguratorToWireIt()
        {
            (BuildModeController controller, GridMapHolder map, _) = BuildStationPlacer();
            var configurator = new RecordingConfigurator();
            controller.ConfigureStationWiring(configurator);
            var cell = new Vector2Int(3, -1);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.AreEqual(1, configurator.Calls);
            Assert.IsNotNull(configurator.Configured);
            map.Map.TryGetOccupant(cell, out object occupant);
            Assert.AreSame(
                ((PlaceableBuilding)occupant).GetComponent<GolemConstructionStation>(),
                configurator.Configured,
                "It must wire the INSTANCE that was placed, not the prefab it came from.");
        }

        [UnityTest]
        public IEnumerator RebuildingASavedStation_AlsoAsksTheConfigurator()
        {
            // A save reloads through the same endpoint registration a placement does, so a
            // restored station must come back working rather than as a box.
            (BuildModeController controller, _, PlaceableBuilding prefab) = BuildStationPlacer();
            controller.ConfigureEconomy(null, "FactoryStockpile", new[] { prefab });
            var configurator = new RecordingConfigurator();
            controller.ConfigureStationWiring(configurator);

            bool rebuilt = controller.TryRebuildSavedBuilding(
                prefab.name, new Vector2Int(-2, 4), Facing.South, out PlaceableBuilding instance);
            yield return null;

            Assert.IsTrue(rebuilt);
            Assert.IsNotNull(instance);
            Assert.AreEqual(1, configurator.Calls);
        }

        [UnityTest]
        public IEnumerator PlacingANonStation_NeverCallsTheConfigurator()
        {
            _root = new GameObject("Root");

            var map = new GameObject("GridMap").AddComponent<GridMapHolder>();
            map.transform.SetParent(_root.transform);

            var prefab = new GameObject("DepotPrefab").AddComponent<PlaceableBuilding>();
            prefab.transform.SetParent(_root.transform);

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, map, prefab, new Vector2(1f, 1f));

            var configurator = new RecordingConfigurator();
            controller.ConfigureStationWiring(configurator);

            controller.PlaceOrRemove(new Vector2Int(0, 0));
            yield return null;

            Assert.AreEqual(0, configurator.Calls);
        }

        [UnityTest]
        public IEnumerator PlacingAStation_WithNoConfigurator_StillPlacesTheBuilding()
        {
            // Main.unity and every existing rig never wire one; placement there is unchanged.
            (BuildModeController controller, GridMapHolder map, _) = BuildStationPlacer();
            var cell = new Vector2Int(1, 1);

            controller.PlaceOrRemove(cell);
            yield return null;

            Assert.IsTrue(map.Map.IsOccupied(cell));
        }
    }
}
