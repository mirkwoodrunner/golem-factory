using System.Collections.Generic;

namespace GolemFactory.Economy
{
    // Plain-C# sampler that turns a StorageBufferRegistry's instantaneous quantities into
    // a short rolling history per (bufferId, itemType), so the inventory UI can show
    // whether a stock is filling or draining.
    //
    // Deliberately NOT part of the simulation: StorageBuffer/StorageBufferRegistry stay
    // time-free quantity stores, and nothing here writes back into them. This is the
    // presentation-side derivation the "no rate tracking in the simulation" constraint
    // asks for -- it lives next to the economy types only because that is what it reads.
    //
    // Follows the same plain-class-plus-Holder split as GridMap/ConveyorSystem: the
    // MonoBehaviour that drives it (BufferThroughputMonitor) does nothing but feed it
    // Time.time, so every line below is EditMode-testable with no scene and no clock.
    public sealed class BufferRateTracker
    {
        // How far back the fit looks. Long enough that a golem cycling every few ticks
        // contributes several samples, short enough that the number still reacts when the
        // player fixes a stalled chain.
        public const float DefaultWindowSeconds = 8f;

        // A sample is only worth taking a few times a second -- the underlying quantities
        // only change on tick boundaries, and oversampling just fills the window with
        // duplicate points that drag the fit toward zero.
        public const float DefaultSampleIntervalSeconds = 0.25f;

        private sealed class Series
        {
            public readonly List<float> Times = new List<float>();
            public readonly List<int> Quantities = new List<int>();
        }

        // Nested rather than a concatenated "bufferId|itemType" key: both are free-form
        // strings, so any single-character delimiter could in principle appear inside one
        // of them and alias two different series onto the same bucket.
        private readonly Dictionary<string, Dictionary<string, Series>> _series =
            new Dictionary<string, Dictionary<string, Series>>();
        private readonly float _windowSeconds;

        public BufferRateTracker(float windowSeconds = DefaultWindowSeconds)
        {
            _windowSeconds = windowSeconds > 0f ? windowSeconds : DefaultWindowSeconds;
        }

        public float WindowSeconds => _windowSeconds;

        /// <summary>
        /// Records the current quantity of every item type in every buffer of
        /// <paramref name="registry"/> at <paramref name="time"/> (seconds), and drops
        /// anything that has aged out of the window.
        /// </summary>
        public void Sample(float time, StorageBufferRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            foreach (StorageBuffer buffer in registry.Buffers.Values)
            {
                foreach (KeyValuePair<string, int> entry in buffer.Quantities)
                {
                    Sample(time, buffer.BufferId, entry.Key, entry.Value);
                }
            }
        }

        /// <summary>
        /// Records one (bufferId, itemType) quantity directly. Public so tests can drive an
        /// exact synthetic series without standing up a registry.
        /// </summary>
        public void Sample(float time, string bufferId, string itemType, int quantity)
        {
            if (bufferId == null || itemType == null)
            {
                return;
            }

            if (!_series.TryGetValue(bufferId, out Dictionary<string, Series> byItem))
            {
                byItem = new Dictionary<string, Series>();
                _series[bufferId] = byItem;
            }

            if (!byItem.TryGetValue(itemType, out Series series))
            {
                series = new Series();
                byItem[itemType] = series;
            }

            series.Times.Add(time);
            series.Quantities.Add(quantity);
            Prune(series, time);
        }

        public bool TryGetRatePerMinute(string bufferId, string itemType, out float ratePerMinute)
        {
            ratePerMinute = 0f;
            if (bufferId == null || itemType == null)
            {
                return false;
            }

            Series series = FindSeries(bufferId, itemType);
            if (series == null)
            {
                return false;
            }

            return BufferTrendUtility.TryComputeRatePerMinute(series.Times, series.Quantities, out ratePerMinute);
        }

        /// <summary>
        /// The trend for a stock, plus whether there was enough history to compute one at
        /// all -- an unknown series reads Steady, which is the honest "no signal yet"
        /// answer rather than a guess in either direction.
        /// </summary>
        public StockTrend GetTrend(string bufferId, string itemType)
        {
            float rate;
            return TryGetRatePerMinute(bufferId, itemType, out rate)
                ? BufferTrendUtility.Classify(rate)
                : StockTrend.Steady;
        }

        public int SampleCount(string bufferId, string itemType)
        {
            Series series = FindSeries(bufferId, itemType);
            return series == null ? 0 : series.Times.Count;
        }

        public void Clear() => _series.Clear();

        private void Prune(Series series, float now)
        {
            float cutoff = now - _windowSeconds;
            int drop = 0;
            while (drop < series.Times.Count && series.Times[drop] < cutoff)
            {
                drop++;
            }

            // Pruned strictly by age, with no "keep the last two anyway" floor: retaining a
            // point from outside the window would anchor the fit to a stale quantity and
            // report a rate that has not been true for minutes. Dropping to fewer than two
            // points simply makes TryGetRatePerMinute report "no reading", which is the
            // honest answer -- and cannot happen in practice while BufferThroughputMonitor
            // is feeding samples every DefaultSampleIntervalSeconds.
            if (drop <= 0)
            {
                return;
            }

            series.Times.RemoveRange(0, drop);
            series.Quantities.RemoveRange(0, drop);
        }

        private Series FindSeries(string bufferId, string itemType)
        {
            if (bufferId == null || itemType == null)
            {
                return null;
            }

            Dictionary<string, Series> byItem;
            if (!_series.TryGetValue(bufferId, out byItem))
            {
                return null;
            }

            Series series;
            return byItem.TryGetValue(itemType, out series) ? series : null;
        }
    }
}
