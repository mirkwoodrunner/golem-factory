using UnityEngine;

namespace GolemFactory.UI
{
    /// <summary>
    /// Pure, engine-decoupled easing used by every short presentation flourish in the
    /// player-facing systems: the interaction ring's idle pulse, the build ghost's blocked
    /// pulse, the pop a resource node gives when harvested, and the rise/fade of a "+1 Scrap"
    /// popup.
    /// <para>
    /// Extracted rather than inlined per call site for the usual reason in this project (see
    /// PlayerMovement.ComputeDisplacement, YSortUtility, BeltSignalUtility): timing curves are
    /// exactly the kind of thing that silently drifts out of sync between four hand-written
    /// copies, and a Mathf.Sin in an Update() is untestable without a scene.
    /// </para>
    /// </summary>
    public static class FeedbackMotion
    {
        /// <summary>
        /// A 0..1 triangle-free sine breathe, phase-locked to absolute time so several
        /// elements pulsing at the same period stay visually in step rather than beating
        /// against each other. Returns 0.5 for a non-positive period instead of dividing by
        /// zero.
        /// </summary>
        public static float Breathe01(float time, float period)
        {
            if (period <= 0f)
            {
                return 0.5f;
            }

            return 0.5f + 0.5f * Mathf.Sin(time * (2f * Mathf.PI / period));
        }

        /// <summary>
        /// Normalized 0..1 progress through an effect, clamped at both ends. A non-positive
        /// duration reads as already finished, so a mis-configured duration makes an effect
        /// vanish rather than stick on screen forever.
        /// </summary>
        public static float Progress01(float elapsed, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(elapsed / duration);
        }

        /// <summary>
        /// A one-shot scale punch: snaps out fast, settles back to 1. Used for the "that
        /// registered" kick on a harvested node. Peaks early (at 25% of the duration) because
        /// a symmetric ease reads as a slow throb, not an impact.
        /// </summary>
        public static float PulseScale(float elapsed, float duration, float peakAmount)
        {
            float t = Progress01(elapsed, duration);
            const float peakAt = 0.25f;
            float shape = t < peakAt
                ? t / peakAt
                : 1f - (t - peakAt) / (1f - peakAt);
            return 1f + peakAmount * Mathf.Clamp01(shape);
        }

        /// <summary>
        /// Alpha for a popup that holds briefly at full opacity and then fades. Holding first
        /// matters: a popup that starts fading on frame one is unreadable at the exact moment
        /// the player's eye arrives.
        /// </summary>
        public static float FadeOutAlpha(float elapsed, float duration)
        {
            float t = Progress01(elapsed, duration);
            const float holdUntil = 0.45f;
            if (t <= holdUntil)
            {
                return 1f;
            }

            return 1f - (t - holdUntil) / (1f - holdUntil);
        }

        /// <summary>
        /// Vertical travel for a floating popup: fast at first, decelerating, so it reads as
        /// something thrown off rather than something drifting.
        /// </summary>
        public static float RiseOffset(float elapsed, float duration, float distance)
        {
            float t = Progress01(elapsed, duration);
            return distance * (1f - (1f - t) * (1f - t));
        }
    }
}
