using NUnit.Framework;
using GolemFactory.Events;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    public class StallTrackerTests
    {
        [Test]
        public void GolemStalled_AddsToStalledSet()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();
            try
            {
                EventBus.Publish(new GolemStalledEvent("GolemA"));

                Assert.IsTrue(tracker.IsStalled("GolemA"));
                CollectionAssert.Contains(tracker.StalledGolemIds, "GolemA");
            }
            finally
            {
                tracker.Unsubscribe();
            }
        }

        [Test]
        public void GolemResumed_RemovesFromStalledSet()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();
            try
            {
                EventBus.Publish(new GolemStalledEvent("GolemA"));
                EventBus.Publish(new GolemResumedEvent("GolemA"));

                Assert.IsFalse(tracker.IsStalled("GolemA"));
            }
            finally
            {
                tracker.Unsubscribe();
            }
        }

        [Test]
        public void GolemStalled_RepeatedEvents_StaysInSetOnce()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();
            try
            {
                EventBus.Publish(new GolemStalledEvent("GolemA"));
                EventBus.Publish(new GolemStalledEvent("GolemA"));
                EventBus.Publish(new GolemStalledEvent("GolemA"));

                Assert.AreEqual(1, tracker.StalledGolemIds.Count);
            }
            finally
            {
                tracker.Unsubscribe();
            }
        }

        [Test]
        public void GolemResumed_ForUntrackedGolem_IsNoOp()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();
            try
            {
                EventBus.Publish(new GolemResumedEvent("NeverStalled"));

                Assert.IsFalse(tracker.IsStalled("NeverStalled"));
                Assert.AreEqual(0, tracker.StalledGolemIds.Count);
            }
            finally
            {
                tracker.Unsubscribe();
            }
        }

        [Test]
        public void MultipleGolems_TrackedIndependently()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();
            try
            {
                EventBus.Publish(new GolemStalledEvent("GolemA"));
                EventBus.Publish(new GolemStalledEvent("GolemB"));
                EventBus.Publish(new GolemResumedEvent("GolemA"));

                Assert.IsFalse(tracker.IsStalled("GolemA"));
                Assert.IsTrue(tracker.IsStalled("GolemB"));
            }
            finally
            {
                tracker.Unsubscribe();
            }
        }

        [Test]
        public void Unsubscribe_StopsReactingToFurtherEvents()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();
            tracker.Unsubscribe();

            EventBus.Publish(new GolemStalledEvent("GolemA"));

            Assert.IsFalse(tracker.IsStalled("GolemA"));
        }
    }
}
