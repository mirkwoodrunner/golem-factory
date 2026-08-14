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

        // Costs are item bundles since §1.5 (progression-design §6, §11 item 8). Kept in this
        // helper's Scrap/Brass shape on purpose: these tests are about the STATION's
        // charge/refund/spawn behaviour, not about which goods a chassis costs, and two goods
        // is the smallest bundle that can exercise the partial-charge refund.
        private static ChassisDefinition MakeChassis(int scrapCost, int brassCost)
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.cost = new System.Collections.Generic.List<RecipeIngredient>();
            if (scrapCost > 0)
            {
                chassis.cost.Add(new RecipeIngredient(ItemType.Scrap, scrapCost));
            }

            if (brassCost > 0)
            {
                chassis.cost.Add(new RecipeIngredient(ItemType.Brass, brassCost));
            }

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

        // --- Respawning a saved golem (Save/IGolemRespawner) ---------------------------------
        //
        // The station is the respawner because it is already the spawner: it holds every
        // reference a working golem needs. These exercise the REAL station rather than the fake
        // that GolemRespawnTests drives, because what can go wrong here is specifically the
        // difference between building a golem and rebuilding one.

        [Test]
        public void TryRespawnGolem_DoesNotChargeTheChassisCostAgain()
        {
            // The player paid for this golem in the session that built it. Charging on load
            // would make loading a game a tax -- and would simply fail for a player who has
            // since spent their stockpile, which is to say most of them.
            ChassisDefinition chassis = MakeChassis(scrapCost: 20, brassCost: 10);
            (GolemConstructionStation station, StorageBufferRegistryHolder buffers) = Build(new[] { chassis });
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 20);

            bool result = station.TryRespawnGolem(
                "PlayerGolem-001", chassis, new Vector2Int(2, 5), GolemFactory.World.Facing.South,
                out GolemEntity golem);

            Assert.IsTrue(result, "a respawn must not be blocked by an empty stockpile");
            Assert.AreEqual(20, buffers.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap),
                "the cost was charged a second time");

            Object.DestroyImmediate(golem.gameObject);
        }

        [Test]
        public void TryRespawnGolem_KeepsTheSavedIdAndMarksTheGolemRebuildable()
        {
            ChassisDefinition chassis = MakeChassis(scrapCost: 0, brassCost: 0);
            (GolemConstructionStation station, StorageBufferRegistryHolder _) = Build(new[] { chassis });

            station.TryRespawnGolem(
                "PlayerGolem-007", chassis, Vector2Int.zero, GolemFactory.World.Facing.North,
                out GolemEntity golem);

            Assert.AreEqual("PlayerGolem-007", golem.GolemId,
                "the id is the save key -- a respawn that renames the golem loses it next save");
            Assert.IsTrue(golem.IsRuntimeSpawned,
                "a rebuilt golem must still be rebuildable, or the factory survives one reload");

            Object.DestroyImmediate(golem.gameObject);
        }

        // The collision this prevents is not cosmetic: a golem id is the key for save entries,
        // stall events, and the spatial and steam registries.
        [Test]
        public void TryRespawnGolem_AdvancesTheIdCounterPastEveryRestoredNumber()
        {
            ChassisDefinition chassis = MakeChassis(scrapCost: 0, brassCost: 0);
            (GolemConstructionStation station, StorageBufferRegistryHolder _) = Build(new[] { chassis });

            station.TryRespawnGolem(
                "PlayerGolem-003", chassis, Vector2Int.zero, GolemFactory.World.Facing.North,
                out GolemEntity restored);
            station.TryConstructGolem(chassis, out GolemEntity fresh);

            Assert.AreNotEqual(restored.GolemId, fresh.GolemId,
                "the next golem built took the name of one that was loaded");
            Assert.AreEqual("PlayerGolem-004", fresh.GolemId);

            Object.DestroyImmediate(restored.gameObject);
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void TryRespawnGolem_UnparseableRestoredId_LeavesTheCounterAlone()
        {
            // An id that does not match the generated pattern cannot collide with one, so
            // guessing a number from it would only risk skipping names for no reason.
            ChassisDefinition chassis = MakeChassis(scrapCost: 0, brassCost: 0);
            (GolemConstructionStation station, StorageBufferRegistryHolder _) = Build(new[] { chassis });

            station.TryRespawnGolem(
                "Bob", chassis, Vector2Int.zero, GolemFactory.World.Facing.North,
                out GolemEntity restored);
            station.TryConstructGolem(chassis, out GolemEntity fresh);

            Assert.AreEqual("PlayerGolem-001", fresh.GolemId);

            Object.DestroyImmediate(restored.gameObject);
            Object.DestroyImmediate(fresh.gameObject);
        }

        [Test]
        public void TryRespawnGolem_NoChassis_RefusesRatherThanSpawningAnEmptyGolem()
        {
            (GolemConstructionStation station, StorageBufferRegistryHolder _) = Build(new ChassisDefinition[0]);

            bool result = station.TryRespawnGolem(
                "PlayerGolem-001", null, Vector2Int.zero, GolemFactory.World.Facing.North,
                out GolemEntity golem);

            Assert.IsFalse(result);
            Assert.IsNull(golem, "a chassis-less golem would stand on a tile doing nothing forever");
        }
    }
}
