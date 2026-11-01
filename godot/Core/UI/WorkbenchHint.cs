using System;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    /// <summary>What the Workbench's next-drag hint outlines.</summary>
    public enum WorkbenchHintKind
    {
        None,

        /// <summary>A vault card (<see cref="WorkbenchHintTarget.Card"/>) and the socket it goes to; with no card, the socket alone.</summary>
        VaultCard,

        /// <summary>A socket whose card the plan doesn't want: drag it out.</summary>
        DragOut,

        /// <summary>The good picker on a Haul's socket.</summary>
        GoodPicker,

        /// <summary>The batch-size dial's button on <see cref="WorkbenchHintTarget.Delta"/>'s side.</summary>
        QuantityDial,

        /// <summary>The ENGAGE GEARS lever.</summary>
        Lever,
    }

    /// <summary>The one thing the hint outlines now.</summary>
    public readonly struct WorkbenchHintTarget : IEquatable<WorkbenchHintTarget>
    {
        public readonly WorkbenchHintKind Kind;

        /// <summary>The vault card's name (a logic core's or an appendage's), or null for "the socket alone".</summary>
        public readonly string Card;

        public readonly WorkbenchZone Zone;

        /// <summary>+1 or -1: which way the dial or the picker should go.</summary>
        public readonly int Delta;

        public WorkbenchHintTarget(WorkbenchHintKind kind, string card, WorkbenchZone zone, int delta)
        {
            Kind = kind;
            Card = card;
            Zone = zone;
            Delta = delta;
        }

        public static WorkbenchHintTarget None => default;
        public static WorkbenchHintTarget Lever => new WorkbenchHintTarget(WorkbenchHintKind.Lever, null, default, 0);

        public bool Equals(WorkbenchHintTarget other) =>
            Kind == other.Kind && Card == other.Card && Zone.Kind == other.Zone.Kind
            && Zone.AppendageIndex == other.Zone.AppendageIndex && Delta == other.Delta;

        public override bool Equals(object obj) => obj is WorkbenchHintTarget other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 31 + (Card?.GetHashCode() ?? 0)) * 31 + Zone.AppendageIndex;

        public override string ToString() =>
            Kind == WorkbenchHintKind.None || Kind == WorkbenchHintKind.Lever ? Kind.ToString()
            : $"{Kind} {Card ?? "-"} -> {(Zone.Kind == DropZoneKind.LogicCore ? "TRIGGER" : "STEP " + (Zone.AppendageIndex + 1))}{(Delta != 0 ? (Delta > 0 ? " +" : " -") : "")}";
    }

    /// <summary>
    /// The Workbench's next-drag hint (docs/onboarding-feedback-design.md §2.2): which card from
    /// the vault goes to which socket next, by the guide's <see cref="ProgramPlan"/>. A rule, read
    /// on demand: it never fills a socket, moves a card, sets a good or a dial, or pulls the lever.
    /// The player makes every drag.
    /// </summary>
    public static class WorkbenchHint
    {
        // TUNING (testscript/phase-2-playtest.md): the outline's pulse.
        public const float PulsePeriodSeconds = 1.2f;
        public const float PulseMinAlpha = 0.35f;
        public const float PulseMaxAlpha = 0.95f;

        /// <summary>The outline's alpha <paramref name="seconds"/> into the pulse: brightest at 0.</summary>
        public static float PulseAlpha(float seconds)
        {
            double phase = 2.0 * Math.PI * seconds / PulsePeriodSeconds;
            float mid = (PulseMinAlpha + PulseMaxAlpha) / 2f;
            float amplitude = (PulseMaxAlpha - PulseMinAlpha) / 2f;
            return mid + amplitude * (float)Math.Cos(phase);
        }

        /// <summary>
        /// The first thing in <paramref name="draft"/> that differs from <paramref name="plan"/>,
        /// walked socket by socket: the trigger, then each slot's card, good and batch, then any
        /// card past the plan. The lever once the draft passes the step's check; nothing once the
        /// golem's committed program does (<paramref name="committedMatches"/>).
        /// </summary>
        public static WorkbenchHintTarget Next(WorkbenchSession draft, ProgramPlan plan, bool committedMatches)
        {
            if (draft == null || plan == null || committedMatches || draft.DraftOverflowCount > 0)
            {
                return WorkbenchHintTarget.None;
            }
            // A plan the golem can't hold, or one naming a card the player doesn't own yet: the
            // step's text says what to do (claim it), and no outline can.
            if (draft.DraftChassis == null || plan.Slots.Count > Math.Min(draft.DraftChassis.maxAppendageSlots, draft.SocketCount))
            {
                return WorkbenchHintTarget.None;
            }
            foreach (PlanSlot slot in plan.Slots)
            {
                if (!draft.OffersCard(slot.Card))
                {
                    return WorkbenchHintTarget.None;
                }
            }

            // Another order or trigger the step accepts: straight to the lever.
            if (plan.Matches(AsProgram(draft)))
            {
                return WorkbenchHintTarget.Lever;
            }

            if (draft.DraftLogicCore?.name != plan.LogicCore)
            {
                return new WorkbenchHintTarget(WorkbenchHintKind.VaultCard, plan.LogicCore, WorkbenchZone.Trigger, 0);
            }
            for (int i = 0; i < plan.Slots.Count; i++)
            {
                PlanSlot slot = plan.Slots[i];
                AppendageActionDefinition card = draft.DraftAppendageAt(i);
                if (card?.name != slot.Card)
                {
                    return new WorkbenchHintTarget(WorkbenchHintKind.VaultCard, slot.Card, WorkbenchZone.Socket(i), 0);
                }
                string good = draft.DraftItemTypeAt(i);
                if (string.IsNullOrEmpty(good))
                {
                    good = card.inputItemType ?? "";
                }
                if (slot.Good != null && card.actionType == AppendageActionType.Haul && good != slot.Good)
                {
                    return new WorkbenchHintTarget(WorkbenchHintKind.GoodPicker, null, WorkbenchZone.Socket(i), +1);
                }
                int quantity = draft.DraftQuantityAt(i);
                if (slot.Quantity > 0 && WorkbenchQuantityPolicy.TakesQuantity(card) && quantity != slot.Quantity)
                {
                    return new WorkbenchHintTarget(WorkbenchHintKind.QuantityDial, null, WorkbenchZone.Socket(i), slot.Quantity > quantity ? +1 : -1);
                }
            }
            for (int i = plan.Slots.Count; i < draft.SocketCount; i++)
            {
                if (draft.DraftAppendageAt(i) != null)
                {
                    return new WorkbenchHintTarget(WorkbenchHintKind.DragOut, null, WorkbenchZone.Socket(i), 0);
                }
            }
            return WorkbenchHintTarget.Lever;
        }

        /// <summary>
        /// Without a plan (§2.3): a golem never told what to do gets TRIGGER, then STEP 1, then
        /// the lever, and no vault card, since no rule knows which. A programmed golem gets nothing:
        /// reprogramming is the player's design.
        /// </summary>
        public static WorkbenchHintTarget Fallback(WorkbenchSession draft, bool targetHasRunnableProgram)
        {
            if (draft == null || targetHasRunnableProgram || draft.DraftOverflowCount > 0)
            {
                return WorkbenchHintTarget.None;
            }
            if (draft.DraftLogicCore == null)
            {
                return new WorkbenchHintTarget(WorkbenchHintKind.VaultCard, null, WorkbenchZone.Trigger, 0);
            }
            for (int i = 0; i < draft.SocketCount; i++)
            {
                if (draft.DraftAppendageAt(i) != null)
                {
                    return WorkbenchHintTarget.Lever;
                }
            }
            return new WorkbenchHintTarget(WorkbenchHintKind.VaultCard, null, WorkbenchZone.Socket(0), 0);
        }

        /// <summary>The draft as the lever would commit it, on a scratch program: the draft itself is never touched.</summary>
        public static GolemProgram AsProgram(WorkbenchSession draft)
        {
            var program = new GolemProgram();
            int n = draft.SocketCount;
            var cards = new AppendageActionDefinition[n];
            var goods = new string[n];
            var quantities = new int[n];
            for (int i = 0; i < n; i++)
            {
                cards[i] = draft.DraftAppendageAt(i);
                goods[i] = draft.DraftItemTypeAt(i);
                quantities[i] = draft.DraftQuantityAt(i);
            }
            GolemProgramWriter.TryWrite(program, draft.DraftChassis, draft.DraftLogicCore, cards, goods, quantities);
            return program;
        }
    }
}
