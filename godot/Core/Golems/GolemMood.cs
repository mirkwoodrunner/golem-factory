namespace GolemFactory.Golems
{
    /// <summary>
    /// What a golem is <em>doing</em>, as the player reads it (docs/cozy-automation-design.md §2).
    ///
    /// <para>
    /// <b>This is not simulation state.</b> It is a classification of state the golem already
    /// exposes -- <c>GolemProgram.State</c>, <c>StallReason</c>, the inventory -- which is exactly
    /// why it can be computed on the presentation side, at frame rate, without the tick loop
    /// knowing it exists. Nothing writes a mood; <see cref="GolemMoodRules.Classify"/> derives it.
    /// </para>
    ///
    /// <para>
    /// Before this there were two visible states: white and bobbing, or red and shaking. A golem
    /// asleep waiting on an Interval trigger, a golem three units from backing up, and a golem
    /// running perfectly all read the same, and a factory-wide steam brownout read as "lots of
    /// red" rather than as one thing with one fix.
    /// </para>
    /// </summary>
    public enum GolemMood
    {
        /// <summary>Running its program. Draws NO badge -- see <see cref="GolemMoodRules"/>.</summary>
        Working = 0,

        /// <summary>Idle, waiting for its trigger to admit the next cycle.</summary>
        Sleeping,

        /// <summary>
        /// Running, but its push stock is close to the per-type cap. The early warning before a
        /// stall: a golem at 9 of 12 with nobody collecting will be stopped within a minute.
        /// </summary>
        Straining,

        /// <summary>
        /// Stalled specifically on <c>StallReason.NoSteam</c>. Split out because it is the one
        /// stall whose fix is a <em>building</em> rather than a rotation or a wait, and the one
        /// that hits the whole factory at once.
        /// </summary>
        Starved,

        /// <summary>Stalled for any other reason.</summary>
        Stalled,

        /// <summary>
        /// Nothing to run: no chassis, or no steps. Distinct from Sleeping because a sleeping
        /// golem is working correctly and this one has never been told what to do.
        /// </summary>
        Unprogrammed
    }
}
