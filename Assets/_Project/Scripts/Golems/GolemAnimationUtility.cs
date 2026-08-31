using UnityEngine;

namespace GolemFactory.Golems
{
    // Pure math for GolemVisual's idle-bob/stall-shake "juice", extracted so it's testable
    // without a scene -- same idiom as World/YSortUtility.ComputeSortingOrder.
    public static class GolemAnimationUtility
    {
        public static float ComputeIdleBobOffset(float time, float amplitude, float frequency) =>
            Mathf.Sin(time * frequency) * amplitude;

        // shakeTimeRemaining counts down from shakeDuration to 0; the shake decays linearly
        // to zero over that window so it reads as a single jolt, not a sustained wobble.
        public static float ComputeShakeOffset(
            float time, float shakeTimeRemaining, float shakeDuration, float amplitude, float frequency)
        {
            if (shakeDuration <= 0f)
            {
                return 0f;
            }

            float decay = Mathf.Clamp01(shakeTimeRemaining / shakeDuration);
            return Mathf.Sin(time * frequency) * amplitude * decay;
        }

        /// <summary>
        /// How a golem in this mood moves (docs/cozy-automation-design.md §2). Scales the
        /// authored bob rather than replacing it, so the Inspector's amplitude and frequency stay
        /// the single place the motion is tuned.
        ///
        /// <para>
        /// A zero amplitude is how "held still" is expressed: <see cref="ComputeIdleBobOffset"/>
        /// returns 0 for it, so the stopped moods need no branch of their own in the applier.
        /// That is why this returns numbers rather than the caller switching on the mood.
        /// </para>
        /// </summary>
        public static BobParameters BobFor(GolemMood mood, float baseAmplitude, float baseFrequency)
        {
            switch (mood)
            {
                case GolemMood.Stalled:
                case GolemMood.Starved:
                    // Dead still. A stalled golem that kept bobbing would read as busy, which is
                    // the opposite of what the badge above it says.
                    return new BobParameters(0f, 0f);
                case GolemMood.Sleeping:
                case GolemMood.Unprogrammed:
                    // Slow and shallow -- breathing rather than working. Frequency drops harder
                    // than amplitude because it is the RATE that reads as effort.
                    return new BobParameters(baseAmplitude * 0.55f, baseFrequency * 0.30f);
                default:
                    // Working, and Straining: a golem whose hold is nearly full is still working,
                    // and slowing it down would say "broken" about a machine doing its job well
                    // enough to fill up.
                    return new BobParameters(baseAmplitude, baseFrequency);
            }
        }

        /// <summary>The amplitude/frequency pair <see cref="BobFor"/> resolves to.</summary>
        public readonly struct BobParameters
        {
            public readonly float Amplitude;
            public readonly float Frequency;

            public BobParameters(float amplitude, float frequency)
            {
                Amplitude = amplitude;
                Frequency = frequency;
            }
        }
    }
}
