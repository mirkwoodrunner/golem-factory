using System;
using System.Collections.Generic;

namespace GolemFactory.Diagnostics
{
    /// <summary>
    /// A series of samples (frame milliseconds, a section's per-frame time, a call count) and its
    /// percentiles (docs/performance-report-template.md §3, §5). Shared by the perf sampler's
    /// sections, the <c>perf</c> scenario's frame histograms and the playtest's frame-rate table,
    /// so all three read percentiles one way: nearest rank, p of n is the ⌈p/100 × n⌉-th smallest.
    /// </summary>
    public sealed class FrameTimeStats
    {
        private readonly List<double> _samples;
        private double[] _sorted;

        public FrameTimeStats(int capacity = 1024)
        {
            _samples = new List<double>(capacity);
        }

        public int Count => _samples.Count;

        public double Sum { get; private set; }

        public double Max { get; private set; }

        public double Mean => _samples.Count == 0 ? 0.0 : Sum / _samples.Count;

        public double P50 => Percentile(50.0);

        public double P95 => Percentile(95.0);

        public void Add(double sample)
        {
            if (_samples.Count == 0 || sample > Max)
            {
                Max = sample;
            }
            _samples.Add(sample);
            Sum += sample;
            _sorted = null;
        }

        public void Clear()
        {
            _samples.Clear();
            _sorted = null;
            Sum = 0.0;
            Max = 0.0;
        }

        /// <summary>The nearest-rank percentile; 0 for an empty series.</summary>
        public double Percentile(double percent)
        {
            int n = _samples.Count;
            if (n == 0)
            {
                return 0.0;
            }
            if (_sorted == null)
            {
                _sorted = _samples.ToArray();
                Array.Sort(_sorted);
            }
            int rank = (int)Math.Ceiling(percent / 100.0 * n);
            return _sorted[Math.Min(n, Math.Max(1, rank)) - 1];
        }
    }
}
