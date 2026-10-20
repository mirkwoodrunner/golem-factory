using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    /// <summary>
    /// How far a <c>Repeat(n)</c> may go (docs/tuning-data-spec.md §13.1, invariant 9).
    ///
    /// <para>
    /// A Repeat runs the Assemble before it n + 1 times from one Haul's hold, and nothing
    /// empties the golem's output stock until the Push after it. So n + 1 runs must fit the
    /// 12-per-good stock twice over: every input, (n + 1) × its quantity, held at once; and
    /// every output, (n + 1) × its quantity, with nowhere to go. Past either, the golem stalls
    /// for good (R1 with Repeat 12 makes a 13th Coke with no room: OutputFull, forever). The
    /// Workbench's dial stops at the limit, ENGAGE refuses a Repeat that can't finish, and a
    /// save holding a larger n loads clamped.
    /// </para>
    /// </summary>
    public static class RepeatRules
    {
        /// <summary>
        /// The largest n a Repeat behind <paramref name="recipe"/> can finish:
        /// min over its inputs, output and byproduct of ⌊12 / quantity⌋, less one. Never below
        /// 0; 0 means a Repeat can't follow this recipe at all.
        /// </summary>
        public static int MaxIterations(RecipeDefinition recipe)
        {
            if (recipe == null)
            {
                return 0;
            }
            int hold = GolemInventory.CapacityPerType;
            int runs = int.MaxValue;
            foreach (RecipeIngredient input in recipe.inputs ?? new List<RecipeIngredient>())
            {
                runs = Fewer(runs, hold, input.quantity);
            }
            runs = Fewer(runs, hold, recipe.outputQuantity);
            if (recipe.HasByproduct)
            {
                runs = Fewer(runs, hold, recipe.byproductQuantity);
            }
            if (runs == int.MaxValue)
            {
                return 0;
            }
            return runs - 1 < 0 ? 0 : runs - 1;
        }

        private static int Fewer(int runs, int hold, int quantity) =>
            quantity <= 0 ? runs : System.Math.Min(runs, hold / quantity);

        /// <summary>
        /// The recipe a Repeat in <paramref name="index"/> would re-run: the Assemble card just
        /// before it, skipping empty sockets as the writer does. Null when the step before it is
        /// not an Assemble (GolemEntity's PrecedingStep rule), or there is none.
        /// </summary>
        public static RecipeDefinition RepeatedRecipe(IReadOnlyList<AppendageActionDefinition> cards, int index)
        {
            if (cards == null)
            {
                return null;
            }
            for (int i = index - 1; i >= 0; i--)
            {
                if (cards[i] == null)
                {
                    continue;
                }
                return cards[i].actionType == AppendageActionType.Assemble ? cards[i].recipe : null;
            }
            return null;
        }

        /// <summary>The Repeat dial's top for the slot in <paramref name="index"/>: at least 1.</summary>
        public static int DialMax(IReadOnlyList<AppendageActionDefinition> cards, int index)
        {
            int max = MaxIterations(RepeatedRecipe(cards, index));
            return max < 1 ? 1 : max;
        }

        /// <summary>
        /// Why these cards can't be written, or null if they can: the first Repeat behind no
        /// Assemble, or behind one that can't run twice in a 12 hold. What ENGAGE says when it
        /// refuses (GolemProgramWriter.TryWrite).
        /// </summary>
        public static string Refusal(IReadOnlyList<AppendageActionDefinition> cards)
        {
            for (int i = 0; cards != null && i < cards.Count; i++)
            {
                if (cards[i] == null || cards[i].actionType != AppendageActionType.Repeat)
                {
                    continue;
                }
                RecipeDefinition recipe = RepeatedRecipe(cards, i);
                if (recipe == null)
                {
                    return "Repeat needs an Assemble before it";
                }
                if (MaxIterations(recipe) < 1)
                {
                    return $"Repeat can't fit another run of {RecipeName(recipe)} in a {GolemInventory.CapacityPerType} hold";
                }
            }
            return null;
        }

        /// <summary>
        /// A load: every Repeat's n held to its limit, the program otherwise kept as saved. A
        /// Repeat with no limit (none, or 0) keeps 1, the dial's floor.
        /// </summary>
        public static void ClampProgram(GolemProgram program)
        {
            if (program?.appendages == null)
            {
                return;
            }
            for (int i = 0; i < program.appendages.Count; i++)
            {
                if (program.appendages[i]?.actionType != AppendageActionType.Repeat)
                {
                    continue;
                }
                int max = DialMax(program.appendages, i);
                if (program.GetQuantityAt(i) > max)
                {
                    program.SetQuantityAt(i, max);
                }
            }
        }

        /// <summary>"Lens Grinding": a recipe's player name, from its id (R10_LensGrinding).</summary>
        private static string RecipeName(RecipeDefinition recipe)
        {
            string name = recipe.name ?? "";
            int split = name.IndexOf('_');
            return UI.ConstructionCostPolicy.DisplayName(split >= 0 ? name.Substring(split + 1) : name);
        }
    }
}
