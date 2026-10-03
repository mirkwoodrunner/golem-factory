namespace GolemFactory.UI
{
    // Pure, engine-free curve for the "that click was rejected" flash the Workbench runs
    // on the chassis plate the player actually clicked.
    //
    // Exists because a rejected chassis click used to produce feedback *only* on a status
    // line at the far bottom-left of the screen -- nothing happened at the interaction
    // point, so the click read as simply not registering. Same idiom as
    // WorkbenchLeverMotion: the shape is unit-testable without a scene, and the
    // MonoBehaviour is a thin applier.
    //
    // 0 = the plate's normal color, 1 = fully flushed to the reject color.
    public static class WorkbenchRejectFlash
    {
        public const float TotalSeconds = 0.45f;
        // How long it stays fully flushed before fading back, so the flash can't be missed
        // between two frames.
        public const float HoldSeconds = 0.12f;

        public static float ComputeStrength(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f)
            {
                return 1f;
            }

            if (elapsedSeconds < HoldSeconds)
            {
                return 1f;
            }

            if (elapsedSeconds >= TotalSeconds)
            {
                return 0f;
            }

            // Linear fade back out over the remainder.
            float t = (elapsedSeconds - HoldSeconds) / (TotalSeconds - HoldSeconds);
            return 1f - t;
        }
    }
}
