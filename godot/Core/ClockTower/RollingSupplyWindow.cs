using System.Collections.Generic;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// A rolling 60-second count of units, keyed by simulation tick -- one of the two windows
    /// §7 asks for per demanded item (delivery, and fresh production).
    ///
    /// <para>
    /// FOLLOWS <c>Economy/BufferRateTracker</c>'s IDIOM -- a trimmed sample list, pruned strictly
    /// by age, with no "keep the last two anyway" floor -- but deliberately does NOT reuse it.
    /// BufferRateTracker samples a *level* (how much is in a buffer right now) and least-squares
    /// fits a slope through it, which is the right shape for "is this stock filling or draining"
    /// and the wrong shape here for two reasons. First, the tower needs a count of DISCRETE
    /// ARRIVAL EVENTS, not a level: nothing about the tower's input has a quantity to sample,
    /// and fresh production has no stock at all, only a stream of completions. Second,
    /// BufferRateTracker is keyed on <c>Time.time</c> (a float, fed by a MonoBehaviour that
    /// exists to hand it <c>Time.time</c>), and the tower's arithmetic has to be reproducible
    /// tick for tick -- §1.4's determinism discipline. A float-keyed presentation-side sampler
    /// cannot be the input to a win condition.
    /// </para>
    ///
    /// <para>
    /// Because the window is EXACTLY 60 s, the count in it IS the rate in items per minute. No
    /// division, no fit, no float -- which is most of why this shape was chosen over reusing the
    /// slope fit.
    /// </para>
    /// </summary>
    public sealed class RollingSupplyWindow
    {
        private readonly List<long> _ticks = new List<long>();
        private readonly List<int> _quantities = new List<int>();
        private readonly int _windowTicks;
        private int _unitsInWindow;

        public RollingSupplyWindow(int windowTicks = ClockTowerProgress.SupplyWindowTicks)
        {
            _windowTicks = windowTicks > 0 ? windowTicks : ClockTowerProgress.SupplyWindowTicks;
        }

        public int WindowTicks => _windowTicks;

        /// <summary>
        /// Units recorded inside the window, AS OF THE LAST <see cref="Trim"/>. Deliberately a
        /// plain property rather than a "trim then count" method: trimming is a mutation, and
        /// the read-only-view-must-never-perturb-the-simulation rule
        /// <c>SteamNetwork.LastEvaluatedPoweredCount</c> exists for applies here too -- a HUD
        /// repainting at 60 fps must not get to decide when samples expire.
        ///
        /// <para>
        /// Since the window spans exactly 60 s this is also the rate in units per minute.
        /// </para>
        /// </summary>
        public int UnitsInWindow => _unitsInWindow;

        public int SampleCount => _ticks.Count;

        /// <summary>
        /// Records <paramref name="quantity"/> units arriving on <paramref name="tick"/>.
        /// </summary>
        public void Record(long tick, int quantity)
        {
            if (quantity <= 0)
            {
                return;
            }

            // Samples must stay sorted by tick or Trim's prefix walk would leave stale entries
            // behind. Two arrivals on the same tick (two golems pushing into the tower) merge
            // into one sample, and an out-of-order arrival merges into the newest sample rather
            // than being appended where Trim could not see it. Out-of-order cannot happen from
            // the tick loop, but ClockTowerInputEndpoint stamps deliveries with the site's
            // last-ticked tick, so it can happen at a tick boundary; merging is exact for the
            // count and shifts the sample's expiry by at most one tick out of six hundred.
            int last = _ticks.Count - 1;
            if (last >= 0 && _ticks[last] >= tick)
            {
                _quantities[last] += quantity;
            }
            else
            {
                _ticks.Add(tick);
                _quantities.Add(quantity);
            }

            _unitsInWindow += quantity;
        }

        /// <summary>
        /// Drops everything that has aged out as of <paramref name="currentTick"/>. The window
        /// is the last <see cref="WindowTicks"/> ticks INCLUDING the current one, so a sample
        /// survives while <c>tick &gt; currentTick - WindowTicks</c>.
        ///
        /// <para>
        /// That inclusive convention is what makes a steady supply read exactly its own rate. At
        /// 6 /min a unit lands every 100 ticks; at tick 600 the window is ticks 1..600, holding
        /// the arrivals at 100, 200, 300, 400, 500 and 600 -- six, and six at every tick after
        /// it. Shifting the boundary by one either drops the newest arrival or keeps a
        /// seventh-hundredth-of-a-minute-old one, and the rate would oscillate between 5 and 7.
        /// </para>
        /// </summary>
        public void Trim(long currentTick)
        {
            long cutoff = currentTick - _windowTicks;

            int drop = 0;
            while (drop < _ticks.Count && _ticks[drop] <= cutoff)
            {
                _unitsInWindow -= _quantities[drop];
                drop++;
            }

            if (drop <= 0)
            {
                return;
            }

            _ticks.RemoveRange(0, drop);
            _quantities.RemoveRange(0, drop);
        }

        public void Clear()
        {
            _ticks.Clear();
            _quantities.Clear();
            _unitsInWindow = 0;
        }
    }
}
