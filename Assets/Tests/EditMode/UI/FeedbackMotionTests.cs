using NUnit.Framework;
using UnityEngine;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    public class FeedbackMotionTests
    {
        [Test]
        public void Breathe01_StaysInsideZeroToOne()
        {
            for (float t = 0f; t < 4f; t += 0.05f)
            {
                float v = FeedbackMotion.Breathe01(t, 1.6f);
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, 1f);
            }
        }

        [Test]
        public void Breathe01_IsPeriodic()
        {
            const float period = 1.6f;

            Assert.AreEqual(
                FeedbackMotion.Breathe01(0.4f, period),
                FeedbackMotion.Breathe01(0.4f + period, period),
                0.0001f);
        }

        [Test]
        public void Breathe01_ActuallyVaries()
        {
            float low = FeedbackMotion.Breathe01(1.2f, 1.6f);
            float high = FeedbackMotion.Breathe01(0.4f, 1.6f);

            Assert.Greater(high - low, 0.9f, "A pulse that never changes brightness is not a pulse.");
        }

        [Test]
        public void Breathe01_NonPositivePeriod_ReturnsMidpointInsteadOfDividingByZero()
        {
            Assert.AreEqual(0.5f, FeedbackMotion.Breathe01(3f, 0f), 0.0001f);
            Assert.AreEqual(0.5f, FeedbackMotion.Breathe01(3f, -1f), 0.0001f);
        }

        [Test]
        public void Progress01_ClampsBothEnds()
        {
            Assert.AreEqual(0f, FeedbackMotion.Progress01(-1f, 2f), 0.0001f);
            Assert.AreEqual(0.5f, FeedbackMotion.Progress01(1f, 2f), 0.0001f);
            Assert.AreEqual(1f, FeedbackMotion.Progress01(5f, 2f), 0.0001f);
        }

        [Test]
        public void Progress01_NonPositiveDuration_ReadsAsFinished()
        {
            Assert.AreEqual(1f, FeedbackMotion.Progress01(0f, 0f), 0.0001f);
        }

        [Test]
        public void PulseScale_StartsAndEndsAtOne_AndPeaksEarly()
        {
            const float duration = 0.22f;
            const float amount = 0.16f;

            Assert.AreEqual(1f, FeedbackMotion.PulseScale(0f, duration, amount), 0.0001f);
            Assert.AreEqual(1f, FeedbackMotion.PulseScale(duration, duration, amount), 0.0001f);

            float peak = FeedbackMotion.PulseScale(duration * 0.25f, duration, amount);
            Assert.AreEqual(1f + amount, peak, 0.0001f);

            // Asymmetric on purpose: it must be past its peak by the halfway point, or it
            // reads as a slow throb instead of an impact.
            Assert.Less(FeedbackMotion.PulseScale(duration * 0.5f, duration, amount), peak);
        }

        [Test]
        public void PulseScale_NeverGoesBelowOne()
        {
            for (float t = 0f; t <= 0.3f; t += 0.005f)
            {
                Assert.GreaterOrEqual(FeedbackMotion.PulseScale(t, 0.22f, 0.16f), 1f);
            }
        }

        [Test]
        public void FadeOutAlpha_HoldsFullyOpaqueBeforeFading()
        {
            Assert.AreEqual(1f, FeedbackMotion.FadeOutAlpha(0f, 1f), 0.0001f);
            Assert.AreEqual(1f, FeedbackMotion.FadeOutAlpha(0.4f, 1f), 0.0001f);
            Assert.Less(FeedbackMotion.FadeOutAlpha(0.8f, 1f), 1f);
            Assert.AreEqual(0f, FeedbackMotion.FadeOutAlpha(1f, 1f), 0.0001f);
        }

        [Test]
        public void FadeOutAlpha_IsMonotonicallyNonIncreasing()
        {
            float previous = 1f;
            for (float t = 0f; t <= 1f; t += 0.02f)
            {
                float alpha = FeedbackMotion.FadeOutAlpha(t, 1f);
                Assert.LessOrEqual(alpha, previous + 0.0001f);
                previous = alpha;
            }
        }

        [Test]
        public void RiseOffset_TravelsTheFullDistanceAndDecelerates()
        {
            Assert.AreEqual(0f, FeedbackMotion.RiseOffset(0f, 1f, 0.55f), 0.0001f);
            Assert.AreEqual(0.55f, FeedbackMotion.RiseOffset(1f, 1f, 0.55f), 0.0001f);

            float firstHalf = FeedbackMotion.RiseOffset(0.5f, 1f, 0.55f);
            float secondHalf = 0.55f - firstHalf;
            Assert.Greater(firstHalf, secondHalf, "Rise should be fast first, then settle.");
        }
    }
}
