using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// A golem pushing into a labelled crate (docs/cozy-automation-design.md §1). This is where
    /// the feature actually becomes a sorter: a mixed hold delivers what the crate is for and
    /// keeps the rest, and a hold with none of it stalls saying so.
    /// </summary>
    public class DepotFilterRoutingTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                Object.DestroyImmediate(go);
            }

            _spawned.Clear();
        }

        private T AddHolder<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private static AppendageActionDefinition PushStep()
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.Push;
            step.durationTicks = 1;
            return step;
        }

        // A Push-only program has no Assemble, so the pure-logistics rule applies and the golem's
        // INPUT stock is what Push drains. That is what lets these tests seed a hold directly.
        private GolemEntity PushOnlyGolem(SpatialEndpointRegistryHolder endpoints)
        {
            var go = new GameObject("Sorter");
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure("Sorter", null);

            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(PushStep());
            entity.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            return entity;
        }

        [Test]
        public void AMixedHoldDeliversWhatTheCrateIsFor_AndKeepsTheRest()
        {
            var stockpile = new StorageBuffer("FactoryStockpile");
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(
                new Vector2Int(0, 2), new FilteredBufferEndpoint(stockpile, ItemType.IronPlate));

            GolemEntity golem = PushOnlyGolem(endpoints);
            golem.Inventory.AddInput(ItemType.IronPlate, 4);
            golem.Inventory.AddInput(ItemType.Slag, 3);

            golem.Tick(0);

            Assert.AreEqual(4, stockpile.GetQuantity(ItemType.IronPlate), "the labelled good goes in");
            Assert.AreEqual(0, stockpile.GetQuantity(ItemType.Slag), "the rest does not");
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.IronPlate));
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Slag), "kept for the next tile");
            Assert.AreEqual(GolemState.Running, golem.Program.State, "a partial push is progress");
        }

        [Test]
        public void AHoldWithNoneOfTheLabelStallsFilterMismatch_NamingTheGoodItIsStuckWith()
        {
            var stockpile = new StorageBuffer("FactoryStockpile");
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(
                new Vector2Int(0, 2), new FilteredBufferEndpoint(stockpile, ItemType.IronPlate));

            GolemEntity golem = PushOnlyGolem(endpoints);
            golem.Inventory.AddInput(ItemType.Slag, 3);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.FilterMismatch, golem.StallReason);
            // The item type, not the depot's name: the fix is to re-route this good, and a
            // player told "FactoryStockpile" would go and look at a crate that is not full.
            Assert.AreEqual(ItemType.Slag, golem.StallResourceId);
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Slag), "nothing is lost on a stall");
        }

        [Test]
        public void ACrateFullOfItsOwnGoodStallsBeltFull_NotFilterMismatch()
        {
            // The distinction the two reasons exist for: this one is fixed by WAITING, and the
            // other by going somewhere else. Reporting a mismatch here would send the player off
            // to re-route a good that is in exactly the right crate.
            var stockpile = new StorageBuffer("Capped", capacityPerType: 2);
            stockpile.Deposit(ItemType.IronPlate, 2);

            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(
                new Vector2Int(0, 2), new FilteredBufferEndpoint(stockpile, ItemType.IronPlate));

            GolemEntity golem = PushOnlyGolem(endpoints);
            golem.Inventory.AddInput(ItemType.IronPlate, 3);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.BeltFull, golem.StallReason);
            Assert.AreEqual("Capped", golem.StallResourceId);
        }

        [Test]
        public void ItResumesOnceTheCrateIsRelabelled()
        {
            // The player's actual fix, end to end: a stalled sorter starts moving the moment the
            // crate in front of it is given the label its hold is full of.
            var stockpile = new StorageBuffer("FactoryStockpile");
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var cell = new Vector2Int(0, 2);
            endpoints.Registry.Register(cell, new FilteredBufferEndpoint(stockpile, ItemType.IronPlate));

            GolemEntity golem = PushOnlyGolem(endpoints);
            golem.Inventory.AddInput(ItemType.Slag, 3);

            golem.Tick(0);
            Assert.AreEqual(StallReason.FilterMismatch, golem.StallReason);

            endpoints.Registry.Register(cell, new FilteredBufferEndpoint(stockpile, ItemType.Slag));
            golem.Tick(1);

            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Assert.AreEqual(3, stockpile.GetQuantity(ItemType.Slag));
        }

        [Test]
        public void AnUnlabelledDepotIsUnchanged()
        {
            // The opt-in-by-null rule: a depot nobody labelled behaves byte for byte as it did
            // before filters existed, mixed hold and all.
            var stockpile = new StorageBuffer("FactoryStockpile");
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(stockpile));

            GolemEntity golem = PushOnlyGolem(endpoints);
            golem.Inventory.AddInput(ItemType.IronPlate, 4);
            golem.Inventory.AddInput(ItemType.Slag, 3);

            golem.Tick(0);

            Assert.AreEqual(4, stockpile.GetQuantity(ItemType.IronPlate));
            Assert.AreEqual(3, stockpile.GetQuantity(ItemType.Slag));
        }

        [Test]
        public void ALabelledCrateIsAlsoATypedSource()
        {
            // The half that fixes StorageBufferEndpoint.PreferredItemType never being set: a
            // golem hauling from a labelled crate pulls the label, not whichever good the shared
            // stockpile happens to enumerate first.
            var stockpile = new StorageBuffer("FactoryStockpile");
            stockpile.Deposit(ItemType.Scrap, 40);
            stockpile.Deposit(ItemType.Coke, 6);

            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            endpoints.Registry.Register(
                new Vector2Int(0, 0), new FilteredBufferEndpoint(stockpile, ItemType.Coke));

            var haul = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            haul.actionType = AppendageActionType.Haul;
            haul.durationTicks = 1;

            var go = new GameObject("Hauler");
            _spawned.Add(go);
            GolemEntity golem = go.AddComponent<GolemEntity>();
            golem.Configure("Hauler", null);
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            golem.Program.logicCore = logicCore;
            golem.Program.appendages.Add(haul);
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.Greater(golem.Inventory.GetInput(ItemType.Coke), 0, "it pulled the label");
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.Scrap), "and nothing else");
            Assert.AreEqual(40, stockpile.GetQuantity(ItemType.Scrap));
        }
    }
}
