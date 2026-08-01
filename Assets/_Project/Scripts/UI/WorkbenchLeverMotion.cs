namespace GolemFactory.UI
{
    // Pure motion curve for the "Engage Gears" lever (docs/digital-design.md: "the
    // physical/visual commit action -- pulling it locks in the current card configuration
    // and boots the golem into the game world"). Engine-free so the throw/hold/return
    // shape is unit-testable without a scene, same idiom as GolemAnimationUtility.
    //
    // 0 = handle at rest (top of the track), 1 = fully thrown (bottom).
    public static class WorkbenchLeverMotion
    {
        // Three phases across one pull: a fast throw down, a brief hold at the bottom
        // (so the commit visibly "lands"), then a slower spring back up.
        public const float ThrowSeconds = 0.10f;
        public const float HoldSeconds = 0.14f;
        public const float ReturnSeconds = 0.26f;

        public const float TotalSeconds = ThrowSeconds + HoldSeconds + ReturnSeconds;

        public static float ComputeHandleNormalized(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f)
            {
                return 0f;
            }

            if (elapsedSeconds < ThrowSeconds)
            {
                // Ease-out: the handle leaves fast and decelerates into the stop.
                float t = elapsedSeconds / ThrowSeconds;
                return 1f - (1f - t) * (1f - t);
            }

            if (elapsedSeconds < ThrowSeconds + HoldSeconds)
            {
                return 1f;
            }

            if (elapsedSeconds < TotalSeconds)
            {
                // Ease-in on the way back, so the spring return reads as slower.
                float t = (elapsedSeconds - ThrowSeconds - HoldSeconds) / ReturnSeconds;
                return 1f - t * t;
            }

            return 0f;
        }
    }
}
