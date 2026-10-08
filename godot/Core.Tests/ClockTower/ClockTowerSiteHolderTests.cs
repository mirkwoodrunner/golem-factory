using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.PlayMode
{
    // The Clock Tower's scene wiring: ClockTowerSiteHolder subscribes to ItemAssembledEvent and
    // relays it into the site as fresh production. The arithmetic itself is covered without a
    // scene in ClockTowerSiteTests; this file covers the wiring half.
    //
    // Ported from Unity's PlayMode suite, which needed Play mode only for OnEnable/OnDisable --
    // Attach()/Detach() now. Unity's `yield return null` after building is Attach(), and
    // `enabled = false` is Detach(). The bus is static, so TearDown detaches everything.
    public class ClockTowerSiteHolderTests
    {
        private readonly List<ClockTowerSiteHolder> _holders = new List<ClockTowerSiteHolder>();
        private readonly List<GolemEntity> _golems = new List<GolemEntity>();

        [TearDown]
        public void TearDown()
        {
            foreach (ClockTowerSiteHolder holder in _holders)
            {
                holder.Detach();
            }
            foreach (GolemEntity golem in _golems)
            {
                golem.Remove();
            }
            _holders.Clear();
            _golems.Clear();
        }

        private ClockTowerSiteHolder BuildHolder()
        {
            var stage = new ClockTowerStageDefinition();
            stage.Configure(1, "Foundation", 360, new StageDemand(ItemType.FrameSection, 6));

            var holder = new ClockTowerSiteHolder();
            holder.Configure(new[] { stage });
            _holders.Add(holder);
            return holder;
        }

        [Test]
        public void TheHolderRelaysItemAssembledEventsIntoFreshProduction()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();

            EventBus.Publish(new ItemAssembledEvent("Assembler", ItemType.FrameSection, 3, 10L));

            Assert.AreEqual(3, holder.Site.FreshProductionRatePerMinute(ItemType.FrameSection));
            Assert.AreEqual(0, holder.Site.DeliveryRatePerMinute(ItemType.FrameSection),
                "producing is not delivering");
        }

        [Test]
        public void TheHolderIgnoresGoodsNoStageEverDemands()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();

            EventBus.Publish(new ItemAssembledEvent("Smelter", ItemType.Slag, 40, 10L));

            Assert.AreEqual(0, holder.Site.FreshProductionRatePerMinute(ItemType.Slag));
        }

        [Test]
        public void ADisabledHolderStopsListening()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();

            holder.Detach(); // Unity: enabled = false

            EventBus.Publish(new ItemAssembledEvent("Assembler", ItemType.FrameSection, 3, 10L));

            Assert.AreEqual(0, holder.Site.FreshProductionRatePerMinute(ItemType.FrameSection));
        }

        [Test]
        public void TheHolderForwardsTicksToTheSite()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();

            holder.Tick(42L);

            Assert.AreEqual(42L, holder.Site.CurrentTick);
        }

        [Test]
        public void AGolemFinishingAnAssembleFeedsTheTowerWithoutAnyDirectWiring()
        {
            // End to end through the bus: nothing connects the golem to the tower except
            // EventBus, which is the architectural point -- Golems/ has no idea the Clock Tower
            // exists, exactly as UI listening to golem state does not reach into Golems/.
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();

            var recipe = new RecipeDefinition();
            recipe.inputs.Add(new RecipeIngredient(ItemType.Casing, 2));
            recipe.outputItemType = ItemType.FrameSection;
            recipe.outputQuantity = 1;
            recipe.durationTicks = 3;

            var card = new AppendageActionDefinition();
            card.actionType = AppendageActionType.Assemble;
            card.recipe = recipe;

            var core = new LogicCoreDefinition();
            core.triggerType = TriggerType.AlwaysOn;

            var golem = new GolemEntity();
            golem.Configure("Assembler", null);
            golem.Program.logicCore = core;
            golem.Program.appendages.Add(card);
            golem.Inventory.AddInput(ItemType.Casing, 2);
            golem.Attach(); // Unity: the golem's OnEnable frame
            _golems.Add(golem);

            for (long tick = 0; tick < 3; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(1, holder.Site.FreshProductionRatePerMinute(ItemType.FrameSection));
        }
    }
}
