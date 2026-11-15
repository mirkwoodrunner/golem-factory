namespace GolemFactory.UI
{
    /// <summary>
    /// The Workbench's read head: the glow on the socket whose card the golem is on
    /// (<see cref="WorkbenchSession.ReadHeadRow"/>), and the sweep that crosses a card as the head
    /// arrives on it. Pure, timed in real seconds since the head moved, ending at rest; the screen
    /// and <c>punch_card.gdshader</c> only draw it.
    /// </summary>
    public static class WorkbenchReadHead
    {
        /// <summary>How long the sweep takes to cross the card the head has just reached.</summary>
        public const float SweepSeconds = 0.32f;

        /// <summary>
        /// How far across the card the sweep has got, 0..1, <paramref name="age"/> seconds after
        /// the head arrived; 1 (past the far edge, nothing drawn) once it is over, before it began,
        /// and at once under Reduced motion.
        /// </summary>
        public static float Sweep(float age, bool motion = true)
        {
            if (!motion || age < 0f || age >= SweepSeconds)
            {
                return 1f;
            }
            float t = age / SweepSeconds;
            // Fast off the perforations, easing as it reaches the far edge.
            return 1f - (1f - t) * (1f - t);
        }
    }
}
