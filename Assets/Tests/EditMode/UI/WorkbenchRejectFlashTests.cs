using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // The flash that gives a rejected chassis click feedback at the click point instead of
    // only on a status line at the opposite corner of the screen.
    public class WorkbenchRejectFlashTests
    {
        [Test]
        public void ComputeStrength_AtStart_IsFullyFlushed()
        {
            Assert.AreEqual(1f, WorkbenchRejectFlash.ComputeStrength(0f), 0.0001f);
        }

        [Test]
        public void ComputeStrength_DuringHold_StaysFullyFlushed()
        {
            // Held long enough that the flash cannot fall between two frames and be missed.
            Assert.AreEqual(1f, WorkbenchRejectFlash.ComputeStrength(WorkbenchRejectFlash.HoldSeconds * 0.5f), 0.0001f);
            Assert.Greater(WorkbenchRejectFlash.HoldSeconds, 1f / 30f);
        }

        [Test]
        public void ComputeStrength_MidFade_IsBetweenTheEndpoints()
        {
            float mid = WorkbenchRejectFlash.HoldSeconds + (WorkbenchRejectFlash.TotalSeconds - WorkbenchRejectFlash.HoldSeconds) * 0.5f;
            float strength = WorkbenchRejectFlash.ComputeStrength(mid);

            Assert.Greater(strength, 0f);
            Assert.Less(strength, 1f);
        }

        [Test]
        public void ComputeStrength_AtAndBeyondTheEnd_IsBackToNormal()
        {
            Assert.AreEqual(0f, WorkbenchRejectFlash.ComputeStrength(WorkbenchRejectFlash.TotalSeconds), 0.0001f);
            Assert.AreEqual(0f, WorkbenchRejectFlash.ComputeStrength(WorkbenchRejectFlash.TotalSeconds + 5f), 0.0001f);
        }

        [Test]
        public void ComputeStrength_DecreasesMonotonicallyOverTheFade()
        {
            float previous = 1f;
            for (float t = WorkbenchRejectFlash.HoldSeconds; t <= WorkbenchRejectFlash.TotalSeconds; t += 0.02f)
            {
                float current = WorkbenchRejectFlash.ComputeStrength(t);
                Assert.LessOrEqual(current, previous + 0.0001f);
                previous = current;
            }
        }
    }
}
