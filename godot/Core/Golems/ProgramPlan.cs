using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    /// <summary>One socket of a <see cref="ProgramPlan"/>: a card, the good it takes, its batch size.</summary>
    public readonly struct PlanSlot
    {
        /// <summary>The card's definition name ("HaulScrap"), never its label.</summary>
        public readonly string Card;

        /// <summary>The good a Haul takes, or null for "whatever the card starts on".</summary>
        public readonly string Good;

        /// <summary>The batch size, or 0 for "whatever the card starts on".</summary>
        public readonly int Quantity;

        public PlanSlot(string card, string good = null, int quantity = 0)
        {
            Card = card;
            Good = good;
            Quantity = quantity;
        }

        public override string ToString() =>
            Card + (Good != null ? "(" + Good + ")" : "") + (Quantity > 0 ? " x" + Quantity : "");
    }

    /// <summary>
    /// A program described as data (docs/onboarding-feedback-design.md §2.1): a logic core and
    /// an ordered list of slots. One plan per guide program step, read three ways, so the three
    /// can never disagree: the Workbench hint walks its slots, the step's Done check is its
    /// <see cref="Matches"/>, and the playtest kit writes it.
    ///
    /// <para>
    /// <see cref="Matches"/> is the step's own check, which may be more lenient than the slots
    /// (two Hauls in either order, any trigger): the hint stops as soon as it passes, exactly when
    /// the step would.
    /// </para>
    /// </summary>
    public sealed class ProgramPlan
    {
        public string LogicCore { get; }
        public IReadOnlyList<PlanSlot> Slots { get; }

        private readonly Func<GolemProgram, bool> _matches;

        public ProgramPlan(string logicCore, Func<GolemProgram, bool> matches, params PlanSlot[] slots)
        {
            LogicCore = logicCore;
            _matches = matches;
            Slots = slots ?? new PlanSlot[0];
        }

        /// <summary>Whether <paramref name="program"/> does what the step asks. Never writes to it.</summary>
        public bool Matches(GolemProgram program) => program != null && (_matches?.Invoke(program) ?? Exactly(program));

        /// <summary>The slots exactly, in order, with the plan's core: the default check.</summary>
        public bool Exactly(GolemProgram program)
        {
            if (program?.logicCore?.name != LogicCore || program.appendages.Count != Slots.Count)
            {
                return false;
            }
            for (int i = 0; i < Slots.Count; i++)
            {
                if (program.appendages[i]?.name != Slots[i].Card
                    || (Slots[i].Good != null && program.GetItemTypeAt(i) != Slots[i].Good)
                    || (Slots[i].Quantity > 0 && program.GetQuantityAt(i) != Slots[i].Quantity))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The cards the plan names, without repeats.</summary>
        public IEnumerable<string> Cards => Slots.Select(s => s.Card).Distinct();

        /// <summary>
        /// The plan's program for <paramref name="chassis"/>, as the lever would commit it: the
        /// card's own good and batch where the plan leaves them open. Null when a name is unknown.
        /// </summary>
        public (LogicCoreDefinition core, AppendageActionDefinition[] cards, string[] goods, int[] quantities)? Resolve(
            IReadOnlyDictionary<string, LogicCoreDefinition> cores, IReadOnlyDictionary<string, AppendageActionDefinition> appendages)
        {
            if (!cores.TryGetValue(LogicCore, out LogicCoreDefinition core))
            {
                return null;
            }
            var cards = new AppendageActionDefinition[Slots.Count];
            var goods = new string[Slots.Count];
            var quantities = new int[Slots.Count];
            for (int i = 0; i < Slots.Count; i++)
            {
                if (!appendages.TryGetValue(Slots[i].Card, out cards[i]))
                {
                    return null;
                }
                goods[i] = Slots[i].Good ?? cards[i].inputItemType ?? "";
                quantities[i] = Slots[i].Quantity > 0 ? Slots[i].Quantity : cards[i].haulQuantity;
            }
            return (core, cards, goods, quantities);
        }

        public override string ToString() => LogicCore + ": " + string.Join(", ", Slots);
    }
}
