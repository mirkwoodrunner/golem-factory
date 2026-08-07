using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.PlayMode
{
    // The Clock Tower's scene wiring: ClockTowerSiteHolder subscribes to ItemAssembledEvent in
    // OnEnable and relays it into the site as fresh production.
    //
    // PLAYMODE, NOT EDITMODE, and for the documented reason: there is no [ExecuteAlways] anywhere
    // in this project, so OnEnable/OnDisable never run in EditMode -- exactly the gotcha that
    // forces GolemEntity's Signal-trigger tests to live here. The arithmetic itself is covered
    // without a scene in ClockTowerSiteTests; this file only covers the half that a scene is
    // required to observe.
    public class ClockTowerSiteHolderTests
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

        private ClockTowerSiteHolder BuildHolder()
        {
            _root = new GameObject("ClockTower");
            var stage = ScriptableObject.CreateInstance<ClockTowerStageDefinition>();
            stage.Configure(1, "Foundation", 360, new StageDemand(ItemType.FrameSection, 6));

            ClockTowerSiteHolder holder = _root.AddComponent<ClockTowerSiteHolder>();
            holder.Configure(new[] { stage });
            return holder;
        }

        [UnityTest]
        public IEnumerator TheHolderRelaysItemAssembledEventsIntoFreshProduction()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            yield return null;

            EventBus.Publish(new ItemAssembledEvent("Assembler", ItemType.FrameSection, 3, 10L));

            Assert.AreEqual(3, holder.Site.FreshProductionRatePerMinute(ItemType.FrameSection));
            Assert.AreEqual(0, holder.Site.DeliveryRatePerMinute(ItemType.FrameSection),
                "producing is not delivering");
        }

        [UnityTest]
        public IEnumerator TheHolderIgnoresGoodsNoStageEverDemands()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            yield return null;

            EventBus.Publish(new ItemAssembledEvent("Smelter", ItemType.Slag, 40, 10L));

            Assert.AreEqual(0, holder.Site.FreshProductionRatePerMinute(ItemType.Slag));
        }

        [UnityTest]
        public IEnumerator ADisabledHolderStopsListening()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            yield return null;

            holder.enabled = false;
            yield return null;

            EventBus.Publish(new ItemAssembledEvent("Assembler", ItemType.FrameSection, 3, 10L));

            Assert.AreEqual(0, holder.Site.FreshProductionRatePerMinute(ItemType.FrameSection));
        }

        [UnityTest]
        public IEnumerator TheHolderForwardsTicksToTheSite()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            yield return null;

            holder.Tick(42L);

            Assert.AreEqual(42L, holder.Site.CurrentTick);
        }

        [UnityTest]
        public IEnumerator AGolemFinishingAnAssembleFeedsTheTowerWithoutAnyDirectWiring()
        {
            // End to end through the bus: nothing connects the golem to the tower except
            // EventBus, which is the architectural point -- Golems/ has no idea the Clock Tower
            // exists, exactly as UI listening to golem state does not reach into Golems/.
            ClockTowerSiteHolder holder = BuildHolder();
            yield return null;

            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.inputs.Add(new RecipeIngredient(ItemType.Casing, 2));
            recipe.outputItemType = ItemType.FrameSection;
            recipe.outputQuantity = 1;
            recipe.durationTicks = 3;

            var card = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            card.actionType = AppendageActionType.Assemble;
            card.recipe = recipe;

            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.AlwaysOn;

            var golemGo = new GameObject("Assembler");
            golemGo.transform.SetParent(_root.transform);
            GolemEntity golem = golemGo.AddComponent<GolemEntity>();
            golem.Configure("Assembler", null);
            golem.Program.logicCore = core;
            golem.Program.appendages.Add(card);
            golem.Inventory.AddInput(ItemType.Casing, 2);
            yield return null;

            for (long tick = 0; tick < 3; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(1, holder.Site.FreshProductionRatePerMinute(ItemType.FrameSection));
        }
    }
}
