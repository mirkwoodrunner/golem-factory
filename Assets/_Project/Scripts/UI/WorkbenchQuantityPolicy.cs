using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    /// <summary>
    /// Which slots get a batch-size control, what it may be set to, and what the player is told
    /// it costs.
    ///
    /// <para>
    /// Pure and engine-free so the rule is testable without a Canvas, following
    /// <c>WorkbenchDropRules</c>, <c>HudScreenPolicy</c> and <c>BuildClickPolicy</c>.
    /// </para>
    ///
    /// <para>
    /// THIS IS THE WORKBENCH'S LAST REAL DECISION, and until now it was data-only. Per-slot batch
    /// size has been stored on <c>GolemProgram.appendageQuantities</c> and saved since the machine
    /// model landed, but nothing exposed it -- so §2's "Consequence 4", the throughput-against-
    /// buffer-pressure trade that is meant to justify keeping the Workbench as the signature
    /// screen once a recipe plus a chassis determines everything else, was unplayable.
    /// </para>
    /// </summary>
    public static class WorkbenchQuantityPolicy
    {
        /// <summary>A batch of zero is a step that does nothing forever, so one is the floor.</summary>
        public const int MinQuantity = 1;

        /// <summary>
        /// The ceiling is the golem's own per-item-type stock cap. Above it the extra units have
        /// nowhere to land, so a bigger number would buy nothing but a longer step -- it would
        /// behave like a permanent partial take, which is exactly what
        /// <c>GolemProgram.SetQuantityAt</c> already clamps against.
        /// </summary>
        public static int MaxQuantity => GolemInventory.CapacityPerType;

        /// <summary>
        /// Whether this card's behaviour actually reads its slot's quantity. Only <c>Haul</c> and
        /// <c>ExtractFromNode</c> do; every other verb ignores it, and showing a stepper on an
        /// <c>Assemble</c> or a <c>Push</c> would advertise a decision that changes nothing --
        /// worse than no control at all, because the player would spend attention on it.
        /// </summary>
        public static bool TakesQuantity(AppendageActionDefinition card) =>
            card != null &&
            (card.actionType == AppendageActionType.Haul ||
             card.actionType == AppendageActionType.ExtractFromNode);

        /// <summary>Clamps to the playable range. Used for both the initial value and each step.</summary>
        public static int Clamp(int quantity) =>
            quantity < MinQuantity ? MinQuantity : (quantity > MaxQuantity ? MaxQuantity : quantity);

        /// <summary>
        /// One press of − or +. Saturates rather than wrapping: a player holding + past the cap
        /// expects it to stop, not to drop back to 1 and quietly retune their factory.
        /// </summary>
        public static int Step(int quantity, int delta) => Clamp(Clamp(quantity) + delta);

        /// <summary>
        /// What the control says. The tick cost is the point of showing anything at all -- the
        /// trade §2 hands the player is throughput against buffer pressure, and a stepper that
        /// showed only the number would hide the half that makes it a decision.
        ///
        /// <para>
        /// Quoted from <see cref="StepDurationRules"/>, the same functions
        /// <c>GolemEntity</c> charges, rather than from a second copy of the arithmetic here.
        /// </para>
        /// </summary>
        public static string Describe(AppendageActionDefinition card, int quantity)
        {
            if (!TakesQuantity(card))
            {
                return string.Empty;
            }

            int clamped = Clamp(quantity);
            int ticks = card.actionType == AppendageActionType.ExtractFromNode
                ? StepDurationRules.ExtractFromNode(clamped)
                : StepDurationRules.Haul(clamped);

            return $"×{clamped} · {ticks}t";
        }
    }
}
