using System;
using System.Collections.Generic;
using System.Globalization;
using GolemFactory.Events;
using GolemFactory.UI;

namespace GolemFactory.Overview
{
    /// <summary>One stop: a golem went red or blue, why, when, and when it ran again.</summary>
    public sealed class AlertEntry
    {
        public string GolemId { get; internal set; }

        /// <summary>The strip's sentence when it stopped: "Scavenger #4 stalled: Crate (2,-1) is full".</summary>
        public string Text { get; internal set; }

        /// <summary>Out of steam (blue), rather than stuck (red).</summary>
        public bool NoSteam { get; internal set; }

        public StallReason Reason { get; internal set; }
        public string ResourceId { get; internal set; }

        /// <summary>The player's seconds when it stopped, and when it ran again (NaN while it is still stopped).</summary>
        public float StartedAt { get; internal set; }
        public float EndedAt { get; internal set; } = float.NaN;

        public bool Open => float.IsNaN(EndedAt);

        /// <summary>
        /// A note, not a stop: a contract posted or done (docs/contracts-design.md §5). No golem,
        /// never open, never red.
        /// </summary>
        public bool IsNote { get; internal set; }
    }

    /// <summary>
    /// The alerts history (docs/menus-design.md §8): every time a golem stopped (red or blue),
    /// newest first, with how long it stayed stopped. It is fed by the alerts strip's own
    /// reconcile, so the history and the strip never disagree about what counts: a golem
    /// waiting on the factory (grey) is never an alert here either. A stop whose cause changes
    /// becomes a new entry. At most <see cref="MaxEntries"/>; not saved.
    /// </summary>
    public sealed class AlertLog
    {
        public const int MaxEntries = 40;

        private readonly List<AlertEntry> _entries = new List<AlertEntry>();
        private readonly Dictionary<string, AlertEntry> _open = new Dictionary<string, AlertEntry>();

        /// <summary>Newest first.</summary>
        public IReadOnlyList<AlertEntry> Entries => _entries;

        /// <summary>
        /// The golems stopped now (<paramref name="stopped"/>, the reconcile's snapshots) at the
        /// player's <paramref name="seconds"/>. <paramref name="describe"/> names a new stop.
        /// </summary>
        public void Observe(float seconds, IReadOnlyList<StallSnapshot> stopped, Func<StallSnapshot, string> describe)
        {
            var now = new HashSet<string>();
            foreach (StallSnapshot stall in stopped)
            {
                now.Add(stall.GolemId);
                if (_open.TryGetValue(stall.GolemId, out AlertEntry open))
                {
                    if (open.Reason == stall.Reason && open.ResourceId == stall.ResourceId)
                    {
                        continue;
                    }
                    open.EndedAt = seconds; // the cause changed: a new stop
                }
                var entry = new AlertEntry
                {
                    GolemId = stall.GolemId,
                    Text = describe?.Invoke(stall) ?? StallDiagnostics.ComposeStripText(1, stall),
                    NoSteam = stall.Reason == StallReason.NoSteam,
                    Reason = stall.Reason,
                    ResourceId = stall.ResourceId,
                    StartedAt = seconds,
                };
                _open[stall.GolemId] = entry;
                _entries.Insert(0, entry);
            }
            foreach (string id in new List<string>(_open.Keys))
            {
                if (!now.Contains(id))
                {
                    _open[id].EndedAt = seconds;
                    _open.Remove(id);
                }
            }
            // The oldest finished stops go first; one still open stays, or it would come back as new.
            for (int i = _entries.Count - 1; i >= 0 && _entries.Count > MaxEntries; i--)
            {
                if (!_entries[i].Open)
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Logs a note at the player's <paramref name="seconds"/>: a moment, not a stop, so it is
        /// over as it is written and only ever ages out.
        /// </summary>
        public void Note(float seconds, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            _entries.Insert(0, new AlertEntry { Text = text, IsNote = true, StartedAt = seconds, EndedAt = seconds });
            for (int i = _entries.Count - 1; i >= 0 && _entries.Count > MaxEntries; i--)
            {
                if (!_entries[i].Open)
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        /// <summary>"2 min ago · out of steam · stopped for 0:42", or a note's "2 min ago · City Office".</summary>
        public static string Line(AlertEntry entry, float now) =>
            entry.IsNote
                ? $"{When(entry, now)} · City Office"
                : $"{When(entry, now)} · {(entry.NoSteam ? "out of steam" : "stuck")} · {For(entry, now)}";

        /// <summary>"2 min ago" for when it stopped.</summary>
        public static string When(AlertEntry entry, float now) => ProductionChart.Ago(now - entry.StartedAt);

        /// <summary>"stopped for 0:42", or "still stopped · 1:05".</summary>
        public static string For(AlertEntry entry, float now) =>
            entry.Open ? "still stopped · " + Clock(now - entry.StartedAt) : "stopped for " + Clock(entry.EndedAt - entry.StartedAt);

        public static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)Math.Round(seconds));
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
