namespace GolemFactory.Economy
{
    /// <summary>What a stock bar is measuring, which decides what its length MEANS.</summary>
    public enum StockBarMode
    {
        /// <summary>
        /// No capacity to measure against, so the bar is scaled to the largest stock on
        /// screen. A comparison between rows, not a percentage of anything.
        /// </summary>
        RelativeToLargest = 0,

        /// <summary>
        /// The buffer caps this item type, so the bar is a genuine fill fraction and a full
        /// bar means backpressure -- the thing that stalls the golem pushing into it.
        /// </summary>
        AgainstCapacity = 1,
    }

    /// <summary>One row's bar: how long, what it means, and whether it is about to bite.</summary>
    public readonly struct StockBar
    {
        public readonly float Fraction;
        public readonly StockBarMode Mode;

        /// <summary>At or over <see cref="NearFullFraction"/> of a real capacity.</summary>
        public readonly bool IsNearFull;

        /// <summary>No room left for this type. A golem pushing into it stalls OutputFull.</summary>
        public readonly bool IsFull;

        public StockBar(float fraction, StockBarMode mode, bool isNearFull, bool isFull)
        {
            Fraction = fraction;
            Mode = mode;
            IsNearFull = isNearFull;
            IsFull = isFull;
        }
    }

    /// <summary>
    /// Pure bar arithmetic for the inventory panel, same static-class split as
    /// <see cref="BufferTrendUtility"/>.
    ///
    /// <para>
    /// The bars were relative-only because a <c>StorageBuffer</c> had no capacity concept:
    /// there was no "full" to draw a percentage against, so the honest comparison was between
    /// rows. §1.2 supplied the missing concept (<c>CapacityPerType</c> / <c>RoomFor</c>) and
    /// nothing read it, which left the one bar that CAN be a percentage still drawn as a
    /// comparison -- and left buffer backpressure, which §5.3(c)'s whole Slag economy runs on,
    /// invisible until a golem stalled.
    /// </para>
    ///
    /// <para>
    /// The two modes are kept apart rather than blended because a half-length bar means two
    /// different things in them: "half of what the biggest row holds" and "half full". The
    /// panel colours them differently for exactly that reason.
    /// </para>
    /// </summary>
    public static class StockBarPolicy
    {
        /// <summary>
        /// Where "filling up" becomes "about to back up". Five sixths: on the scene's
        /// 100-per-type production buffers that is 84 units, roughly a minute of §5.3(c)'s
        /// ~96 Slag/min -- about one player reaction away from the stall, which is what a
        /// warning is for.
        /// </summary>
        public const float NearFullFraction = 5f / 6f;

        public static StockBar Evaluate(int quantity, int capacityPerType, int largestOnScreen)
        {
            if (quantity < 0)
            {
                quantity = 0;
            }

            // Unlimited (-1) is the sentinel StorageBuffer already uses. A capacity of zero is
            // also treated as no readout rather than as a permanently full bar: a zero-capacity
            // buffer holds nothing, so a "100% full" bar would be nonsense sitting next to a
            // row that cannot exist.
            if (capacityPerType <= 0)
            {
                float relative = largestOnScreen > 0
                    ? Clamp01((float)quantity / largestOnScreen)
                    : 0f;
                return new StockBar(relative, StockBarMode.RelativeToLargest, false, false);
            }

            float fill = Clamp01((float)quantity / capacityPerType);
            bool full = quantity >= capacityPerType;
            return new StockBar(fill, StockBarMode.AgainstCapacity, fill >= NearFullFraction, full);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
