using NUnit.Framework;
using GolemFactory.Golems;

namespace GolemFactory.Tests.EditMode
{
    public class GolemAnimationUtilityTests
    {
        [Test]
        public void ComputeIdleBobOffset_ZeroTime_IsZero()
        {
            Assert.AreEqual(0f, GolemAnimationUtility.ComputeIdleBobOffset(time: 0f, amplitude: 0.05f, frequency: 2f));
        }

        [Test]
        public void ComputeIdleBobOffset_ScalesWithAmplitude()
        {
            float small = GolemAnimationUtility.ComputeIdleBobOffset(time: 1f, amplitude: 0.05f, frequency: 2f);
            float large = GolemAnimationUtility.ComputeIdleBobOffset(time: 1f, amplitude: 0.5f, frequency: 2f);

            Assert.AreEqual(small * 10f, large, 1e-4f);
        }

        [Test]
        public void ComputeShakeOffset_AtFullDuration_UsesFullAmplitude()
        {
            float offset = GolemAnimationUtility.ComputeShakeOffset(
                time: 0.25f, shakeTimeRemaining: 1f, shakeDuration: 1f, amplitude: 1f, frequency: 40f);
            float raw = UnityEngine.Mathf.Sin(0.25f * 40f);

            Assert.AreEqual(raw, offset, 1e-4f);
        }

        [Test]
        public void ComputeShakeOffset_AtZeroTimeRemaining_IsZero()
        {
            float offset = GolemAnimationUtility.ComputeShakeOffset(
                time: 0.25f, shakeTimeRemaining: 0f, shakeDuration: 1f, amplitude: 1f, frequency: 40f);

            Assert.AreEqual(0f, offset);
        }

        [Test]
        public void ComputeShakeOffset_HalfwayThroughDuration_HalvesAmplitude()
        {
            float full = GolemAnimationUtility.ComputeShakeOffset(
                time: 0.25f, shakeTimeRemaining: 1f, shakeDuration: 1f, amplitude: 1f, frequency: 40f);
            float half = GolemAnimationUtility.ComputeShakeOffset(
                time: 0.25f, shakeTimeRemaining: 0.5f, shakeDuration: 1f, amplitude: 1f, frequency: 40f);

            Assert.AreEqual(full * 0.5f, half, 1e-4f);
        }

        [Test]
        public void ComputeShakeOffset_ZeroDuration_IsZero()
        {
            float offset = GolemAnimationUtility.ComputeShakeOffset(
                time: 0.25f, shakeTimeRemaining: 0f, shakeDuration: 0f, amplitude: 1f, frequency: 40f);

            Assert.AreEqual(0f, offset);
        }
    }
}
