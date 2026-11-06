using System;
using System.Collections.Generic;
using System.Linq;

namespace GolemFactory.Overview
{
    /// <summary>How far back the production graph looks.</summary>
    public enum HistoryRange
    {
        TenMinutes,
        Hour,
    }

    /// <summary>
    /// What the factory has made, over the last hour, in ten-second buckets (docs/menus-design.md
    /// §8): the production graph's data. It reads the same counts as the Ledger's live rate
    /// (<see cref="Economy.ProductionTally"/>, fresh goods only), so the two never disagree, and
    /// like it runs on the player's time: a paused factory makes nothing. Not saved; a session
    /// starts with an empty graph.
    /// </summary>
    public sealed class ProductionHistory
    {
        public const float BucketSeconds = 10f;

        /// <summary>An hour of buckets.</summary>
        public const int MaxBuckets = 360;

        /// <summary>Every range is drawn as this many points.</summary>
        public const int Points = 60;

        private readonly List<Dictionary<string, int>> _buckets = new List<Dictionary<string, int>>();
        private Dictionary<string, int> _lastTotals;
        private float _bucketEnds = float.NaN;

        /// <summary>Complete buckets held, oldest first; at most <see cref="MaxBuckets"/>.</summary>
        public int BucketCount => _buckets.Count;

        /// <summary>
        /// Called every frame with the time (real seconds) and the running totals; closes a bucket
        /// each <see cref="BucketSeconds"/>, as many as have passed.
        /// </summary>
        public void Record(float seconds, IReadOnlyDictionary<string, int> totals)
        {
            if (float.IsNaN(_bucketEnds))
            {
                _lastTotals = Copy(totals);
                _bucketEnds = seconds + BucketSeconds;
                return;
            }
            while (seconds >= _bucketEnds)
            {
                var made = new Dictionary<string, int>();
                foreach (KeyValuePair<string, int> pair in totals)
                {
                    _lastTotals.TryGetValue(pair.Key, out int before);
                    if (pair.Value > before)
                    {
                        made[pair.Key] = pair.Value - before;
                    }
                }
                _lastTotals = Copy(totals);
                _buckets.Add(made);
                if (_buckets.Count > MaxBuckets)
                {
                    _buckets.RemoveAt(0);
                }
                _bucketEnds += BucketSeconds;
            }
        }

        private static Dictionary<string, int> Copy(IReadOnlyDictionary<string, int> totals) =>
            totals?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, int>();

        /// <summary>Buckets per point: ten seconds a point over ten minutes, a minute a point over the hour.</summary>
        public static int BucketsPerPoint(HistoryRange range) => range == HistoryRange.Hour ? 6 : 1;

        /// <summary>The range's length in seconds.</summary>
        public static float Seconds(HistoryRange range) => Points * BucketsPerPoint(range) * BucketSeconds;

        /// <summary>
        /// <see cref="Points"/> rates in goods a minute, oldest first, the newest last. A point
        /// from before the history began is NaN: the line starts where the record does.
        /// </summary>
        public float[] Rates(string itemType, HistoryRange range)
        {
            int per = BucketsPerPoint(range);
            var rates = new float[Points];
            for (int point = 0; point < Points; point++)
            {
                // The last point ends at the newest bucket.
                int last = _buckets.Count - 1 - (Points - 1 - point) * per;
                int first = last - per + 1;
                if (first < 0)
                {
                    rates[point] = float.NaN;
                    continue;
                }
                int made = 0;
                for (int b = first; b <= last; b++)
                {
                    _buckets[b].TryGetValue(itemType, out int n);
                    made += n;
                }
                rates[point] = made * 60f / (per * BucketSeconds);
            }
            return rates;
        }

        /// <summary>Goods made in the range, the most made first; ties by name, so the order is stable.</summary>
        public IReadOnlyList<string> Goods(HistoryRange range)
        {
            var made = new Dictionary<string, int>();
            foreach (Dictionary<string, int> bucket in InRange(range))
            {
                foreach (KeyValuePair<string, int> pair in bucket)
                {
                    made.TryGetValue(pair.Key, out int n);
                    made[pair.Key] = n + pair.Value;
                }
            }
            return made.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).ToList();
        }

        /// <summary>The good's average over the part of the range that has been recorded, a minute; 0 with nothing recorded.</summary>
        public float Average(string itemType, HistoryRange range)
        {
            List<Dictionary<string, int>> buckets = InRange(range).ToList();
            if (buckets.Count == 0)
            {
                return 0f;
            }
            int made = buckets.Sum(b => b.TryGetValue(itemType, out int n) ? n : 0);
            return made * 60f / (buckets.Count * BucketSeconds);
        }

        private IEnumerable<Dictionary<string, int>> InRange(HistoryRange range)
        {
            int count = Math.Min(_buckets.Count, Points * BucketsPerPoint(range));
            return _buckets.Skip(_buckets.Count - count);
        }
    }
}
