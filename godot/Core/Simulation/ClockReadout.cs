namespace GolemFactory.Simulation
{
    // Presentation-side formatting for the simulation clock. Engine-free static so it is
    // unit-testable without a scene, the same math/state split GridCoordinateConverter,
    // YSortUtility, WorkbenchDiagnostics, StallDiagnostics and BeltSignalUtility already use.
    //
    // Nothing here reads or mutates the clock -- SimulationClock stays the single source of
    // truth for tick advancement; this only decides how that state is worded.
    public static class ClockReadout
    {
        // The speeds the control bar offers. 1x is the authored TicksPerSecond; the rest are
        // multipliers on it, matching SimulationClock.Speed's meaning.
        public static readonly float[] SpeedPresets = { 0.5f, 1f, 2f, 4f };

        // Ticks are the unit golem programs are authored in (durationTicks, Interval triggers),
        // so the raw counter is genuinely useful to a player debugging a cycle -- but a bare
        // long is unreadable past a few thousand, hence the grouping separator.
        public static string FormatTick(long tick)
        {
            if (tick < 0)
            {
                tick = 0;
            }

            return tick.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Trailing ".0" on whole multipliers reads as noise on a HUD that updates every frame.
        public static string FormatSpeed(float speed)
        {
            if (speed < 0f)
            {
                speed = 0f;
            }

            bool isWhole = speed == (long)speed;
            string number = isWhole
                ? ((long)speed).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return number + "x";
        }

        // Paused is a state the player chose, so it is stated plainly rather than as an alarm.
        // A running clock reports the effective rate, since that is what actually determines
        // how fast their factory runs -- Speed alone is meaningless without TicksPerSecond.
        public static string Describe(ClockState state, float ticksPerSecond, float speed)
        {
            if (state == ClockState.Paused)
            {
                return "PAUSED";
            }

            float effective = ticksPerSecond * speed;
            string rate = effective == (long)effective
                ? ((long)effective).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : effective.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return rate + " ticks/s";
        }

        // Which preset a Play/Pause-independent speed corresponds to, so the bar can show the
        // active one selected. Returns -1 when the clock is on a speed no button offers (a
        // bootstrap or a test may set any value), rather than falsely highlighting the nearest.
        public static int IndexOfSpeed(float speed)
        {
            for (int i = 0; i < SpeedPresets.Length; i++)
            {
                if (SpeedPresets[i] == speed)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
