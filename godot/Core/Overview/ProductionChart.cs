using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GolemFactory.Economy;

namespace GolemFactory.Overview
{
    /// <summary>
    /// A row of the graph's legend, which is also its table view: a good made or spent in the
    /// range, both its averages, and its line if shown.
    /// </summary>
    public readonly struct ChartRow
    {
        public readonly string ItemType;
        public readonly string Name;

        /// <summary>The range's averages, goods a minute.</summary>
        public readonly float Made;
        public readonly float Spent;

        /// <summary>The colour slot of its line, 0-based, or -1 when it isn't drawn.</summary>
        public readonly int Slot;

        public ChartRow(string itemType, string name, float made, float spent, int slot)
        {
            ItemType = itemType;
            Name = name;
            Made = made;
            Spent = spent;
            Slot = slot;
        }

        public bool Shown => Slot >= 0;

        /// <summary>The legend's columns: "18", "0.5", "0".</summary>
        public string MadeText => ProductionChart.Number(Made);
        public string SpentText => ProductionChart.Number(Spent);

        /// <summary>"18 a min", the made average.</summary>
        public string RateText => ProductionChart.RateText(Made);
    }

    /// <summary>
    /// The production graph's state (docs/menus-design.md §8): its range, whether it draws
    /// goods made or goods spent (docs/supply-design.md §4), which goods are drawn, and each
    /// one's colour. At most <see cref="MaxLines"/> lines, so every line can carry its name at
    /// its end. The legend lists every good made or spent in the range with both averages, and
    /// clicking a row draws or hides its line. A good keeps its colour while it is drawn:
    /// hiding one frees its slot without repainting the rest, and changing the range or the
    /// measure changes nothing but what the lines count.
    /// </summary>
    public sealed class ProductionChart
    {
        public const int MaxLines = 4;

        private readonly ProductionHistory _history;
        private readonly string[] _slots = new string[MaxLines];
        private bool _chosen;

        public ProductionChart(ProductionHistory history)
        {
            _history = history;
        }

        public HistoryRange Range { get; set; } = HistoryRange.TenMinutes;

        /// <summary>What the lines count. The legend shows both, whichever is drawn.</summary>
        public ChartMeasure Measure { get; set; } = ChartMeasure.Made;

        /// <summary>The goods drawn, in slot order (an empty slot is skipped).</summary>
        public IEnumerable<string> Shown => _slots.Where(s => s != null);

        public int SlotOf(string itemType) => Array.IndexOf(_slots, itemType);

        /// <summary>
        /// Catches up with the history: the first time anything has been made or spent, the
        /// most-made goods take the slots (then the most spent). After that only the player's
        /// clicks change what is drawn.
        /// </summary>
        public void Refresh()
        {
            if (_chosen)
            {
                return;
            }
            IReadOnlyList<string> goods = _history.Goods(Range);
            if (goods.Count == 0)
            {
                return;
            }
            for (int i = 0; i < MaxLines && i < goods.Count; i++)
            {
                _slots[i] = goods[i];
            }
            _chosen = true;
        }

        /// <summary>
        /// Draws or hides a good's line. A fifth is refused (false): one of the four must be
        /// hidden first, so the colours on screen never change under the player.
        /// </summary>
        public bool Toggle(string itemType)
        {
            _chosen = true;
            int slot = SlotOf(itemType);
            if (slot >= 0)
            {
                _slots[slot] = null;
                return true;
            }
            int free = Array.IndexOf(_slots, null);
            if (free < 0)
            {
                return false;
            }
            _slots[free] = itemType;
            return true;
        }

        /// <summary>Every good made or spent in the range, and any drawn but no longer either, most made first.</summary>
        public IReadOnlyList<ChartRow> Rows()
        {
            List<string> goods = _history.Goods(Range).ToList();
            goods.AddRange(Shown.Where(s => !goods.Contains(s)));
            return goods.Select(g => new ChartRow(g, ItemTiers.DisplayName(g),
                _history.Average(g, Range, ChartMeasure.Made), _history.Average(g, Range, ChartMeasure.Spent), SlotOf(g))).ToList();
        }

        /// <summary>The y axis's top: the highest drawn point, rounded up to a round number; at least 10.</summary>
        public float AxisTop()
        {
            float max = 0f;
            foreach (string good in Shown)
            {
                foreach (float rate in Rates(good))
                {
                    if (!float.IsNaN(rate) && rate > max)
                    {
                        max = rate;
                    }
                }
            }
            return RoundUp(max);
        }

        /// <summary>10, 20, 25, 50, 100, 200, 250, 500 ... the first at or above <paramref name="value"/>.</summary>
        public static float RoundUp(float value)
        {
            float step = 10f;
            while (true)
            {
                foreach (float m in new[] { 1f, 2f, 2.5f, 5f })
                {
                    if (step * m >= value)
                    {
                        return step * m;
                    }
                }
                step *= 10f;
            }
        }

        /// <summary>"24 a min"; below ten with one decimal, "0.5 a min".</summary>
        public static string RateText(float perMinute) => Number(perMinute) + " a min";

        /// <summary>"24"; below ten with one decimal, "0.5"; "0" for nothing.</summary>
        public static string Number(float perMinute)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            if (perMinute <= 0f)
            {
                return "0";
            }
            return perMinute < 10f ? perMinute.ToString("0.#", c) : Math.Round(perMinute).ToString("0", c);
        }

        /// <summary>The time under point <paramref name="point"/> of <see cref="ProductionHistory.Points"/>: "now", "40 s ago", "12 min ago".</summary>
        public string TimeAt(int point)
        {
            float secondsAgo = (ProductionHistory.Points - 1 - point) * ProductionHistory.BucketsPerPoint(Range) * ProductionHistory.BucketSeconds;
            return Ago(secondsAgo);
        }

        public static string Ago(float seconds)
        {
            if (seconds < 1f)
            {
                return "now";
            }
            return seconds < 60f ? $"{Math.Round(seconds)} s ago" : $"{Math.Round(seconds / 60f)} min ago";
        }

        /// <summary>The axis's left end: "10 min ago", "1 hour ago".</summary>
        public string RangeStart => Range == HistoryRange.Hour ? "1 hour ago" : "10 min ago";

        public static string RangeName(HistoryRange range) => range == HistoryRange.Hour ? "1 hour" : "10 min";

        public static string MeasureName(ChartMeasure measure) => measure == ChartMeasure.Spent ? "Spent" : "Made";

        /// <summary>The graph's heading: "Goods made a minute", "Goods spent a minute".</summary>
        public string Heading => "Goods " + MeasureName(Measure).ToLowerInvariant() + " a minute";

        /// <summary>
        /// What the hover says over point <paramref name="point"/>: its time and what it counts
        /// ("40 s ago · spent"), then each drawn good's rate there.
        /// </summary>
        public IReadOnlyList<string> HoverLines(int point)
        {
            var lines = new List<string> { TimeAt(point) + " · " + MeasureName(Measure).ToLowerInvariant() };
            foreach (string good in Shown)
            {
                float rate = Rates(good)[point];
                lines.Add(ItemTiers.DisplayName(good) + "  " + (float.IsNaN(rate) ? "not yet recorded" : RateText(rate)));
            }
            return lines;
        }

        /// <summary>The good's rates in the measure drawn.</summary>
        public float[] Rates(string itemType) => _history.Rates(itemType, Range, Measure);
    }
}
