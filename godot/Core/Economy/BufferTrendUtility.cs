using System.Collections.Generic;
using System.Globalization;

namespace GolemFactory.Economy
{
    // Which way a stock level is moving. Deliberately three-valued (not just a signed
    // float) so the UI can pick a distinct glyph/brightness per state instead of trying to
    // read a sign off a number that is almost never exactly zero.
    public enum StockTrend
    {
        Steady,
        Rising,
        Falling
    }

    // Pure, engine-free math for the inventory's rate/trend readout -- same "extract the
    // math into a static class the tests can call without a scene" split as
    // GridCoordinateConverter / YSortUtility / BeltFlowUtility / StallDiagnostics.
    //
    // This is PRESENTATION ONLY. StorageBufferRegistry holds current quantities and
    // nothing else; deriving "is this filling or draining" belongs in the UI layer, not in
    // the simulation, so the sim stays a plain quantity store with no time dependence.
    //
    // The slope is a least-squares fit rather than a first-to-last delta because buffer
    // quantities are integers that move in single-unit steps at tick boundaries: an
    // endpoint delta over a short window quantizes hard (a 1-item step over 6s reads as
    // either 0/min or 10/min depending purely on where the window edges land), while the
    // fit over every sample in the window averages that jitter out.
    public static class BufferTrendUtility
    {
        // Below this magnitude a rate reads as "Steady". One item every two minutes is not
        // a trend the player can act on, and showing a flickering arrow for it is worse
        // than showing none.
        public const float DefaultDeadbandPerMinute = 0.5f;

        // A fit needs a real time base: two samples 20ms apart that happen to straddle a
        // single deposit would extrapolate to 3000/min. Rates are suppressed entirely
        // until the window actually spans this long.
        public const float MinimumSpanSeconds = 1f;

        /// <summary>
        /// Least-squares slope of quantity against time, scaled to per-minute.
        /// Returns false (and 0) when there is not enough data to fit a meaningful line --
        /// fewer than two samples, or a time span shorter than
        /// <see cref="MinimumSpanSeconds"/>.
        /// </summary>
        public static bool TryComputeRatePerMinute(
            IReadOnlyList<float> times, IReadOnlyList<int> quantities, out float ratePerMinute)
        {
            ratePerMinute = 0f;

            if (times == null || quantities == null)
            {
                return false;
            }

            int count = times.Count < quantities.Count ? times.Count : quantities.Count;
            if (count < 2)
            {
                return false;
            }

            float span = times[count - 1] - times[0];
            if (span < MinimumSpanSeconds)
            {
                return false;
            }

            // Accumulate in double: the times are game-clock seconds, which grow without
            // bound, and a float sum-of-squares of those loses precision fast in a long
            // session.
            double meanTime = 0d;
            double meanQuantity = 0d;
            for (int i = 0; i < count; i++)
            {
                meanTime += times[i];
                meanQuantity += quantities[i];
            }
            meanTime /= count;
            meanQuantity /= count;

            double covariance = 0d;
            double variance = 0d;
            for (int i = 0; i < count; i++)
            {
                double dt = times[i] - meanTime;
                covariance += dt * (quantities[i] - meanQuantity);
                variance += dt * dt;
            }

            // Every sample landed on the same timestamp. Guarded separately from the span
            // check above because a caller can pass unsorted/duplicated times.
            if (variance <= 0d)
            {
                return false;
            }

            ratePerMinute = (float)(covariance / variance * 60d);
            return true;
        }

        public static StockTrend Classify(float ratePerMinute) =>
            Classify(ratePerMinute, DefaultDeadbandPerMinute);

        public static StockTrend Classify(float ratePerMinute, float deadbandPerMinute)
        {
            if (ratePerMinute > deadbandPerMinute)
            {
                return StockTrend.Rising;
            }

            if (ratePerMinute < -deadbandPerMinute)
            {
                return StockTrend.Falling;
            }

            return StockTrend.Steady;
        }

        /// <summary>
        /// The rate as the player reads it, always signed so "+" reads as income at a
        /// glance. A Steady rate collapses to a flat "0/min" rather than printing the
        /// sub-deadband noise that produced it.
        /// </summary>
        public static string FormatRate(float ratePerMinute) =>
            FormatRate(ratePerMinute, DefaultDeadbandPerMinute);

        public static string FormatRate(float ratePerMinute, float deadbandPerMinute)
        {
            if (Classify(ratePerMinute, deadbandPerMinute) == StockTrend.Steady)
            {
                return "0/min";
            }

            string magnitude = System.Math.Abs(ratePerMinute) >= 10f
                ? ratePerMinute.ToString("0", CultureInfo.InvariantCulture)
                : ratePerMinute.ToString("0.0", CultureInfo.InvariantCulture);

            return (ratePerMinute > 0f ? "+" : "") + magnitude + "/min";
        }

        // Plain ASCII arrows, not the Unicode triangles: TextMeshPro falls back to a
        // missing-glyph box for anything outside the default font atlas, and a literal
        // box next to every number is worse than no arrow at all.
        public static string TrendGlyph(StockTrend trend)
        {
            switch (trend)
            {
                case StockTrend.Rising:
                    return "^";
                case StockTrend.Falling:
                    return "v";
                default:
                    return "-";
            }
        }

        /// <summary>
        /// Canonical display order for item types: the three named resources in production
        /// order (raw -> refined -> exotic), then anything else alphabetically. Keeps a row
        /// in the same place between refreshes, which a raw Dictionary iteration does not
        /// guarantee -- a list that reorders itself under the player is unscannable.
        /// </summary>
        public static int ItemSortKey(string itemType)
        {
            if (itemType == ItemType.Scrap)
            {
                return 0;
            }

            if (itemType == ItemType.Brass)
            {
                return 1;
            }

            if (itemType == ItemType.Aether)
            {
                return 2;
            }

            return 3;
        }

        /// <summary>
        /// Sorts item type ids into the canonical display order above, in place.
        /// </summary>
        public static void SortItemTypes(List<string> itemTypes)
        {
            if (itemTypes == null)
            {
                return;
            }

            itemTypes.Sort(CompareItemTypes);
        }

        private static int CompareItemTypes(string a, string b)
        {
            int keyA = ItemSortKey(a);
            int keyB = ItemSortKey(b);
            if (keyA != keyB)
            {
                return keyA < keyB ? -1 : 1;
            }

            return string.CompareOrdinal(a, b);
        }
    }
}
