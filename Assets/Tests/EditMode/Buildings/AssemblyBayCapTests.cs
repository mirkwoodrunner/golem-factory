using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §8's concurrent-golem cap, finally in the loop (§11 item 14). The bay's capacity and
    /// upgrade bookkeeping existed and was tested for several milestones; what it did not have
    /// was a job -- nothing consulted it, so it capped nothing.
    /// </summary>
    public class AssemblyBayCapTests
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

        private AssemblyBayStructure BuildBay()
        {
            _root = new GameObject("Root");
            return _root.AddComponent<AssemblyBayStructure>();
        }

        private GolemEntity MakeGolem(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            return go.AddComponent<GolemEntity>();
        }

        [Test]
        public void BaysStartAtTen_AboveTheNaturalPhaseTwoCount()
        {
            AssemblyBayStructure bay = BuildBay();

            // §8's number, and its reasoning: ten is "above the natural Phase-2 count of ~8",
            // so the cap becomes a decision in the middle game rather than a wall in Phase 1.
            Assert.AreEqual(10, bay.MaxGolemSlots);
            Assert.AreEqual(10, AssemblyBayStructure.DefaultSlots);
            Assert.IsTrue(bay.HasFreeSlot);
        }

        [Test]
        public void UpgradeBuysSixSlotsForPresserTierGoods()
        {
            AssemblyBayStructure bay = BuildBay();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 40);
            buffers.Deposit("FactoryStockpile", ItemType.IronPlate, 20);

            Assert.IsTrue(bay.TryUpgrade(buffers, "FactoryStockpile"));

            Assert.AreEqual(16, bay.MaxGolemSlots);
            Assert.AreEqual(2, bay.Tier);
            Assert.AreEqual(0, buffers.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, buffers.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.IronPlate));
        }

        [Test]
        public void UpgradeCostIsPresserTierOnly_SoTheCapCannotGateOnWhatItBlocks()
        {
            // The load-bearing property in §8: an upgrade priced in Tier-4 goods would be a
            // soft-lock the moment a player filled their bays with the wrong golems.
            AssemblyBayStructure bay = BuildBay();

            foreach (RecipeIngredient ingredient in bay.UpgradeCost)
            {
                Assert.IsTrue(
                    ingredient.itemType == ItemType.Scrap || ingredient.itemType == ItemType.IronPlate,
                    "Bay upgrades must stay payable with hand-crankable goods; found "
                    + ingredient.itemType);
            }
        }

        [Test]
        public void UpgradeShortOfTheBundle_ChargesNothingAndAddsNoSlots()
        {
            AssemblyBayStructure bay = BuildBay();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 40);

            Assert.IsFalse(bay.TryUpgrade(buffers, "FactoryStockpile"));

            Assert.AreEqual(10, bay.MaxGolemSlots);
            Assert.AreEqual(40, buffers.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap),
                "An atomic bundle withdrawal refunds in full -- a partial charge would take the " +
                "Scrap and hand back nothing.");
        }

        [Test]
        public void ADestroyedGolem_GivesItsSlotBack()
        {
            // §10's recovery route from an over-built factory is "delete golems, freeing both
            // bay slots and upkeep instantly". A slot that never came back would break the
            // escape hatch, not just the accounting.
            AssemblyBayStructure bay = BuildBay();
            GolemEntity golem = MakeGolem("Doomed");
            bay.TryAssignGolem(golem);
            Assert.AreEqual(1, bay.OccupiedSlots);

            Object.DestroyImmediate(golem.gameObject);

            Assert.AreEqual(0, bay.OccupiedSlots);
        }

        [Test]
        public void AFullBay_HasNoFreeSlot()
        {
            AssemblyBayStructure bay = BuildBay();
            for (int i = 0; i < AssemblyBayStructure.DefaultSlots; i++)
            {
                Assert.IsTrue(bay.TryAssignGolem(MakeGolem("Golem" + i)));
            }

            Assert.IsFalse(bay.HasFreeSlot);
            Assert.IsFalse(bay.TryAssignGolem(MakeGolem("OneTooMany")));
        }

        [Test]
        public void ForceAssign_TakesAGolemOverTheCap_ForTheLoadPath()
        {
            // A save describes a factory that was legal when it was built. Refusing part of it
            // on load would delete golems the player owns because a cap moved.
            AssemblyBayStructure bay = BuildBay();
            for (int i = 0; i < AssemblyBayStructure.DefaultSlots; i++)
            {
                bay.TryAssignGolem(MakeGolem("Golem" + i));
            }

            bay.ForceAssignGolem(MakeGolem("Restored"));

            Assert.AreEqual(11, bay.OccupiedSlots);
            Assert.IsFalse(bay.HasFreeSlot);
        }

        // --- In the loop ---------------------------------------------------------------------

        private GolemConstructionStation BuildStation(AssemblyBayStructure bay, out ChassisDefinition chassis)
        {
            var golemPrefab = new GameObject("GolemPrefab").AddComponent<GolemEntity>();
            golemPrefab.transform.SetParent(_root.transform);

            var buffers = new GameObject("Buffers").AddComponent<StorageBufferRegistryHolder>();
            buffers.transform.SetParent(_root.transform);

            chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.cost = new System.Collections.Generic.List<RecipeIngredient>
            {
                new RecipeIngredient(ItemType.Scrap, 5),
            };
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 500);

            var stationGo = new GameObject("Station", typeof(PlaceableBuilding));
            stationGo.transform.SetParent(_root.transform);
            var station = stationGo.AddComponent<GolemConstructionStation>();
            station.Configure(
                new[] { chassis }, golemPrefab, null, null, buffers, null, null, "FactoryStockpile");
            station.ConfigureAssemblyBay(bay);
            return station;
        }

        [Test]
        public void TheStationRefusesToBuildPastTheCap_AndChargesNothingForTheRefusal()
        {
            AssemblyBayStructure bay = BuildBay();
            GolemConstructionStation station = BuildStation(bay, out ChassisDefinition chassis);

            var built = new System.Collections.Generic.List<GolemEntity>();
            for (int i = 0; i < AssemblyBayStructure.DefaultSlots; i++)
            {
                Assert.IsTrue(station.TryConstructGolem(chassis, out GolemEntity golem), "golem " + i);
                built.Add(golem);
            }

            station.TryGetStockpile(out int scrapBefore, out _);
            Assert.IsFalse(station.TryConstructGolem(chassis, out GolemEntity refused));
            Assert.IsNull(refused);
            station.TryGetStockpile(out int scrapAfter, out _);

            Assert.AreEqual(scrapBefore, scrapAfter,
                "The cap is checked BEFORE the cost, so a refusal never touches the stockpile.");
            Assert.IsNotEmpty(station.LastRefusalReason);
            StringAssert.Contains("bay", station.LastRefusalReason.ToLowerInvariant());

            foreach (GolemEntity golem in built)
            {
                Object.DestroyImmediate(golem.gameObject);
            }
        }

        [Test]
        public void AStationWithNoBay_BuildsWithoutALimit()
        {
            // Main.unity's demos and every pre-existing rig take this branch, exactly as they
            // do for steam and the extractor cap.
            _root = new GameObject("Root");
            GolemConstructionStation station = BuildStation(null, out ChassisDefinition chassis);

            var built = new System.Collections.Generic.List<GolemEntity>();
            for (int i = 0; i < AssemblyBayStructure.DefaultSlots + 3; i++)
            {
                Assert.IsTrue(station.TryConstructGolem(chassis, out GolemEntity golem));
                built.Add(golem);
            }

            foreach (GolemEntity golem in built)
            {
                Object.DestroyImmediate(golem.gameObject);
            }
        }
    }
}
