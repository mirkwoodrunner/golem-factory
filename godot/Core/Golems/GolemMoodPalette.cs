using GolemFactory.Compat;

namespace GolemFactory.Golems
{
    /// <summary>
    /// The colours a <see cref="GolemMood"/> is drawn in (docs/cozy-automation-design.md §2).
    ///
    /// <para>
    /// Separated from <see cref="GolemMoodRules"/> so the rules stay engine-free: the
    /// classification and the dwell table are pure logic a test can call, and this is the part
    /// that needs <c>UnityEngine.Color</c>. Both halves live in <c>Golems/</c> because the badge
    /// (<c>UI/GolemStallIndicator</c>) and the sprite tint (<c>Golems/GolemVisual</c>) both read
    /// them, and a palette owned by either one would be reached across for by the other.
    /// </para>
    ///
    /// <para>
    /// The palette is deliberately not five equally loud colours. Warm amber is the project's
    /// established "something good / pay attention" (the harvest popup, the build ghost); the
    /// desaturated steel is its established "nothing happened" (the out-of-range ring). The two
    /// stopped moods are the only saturated ones, and they are different hues rather than
    /// different reds because a field of blue badges must read as ONE fault, not many.
    /// </para>
    /// </summary>
    public static class GolemMoodPalette
    {
        // --- Sprite tints -----------------------------------------------------------------

        private static readonly Color StalledTint = new Color(0.85f, 0.35f, 0.35f, 1f);

        // A cold steel-blue rather than a second red: a boiler that has gone out is a different
        // kind of wrong from a golem facing a wall, and the whole point of splitting Starved out
        // is that a factory full of these reads as one fault with one fix.
        private static readonly Color StarvedTint = new Color(0.55f, 0.68f, 0.85f, 1f);

        // Barely a change -- a sleeping golem is working correctly, and dimming it hard would
        // make "waiting for its trigger" look like a failure.
        private static readonly Color SleepingTint = new Color(0.88f, 0.88f, 0.90f, 1f);

        /// <summary>The sprite tint for a mood. White for anything running normally.</summary>
        public static Color Tint(GolemMood mood)
        {
            switch (mood)
            {
                case GolemMood.Stalled:
                    return StalledTint;
                case GolemMood.Starved:
                    return StarvedTint;
                case GolemMood.Sleeping:
                case GolemMood.Unprogrammed:
                    return SleepingTint;
                default:
                    // Working and Straining. A straining golem is running, and tinting it would
                    // say "broken" about a machine that is doing its job well enough to fill up.
                    return Color.white;
            }
        }

        // --- Badge plates -------------------------------------------------------------------

        private static readonly Color StalledBadge = new Color(0.75f, 0.15f, 0.10f, 0.92f);
        private static readonly Color StarvedBadge = new Color(0.20f, 0.36f, 0.58f, 0.92f);
        private static readonly Color StrainingBadge = new Color(0.72f, 0.50f, 0.12f, 0.90f);
        private static readonly Color SleepingBadge = new Color(0.28f, 0.31f, 0.35f, 0.62f);
        private static readonly Color UnprogrammedBadge = new Color(0.38f, 0.26f, 0.52f, 0.86f);

        /// <summary>The badge plate colour behind the caption.</summary>
        public static Color Badge(GolemMood mood)
        {
            switch (mood)
            {
                case GolemMood.Stalled:
                    return StalledBadge;
                case GolemMood.Starved:
                    return StarvedBadge;
                case GolemMood.Straining:
                    return StrainingBadge;
                case GolemMood.Unprogrammed:
                    return UnprogrammedBadge;
                default:
                    // Sleeping, and Working -- which never draws one, but must not be able to
                    // draw a badge with an uninitialised colour if that ever changes.
                    return SleepingBadge;
            }
        }

        /// <summary>
        /// How large the badge is drawn, as a multiple of the stall badge's size. The advisory
        /// moods are physically smaller so a room of sleeping golems does not shout.
        /// </summary>
        public static float BadgeScale(GolemMood mood) =>
            GolemMoodRules.IsStopped(mood) ? 1f : 0.7f;
    }
}
