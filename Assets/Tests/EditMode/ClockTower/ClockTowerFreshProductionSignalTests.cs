using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The fresh-production signal (docs/progression-design.md §7). ItemAssembledEvent is the one
    // thing in the game that says "this good came into existence just now", and it is what the
    // Clock Tower's multiplier is floored by. Without it a warehouse drain is indistinguishable
    // from a running factory.
    //
    // Two claims are pinned here: that a golem finishing an Assemble publishes it (including for
    // the byproduct), and that a golem merely MOVING goods does not.
    public class ClockTowerFreshProductionSignalTests
    {
        private const string Slag = "Slag";

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private readonly List<ItemAssembledEvent> _heard = new List<ItemAssembledEvent>();

        [SetUp]
        public void SetUp()
        {
            _heard.Clear();
            EventBus.ItemAssembled += Record;
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ItemAssembled -= Record;

            foreach (GameObject go in _spawned)
            {
                Object.DestroyImmediate(go);
            }

            foreach (Object asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }

            _spawned.Clear();
            _assets.Clear();
        }

        private void Record(ItemAssembledEvent e) => _heard.Add(e);

        private T Asset<T>() where T : ScriptableObject
        {
            var created = ScriptableObject.CreateInstance<T>();
            _assets.Add(created);
            return created;
        }

        private RecipeDefinition IronSmelting()
        {
            // R4, the one recipe in the design with a byproduct: 2 Scrap + 1 Coke -> 2 Iron Plate
            // + 1 Slag.
            RecipeDefinition recipe = Asset<RecipeDefinition>();
            recipe.inputs.Add(new RecipeIngredient(ItemType.Scrap, 2));
            recipe.inputs.Add(new RecipeIngredient(ItemType.Coke, 1));
            recipe.outputItemType = ItemType.IronPlate;
            recipe.outputQuantity = 2;
            recipe.byproductItemType = Slag;
            recipe.byproductQuantity = 1;
            recipe.durationTicks = 4;
            return recipe;
        }

        private GolemEntity Golem(params AppendageActionDefinition[] steps)
        {
            var go = new GameObject("Smelter");
            _spawned.Add(go);

            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure("Smelter", null);

            LogicCoreDefinition core = Asset<LogicCoreDefinition>();
            core.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = core;

            foreach (AppendageActionDefinition step in steps)
            {
                entity.Program.appendages.Add(step);
            }

            return entity;
        }

        [Test]
        public void AnAssembleThatFinishes_PublishesItsOutputAndItsByproductAsFreshProduction()
        {
            RecipeDefinition recipe = IronSmelting();

            AppendageActionDefinition card = Asset<AppendageActionDefinition>();
            card.actionType = AppendageActionType.Assemble;
            card.recipe = recipe;

            GolemEntity golem = Golem(card);
            golem.Inventory.AddInput(ItemType.Scrap, 2);
            golem.Inventory.AddInput(ItemType.Coke, 1);

            for (long tick = 0; tick < 4; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(2, _heard.Count, "one event for the output, one for the byproduct");

            Assert.AreEqual(ItemType.IronPlate, _heard[0].ItemType);
            Assert.AreEqual(2, _heard[0].Quantity, "the OUTPUT QUANTITY, not one per completion");
            Assert.IsFalse(_heard[0].IsByproduct);
            Assert.AreEqual("Smelter", _heard[0].GolemId);
            Assert.AreEqual(3L, _heard[0].Tick, "stamped with the tick the recipe finished on");

            // The byproduct is fresh production of its own type. R4's Slag is genuinely new
            // Slag, and section 5.3(c)'s whole disposal economy depends on it counting as such.
            Assert.AreEqual(Slag, _heard[1].ItemType);
            Assert.AreEqual(1, _heard[1].Quantity);
            Assert.IsTrue(_heard[1].IsByproduct);
        }

        [Test]
        public void AnAssembleThatStalls_PublishesNothing()
        {
            // Crediting at withdrawal rather than at completion would let a golem short of its
            // second ingredient count as production it never performed.
            RecipeDefinition recipe = IronSmelting();

            AppendageActionDefinition card = Asset<AppendageActionDefinition>();
            card.actionType = AppendageActionType.Assemble;
            card.recipe = recipe;

            GolemEntity golem = Golem(card);
            golem.Inventory.AddInput(ItemType.Scrap, 2);

            for (long tick = 0; tick < 20; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(0, _heard.Count);
        }

        [Test]
        public void MovingGoodsIsNotProducingThem()
        {
            // THE distinction the hoard-blitz rule rests on. A Haul-and-Push logistics golem
            // shifts goods that already existed; if that counted as fresh production, emptying a
            // warehouse into the tower would earn the full x3 multiplier and the exploit would be
            // wide open again.
            AppendageActionDefinition haul = Asset<AppendageActionDefinition>();
            haul.actionType = AppendageActionType.Haul;
            haul.inputItemType = ItemType.FrameSection;
            haul.durationTicks = 1;

            AppendageActionDefinition push = Asset<AppendageActionDefinition>();
            push.actionType = AppendageActionType.Push;
            push.durationTicks = 1;

            var endpointsGo = new GameObject("Endpoints");
            _spawned.Add(endpointsGo);
            SpatialEndpointRegistryHolder endpoints =
                endpointsGo.AddComponent<SpatialEndpointRegistryHolder>();

            var source = new StorageBuffer("Warehouse");
            source.Deposit(ItemType.FrameSection, 20);
            endpoints.Registry.Register(new Vector2Int(0, -1), new StorageBufferEndpoint(source));

            var site = new ClockTowerSite();
            ClockTowerStageDefinition stage = Asset<ClockTowerStageDefinition>();
            stage.Configure(1, "Foundation", 360, new StageDemand(ItemType.FrameSection, 6));
            site.SetStages(new[] { stage });
            endpoints.Registry.Register(new Vector2Int(0, 1), new ClockTowerInputEndpoint(site));

            GolemEntity golem = Golem(haul, push);
            golem.Program.SetQuantityAt(0, 4);
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);

            for (long tick = 0; tick < 40; tick++)
            {
                site.Tick(tick);
                golem.Tick(tick);
            }

            Assert.AreEqual(0, _heard.Count, "no Assemble ran, so nothing was produced");
            Assert.Greater(site.DeliveryRatePerMinute(ItemType.FrameSection), 0,
                "but goods really did arrive at the tower");
            Assert.AreEqual(0, site.FreshProductionRatePerMinute(ItemType.FrameSection));
            Assert.AreEqual(0L, site.ProgressUnits,
                "delivery alone earns the tower nothing at all");
        }

        [Test]
        public void APushOfAMixedHold_DeliversOnlyWhatTheStageDemandsAndKeepsTheRest()
        {
            // §7's "an input buffer tile golems Push into like any other" -- the tower goes
            // through the same BeginPush, with the same per-type skip a full StorageBuffer slot
            // gets. An undemanded good stays in the golem instead of vanishing into the tower.
            AppendageActionDefinition push = Asset<AppendageActionDefinition>();
            push.actionType = AppendageActionType.Push;
            push.durationTicks = 1;

            var endpointsGo = new GameObject("Endpoints");
            _spawned.Add(endpointsGo);
            SpatialEndpointRegistryHolder endpoints =
                endpointsGo.AddComponent<SpatialEndpointRegistryHolder>();

            var site = new ClockTowerSite();
            ClockTowerStageDefinition stage = Asset<ClockTowerStageDefinition>();
            stage.Configure(1, "Foundation", 360, new StageDemand(ItemType.FrameSection, 6));
            site.SetStages(new[] { stage });
            endpoints.Registry.Register(new Vector2Int(0, 1), new ClockTowerInputEndpoint(site));

            GolemEntity golem = Golem(push);
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);
            golem.Inventory.AddInput(ItemType.FrameSection, 5);
            golem.Inventory.AddInput(ItemType.Lens, 3);

            for (long tick = 0; tick < 20; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(5, site.DeliveryRatePerMinute(ItemType.FrameSection));
            Assert.AreEqual(0, site.DeliveryRatePerMinute(ItemType.Lens));
            Assert.AreEqual(0, golem.Inventory.GetInput(ItemType.FrameSection));
            Assert.AreEqual(3, golem.Inventory.GetInput(ItemType.Lens),
                "the undemanded good was refused, not destroyed");
        }
    }
}
