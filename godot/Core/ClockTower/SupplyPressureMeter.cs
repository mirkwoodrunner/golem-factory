namespace GolemFactory.ClockTower
{
    /// <summary>
    /// §7's supply-pressure meter for one demanded item: <c>+1</c> per unit delivered,
    /// <c>-demandRate/60</c> per second, clamped to <c>[0, 60]</c>. When it hits 0 the stage's
    /// progress is frozen (never negative) and the HUD names the starved item.
    ///
    /// <para>
    /// INTEGER ACCUMULATOR, NO FLOATS -- the same decision, for the same reason, as
    /// <c>SteamBoiler.Accrue</c>'s fractional Coke burn. The meter is stored in
    /// <see cref="Scale"/>ths of a unit, chosen so that ONE TICK OF DECAY IS EXACTLY THE DEMAND
    /// RATE IN SCALED UNITS: at 10 ticks/s, <c>demandRate/60</c> per second is
    /// <c>demandRate/600</c> per tick, so with a scale of 600 a tick costs precisely
    /// <c>demandRate</c> scaled units. Exactly proportional, exact in integers, reproducible --
    /// 6 /min drains one whole unit per 100 ticks with no remainder and no drift, forever.
    /// </para>
    ///
    /// <para>
    /// THE CLAMP AT 60 IS THE ANTI-HOARD CAP on *stock*: no matter how much a player dumps in
    /// at once, only a minute's worth of pressure is ever banked. It is not the whole anti-hoard
    /// rule, though -- delivering out of a warehouse is still a delivery rate, which is what
    /// <c>ClockTowerProgress.EffectiveRate</c>'s fresh-production floor exists to close.
    /// </para>
    /// </summary>
    public sealed class SupplyPressureMeter
    {
        /// <summary>
        /// Scaled units per whole meter unit: 60 s x <c>TicksPerSecond</c>. Derived from the
        /// tick constant rather than written as 600, so a retune of the tick rate cannot leave
        /// the decay quietly wrong by a factor.
        /// </summary>
        public const int Scale = 60 * ClockTowerProgress.TicksPerSecond;

        /// <summary>§7's upper clamp, in whole units.</summary>
        public const int CapUnits = 60;

        public const int CapScaled = CapUnits * Scale;

        /// <summary>The meter in scaled units, always in <c>[0, CapScaled]</c>.</summary>
        public int Scaled { get; private set; }

        /// <summary>The meter as the player would read it, in whole units of <c>[0, 60]</c>.</summary>
        public int Units => Scaled / Scale;

        /// <summary>
        /// True when the meter has bottomed out, which §7 makes the freeze condition: progress
        /// is 0 while this holds, and never negative.
        /// </summary>
        public bool IsEmpty => Scaled <= 0;

        /// <summary>§7's "+1 per unit delivered", clamped at 60.</summary>
        public void Credit(int units)
        {
            if (units <= 0)
            {
                return;
            }

            long raised = (long)Scaled + (long)units * Scale;
            Scaled = raised > CapScaled ? CapScaled : (int)raised;
        }

        /// <summary>
        /// One tick of §7's <c>-demandRate/60</c> per second. Floors at zero: a meter that has
        /// run out cannot go into debt, because a debt would have to be repaid before progress
        /// resumed and §7 is explicit that a stage can only take longer, never regress.
        /// </summary>
        public void Decay(int demandRatePerMinute)
        {
            if (demandRatePerMinute <= 0)
            {
                return;
            }

            Scaled -= demandRatePerMinute;
            if (Scaled < 0)
            {
                Scaled = 0;
            }
        }

        public void Reset() => Scaled = 0;
    }
}
