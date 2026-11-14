using NUnit.Framework;
using GolemFactory.Simulation;

namespace GolemFactory.Tests.EditMode
{
    public class TickSchedulerTests
    {
        [TestCase(4, false, TestName = "Tick_BeforeDueTick_DoesNotFireCallback")]
        [TestCase(5, true, TestName = "Tick_AtDueTick_FiresCallback")]
        public void Tick_FiresACallbackOnlyOnceItIsDue(int tick, bool expected)
        {
            var scheduler = new TickScheduler();
            bool fired = false;
            scheduler.ScheduleAfter(currentTick: 0, ticksFromNow: 5, () => fired = true);

            scheduler.Tick(tick);

            Assert.AreEqual(expected, fired);
        }

        [Test]
        public void Tick_AfterFiring_DoesNotFireCallbackAgain()
        {
            var scheduler = new TickScheduler();
            int callCount = 0;
            scheduler.ScheduleAfter(currentTick: 0, ticksFromNow: 5, () => callCount++);

            scheduler.Tick(5);
            scheduler.Tick(6);

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Tick_FiresMultiplePendingCallbacksIndependently()
        {
            var scheduler = new TickScheduler();
            bool firstFired = false;
            bool secondFired = false;
            scheduler.ScheduleAfter(currentTick: 0, ticksFromNow: 3, () => firstFired = true);
            scheduler.ScheduleAfter(currentTick: 0, ticksFromNow: 7, () => secondFired = true);

            scheduler.Tick(3);
            Assert.IsTrue(firstFired);
            Assert.IsFalse(secondFired);

            scheduler.Tick(7);
            Assert.IsTrue(secondFired);
        }
    }
}
