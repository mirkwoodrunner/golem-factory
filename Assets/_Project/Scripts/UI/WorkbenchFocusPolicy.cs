namespace GolemFactory.UI
{
    /// <summary>
    /// §8's "Patents become the scaling tool": what pulling the lever costs in Focus.
    ///
    /// <para>
    /// A flat cost per reprogram is the same price for a two-step Scavenger and a six-step
    /// Zeppelin, which makes patenting a convenience rather than a decision. Scaling with
    /// program length is what turns it into the Factorio blueprint arc the design wants: by
    /// stage 4 the player needs ~23 identical coking golems and ~12 identical smelters, and
    /// patenting once and stamping the rest becomes obviously correct rather than merely tidy.
    /// </para>
    ///
    /// <para>
    /// Pure and engine-free, like <c>StepDurationRules</c> and <c>ConstructionCostPolicy</c>:
    /// the Workbench charges it, the readout quotes it, and both read the same function rather
    /// than two copies of the arithmetic.
    /// </para>
    /// </summary>
    public static class WorkbenchFocusPolicy
    {
        /// <summary>§8: <c>8 + 6 × appendageCount</c>.</summary>
        public const float EngageBase = 8f;

        /// <summary>§8: the per-appendage term.</summary>
        public const float EngagePerAppendage = 6f;

        /// <summary>
        /// §8: committing an already-patented blueprint is a FLAT 10, whatever its length.
        /// That flatness is the whole mechanism -- it is what makes the second identical golem
        /// cheaper than the first and the twenty-third much cheaper.
        /// </summary>
        public const float StampedBlueprintCost = 10f;

        /// <summary>
        /// What Engage Gears costs for a program of <paramref name="appendageCount"/> steps.
        /// A draft loaded from a patent costs <see cref="StampedBlueprintCost"/> instead, which
        /// is the saving the patent bought.
        /// </summary>
        public static float EngageCost(int appendageCount, bool fromPatentedBlueprint)
        {
            if (fromPatentedBlueprint)
            {
                return StampedBlueprintCost;
            }

            int steps = appendageCount < 0 ? 0 : appendageCount;
            return EngageBase + EngagePerAppendage * steps;
        }

        /// <summary>
        /// Whether stamping actually saves anything at this length -- true from three
        /// appendages up (8 + 18 = 26 against a flat 10). Exposed so the Workbench can say
        /// "patented: 10 Focus" rather than leaving the player to compare two numbers.
        /// </summary>
        public static bool StampingIsCheaper(int appendageCount) =>
            EngageCost(appendageCount, false) > StampedBlueprintCost;
    }
}
