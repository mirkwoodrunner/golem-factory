using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Events;

namespace GolemFactory.Tests.PlayMode
{
    // The Clock Tower's scene wiring. The holder used to relay ItemAssembledEvent into the site as
    // fresh production; any source counts now (docs/contracts-design.md §11), so it seeds the
    // stages and ticks the site, and listens to nothing. The arithmetic is covered without a
    // scene in ClockTowerSiteTests.
    public class ClockTowerSiteHolderTests
    {
        private readonly List<ClockTowerSiteHolder> _holders = new List<ClockTowerSiteHolder>();

        [TearDown]
        public void TearDown()
        {
            foreach (ClockTowerSiteHolder holder in _holders)
            {
                holder.Detach();
            }
            _holders.Clear();
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
        public void TheHolderListensToNothing_SoMakingAGoodIsNotDeliveringIt()
        {
            int before = EventBus.ListenerCount;
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();
            Assert.AreEqual(before, EventBus.ListenerCount, "no subscription left on the static bus");

            EventBus.Publish(new ItemAssembledEvent("Assembler", ItemType.FrameSection, 3, 10L));
            holder.Tick(11L);

            Assert.AreEqual(0, holder.Site.DeliveryRatePerMinute(ItemType.FrameSection),
                "only a delivery counts");
            Assert.AreEqual(0L, holder.Site.ProgressUnits);
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
        public void ADeliveryThroughTheHolder_BuildsTheStage()
        {
            ClockTowerSiteHolder holder = BuildHolder();
            holder.Attach();

            Assert.AreEqual(6, holder.Site.RecordDelivery(ItemType.FrameSection, 6, 0L));
            holder.Tick(1L);

            Assert.Greater(holder.Site.ProgressUnits, 0L);
        }
    }
}
