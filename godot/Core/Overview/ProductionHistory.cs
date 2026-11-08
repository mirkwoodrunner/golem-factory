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

    /// <summary>What the graph counts: goods made, or goods spent (docs/supply-design.md §4).</summary>
    public enum ChartMeasure
    {
        Made,
        Spent,
    }

    /// <summary>
    /// What the factory has made and spent, over the last hour, in ten-second buckets
    /// (docs/menus-design.md §8, docs/supply-design.md §4): the production graph's data. Made
    /// reads the same counts as the Ledger's live rate (<see cref="Economy.ProductionTally"/>,
    /// fresh goods only), so the two never disagree; spent reads
    /// <see cref="Economy.ConsumptionTally"/>. Both run on the player's time: a paused factory
    /// makes and spends nothing. Not saved; a session starts with an empty graph.
    /// </summary>
    public sealed class ProductionHistory
    {
        public const float BucketSeconds = 10f;

        /// <summary>An hour of buckets.</summary>
        public const int MaxBuckets = 360;

        /// <summary>Every range is drawn as this many points.</summary>
        public const int Points = 60;

        private static readonly Dictionary<string, int> Empty = new Dictionary<string, int>();

        // One list of buckets for each measure, indexed by ChartMeasure; always the same length.
        private readonly List<Dictionary<string, int>>[] _buckets =
        {
            new List<Dictionary<string, int>>(), new List<Dictionary<string, int>>(),
        };
        private readonly Dictionary<string, int>[] _lastTotals = new Dictionary<string, int>[2];
        private float _bucketEnds = float.NaN;

        /// <summary>Complete buckets held, oldest first; at most <see cref="MaxBuckets"/>.</summary>
        public int BucketCount => _buckets[0].Count;

        /// <summary>
        /// Called every frame with the time (real seconds) and the running totals made and
        /// spent; closes a bucket each <see cref="BucketSeconds"/>, as many as have passed.
        /// </summary>
        public void Record(float seconds, IReadOnlyDictionary<string, int> made, IReadOnlyDictionary<string, int> spent = null)
        {
            IReadOnlyDictionary<string, int>[] totals = { made ?? Empty, spent ?? Empty };
            if (float.IsNaN(_bucketEnds))
            {
                for (int m = 0; m < totals.Length; m++)
                {
                    _lastTotals[m] = Copy(totals[m]);
                }
                _bucketEnds = seconds + BucketSeconds;
                return;
            }
            while (seconds >= _bucketEnds)
            {
                for (int m = 0; m < totals.Length; m++)
                {
                    var counted = new Dictionary<string, int>();
                    foreach (KeyValuePair<string, int> pair in totals[m])
                    {
                        _lastTotals[m].TryGetValue(pair.Key, out int before);
                        if (pair.Value > before)
                        {
                            counted[pair.Key] = pair.Value - before;
                        }
                    }
                    _lastTotals[m] = Copy(totals[m]);
                    _buckets[m].Add(counted);
                    if (_buckets[m].Count > MaxBuckets)
                    {
                        _buckets[m].RemoveAt(0);
                    }
                }
                _bucketEnds += BucketSeconds;
            }
        }

        private static Dictionary<string, int> Copy(IReadOnlyDictionary<string, int> totals) =>
            totals.ToDictionary(p => p.Key, p => p.Value);

        /// <summary>Buckets per point: ten seconds a point over ten minutes, a minute a point over the hour.</summary>
        public static int BucketsPerPoint(HistoryRange range) => range == HistoryRange.Hour ? 6 : 1;

        /// <summary>The range's length in seconds.</summary>
        public static float Seconds(HistoryRange range) => Points * BucketsPerPoint(range) * BucketSeconds;

        /// <summary>
        /// <see cref="Points"/> rates in goods a minute, oldest first, the newest last. A point
        /// from before the history began is NaN: the line starts where the record does.
        /// </summary>
        public float[] Rates(string itemType, HistoryRange range, ChartMeasure measure = ChartMeasure.Made)
        {
            List<Dictionary<string, int>> buckets = _buckets[(int)measure];
            int per = BucketsPerPoint(range);
            var rates = new float[Points];
            for (int point = 0; point < Points; point++)
            {
                // The last point ends at the newest bucket.
                int last = buckets.Count - 1 - (Points - 1 - point) * per;
                int first = last - per + 1;
                if (first < 0)
                {
                    rates[point] = float.NaN;
                    continue;
                }
                int counted = 0;
                for (int b = first; b <= last; b++)
                {
                    buckets[b].TryGetValue(itemType, out int n);
                    counted += n;
                }
                rates[point] = counted * 60f / (per * BucketSeconds);
            }
            return rates;
        }

        /// <summary>
        /// Goods made or spent in the range: the most made first, then the most spent (a raw
        /// good is only ever spent); ties by name, so the order is stable.
        /// </summary>
        public IReadOnlyList<string> Goods(HistoryRange range)
        {
            Dictionary<string, int> made = Sums(range, ChartMeasure.Made);
            Dictionary<string, int> spent = Sums(range, ChartMeasure.Spent);
            return made.Keys.Union(spent.Keys)
                .OrderByDescending(g => made.TryGetValue(g, out int n) ? n : 0)
                .ThenByDescending(g => spent.TryGetValue(g, out int n) ? n : 0)
                .ThenBy(g => g, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The good's average over the part of the range that has been recorded, a minute; 0 with nothing recorded.</summary>
        public float Average(string itemType, HistoryRange range, ChartMeasure measure = ChartMeasure.Made)
        {
            List<Dictionary<string, int>> buckets = InRange(range, measure).ToList();
            if (buckets.Count == 0)
            {
                return 0f;
            }
            int counted = buckets.Sum(b => b.TryGetValue(itemType, out int n) ? n : 0);
            return counted * 60f / (buckets.Count * BucketSeconds);
        }

        private Dictionary<string, int> Sums(HistoryRange range, ChartMeasure measure)
        {
            var sums = new Dictionary<string, int>();
            foreach (Dictionary<string, int> bucket in InRange(range, measure))
            {
                foreach (KeyValuePair<string, int> pair in bucket)
                {
                    sums.TryGetValue(pair.Key, out int n);
                    sums[pair.Key] = n + pair.Value;
                }
            }
            return sums;
        }

        private IEnumerable<Dictionary<string, int>> InRange(HistoryRange range, ChartMeasure measure)
        {
            List<Dictionary<string, int>> buckets = _buckets[(int)measure];
            int count = Math.Min(buckets.Count, Points * BucketsPerPoint(range));
            return buckets.Skip(buckets.Count - count);
        }
    }
}
