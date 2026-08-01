using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // The Engage Gears lever's throw/hold/return curve, extracted engine-free so the shape
    // of the pull is testable without a scene or a Canvas (GolemAnimationUtility idiom).
    public class WorkbenchLeverMotionTests
    {
        [Test]
        public void AtRest_BeforeAndAfterThePull_HandleIsAtTheTop()
        {
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeHandleNormalized(0f));
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeHandleNormalized(-1f));
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeHandleNormalized(WorkbenchLeverMotion.TotalSeconds));
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeHandleNormalized(WorkbenchLeverMotion.TotalSeconds + 5f));
        }

        [Test]
        public void DuringHold_HandleSitsFullyThrown()
        {
            float midHold = WorkbenchLeverMotion.ThrowSeconds + WorkbenchLeverMotion.HoldSeconds * 0.5f;
            Assert.AreEqual(1f, WorkbenchLeverMotion.ComputeHandleNormalized(midHold));
        }

        [Test]
        public void ThrowPhase_DeceleratesIntoTheStop()
        {
            // Ease-out: more than half the travel is done by the halfway point in time.
            float half = WorkbenchLeverMotion.ComputeHandleNormalized(WorkbenchLeverMotion.ThrowSeconds * 0.5f);
            Assert.Greater(half, 0.5f);
            Assert.Less(half, 1f);
        }

        [Test]
        public void ThrowPhase_IsMonotonicDownward()
        {
            float previous = 0f;
            for (int i = 1; i <= 10; i++)
            {
                float t = WorkbenchLeverMotion.ThrowSeconds * (i / 10f) * 0.999f;
                float value = WorkbenchLeverMotion.ComputeHandleNormalized(t);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }
        }

        [Test]
        public void ReturnPhase_SpringsBackTowardRest()
        {
            float startOfReturn = WorkbenchLeverMotion.ThrowSeconds + WorkbenchLeverMotion.HoldSeconds;
            float early = WorkbenchLeverMotion.ComputeHandleNormalized(startOfReturn + WorkbenchLeverMotion.ReturnSeconds * 0.25f);
            float late = WorkbenchLeverMotion.ComputeHandleNormalized(startOfReturn + WorkbenchLeverMotion.ReturnSeconds * 0.75f);

            Assert.Less(late, early);
            Assert.Greater(early, 0f);
            Assert.Less(late, 1f);
        }

        [Test]
        public void ReturnIsSlowerThanTheThrow_SoThePullReadsAsASpringBack()
        {
            Assert.Greater(WorkbenchLeverMotion.ReturnSeconds, WorkbenchLeverMotion.ThrowSeconds);
        }
    }
}
