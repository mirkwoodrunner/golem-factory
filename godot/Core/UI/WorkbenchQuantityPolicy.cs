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
        /// Whether this card's behaviour actually reads its slot's quantity. <c>Haul</c> and
        /// <c>ExtractFromNode</c> take a batch size; <c>Repeat</c> takes its n. Every other verb
        /// ignores it, and showing a stepper on an <c>Assemble</c> or a <c>Push</c> would
        /// advertise a decision that changes nothing -- worse than no control at all, because
        /// the player would spend attention on it.
        ///
        /// <para>
        /// <c>Repeat</c> shares the dial rather than getting one of its own because it shares
        /// the CEILING for the same reason: n more assemblies have to be fed out of the same
        /// 12-per-type input stock, which is precisely the bind §6 says makes Repeat on a
        /// 10-Casing recipe impossible.
        /// </para>
        /// </summary>
        public static bool TakesQuantity(AppendageActionDefinition card) =>
            card != null &&
            (card.actionType == AppendageActionType.Haul ||
             card.actionType == AppendageActionType.ExtractFromNode ||
             card.actionType == AppendageActionType.Repeat);

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
            int ticks;
            switch (card.actionType)
            {
                case AppendageActionType.ExtractFromNode:
                    ticks = StepDurationRules.ExtractFromNode(clamped);
                    break;
                case AppendageActionType.Repeat:
                    // Quoted off the card's own recipe, so repeating a 90-tick Chronometer Core
                    // does not advertise the same cost as repeating a 12-tick coking run. A
                    // Repeat card carries no recipe of its own -- it borrows the assembly in
                    // front of it -- so with none authored this quotes one iteration and the
                    // real cost appears once the card sits behind an Assemble.
                    ticks = StepDurationRules.Repeat(
                        clamped, card.recipe != null ? card.recipe.durationTicks : 1);
                    break;
                default:
                    ticks = StepDurationRules.Haul(clamped);
                    break;
            }

            return $"×{clamped} · {ticks}t";
        }
    }
}
