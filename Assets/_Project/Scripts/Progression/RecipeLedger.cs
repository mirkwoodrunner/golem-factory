using System.Collections.Generic;
using System.Text;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Progression
{
    /// <summary>
    /// What the Artificer's Ledger says about a recipe (docs/cozy-automation-design.md §4a):
    /// its full ratio, its byproduct in lowest terms, and how fast it can possibly run.
    ///
    /// <para>
    /// <b>The gap this fills.</b> The Ledger is a chart -- it lights nodes up as the player
    /// produces, builds and claims -- and it says nothing about the recipe a node names. A node's
    /// <c>Detail</c> is a hand-written transcription (<c>"1-input recipes by hand, at 25% speed"</c>)
    /// which <c>TechTreeCatalogTests</c> pins for <em>existence</em> but not for <em>content</em>,
    /// so the real ratio lived only in the <c>.asset</c> file and in §5.2. A player wanting to
    /// know what R4 actually costs had to read the source.
    /// </para>
    ///
    /// <para>
    /// Engine-free static, formatting only. It reads a <see cref="RecipeDefinition"/> and returns
    /// strings; it never decides what is <em>displayed</em>, which keeps every sentence here a
    /// test rather than a screenshot.
    /// </para>
    /// </summary>
    public static class RecipeLedger
    {
        /// <summary>
        /// What a rate reads as when it cannot be computed. NOT "0.0/min": a malformed duration
        /// is an unknown rate, not a stopped one, and a confident zero is the kind of readout
        /// that sends a player off to fix a line that is fine.
        ///
        /// <para>
        /// Two hyphens rather than an em dash, for the same reason the arrow is "->" -- TMP's
        /// default LiberationSans SDF atlas has no U+2014 either, so the prettier character
        /// would render as the missing-glyph box it is trying to avoid looking like.
        /// </para>
        /// </summary>
        public const string UnknownRate = "--";

        /// <summary>
        /// The full ratio, e.g. <c>2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag</c>.
        ///
        /// <para>
        /// ASCII <c>-&gt;</c> rather than an arrow glyph: TMP's default LiberationSans SDF atlas
        /// has no U+2192, the constraint <c>StallDiagnostics</c> and <c>WorkbenchLoopLabels</c>
        /// both record. Item names go through <see cref="ItemTiers.DisplayName"/>, so the Ledger
        /// says "Iron Plate" where the asset says "IronPlate".
        /// </para>
        /// </summary>
        public static string FormatRatio(RecipeDefinition recipe)
        {
            if (recipe == null)
            {
                return "";
            }

            var text = new StringBuilder(96);
            AppendInputs(text, recipe.inputs);
            text.Append(" -> ");
            AppendQuantity(text, recipe.outputItemType, recipe.outputQuantity);

            if (recipe.HasByproduct)
            {
                text.Append(" + ");
                AppendQuantity(text, recipe.byproductItemType, recipe.byproductQuantity);
            }

            return text.ToString();
        }

        /// <summary>
        /// The byproduct obligation in lowest terms, e.g. <c>1 Slag per 2 Iron Plate</c>, or
        /// empty for a recipe that has none.
        ///
        /// <para>
        /// <b>Reduced, because the raw pair is the wrong number to think in.</b> R4 emits 1 Slag
        /// per 2 Plate whether the player wants it or not, and §5.3(c)'s entire decision is how
        /// much Slag a given Plate throughput obliges them to route. "1 per 2" is the sentence
        /// that sizes a Slag Heap; "byproductQuantity: 1" is not.
        /// </para>
        /// </summary>
        public static string FormatByproductRatio(RecipeDefinition recipe)
        {
            if (recipe == null || !recipe.HasByproduct || recipe.outputQuantity < 1)
            {
                return "";
            }

            int divisor = GreatestCommonDivisor(recipe.byproductQuantity, recipe.outputQuantity);
            int byproduct = recipe.byproductQuantity / divisor;
            int output = recipe.outputQuantity / divisor;

            var text = new StringBuilder(48);
            AppendQuantity(text, recipe.byproductItemType, byproduct);
            text.Append(" per ");
            AppendQuantity(text, recipe.outputItemType, output);
            return text.ToString();
        }

        /// <summary>
        /// Units of output per minute at 1x, or a negative number when the duration is not
        /// usable. Never <c>Infinity</c>: a zero duration is an authoring error, and a readout
        /// that printed infinity would be reporting a bug as a throughput.
        /// </summary>
        public static float RatePerMinute(int outputQuantity, int durationTicks, float ticksPerSecond)
        {
            if (durationTicks <= 0 || ticksPerSecond <= 0f || outputQuantity < 1)
            {
                return -1f;
            }

            return outputQuantity * ticksPerSecond * 60f / durationTicks;
        }

        /// <summary>
        /// The cycle line, e.g. <c>24 ticks · 5.0 Iron Plate/min at 1x</c>.
        /// </summary>
        public static string FormatCycle(RecipeDefinition recipe, float ticksPerSecond)
        {
            if (recipe == null)
            {
                return "";
            }

            float rate = RatePerMinute(recipe.outputQuantity, recipe.durationTicks, ticksPerSecond);
            var text = new StringBuilder(64);
            text.Append(recipe.durationTicks);
            text.Append(recipe.durationTicks == 1 ? " tick  ·  " : " ticks  ·  ");

            if (rate < 0f)
            {
                text.Append(UnknownRate);
                return text.ToString();
            }

            text.Append(rate.ToString("F1")).Append(' ');
            text.Append(ItemTiers.DisplayName(recipe.outputItemType));
            text.Append("/min at 1x");
            return text.ToString();
        }

        /// <summary>
        /// The live line, e.g. <c>now: 3.2/min</c> -- or empty when nothing has been measured.
        ///
        /// <para>
        /// <b>Deliberately separate from <see cref="FormatCycle"/>, and omitted rather than
        /// zeroed.</b> A theoretical rate is a property of the RECIPE; a live one is a property
        /// of the FACTORY. Printing "now: 0.0/min" for a line the player has not built yet would
        /// make an unstarted branch look broken, which is the same misdirection
        /// <c>StallDiagnostics</c> exists to prevent at the other end of the game.
        /// </para>
        /// </summary>
        public static string FormatLiveRate(bool measured, float ratePerMinute)
        {
            if (!measured)
            {
                return "";
            }

            return "now: " + ratePerMinute.ToString("F1") + "/min";
        }

        /// <summary>
        /// The authoring problem for a malformed recipe, prefixed so it cannot be mistaken for
        /// a ratio. <c>RecipeDefinition.IsWellFormed(out problem)</c> has always produced this
        /// string and nothing has ever shown it to anybody.
        /// </summary>
        public static string FormatProblem(RecipeDefinition recipe)
        {
            if (recipe == null)
            {
                return "no recipe";
            }

            return recipe.IsWellFormed(out string problem) ? "" : "malformed: " + problem;
        }

        /// <summary>
        /// The whole readout for one recipe, newline-separated, skipping the lines that do not
        /// apply. One call so the panel does no assembling of its own.
        /// </summary>
        public static string Describe(
            RecipeDefinition recipe, float ticksPerSecond, bool measured = false, float livePerMinute = 0f)
        {
            if (recipe == null)
            {
                return "";
            }

            string problem = FormatProblem(recipe);
            if (!string.IsNullOrEmpty(problem))
            {
                // A malformed recipe gets its problem and nothing else: printing a ratio derived
                // from bad data beside it would dress the fault up as a fact.
                return problem;
            }

            var lines = new List<string> { FormatRatio(recipe) };

            string byproduct = FormatByproductRatio(recipe);
            if (!string.IsNullOrEmpty(byproduct))
            {
                lines.Add("byproduct: " + byproduct);
            }

            lines.Add(FormatCycle(recipe, ticksPerSecond));

            string live = FormatLiveRate(measured, livePerMinute);
            if (!string.IsNullOrEmpty(live))
            {
                lines.Add(live);
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// The recipe in <paramref name="recipes"/> that produces <paramref name="itemType"/> as
        /// its MAIN output, or null.
        ///
        /// <para>
        /// Main output only, deliberately. Slag is R4's byproduct and R3's input, and no recipe
        /// makes it on purpose -- so "the recipe for Slag" is a question with no honest answer,
        /// and returning R4 for it would show the player an iron recipe under a Slag heading.
        /// </para>
        /// </summary>
        public static RecipeDefinition FindByOutput(IReadOnlyList<RecipeDefinition> recipes, string itemType)
        {
            if (recipes == null || string.IsNullOrEmpty(itemType))
            {
                return null;
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe != null &&
                    string.Equals(recipe.outputItemType, itemType, System.StringComparison.Ordinal))
                {
                    return recipe;
                }
            }

            return null;
        }

        /// <summary>
        /// The recipe a Ledger node names, resolved by its <b>recipe number</b>: a node called
        /// <c>"R4 Iron Smelting"</c> resolves to the asset <c>R4_IronSmelting</c>.
        ///
        /// <para>
        /// <b>Not by the node's unlock signal, which is a different thing and sometimes a
        /// different good.</b> <c>r4.ironsmelting</c> signals on <see cref="ItemType.Slag"/>
        /// rather than on Iron Plate -- deliberately, because R4 is the only recipe that makes
        /// Slag while R2 also makes Plate, so Slag is the sharper detector. Resolving by output
        /// therefore found nothing for R4 and the readout quietly fell back to the catalog's
        /// hand-written line. The number is the one identifier that is exact for every node.
        /// </para>
        /// </summary>
        public static RecipeDefinition FindByNodeName(
            IReadOnlyList<RecipeDefinition> recipes, string nodeDisplayName)
        {
            string prefix = RecipeAssetPrefix(nodeDisplayName);
            if (prefix == null || recipes == null)
            {
                return null;
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe != null && recipe.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return recipe;
                }
            }

            return null;
        }

        /// <summary>
        /// <c>"R19 Wire Drawing"</c> becomes <c>"R19_"</c>, or null for a name that does not lead
        /// with a recipe number. The underscore is part of the prefix on purpose: without it
        /// <c>"R1"</c> would match <c>R19_WireDrawing</c>.
        /// </summary>
        public static string RecipeAssetPrefix(string nodeDisplayName)
        {
            if (string.IsNullOrEmpty(nodeDisplayName) || nodeDisplayName[0] != 'R')
            {
                return null;
            }

            int digits = 1;
            while (digits < nodeDisplayName.Length && char.IsDigit(nodeDisplayName[digits]))
            {
                digits++;
            }

            // "R" alone, or "Regulator" -- neither is a recipe number.
            return digits > 1 ? nodeDisplayName.Substring(0, digits) + "_" : null;
        }

        private static void AppendInputs(StringBuilder text, List<RecipeIngredient> inputs)
        {
            if (inputs == null || inputs.Count == 0)
            {
                text.Append("nothing");
                return;
            }

            for (int i = 0; i < inputs.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(" + ");
                }

                AppendQuantity(text, inputs[i].itemType, inputs[i].quantity);
            }
        }

        private static void AppendQuantity(StringBuilder text, string itemType, int quantity)
        {
            text.Append(quantity).Append(' ').Append(ItemTiers.DisplayName(itemType));
        }

        private static int GreatestCommonDivisor(int a, int b)
        {
            a = a < 0 ? -a : a;
            b = b < 0 ? -b : b;
            while (b != 0)
            {
                int remainder = a % b;
                a = b;
                b = remainder;
            }

            // Guards a 0/0 pair, which the callers already exclude but which must not divide by
            // zero if a future one forgets.
            return a == 0 ? 1 : a;
        }
    }
}
