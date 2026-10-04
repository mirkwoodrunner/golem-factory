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

        // The refusal curve. The lever used to run the full committed throw above on
        // every failure path, because Pull() was registered on the same Button.onClick as
        // EngageGears regardless of outcome -- a satisfying pull as feedback for a no-op.

        [Test]
        public void Refused_NeverReachesTheBottomStop()
        {
            for (float t = 0f; t <= WorkbenchLeverMotion.RefuseSeconds; t += 0.005f)
            {
                float n = WorkbenchLeverMotion.ComputeRefusedNormalized(t);
                Assert.LessOrEqual(n, WorkbenchLeverMotion.RefuseDepth + 0.0001f,
                    "a refused pull must visibly catch, not latch at the bottom like a committed one");
            }
        }

        [Test]
        public void Refused_IsShallowerAndShorterThanACommittedPull()
        {
            Assert.Less(WorkbenchLeverMotion.RefuseDepth, 1f);
            Assert.Less(WorkbenchLeverMotion.RefuseSeconds, WorkbenchLeverMotion.TotalSeconds);
        }

        [Test]
        public void Refused_StartsAndEndsAtRest()
        {
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeRefusedNormalized(0f), 0.0001f);
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeRefusedNormalized(-1f), 0.0001f);
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeRefusedNormalized(WorkbenchLeverMotion.RefuseSeconds), 0.0001f);
            Assert.AreEqual(0f, WorkbenchLeverMotion.ComputeRefusedNormalized(WorkbenchLeverMotion.RefuseSeconds + 3f), 0.0001f);
        }

        [Test]
        public void Refused_PeaksInTheMiddleSoTheJudderIsVisible()
        {
            float peak = WorkbenchLeverMotion.ComputeRefusedNormalized(WorkbenchLeverMotion.RefuseSeconds * 0.5f);

            Assert.AreEqual(WorkbenchLeverMotion.RefuseDepth, peak, 0.001f);
            Assert.Greater(peak, 0.05f, "the judder must be big enough to actually see");
        }
    }
}
