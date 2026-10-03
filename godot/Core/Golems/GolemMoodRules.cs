using GolemFactory.Events;

namespace GolemFactory.Golems
{
    /// <summary>
    /// Turns a golem's live state into a <see cref="GolemMood"/>, and decides how loudly to say
    /// it (docs/cozy-automation-design.md §2).
    ///
    /// <para>
    /// Engine-free static so the classification and the dwell table are a test rather than a
    /// comment -- the same split <c>StallDiagnostics</c>, <c>GridCoordinateConverter</c> and
    /// <c>PlayerMovement.ComputeDisplacement</c> already use. The view owns only a stopwatch.
    /// </para>
    /// </summary>
    public static class GolemMoodRules
    {
        /// <summary>
        /// Units of one good, in the stock <c>Push</c> drains, at which a running golem starts
        /// reading as <see cref="GolemMood.Straining"/>. <b>TUNING.</b>
        ///
        /// <para>
        /// Three quarters of <c>GolemInventory.CapacityPerType</c>. Deliberately expressed as an
        /// absolute rather than a fraction: the cap is an integer the whole design is derived
        /// from, and "9 of 12" is a number the player can count on the readout.
        /// </para>
        /// </summary>
        public const int StrainingUnits = 9;

        /// <summary>
        /// How long <see cref="GolemMood.Sleeping"/> must be held before its badge appears.
        /// <b>TUNING.</b>
        ///
        /// <para>
        /// THIS IS WHY DWELL EXISTS AT ALL. An <c>AlwaysOn</c> golem is <c>Idle</c> for a single
        /// tick at the end of every cycle, so without a dwell it would strobe a badge at cycle
        /// rate. With one, only a golem genuinely waiting on an Interval, Threshold or Signal
        /// trigger reads as asleep -- which is exactly when "asleep" is the useful word.
        /// </para>
        /// </summary>
        public const float SleepingDwellSeconds = 1.5f;

        /// <summary>
        /// Dwell for the two advisory moods. <b>TUNING.</b> Longer than sleep's, because a
        /// momentary spike to 9 units on a line that is keeping up is not news.
        /// </summary>
        public const float AdvisoryDwellSeconds = 2f;

        /// <summary>
        /// The mood this golem is in right now.
        /// </summary>
        /// <param name="state">The program's own state machine.</param>
        /// <param name="stallReason">
        /// Only consulted when <paramref name="state"/> is <see cref="GolemState.Stalled"/>.
        /// </param>
        /// <param name="hasRunnableProgram">
        /// False when there is nothing to run -- no chassis, or no steps. Checked BEFORE the
        /// stall branch on purpose: an unprogrammed golem that has also managed to stall is
        /// still, to the player, a golem nobody has told what to do, and "not wired up" is a
        /// worse sentence than "no program" for something they fix in the Workbench.
        /// </param>
        /// <param name="fullestPushStockUnits">
        /// The largest per-type count in the stock <c>Push</c> would drain. One number rather
        /// than the stock itself, so this stays engine-free and allocation-free.
        /// </param>
        public static GolemMood Classify(
            GolemState state, StallReason stallReason, bool hasRunnableProgram, int fullestPushStockUnits)
        {
            if (!hasRunnableProgram)
            {
                return GolemMood.Unprogrammed;
            }

            if (state == GolemState.Stalled)
            {
                return stallReason == StallReason.NoSteam ? GolemMood.Starved : GolemMood.Stalled;
            }

            if (state == GolemState.Idle)
            {
                return GolemMood.Sleeping;
            }

            // Running, and the only question left is whether it is about to stop being able to.
            return fullestPushStockUnits >= StrainingUnits ? GolemMood.Straining : GolemMood.Working;
        }

        /// <summary>
        /// Seconds this mood must be held before it is worth drawing. Zero for the two that
        /// mean the factory has already stopped.
        /// </summary>
        public static float DwellSeconds(GolemMood mood)
        {
            switch (mood)
            {
                case GolemMood.Stalled:
                case GolemMood.Starved:
                    return 0f;
                case GolemMood.Sleeping:
                    return SleepingDwellSeconds;
                case GolemMood.Straining:
                case GolemMood.Unprogrammed:
                    return AdvisoryDwellSeconds;
                default:
                    // Including Working, which never draws a badge at all -- but a mood with no
                    // entry must fall through to "show it" rather than "hide it forever", so a
                    // seventh mood added later is visibly wrong instead of silently invisible.
                    return 0f;
            }
        }

        /// <summary>
        /// Whether this mood's badge should be on screen after being held for
        /// <paramref name="heldSeconds"/>.
        ///
        /// <para>
        /// <b>Silence is a state.</b> <see cref="GolemMood.Working"/> deliberately draws nothing:
        /// a factory of forty golems each wearing a "working" icon is a factory nobody can read.
        /// The hierarchy the player learns is nothing = fine, amber = soon, red or blue = stopped.
        /// </para>
        /// </summary>
        public static bool ShouldShowBadge(GolemMood mood, float heldSeconds)
        {
            if (mood == GolemMood.Working)
            {
                return false;
            }

            return heldSeconds >= DwellSeconds(mood);
        }

        /// <summary>
        /// True for the moods that mean the golem has stopped and will not restart on its own.
        /// The two that are drawn loud, shaken on entry, and counted by the alerts strip.
        /// </summary>
        public static bool IsStopped(GolemMood mood) =>
            mood == GolemMood.Stalled || mood == GolemMood.Starved;

        /// <summary>
        /// The caption for a mood that does not carry a stall reason of its own. The two stopped
        /// moods are captioned by <c>UI.StallDiagnostics</c> instead, which already names the
        /// blocking resource -- and naming the resource is the whole reason that class exists.
        ///
        /// <para>
        /// ASCII only: TMP's default LiberationSans SDF atlas has no arrows and no U+26A0, the
        /// same constraint <c>StallDiagnostics.ComposeStripText</c> records.
        /// </para>
        /// </summary>
        public static string Caption(GolemMood mood)
        {
            switch (mood)
            {
                case GolemMood.Sleeping:
                    return "zzz";
                case GolemMood.Straining:
                    return "hold nearly full";
                case GolemMood.Unprogrammed:
                    return "no program";
                default:
                    return "";
            }
        }
    }
}
