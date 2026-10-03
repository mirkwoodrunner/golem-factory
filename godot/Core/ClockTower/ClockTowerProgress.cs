namespace GolemFactory.ClockTower
{
    /// <summary>
    /// The Clock Tower's rate-scaled progress arithmetic (docs/progression-design.md §7), as
    /// pure engine-free statics -- the same "extract the math into something the tests can call
    /// without a scene" split <c>GridCoordinateConverter</c>, <c>BufferTrendUtility</c>,
    /// <c>SteamGaugeUtility</c> and <c>BeltSignalUtility</c> already use. Everything with a
    /// decision in it lives here; <see cref="ClockTowerSite"/> only keeps the state and
    /// <c>ClockTowerSiteHolder</c> only gives it a scene presence.
    ///
    /// <para>
    /// §7 in one block:
    /// <code>
    /// effectiveRate_i        = min( deliveryRate_i , freshProductionRate_i )   // 60 s windows
    /// stageProgressPerSecond = min over i of clamp(effectiveRate_i / demandRate_i, 0, 3)
    /// </code>
    /// Progress scales with delivered rate and is capped at 3x, so a factory running triple the
    /// demand finishes a stage in a third of the time; it is gated by the weakest line, so every
    /// line has to run; and if any supply-pressure meter empties, progress is zero -- frozen,
    /// never negative. Stages cannot fail, only take longer. That last sentence is the design's
    /// rubric-5 guarantee and nothing in this file may violate it: every quantity below is
    /// non-negative and progress only ever accumulates.
    /// </para>
    ///
    /// <para>
    /// DETERMINISM: INTEGERS ONLY, no floats anywhere in the accumulation path. This is the same
    /// discipline §1.4 settled on for the boiler burn (see the third determinism note at the top
    /// of <c>Steam/SteamNetwork.cs</c>, and <c>SteamBoiler.Accrue</c>): a per-tick fraction
    /// accumulated in floating point makes the total depend on rounding order, so two
    /// identically-supplied towers drift apart over a 40-minute Phase 6. Progress is counted in
    /// <see cref="ProgressScale"/>ths of a nominal tick, and the per-tick increment is an exact
    /// integer at every multiple of the demand rate -- in particular at 1x (exactly
    /// <see cref="ProgressScale"/>) and at the 3x cap (exactly three times that), which is what
    /// makes the nominal durations land on the tick.
    /// </para>
    /// </summary>
    public static class ClockTowerProgress
    {
        /// <summary>
        /// Ticks per simulated second. Restated here rather than read off
        /// <c>SimulationClock.TicksPerSecond</c> for exactly the reason
        /// <c>SteamNetwork.TicksPerCokePerPoweredGolem</c> is expressed in ticks: the tower's
        /// windows and pacing must not depend on the clock's speed multiplier or on frame
        /// timing. A factory run at 4x speed reaches a stage's nominal duration after the same
        /// amount of WORK, which is the only reading that keeps a rate a rate.
        /// </summary>
        public const int TicksPerSecond = 10;

        /// <summary>§7's rolling window: 60 s, which at 10 ticks/s is 600 ticks.</summary>
        public const int SupplyWindowTicks = 60 * TicksPerSecond;

        /// <summary>§7: the multiplier is capped at 3x. Surplus above triple demand is free.</summary>
        public const int MaxMultiplier = 3;

        /// <summary>
        /// Progress is counted in millionths of a nominal tick. One tick at exactly 1x supply
        /// adds exactly this much, and a stage needs
        /// <c>nominalSeconds * TicksPerSecond * ProgressScale</c> of it.
        ///
        /// <para>
        /// A million rather than, say, a thousand because the per-tick increment is
        /// <c>effective * ProgressScale / demand</c> under integer division, and the truncated
        /// remainder is dropped rather than carried (carrying it exactly would need a running
        /// remainder in a denominator that changes whenever the limiting line changes, which is
        /// a lot of machinery to recover a millionth). At a scale of a million the worst-case
        /// loss is under 24 parts per million per tick -- a hundredth of a tick across a
        /// ten-minute stage -- and it is always a LOSS, never a gain, so it can never make a
        /// stage finish early. At 1x and at the 3x cap there is no remainder at all.
        /// </para>
        /// </summary>
        public const int ProgressScale = 1_000_000;

        /// <summary>
        /// Total progress units a stage of <paramref name="nominalSeconds"/> needs. §7's
        /// "nominal duration is at exactly 1x supply; at 3x it is a third of that" falls straight
        /// out: at 1x each tick contributes <see cref="ProgressScale"/>, so the stage takes
        /// <c>nominalSeconds * TicksPerSecond</c> ticks, and at 3x a third of them.
        /// </summary>
        public static long RequiredProgressUnits(int nominalSeconds)
        {
            int seconds = nominalSeconds > 0 ? nominalSeconds : 1;
            return (long)seconds * TicksPerSecond * ProgressScale;
        }

        /// <summary>
        /// §7's <c>effectiveRate_i = min(deliveryRate_i, freshProductionRate_i)</c>.
        ///
        /// <para>
        /// THE <c>min</c> IS THE ANTI-HOARD RULE, not an optimisation. The 60-unit meter clamp
        /// caps how much *stock* credit a delivery can bank, but delivering out of a warehouse
        /// is still a delivery rate -- so on delivery alone a player could stockpile stage-4
        /// goods through stages 1-3 and unload at 3x to finish the climax in three minutes.
        /// Taking the smaller of the two means a stockpile can smooth a dip (delivery covers a
        /// production hiccup) but can never raise the multiplier above what the factory is
        /// genuinely making right now.
        /// </para>
        /// </summary>
        public static int EffectiveRate(int deliveryRatePerMinute, int freshRatePerMinute)
        {
            int delivery = deliveryRatePerMinute > 0 ? deliveryRatePerMinute : 0;
            int fresh = freshRatePerMinute > 0 ? freshRatePerMinute : 0;
            return delivery < fresh ? delivery : fresh;
        }

        /// <summary>
        /// One line's <c>clamp(effectiveRate / demandRate, 0, 3)</c>, in
        /// <see cref="ProgressScale"/> units.
        ///
        /// <para>
        /// Clamped BEFORE the division, on the integer rate, so the 3x cap is exact rather than
        /// a comparison against a truncated quotient.
        /// </para>
        /// </summary>
        public static long ScaledRatio(int effectiveRatePerMinute, int demandRatePerMinute)
        {
            // A demand of zero is an authoring error (ClockTowerStageDefinition.IsWellFormed
            // rejects it), but if one ever reached here it must not be the min: a line nobody
            // has to supply cannot be the thing gating the stage.
            if (demandRatePerMinute <= 0)
            {
                return (long)MaxMultiplier * ProgressScale;
            }

            if (effectiveRatePerMinute <= 0)
            {
                return 0L;
            }

            int cap = MaxMultiplier * demandRatePerMinute;
            int effective = effectiveRatePerMinute > cap ? cap : effectiveRatePerMinute;
            return (long)effective * ProgressScale / demandRatePerMinute;
        }

        /// <summary>
        /// A scaled multiplier as thousandths, for display. <c>x1.00</c> reads better than
        /// <c>x1000000</c>, and the HUD never needs more than two decimals.
        /// </summary>
        public static int MultiplierMilli(long scaledMultiplier)
        {
            if (scaledMultiplier <= 0L)
            {
                return 0;
            }

            return (int)(scaledMultiplier / (ProgressScale / 1000));
        }

        /// <summary>Whole per-cent of a stage completed, clamped to [0, 100].</summary>
        public static int ProgressPercent(long progressUnits, long requiredUnits)
        {
            if (requiredUnits <= 0L || progressUnits <= 0L)
            {
                return 0;
            }

            if (progressUnits >= requiredUnits)
            {
                return 100;
            }

            return (int)(progressUnits * 100L / requiredUnits);
        }
    }
}
