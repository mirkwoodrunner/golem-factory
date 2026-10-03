namespace GolemFactory.Economy
{
    /// <summary>
    /// What the inventory's rate column is actually saying. Separate from
    /// <see cref="StockTrend"/> on purpose: that enum is the shape of a LEVEL's movement and
    /// is used elsewhere, while this is the shape of the READOUT, which has one more case.
    /// </summary>
    public enum StockFlowKind
    {
        /// <summary>No history yet. Renders as "--", never as a confident zero.</summary>
        Unknown = 0,

        /// <summary>Level is climbing or falling faster than the deadband.</summary>
        Net = 1,

        /// <summary>
        /// Level is flat but goods are moving through. The case a level-only readout cannot
        /// see, and the one the player most needs distinguished from a dead line.
        /// </summary>
        Throughput = 2,

        /// <summary>Flat, and nothing moving. Genuinely idle.</summary>
        Idle = 3,
    }

    /// <summary>
    /// Pure formatting for the inventory rate column, following
    /// <see cref="BufferTrendUtility"/>'s "the math is a static class a test can call with no
    /// scene" split.
    ///
    /// <para>
    /// It exists because the old readout was NET stock change: 60/min in and 60/min out --
    /// a line running at full tilt -- printed "0/min, Steady", identical to a line that had
    /// been dead for ten minutes. The fix is not a bigger number but a second question:
    /// having established the level is flat, ask whether anything is passing through it.
    /// </para>
    /// </summary>
    public static class BufferFlowUtility
    {
        /// <summary>
        /// Throughput is <c>min(in, out)</c>: what genuinely passes THROUGH. The surplus of
        /// either side over the other is already reported as net movement, so taking the max
        /// (or the sum) would double-count it and read as twice the traffic there is.
        /// </summary>
        public static float ThroughputPerMinute(float inPerMinute, float outPerMinute)
        {
            float low = inPerMinute < outPerMinute ? inPerMinute : outPerMinute;
            return low > 0f ? low : 0f;
        }

        /// <summary>
        /// Which of the four things the column is saying. <paramref name="hasFlow"/> is false
        /// when gross counters were never sampled, in which case this can only ever answer
        /// about the level -- exactly as the readout did before gross flow existed.
        /// </summary>
        public static StockFlowKind Classify(
            bool hasRate, float netPerMinute, bool hasFlow, float inPerMinute, float outPerMinute) =>
            Classify(hasRate, netPerMinute, hasFlow, inPerMinute, outPerMinute,
                BufferTrendUtility.DefaultDeadbandPerMinute);

        public static StockFlowKind Classify(
            bool hasRate, float netPerMinute, bool hasFlow, float inPerMinute, float outPerMinute,
            float deadbandPerMinute)
        {
            if (!hasRate)
            {
                return StockFlowKind.Unknown;
            }

            if (BufferTrendUtility.Classify(netPerMinute, deadbandPerMinute) != StockTrend.Steady)
            {
                return StockFlowKind.Net;
            }

            if (hasFlow && ThroughputPerMinute(inPerMinute, outPerMinute) > deadbandPerMinute)
            {
                return StockFlowKind.Throughput;
            }

            return StockFlowKind.Idle;
        }

        /// <summary>
        /// Plain ASCII, for the reason <see cref="BufferTrendUtility.TrendGlyph"/> gives:
        /// TextMeshPro draws a missing-glyph box for anything outside the default atlas.
        /// ">>" reads as "passing through" without competing with the ^/v pair.
        /// </summary>
        public static string Glyph(StockFlowKind kind, StockTrend trend)
        {
            switch (kind)
            {
                case StockFlowKind.Unknown:
                    return "";
                case StockFlowKind.Throughput:
                    return ">>";
                case StockFlowKind.Net:
                    return BufferTrendUtility.TrendGlyph(trend);
                default:
                    return BufferTrendUtility.TrendGlyph(StockTrend.Steady);
            }
        }

        /// <summary>The number beside the glyph. Unsigned for throughput: it is a magnitude
        /// passing through, not a gain or a loss.</summary>
        public static string FormatFlow(
            StockFlowKind kind, float netPerMinute, float inPerMinute, float outPerMinute)
        {
            switch (kind)
            {
                case StockFlowKind.Unknown:
                    // Not "0/min": no reading yet and a genuinely flat stock are different
                    // claims, and printing the second when we only know the first is a lie
                    // the player would plan around.
                    return "--";
                case StockFlowKind.Throughput:
                {
                    float through = ThroughputPerMinute(inPerMinute, outPerMinute);
                    string magnitude = through >= 10f
                        ? through.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                        : through.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    return magnitude + "/min";
                }
                default:
                    return BufferTrendUtility.FormatRate(netPerMinute);
            }
        }
    }
}
