namespace GolemFactory.Golems
{
    /// <summary>
    /// How long a quantity-driven step takes, as a pure function of its batch size.
    ///
    /// <para>
    /// EXTRACTED SO THE UI CAN QUOTE THE SAME NUMBER THE SIMULATION CHARGES. These two formulas
    /// (docs/progression-design.md §2) used to live inline in <c>GolemEntity.BeginHaul</c> and
    /// <c>BeginExtractFromNode</c>, which was fine while nothing else needed them. The Workbench's
    /// batch-size control needs them: the decision §2 hands the player is throughput traded
    /// against buffer pressure, and a control that let you pick a batch without showing what it
    /// costs in ticks would be hiding the half of the trade that makes it a decision. A second
    /// copy of the arithmetic in the UI would drift from the simulation the first time either was
    /// retuned, and the symptom would be a screen that lies quietly.
    /// </para>
    ///
    /// <para>
    /// Plain C# with no <c>Mathf</c>, following the same instinct as
    /// <c>GolemFactory.Simulation</c>'s engine-free rule -- there is nothing about a duration
    /// that needs Unity.
    /// </para>
    /// </summary>
    public static class StepDurationRules
    {
        /// <summary>
        /// <c>max(2, qty)</c>: a floor of 2 ticks of fixed overhead, then one tick per unit, so a
        /// big batch amortises the fixed cost of the Push at the end of the cycle. That trade --
        /// throughput against holding N units hostage inside one golem and pulling N at a time
        /// out of a shared buffer -- is the decision §2 hands the player.
        /// </summary>
        public static int Haul(int quantity) => quantity < 2 ? 2 : quantity;

        /// <summary>
        /// <c>6 + qty</c>: a fixed setup cost plus one tick per unit, which is what makes a
        /// 4-unit extractor 10 ticks and worth batching. The setup cost is much larger than
        /// Haul's, so extraction rewards batching far more strongly -- which is the whole reason
        /// the two verbs have different formulas rather than one shared one.
        /// </summary>
        public static int ExtractFromNode(int quantity) => 6 + quantity;

        /// <summary>
        /// What a <c>Repeat(n)</c> costs: n runs of the assembly it repeats
        /// (docs/progression-design.md §6's table -- "n x the repeated Assemble").
        ///
        /// <para>
        /// Takes the recipe's own duration rather than a fixed number, because one Assemble
        /// card can point at R1 (12t) or R15 (90t) and a repeat of each costs what each costs.
        /// The golem never calls this -- it runs n real assemblies and pays each duration as it
        /// goes -- so this exists for the Workbench, which has to quote the cost of a decision
        /// before the player commits to it.
        /// </para>
        /// </summary>
        public static int Repeat(int iterations, int assembleDurationTicks) =>
            (iterations < 1 ? 1 : iterations) * (assembleDurationTicks < 1 ? 1 : assembleDurationTicks);
    }
}
