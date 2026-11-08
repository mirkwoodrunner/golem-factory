using System;

namespace GolemFactory.Supply
{
    /// <summary>
    /// A cart's goods in and out over the last minute (docs/supply-design.md §4, the overview's
    /// market view): what its trickle brought, and what golems and hands took. Counted on the
    /// clock in one-second buckets, so it is the same every run and stops when the factory is
    /// paused. Not saved: a cart's flow starts again on a load, as the graph does.
    /// <para>
    /// What was taken is read from the stock rather than from every taker: the cart's stock only
    /// falls when something takes from it, so a fall since the last tick is taken goods. That
    /// counts a golem's extract and the player's hands alike, without a hook in either.
    /// </para>
    /// </summary>
    public sealed class CartFlow
    {
        /// <summary>The window: a minute of one-second buckets.</summary>
        public const int Seconds = 60;

        /// <summary>A rate is shown over at least this many seconds, so a cart's first take isn't a spike.</summary>
        public const int MinimumSeconds = 10;

        private readonly int _ticksPerSecond;
        private readonly int[] _in = new int[Seconds];
        private readonly int[] _out = new int[Seconds];
        private long _ticks;
        private int _lastStock = -1;

        public CartFlow(int ticksPerSecond)
        {
            _ticksPerSecond = Math.Max(1, ticksPerSecond);
        }

        /// <summary>Seconds counted so far, up to <see cref="Seconds"/>.</summary>
        public int SecondsCounted => (int)Math.Min(Seconds, _ticks / _ticksPerSecond);

        /// <summary>
        /// One tick. <paramref name="stockBefore"/> is the stock before this tick's trickle and
        /// <paramref name="brought"/> what the trickle added; the stock after is their sum.
        /// </summary>
        public void Tick(int stockBefore, int brought)
        {
            int bucket = (int)(_ticks / _ticksPerSecond % Seconds);
            if (_ticks % _ticksPerSecond == 0)
            {
                _in[bucket] = 0;
                _out[bucket] = 0;
            }
            if (_lastStock >= 0 && stockBefore < _lastStock)
            {
                _out[bucket] += _lastStock - stockBefore;
            }
            _in[bucket] += Math.Max(0, brought);
            _lastStock = stockBefore + Math.Max(0, brought);
            _ticks++;
        }

        /// <summary>Goods a minute brought in, over the window counted (at least <see cref="MinimumSeconds"/>).</summary>
        public float InPerMinute => PerMinute(_in);

        /// <summary>Goods a minute taken out, over the window counted (at least <see cref="MinimumSeconds"/>).</summary>
        public float OutPerMinute => PerMinute(_out);

        private float PerMinute(int[] buckets)
        {
            int counted = (int)Math.Min(Seconds, (_ticks + _ticksPerSecond - 1) / _ticksPerSecond);
            if (counted == 0)
            {
                return 0f;
            }
            int sum = 0;
            for (int i = 0; i < Seconds; i++)
            {
                sum += buckets[i];
            }
            return sum * 60f / Math.Max(counted, MinimumSeconds);
        }
    }
}
