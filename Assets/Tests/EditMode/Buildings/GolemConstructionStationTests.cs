using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemConstructionStationTests
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

        private static ChassisDefinition MakeChassis(int scrapCost, int brassCost)
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.scrapCost = scrapCost;
            chassis.brassCost = brassCost;
            return chassis;
        }

        private (GolemConstructionStation station, StorageBufferRegistryHolder buffers) Build(ChassisDefinition[] roster)
        {
            _root = new GameObject("Root");

            var golemPrefab = new GameObject("GolemPrefab").AddComponent<GolemEntity>();
            golemPrefab.transform.SetParent(_root.transform);

            var buffers = new GameObject("Buffers").AddComponent<StorageBufferRegistryHolder>();
            buffers.transform.SetParent(_root.transform);

            var stationGo = new GameObject("Station", typeof(PlaceableBuilding));
            stationGo.transform.SetParent(_root.transform);
            var station = stationGo.AddComponent<GolemConstructionStation>();
            station.Configure(roster, golemPrefab, null, null, buffers, null, null, "FactoryStockpile");

            return (station, buffers);
        }

        [Test]
        public void TryConstructGolem_SufficientResources_SpawnsGolemAndWithdrawsCost()
        {
            ChassisDefinition chassis = MakeChassis(scrapCost: 20, brassCost: 10);
            (GolemConstructionStation station, StorageBufferRegistryHolder buffers) = Build(new[] { chassis });
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 20);
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Brass, 10);

            bool result = station.TryConstructGolem(chassis, out GolemEntity golem);

            Assert.IsTrue(result);
            Assert.IsNotNull(golem);
            Assert.AreEqual(chassis, golem.Program.chassis);
            Assert.AreEqual(0, buffers.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, buffers.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Brass));

            Object.DestroyImmediate(golem.gameObject);
        }

        [Test]
        public void TryConstructGolem_InsufficientScrap_Fails_NoWithdrawalNoSpawn()
        {
            ChassisDefinition chassis = MakeChassis(scrapCost: 20, brassCost: 10);
            (GolemConstructionStation station, StorageBufferRegistryHolder buffers) = Build(new[] { chassis });
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Brass, 10);

            bool result = station.TryConstructGolem(chassis, out GolemEntity golem);

            Assert.IsFalse(result);
            Assert.IsNull(golem);
            Assert.AreEqual(10, buffers.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Brass));
        }

        [Test]
        public void TryConstructGolem_InsufficientBrass_Fails_RefundsScrapNoSpawn()
        {
            ChassisDefinition chassis = MakeChassis(scrapCost: 20, brassCost: 10);
            (GolemConstructionStation station, StorageBufferRegistryHolder buffers) = Build(new[] { chassis });
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 20);

            bool result = station.TryConstructGolem(chassis, out GolemEntity golem);

            Assert.IsFalse(result);
            Assert.IsNull(golem);
            Assert.AreEqual(20, buffers.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void TryConstructGolem_EachCallProducesAUniqueGolemId()
        {
            ChassisDefinition chassis = MakeChassis(scrapCost: 0, brassCost: 0);
            (GolemConstructionStation station, StorageBufferRegistryHolder _) = Build(new[] { chassis });

            station.TryConstructGolem(chassis, out GolemEntity golemA);
            station.TryConstructGolem(chassis, out GolemEntity golemB);

            Assert.AreNotEqual(golemA.GolemId, golemB.GolemId);

            Object.DestroyImmediate(golemA.gameObject);
            Object.DestroyImmediate(golemB.gameObject);
        }
    }
}
