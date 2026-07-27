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
    }
}
