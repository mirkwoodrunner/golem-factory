using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The two halves a station's wiring was split into, so a station that came into the world
    /// AFTER the scene bootstrap's one-shot sweep can be given each half from a different place:
    /// its roster from a template (assets), its services from the scene.
    ///
    /// <para>
    /// The bug behind this: every serialized reference on GolemConstructionStationPrefab is
    /// null, so a station the player paid 25 Scrap + 5 Brass for early-outed in
    /// TryConstructGolem and built nothing, silently.
    /// </para>
    /// </summary>
    public class PlacedStationWiringTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private GolemConstructionStation MakeStation(string name)
        {
            if (_root == null)
            {
                _root = new GameObject("Root");
            }

            var go = new GameObject(name, typeof(PlaceableBuilding));
            go.transform.SetParent(_root.transform);
            return go.AddComponent<GolemConstructionStation>();
        }

        private GolemEntity MakeGolemPrefab()
        {
            var go = new GameObject("GolemPrefab");
            go.transform.SetParent(_root.transform);
            return go.AddComponent<GolemEntity>();
        }

        [Test]
        public void HasBuildRoster_FreshlyPlacedStation_IsFalse()
        {
            GolemConstructionStation station = MakeStation("Placed");

            Assert.IsFalse(station.HasBuildRoster,
                "A station straight off the prefab carries no roster and no golem prefab -- " +
                "that is exactly the state that made it a decorative box.");
        }

        [Test]
        public void ConfigureBuildRoster_ThenHasBuildRoster_IsTrue()
        {
            GolemConstructionStation station = MakeStation("Placed");
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();

            station.ConfigureBuildRoster(new[] { chassis }, MakeGolemPrefab());

            Assert.IsTrue(station.HasBuildRoster);
            Assert.AreEqual(1, station.ChassisRoster.Length);
            Assert.IsNotNull(station.GolemPrefab);
        }

        [Test]
        public void ConfigureSceneServices_DoesNotTouchTheRoster()
        {
            GolemConstructionStation station = MakeStation("Authored");
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            station.ConfigureBuildRoster(new[] { chassis }, MakeGolemPrefab());

            var buffers = new GameObject("Buffers").AddComponent<StorageBufferRegistryHolder>();
            buffers.transform.SetParent(_root.transform);
            station.ConfigureSceneServices(null, null, buffers, null, null, "FactoryStockpile");

            // The whole point of the split: a sweep that hands out scene references must never
            // be able to blank the authored assets a working station already carries.
            Assert.IsTrue(station.HasBuildRoster);
            Assert.AreEqual(chassis, station.ChassisRoster[0]);
        }

        [Test]
        public void ConfigureSceneServices_GivesAPlacedStationAWorkingStockpile()
        {
            GolemConstructionStation station = MakeStation("Placed");
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.cost = new System.Collections.Generic.List<RecipeIngredient>
            {
                new RecipeIngredient(ItemType.Scrap, 5),
            };
            station.ConfigureBuildRoster(new[] { chassis }, MakeGolemPrefab());

            var buffers = new GameObject("Buffers").AddComponent<StorageBufferRegistryHolder>();
            buffers.transform.SetParent(_root.transform);
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 5);

            station.ConfigureSceneServices(null, null, buffers, null, null, "FactoryStockpile");
            bool built = station.TryConstructGolem(chassis, out GolemEntity golem);

            Assert.IsTrue(built, "A wired station must actually build the golem it charges for.");
            Assert.IsNotNull(golem);
            Object.DestroyImmediate(golem.gameObject);
        }

        [Test]
        public void UnwiredStation_RefusesToBuildAndChargesNothing()
        {
            // The pre-fix behaviour, pinned: an unwired station must not quietly take the money.
            GolemConstructionStation station = MakeStation("Placed");
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();

            Assert.IsFalse(station.TryConstructGolem(chassis, out GolemEntity golem));
            Assert.IsNull(golem);
        }
    }
}
