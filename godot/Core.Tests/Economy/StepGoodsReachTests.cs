using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Tutorial;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/tuning-data-spec.md invariant 3 and §15.3: every good a guide step asks for is
    /// makeable when the step opens, from the goods the Ledger has seen, the stalls, and the
    /// recipes whose cards are claimable by then on a chassis the player can build (or at the
    /// bench). The kit plays the guide; what it puts into the pack while performing a step is
    /// what that step needs the player to have.
    /// </summary>
    public class StepGoodsReachTests
    {
        [Test]
        public void EveryGoodAStepAsksFor_IsMakeableWhenTheStepOpens()
        {
            PriceList list = PriceList.Play();
            var defs = list.World.Definitions;
            List<DraftableCardDefinition> cards = list.Cards.ToList();
            var stallGoods = new HashSet<string>(list.World.Setup.nodes.Select(n => n.itemType));
            IReadOnlyList<RecipeDefinition> bench = list.World.StarterBench.CrankableRecipes;

            IReadOnlyList<TutorialStep> steps = list.World.Tutorial.Steps;
            for (int s = 0; s < steps.Count; s++)
            {
                TutorialStep step = steps[s];
                var asked = new HashSet<string>(list.Deposited[s].Keys);
                if (step.Card != null)
                {
                    DraftableCardDefinition card = list.CardFor(step);
                    asked.UnionWith(PriceList.ClaimPrice(card).Select(c => c.itemType));
                }

                HashSet<string> makeable = Makeable(
                    new HashSet<string>(stallGoods.Concat(s > 0 ? list.Seen[s - 1] : Enumerable.Empty<string>())),
                    defs.Recipes.Values, cards, defs.Chassis.Values, bench);
                foreach (string good in asked)
                {
                    Assert.IsTrue(makeable.Contains(good),
                        $"'{step.Id}' asks for {good}, which nothing can make when it opens (makeable: {string.Join(", ", makeable.OrderBy(g => g))})");
                }
                TestContext.Out.WriteLine($"{step.Id}: {string.Join(", ", asked.OrderBy(g => g))}");
            }
        }

        /// <summary>
        /// The fixpoint: a recipe's output (and byproduct) joins once its inputs are makeable, its
        /// card is claimable (its prerequisite and its price makeable), and something can run it:
        /// the bench, or a chassis with room for its Hauls, an Assemble and a Push whose own price
        /// is makeable.
        /// </summary>
        private static HashSet<string> Makeable(
            HashSet<string> makeable, IEnumerable<RecipeDefinition> recipes, List<DraftableCardDefinition> cards,
            IEnumerable<ChassisDefinition> chassis, IReadOnlyList<RecipeDefinition> bench)
        {
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (RecipeDefinition recipe in recipes)
                {
                    if (makeable.Contains(recipe.outputItemType) && (!recipe.HasByproduct || makeable.Contains(recipe.byproductItemType)))
                    {
                        continue;
                    }
                    if (!recipe.inputs.All(i => makeable.Contains(i.itemType)))
                    {
                        continue;
                    }
                    DraftableCardDefinition card = cards.FirstOrDefault(c => c.appendage?.recipe == recipe);
                    bool claimable = card != null
                        && (string.IsNullOrEmpty(card.prerequisiteItemProduced) || makeable.Contains(card.prerequisiteItemProduced))
                        && PriceList.ClaimPrice(card).All(c => makeable.Contains(c.itemType));
                    bool runnable = bench.Contains(recipe)
                        || chassis.Any(c => c.maxAppendageSlots >= recipe.inputs.Count + 2 && c.cost.All(l => makeable.Contains(l.itemType)));
                    if (!claimable || !runnable)
                    {
                        continue;
                    }
                    grew |= makeable.Add(recipe.outputItemType);
                    if (recipe.HasByproduct)
                    {
                        grew |= makeable.Add(recipe.byproductItemType);
                    }
                }
            }
            return makeable;
        }
    }
}
