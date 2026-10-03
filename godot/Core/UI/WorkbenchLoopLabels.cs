namespace GolemFactory.UI
{
    /// <summary>
    /// The captions on the Workbench's socket stack (docs/cozy-automation-design.md §3).
    ///
    /// <para>
    /// <b>This exists because of a playtest finding.</b> §3y logged <i>"What are steps 1-6?"</i>
    /// and deliberately did not fix it mid-session: the sockets read <c>TRIGGER</c>,
    /// <c>STEP 1</c> … <c>STEP 6</c> and nothing else, and six numbered boxes do not by
    /// themselves say <em>sequence</em> -- nothing at all said <em>loop</em>.
    /// </para>
    ///
    /// <para>
    /// The fix is a second clause on the captions the sockets already have, driven from the live
    /// draft. <c>STEP 3 · loops back to 1</c> is the sentence the finding asked for, it appears
    /// where the player is already looking, and it MOVES as they build: dropping a card into
    /// step 4 walks the loop marker down, which demonstrates the cycle rather than describing it.
    /// No redesign, no new panel, no scene authoring.
    /// </para>
    ///
    /// <para>
    /// Engine-free static so every string is a test rather than a comment, following
    /// <c>WorkbenchDiagnostics</c> and <c>StallDiagnostics</c> beside it.
    /// </para>
    /// </summary>
    public static class WorkbenchLoopLabels
    {
        /// <summary>The logic-core socket. Row 0; every other row is a numbered step.</summary>
        public const int TriggerRow = 0;

        /// <summary>
        /// Latin-1 middle dot, matching the vault headings ("LOGIC CORES  ·  triggers") and the
        /// tech tree's phase titles.
        ///
        /// <para>
        /// NOT an arrow. TMP's default LiberationSans SDF atlas has no entry for U+2193 or
        /// U+21BA and renders them as missing-glyph boxes -- the same constraint
        /// <c>StallDiagnostics.ComposeStripText</c> and <c>GolemStallIndicator</c> record. A
        /// caption made of boxes would be a worse answer to the finding than saying nothing.
        /// </para>
        /// </summary>
        public const string Separator = "  ·  ";

        public const string TriggerCaption = "TRIGGER";
        public const string TriggerHint = "when to start";

        /// <summary>What the trigger row says while there is no chassis to give it sockets.</summary>
        public const string NoChassisHint = "fit a chassis first";

        /// <summary>What the trigger row says while the program is empty.</summary>
        public const string EmptyProgramHint = "drop cards below to build a cycle";

        /// <summary>A filled step that is not the last one.</summary>
        public const string ThenHint = "then";

        /// <summary>The last filled step, which is where the cycle turns around.</summary>
        public const string LoopHint = "loops back to 1";

        /// <summary>A socket inside the chassis's capacity that holds no card.</summary>
        public const string UnusedHint = "unused";

        /// <summary>
        /// The bare caption: <c>TRIGGER</c> for row 0, <c>STEP n</c> otherwise.
        ///
        /// <para>
        /// Derived from the index and never read off the row, for exactly the reason
        /// <c>ProgressionSceneAuthoring.RespaceSlotStack</c> records: <c>AppendageSlot5</c> was
        /// cloned from slot 4 and arrived carrying its caption, so the screen showed
        /// <c>STEP 5</c> twice -- two sockets claiming to be the same step, in the one UI whose
        /// entire job is showing the order of a program.
        /// </para>
        /// </summary>
        public static string Caption(int rowIndex) =>
            rowIndex == TriggerRow ? TriggerCaption : "STEP " + rowIndex;

        /// <summary>
        /// The clause after the separator, or empty for a row that is not part of the cycle.
        /// </summary>
        /// <param name="rowIndex">0 for the trigger, 1-based for steps.</param>
        /// <param name="rowFilled">Whether this socket currently holds a card.</param>
        /// <param name="lastFilledStep">
        /// The highest 1-based step number holding a card, or 0 for an empty program. This is
        /// where the cycle turns around, and it is the LAST FILLED step rather than the last
        /// step: a program with a gap at step 2 still loops from step 3.
        /// </param>
        /// <param name="capacitySteps">The fitted chassis's slot count, or 0 for no chassis.</param>
        public static string Hint(int rowIndex, bool rowFilled, int lastFilledStep, int capacitySteps)
        {
            if (rowIndex == TriggerRow)
            {
                // A progressive teacher: the trigger row is always on screen, so it carries
                // whichever sentence the player needs at their current stage.
                if (capacitySteps <= 0)
                {
                    return NoChassisHint;
                }

                return lastFilledStep <= 0 ? EmptyProgramHint : TriggerHint;
            }

            // Beyond the fitted chassis. Such a socket is only drawn at all when it is occupied
            // by a card the chassis can no longer hold (the render rule RebuildUI records), and
            // it is genuinely not part of the cycle, so it gets no clause rather than a wrong one.
            if (capacitySteps > 0 && rowIndex > capacitySteps)
            {
                return "";
            }

            if (!rowFilled)
            {
                return UnusedHint;
            }

            // A one-step program reads "STEP 1 · loops back to 1", which is correct and slightly
            // funny, and that is the right amount of funny.
            return rowIndex == lastFilledStep ? LoopHint : ThenHint;
        }

        /// <summary>The whole caption line, separator included only when there is a clause.</summary>
        public static string Compose(int rowIndex, bool rowFilled, int lastFilledStep, int capacitySteps)
        {
            string hint = Hint(rowIndex, rowFilled, lastFilledStep, capacitySteps);
            string caption = Caption(rowIndex);
            return string.IsNullOrEmpty(hint) ? caption : caption + Separator + hint;
        }

        /// <summary>
        /// The highest 1-based step number holding a card, or 0 when none do.
        ///
        /// <para>
        /// Takes the draft array's occupancy as a predicate rather than the array itself, so this
        /// stays free of <c>PunchCards</c> and testable with a plain lambda.
        /// </para>
        /// </summary>
        public static int LastFilledStep(int stepCount, System.Func<int, bool> isFilled)
        {
            if (isFilled == null)
            {
                return 0;
            }

            for (int step = stepCount; step >= 1; step--)
            {
                if (isFilled(step))
                {
                    return step;
                }
            }

            return 0;
        }
    }
}
