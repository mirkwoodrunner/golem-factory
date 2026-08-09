using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// Which recipes a person can turn by hand, and how much slower than a machine.
    /// docs/progression-design.md §11 item 7: "held-Interact runs any 1-input Tier-1 recipe at
    /// 25 % speed into the player's inventory. <b>Unpowered by design</b> (see §10)."
    ///
    /// <para>
    /// Pure and engine-free apart from reading an authored <see cref="RecipeDefinition"/>, so the
    /// admission rule and the arithmetic are unit-testable without a scene -- the same split
    /// <c>SteamPipeRules</c> and <c>BeltPlacementRules</c> use.
    /// </para>
    ///
    /// <para>
    /// A DESIGN DISCREPANCY, FOUND AND RESOLVED IN FAVOUR OF ARITY. §11 item 7 says "1-input
    /// <b>Tier-1</b>" recipe, but the two constraints cannot both hold:
    /// </para>
    /// <list type="bullet">
    /// <item>§5.1 classes Gear (R8's output) as <b>Tier 3</b>.</item>
    /// <item>§6's chassis table and §9's Phase 1 both require Gears to be hand-cranked -- "The
    /// Plate <b>and Gears</b> must be hand-cranked (~10-15 min), so the first Presser is the
    /// payoff for the manual era."</item>
    /// </list>
    /// <para>
    /// Read literally as Tier-1-only the bench makes Coke, Iron Plate and Glass but not Gears,
    /// the Brass Presser (60 Scrap + 20 Iron Plate + <b>10 Gear</b>) stays unbuildable, and the
    /// bench fails to do the one job Phase 1 gives it. <b>Arity is the constraint that is
    /// mechanically load-bearing</b>: one distinct input type is what a person feeding a machine
    /// by hand can manage, which is plainly why the rule was written, and "Tier-1" reads as
    /// shorthand for "the simple ones" that predates R8's output being classified Tier 3.
    /// </para>
    /// <para>
    /// It is also SELF-LIMITING, which is what makes it safe: of the 19 authored recipes exactly
    /// five take a single input (R1 Coke, R2 Iron Plate, R3 Glass, R8 Gear, R19 Copper Wire), and
    /// every Tier 4 and Tier 5 good needs three or four. <b>No amount of cranking reaches the
    /// endgame</b> -- a Mechanism, a Regulator, a Frame Section and the Chronometer Core all
    /// require a machine, so the bench cannot shortcut past the factory it exists to bootstrap.
    /// R19 is nominally Tier 2 but needs a Copper Ingot, which is a 2-input smelt, so it is
    /// unreachable by hand alone anyway.
    /// </para>
    /// </summary>
    public static class HandCrankRules
    {
        /// <summary>§11 item 7's "25 % speed", as a whole-number divisor rather than a float.</summary>
        public const int SpeedPercent = 25;

        /// <summary>
        /// How many ticks a hand-cranked craft takes: four times the authored machine duration.
        ///
        /// <para>
        /// Integer arithmetic, following the discipline §1.4's burn accumulator and §1.6's
        /// progress counter established -- 25 % is exactly 1/4, so this is exact rather than
        /// nearly-exact, and two players cranking the same recipe always finish on the same tick.
        /// </para>
        /// </summary>
        public static int CrankTicks(int machineDurationTicks)
        {
            int duration = machineDurationTicks > 0 ? machineDurationTicks : 1;
            return duration * (100 / SpeedPercent);
        }

        /// <summary>
        /// Whether one person can turn this recipe: exactly one distinct input type, and
        /// otherwise well-formed. Quantity is deliberately NOT capped -- R8 asks for 2 Iron Plate
        /// and is still one thing to feed in.
        /// </summary>
        public static bool IsCrankable(RecipeDefinition recipe) =>
            recipe != null && recipe.IsWellFormed() && recipe.inputs != null && recipe.inputs.Count == 1;

        /// <summary>
        /// Every crankable recipe from <paramref name="candidates"/>, in the order given.
        /// Order is the caller's (the authoring pass supplies R1..R19), because it becomes the
        /// order the player cycles through at the bench and must not depend on asset-database
        /// iteration.
        /// </summary>
        public static List<RecipeDefinition> Filter(IEnumerable<RecipeDefinition> candidates)
        {
            var result = new List<RecipeDefinition>();
            if (candidates == null)
            {
                return result;
            }

            foreach (RecipeDefinition recipe in candidates)
            {
                if (IsCrankable(recipe))
                {
                    result.Add(recipe);
                }
            }

            return result;
        }

        /// <summary>
        /// Next index when the player cycles the selection, wrapping. Returns 0 for an empty
        /// list rather than throwing, so a bench with nothing to make is inert, not broken.
        /// </summary>
        public static int NextIndex(int current, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int next = current + 1;
            return next >= count ? 0 : next;
        }
    }
}
