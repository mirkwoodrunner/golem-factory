using System.Globalization;
using System.Text;
using GolemFactory.PunchCards;

namespace GolemFactory.Buildings
{
    /// <summary>One frame's worth of Hand-Crank Bench state, already reduced to what a HUD needs.</summary>
    public readonly struct HandCrankReading
    {
        public readonly bool HasBench;
        public readonly string RecipeLabel;
        public readonly string ConversionLabel;
        public readonly int ProgressPercent;
        public readonly int RemainingSeconds;
        public readonly bool CanAfford;
        public readonly string ShortfallLabel;
        public readonly bool IsCranking;
        public readonly int RecipeCount;

        public HandCrankReading(
            bool hasBench, string recipeLabel, string conversionLabel, int progressPercent,
            int remainingSeconds, bool canAfford, string shortfallLabel, bool isCranking, int recipeCount)
        {
            HasBench = hasBench;
            RecipeLabel = recipeLabel;
            ConversionLabel = conversionLabel;
            ProgressPercent = progressPercent;
            RemainingSeconds = remainingSeconds;
            CanAfford = canAfford;
            ShortfallLabel = shortfallLabel;
            IsCranking = isCranking;
            RecipeCount = recipeCount;
        }

        public static HandCrankReading Away() =>
            new HandCrankReading(false, null, null, 0, 0, false, null, false, 0);
    }

    /// <summary>
    /// Pure formatting for the bench readout -- every number and every string the HUD shows, with
    /// no <c>MonoBehaviour</c> anywhere, so it is unit-testable without a scene. Same split
    /// <c>SteamGaugeUtility</c> has under <c>SteamFuelGaugeView</c> and <c>ClockTowerReadout</c>
    /// has under <c>ClockTowerPanelView</c>.
    ///
    /// <para>
    /// This exists because cranking without it was invisible work: a 96-tick craft with no bar, no
    /// statement of what is being made and no warning that you cannot afford it means the player
    /// holds a key for ten seconds and finds out at the end. The bench is the slowest thing in the
    /// game by design, which makes it the thing that least tolerates having no feedback.
    /// </para>
    /// </summary>
    public static class HandCrankReadout
    {
        /// <summary>Cells in the progress bar. Ten reads cleanly at HUD size and maps to 10 % steps.</summary>
        public const int BarCells = 10;

        /// <summary>
        /// "R2_ScrapReclamation" -> "R2 · Scrap Reclamation". The R-number is kept because it is
        /// how the design doc, the recipe assets and the appendage cards all refer to a recipe, so
        /// a player reading any of those can match them up.
        /// </summary>
        public static string RecipeLabel(RecipeDefinition recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.name))
            {
                return "nothing";
            }

            string name = recipe.name;
            int underscore = name.IndexOf('_');
            string number = underscore > 0 ? name.Substring(0, underscore) : null;
            string body = underscore >= 0 && underscore + 1 < name.Length
                ? name.Substring(underscore + 1)
                : name;

            string spaced = SpaceCamelCase(body);
            return string.IsNullOrEmpty(number) ? spaced : number + " · " + spaced;
        }

        /// <summary>"1 Scrap -> 1 Iron Plate", including a byproduct when there is one.</summary>
        public static string ConversionLabel(RecipeDefinition recipe)
        {
            if (recipe == null || recipe.inputs == null || recipe.inputs.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < recipe.inputs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" + ");
                }

                sb.Append(recipe.inputs[i].quantity.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(SpaceCamelCase(recipe.inputs[i].itemType));
            }

            sb.Append(" → ")
              .Append(recipe.outputQuantity.ToString(CultureInfo.InvariantCulture))
              .Append(' ').Append(SpaceCamelCase(recipe.outputItemType));

            if (!string.IsNullOrEmpty(recipe.byproductItemType) && recipe.byproductQuantity > 0)
            {
                sb.Append(" + ").Append(recipe.byproductQuantity.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(SpaceCamelCase(recipe.byproductItemType));
            }

            return sb.ToString();
        }

        /// <summary>Whole percent, clamped. Zero required ticks reads as 0 rather than dividing by it.</summary>
        public static int ProgressPercent(int progressTicks, int requiredTicks)
        {
            if (requiredTicks <= 0)
            {
                return 0;
            }

            int percent = (int)((long)progressTicks * 100 / requiredTicks);
            return percent < 0 ? 0 : percent > 100 ? 100 : percent;
        }

        /// <summary>
        /// Seconds left at the nominal tick rate. Rounded UP, so a bar that still shows movement
        /// never claims "0s" -- a countdown that sits on zero while the handle is still turning
        /// reads as a hang.
        /// </summary>
        public static int RemainingSeconds(int progressTicks, int requiredTicks, float ticksPerSecond)
        {
            if (requiredTicks <= 0 || ticksPerSecond <= 0f)
            {
                return 0;
            }

            int remaining = requiredTicks - progressTicks;
            if (remaining <= 0)
            {
                return 0;
            }

            return (int)System.Math.Ceiling(remaining / ticksPerSecond);
        }

        /// <summary>`[####------]` -- a text bar, so it needs no Image and no layout work.</summary>
        public static string ProgressBar(int percent)
        {
            int clamped = percent < 0 ? 0 : percent > 100 ? 100 : percent;
            int filled = clamped * BarCells / 100;

            var sb = new StringBuilder(BarCells + 2);
            sb.Append('[');
            for (int i = 0; i < BarCells; i++)
            {
                sb.Append(i < filled ? '█' : '░');
            }

            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// The whole readout as one block of text. Kept as a single string so the view is a single
        /// TMP assignment rather than a layout of five labels that has to stay aligned.
        /// </summary>
        public static string Format(HandCrankReading reading)
        {
            if (!reading.HasBench)
            {
                return string.Empty;
            }

            if (reading.RecipeCount == 0)
            {
                return "HAND-CRANK BENCH\nnothing here can be made by hand";
            }

            var sb = new StringBuilder();
            sb.Append("HAND-CRANK BENCH   ").Append(reading.RecipeLabel);
            sb.Append('\n').Append(reading.ConversionLabel);
            sb.Append('\n').Append(ProgressBar(reading.ProgressPercent))
              .Append(' ').Append(reading.ProgressPercent.ToString(CultureInfo.InvariantCulture)).Append('%');

            if (reading.IsCranking && reading.RemainingSeconds > 0)
            {
                sb.Append("   ").Append(reading.RemainingSeconds.ToString(CultureInfo.InvariantCulture)).Append("s left");
            }

            sb.Append('\n');
            if (!reading.CanAfford)
            {
                // Said before a minute of turning a handle is spent on it, not after.
                sb.Append(reading.ShortfallLabel);
            }
            else
            {
                sb.Append("hold [E] to crank   ·   [R] changes recipe");
            }

            return sb.ToString();
        }

        /// <summary>"need 2 more Iron Plate" -- the first shortfall only, matching how a stalled
        /// golem names one blocked input rather than listing them all.</summary>
        public static string ShortfallLabel(RecipeDefinition recipe, System.Func<string, int> stockOf)
        {
            if (recipe == null || recipe.inputs == null || stockOf == null)
            {
                return "nothing selected";
            }

            for (int i = 0; i < recipe.inputs.Count; i++)
            {
                RecipeIngredient input = recipe.inputs[i];
                int have = stockOf(input.itemType);
                if (have < input.quantity)
                {
                    return "need " + (input.quantity - have).ToString(CultureInfo.InvariantCulture) +
                           " more " + SpaceCamelCase(input.itemType);
                }
            }

            return string.Empty;
        }

        // "IronPlate" -> "Iron Plate". Item ids are camel-case constants everywhere in the
        // simulation; only the HUD needs them spaced.
        private static string SpaceCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(value[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
