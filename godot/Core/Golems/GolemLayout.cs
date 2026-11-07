namespace GolemFactory.Golems
{
    /// <summary>
    /// A count of changes to where golems stand and what they run (docs/endless-design.md §4):
    /// a move, a turn, being carried or set down, removal, a mast bound, a program written or a
    /// card added or taken off. <c>World.StallFactsProbe</c> re-checks its index of who feeds whom
    /// when this moves, instead of on every read: the per-read check walked every golem, so a
    /// starved frame cost the square of the golem count.
    ///
    /// <para>
    /// It only ever counts up. A change it doesn't see (a program field written directly, as the
    /// demos and some tests do) is still caught, because the probe re-checks its whole index once
    /// every presentation frame regardless.
    /// </para>
    /// </summary>
    public static class GolemLayout
    {
        public static long Epoch { get; private set; }

        public static void Changed() => Epoch++;
    }
}
