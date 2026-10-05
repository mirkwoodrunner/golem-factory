using System.Globalization;

namespace GolemFactory.Steam
{
    /// <summary>
    /// One boiler gauge reading, already resolved to the three numbers §8 asks for.
    /// </summary>
    public readonly struct SteamGaugeReading
    {
        public SteamGaugeReading(
            int cokeStock, int poweredGolems, int burnPerMinute,
            bool hasCountdown, int secondsRemaining, bool isLow)
        {
            CokeStock = cokeStock;
            PoweredGolems = poweredGolems;
            BurnPerMinute = burnPerMinute;
            HasCountdown = hasCountdown;
            SecondsRemaining = secondsRemaining;
            IsLow = isLow;
        }

        public int CokeStock { get; }
        public int PoweredGolems { get; }

        /// <summary>Coke per minute at the CURRENT powered-golem count.</summary>
        public int BurnPerMinute { get; }

        /// <summary>
        /// False when nothing is drawing steam. An idle boiler burns nothing (§3.1), so its
        /// countdown is genuinely infinite -- and printing "999:59 left" or a bare "0:00" would
        /// both be lies about a factory that is simply not running yet.
        /// </summary>
        public bool HasCountdown { get; }

        /// <summary>Whole seconds until the Coke runs out. Meaningless unless HasCountdown.</summary>
        public int SecondsRemaining { get; }

        /// <summary>§8's "alert at 25 %". See <see cref="SteamGaugeUtility.LowFuelNumerator"/>.</summary>
        public bool IsLow { get; }
    }

    // The fuel gauge (docs/progression-design.md §8, and §3.1's "the countdown responds live to
    // every golem placed"). §12's self-assessment is explicit that this is not polish: cut the
    // legibility surfaces and "the economy becomes invisible and this point fails" -- steam is
    // ~41 % of the endgame factory, and a running cost the player cannot see is a running cost
    // they cannot plan against.
    //
    // Pure and engine-free, beside a thin view (UI/SteamFuelGaugeView), the same split
    // BufferTrendUtility/BeltSignalUtility/StallDiagnostics already use: the arithmetic is
    // unit-tested without a scene and the MonoBehaviour only paints the result.
    public static class SteamGaugeUtility
    {
        /// <summary>
        /// 3 Coke/min per working golem (G10; §3.1 had 6). Derived here from the tick constant
        /// rather than restated as a literal, so a retune of the burn rate can never leave the
        /// gauge quoting the old figure: 60 s x 10 ticks / 200 ticks-per-Coke.
        /// </summary>
        public const int CokePerMinutePerGolem =
            60 * 10 / SteamNetwork.TicksPerCokePerPoweredGolem;

        /// <summary>
        /// The alert fires at or below one quarter of the reference stock (§8). Held as an
        /// integer fraction rather than a 0.25f so the boundary case is exact -- at 240/60 a
        /// float comparison is one ULP away from flickering the alert on and off.
        /// </summary>
        public const int LowFuelNumerator = 1;
        public const int LowFuelDenominator = 4;

        /// <summary>
        /// Coke per minute at <paramref name="poweredGolems"/>. Exactly proportional, which is
        /// the entire point of §3.1: a flat per-boiler burn would make this number identical for
        /// 1 golem and 8, and the marginal cost the player optimises against would be zero.
        /// </summary>
        public static int BurnPerMinute(int poweredGolems) =>
            poweredGolems > 0 ? poweredGolems * CokePerMinutePerGolem : 0;

        /// <summary>
        /// The whole gauge. <paramref name="referenceCoke"/> is what the 25 % alert measures
        /// against -- SteamBoiler.PeakCokeStock in the game; see the note there for why a
        /// high-water mark rather than an invented capacity.
        /// </summary>
        public static SteamGaugeReading Compute(int cokeStock, int poweredGolems, int referenceCoke)
        {
            if (cokeStock < 0)
            {
                cokeStock = 0;
            }

            if (poweredGolems < 0)
            {
                poweredGolems = 0;
            }

            int burnPerMinute = BurnPerMinute(poweredGolems);

            bool hasCountdown = burnPerMinute > 0;
            int secondsRemaining = 0;
            if (hasCountdown)
            {
                // Integer division, rounding DOWN: the gauge must never promise more time than
                // the player has. 239 Coke at 24/min is 9:57, not 10:00.
                secondsRemaining = (int)((long)cokeStock * 60L / burnPerMinute);
            }

            // A reference of 0 means the boiler has never held any Coke, so there is no 100 % to
            // take a quarter of. An empty boiler with nothing drawing on it is not an emergency
            // -- it is a boiler nobody has fuelled yet -- so it does not raise the alert.
            bool isLow = referenceCoke > 0 &&
                         cokeStock * LowFuelDenominator <= referenceCoke * LowFuelNumerator;

            return new SteamGaugeReading(
                cokeStock, poweredGolems, burnPerMinute, hasCountdown, secondsRemaining, isLow);
        }

        /// <summary>
        /// §8's readout: <c>240 Coke - 24/min - 10:00 left</c>.
        ///
        /// PLAIN ASCII SEPARATORS, not the design's middle dot. TextMeshPro's default
        /// LiberationSans SDF atlas is an ASCII range, so U+00B7 renders as a missing-glyph box
        /// -- the same constraint that keeps BufferTrendUtility.TrendGlyph on "^"/"v" and
        /// StallDiagnostics.ComposeStripText on "[!]".
        /// </summary>
        public static string Format(SteamGaugeReading reading)
        {
            string head = reading.CokeStock.ToString(CultureInfo.InvariantCulture) + " Coke";

            if (!reading.HasCountdown)
            {
                // No draw at all. Named rather than shown as "0:00 left", which reads as an
                // emergency when it actually means the boiler is untouched.
                return head + " - 0/min - idle";
            }

            return head +
                   " - " + reading.BurnPerMinute.ToString(CultureInfo.InvariantCulture) + "/min" +
                   " - " + FormatCountdown(reading.SecondsRemaining) + " left";
        }

        /// <summary>mm:ss, with minutes uncapped so a long-lived boiler reads 102:30 rather than wrapping.</summary>
        /// <summary>
        /// One boiler's line, for its [E] caption (G10, from playtest: "there isn't a good
        /// indication of how long coke lasts"): its Coke, who draws on it, and how long that
        /// lasts -- "18 Coke · 2 golems · 1:30 left", or "18 Coke · no golems working".
        /// </summary>
        public static string FormatBoiler(int cokeStock, int poweredGolems)
        {
            SteamGaugeReading reading = Compute(cokeStock, poweredGolems, 0);
            string head = reading.CokeStock.ToString(CultureInfo.InvariantCulture) + " Coke";
            if (!reading.HasCountdown)
            {
                return head + " · no golems working";
            }
            string golems = (reading.PoweredGolems == 1 ? "1 golem" : reading.PoweredGolems.ToString(CultureInfo.InvariantCulture) + " golems") + " working";
            return head + " · " + golems + " · " + FormatCountdown(reading.SecondsRemaining) + " left";
        }

        /// <summary>
        /// How long <paramref name="coke"/> lasts one golem, for the refuel popup: the player
        /// learns the rate at the moment they pay it.
        /// </summary>
        public static string FormatLastsOneGolem(int coke) =>
            FormatCountdown((int)((long)coke * 60L / CokePerMinutePerGolem));

        public static string FormatCountdown(int totalSeconds)
        {
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" +
                   seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
