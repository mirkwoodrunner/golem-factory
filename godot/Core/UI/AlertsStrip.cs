using System;
using System.Collections.Generic;
using GolemFactory.Diagnostics;
using GolemFactory.Golems;
using GolemFactory.Overview;

namespace GolemFactory.UI
{
    /// <summary>
    /// The one-line alerts strip at the top of the HUD -- "All golems running." or the worst
    /// stall and how many more -- Unity's AlertsPanel with its MonoBehaviour taken out (G8).
    ///
    /// <para>
    /// Events alone are not trusted: <see cref="StallTracker"/> listens for stalls and
    /// resumes, but a golem built after the strip started listening, or one that recovered
    /// without an event reaching it, would be missed or never cleared. So every
    /// <see cref="ReconcileIntervalSeconds"/> it re-reads every live golem's actual state from
    /// the roster -- the reconcile pass the three ported tests pin.
    /// </para>
    ///
    /// <para>
    /// It counts STOPPED golems only (red or blue, <see cref="GolemMoodRules.IsStopped"/>). A
    /// golem waiting on the factory (grey) is never an alert; with nothing stopped the strip
    /// says how many are waiting (docs/onboarding-feedback-design.md §3.6).
    /// </para>
    /// </summary>
    public sealed class AlertsStrip
    {
        public const float ReconcileIntervalSeconds = 0.5f;

        private readonly StallTracker _tracker = new StallTracker();
        private readonly List<StallSnapshot> _snapshots = new List<StallSnapshot>();
        private readonly Func<IEnumerable<GolemEntity>> _roster;
        private float _untilReconcile;

        public AlertsStrip(Func<IEnumerable<GolemEntity>> roster)
        {
            _roster = roster;
            // A golem that starts waiting publishes a stall too; the event path asks the roster
            // so it never shows for the half second before the next reconcile. One the roster
            // doesn't know counts, as every stall did before.
            _tracker.IsStopped = id =>
            {
                GolemEntity golem = Find(id);
                return golem == null || GolemMoodRules.IsStopped(golem.Mood);
            };
        }

        public StallTracker Tracker => _tracker;

        /// <summary>
        /// Names tiles for the strip's sentence; null (bare tests) prints the stall's ids as
        /// before. <c>SandboxWorld</c> wires its <c>TileNames</c>.
        /// </summary>
        public TileNames Names { get; set; }

        /// <summary>Every stop so far, newest first (docs/menus-design.md §8), fed by each reconcile.</summary>
        public AlertLog Log { get; } = new AlertLog();

        /// <summary>The player's seconds since the strip began: the log's clock.</summary>
        public float Seconds { get; private set; }

        /// <summary>Golems stalled but waiting on the factory (grey), as of the last reconcile.</summary>
        public int WaitingCount { get; private set; }

        public void Attach() => _tracker.Subscribe();

        public void Detach() => _tracker.Unsubscribe();

        /// <summary>Per frame (real time): reconcile on the interval.</summary>
        public void Update(float seconds)
        {
            Seconds += seconds;
            _untilReconcile -= seconds;
            if (_untilReconcile <= 0f)
            {
                _untilReconcile = ReconcileIntervalSeconds;
                Reconcile();
            }
        }

        /// <summary>The endgame profile's sampler (§3's <c>strip</c>); null, the default, measures nothing.</summary>
        public PerfSampler Perf { get; set; }

        /// <summary>Re-reads every golem's state into the tracker.</summary>
        public void Reconcile()
        {
            using (Perf.Measure(PerfSampler.Strip))
            {
                ReconcileNow();
            }
        }

        private void ReconcileNow()
        {
            _snapshots.Clear();
            int waiting = 0;
            IReadOnlyList<GolemEntity> roster = Roster();
            for (int i = 0; i < roster.Count; i++)
            {
                GolemEntity golem = roster[i];
                if (golem == null || golem.IsRemoved || golem.Program == null ||
                    golem.Program.State != GolemState.Stalled)
                {
                    continue;
                }
                if (!GolemMoodRules.IsStopped(golem.Mood))
                {
                    waiting++;
                    continue;
                }
                _snapshots.Add(new StallSnapshot(golem.GolemId, golem.StallReason, golem.StallResourceId, golem.StallShortfall, golem.SteamShortage));
            }
            _tracker.Reconcile(_snapshots);
            WaitingCount = waiting;
            Log.Observe(Seconds, _snapshots, _describe ??= Describe);
        }

        private Func<StallSnapshot, string> _describe;

        // The roster by index. Walking it through its interface boxed an enumerator on every
        // reconcile, and on every stall a golem published during a tick (Find).
        private readonly List<GolemEntity> _rosterScratch = new List<GolemEntity>();

        private IReadOnlyList<GolemEntity> Roster()
        {
            IEnumerable<GolemEntity> golems = _roster?.Invoke();
            if (golems is IReadOnlyList<GolemEntity> list)
            {
                return list;
            }
            _rosterScratch.Clear();
            if (golems != null)
            {
                _rosterScratch.AddRange(golems);
            }
            return _rosterScratch;
        }

        /// <summary>A stop's sentence, as the strip would say it, in player names when it can.</summary>
        private string Describe(StallSnapshot stall)
        {
            GolemEntity golem = Find(stall.GolemId);
            string description = golem != null && Names != null ? StallDiagnostics.Describe(golem, stall, Names) : null;
            return description ?? StallDiagnostics.ComposeStripText(1, stall);
        }

        private GolemEntity Find(string golemId)
        {
            IReadOnlyList<GolemEntity> roster = Roster();
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] != null && roster[i].GolemId == golemId)
                {
                    return roster[i];
                }
            }
            return null;
        }

        /// <summary>What the strip says now.</summary>
        public string Text
        {
            get
            {
                _tracker.TryGetPrimaryStall(out StallSnapshot primary);

                // A NoSteam stall's cause is read from the golem NOW, not from the snapshot: the
                // tracker also takes GolemStalled events, which carry no cause, and each one
                // overwrote the reconciled snapshot -- so the live strip kept the vague line
                // while the golem's own badge said why (G10, caught in the frames).
                GolemEntity golem = _tracker.Count > 0 ? Find(primary.GolemId) : null;
                if (primary.Reason == Events.StallReason.NoSteam && golem != null)
                {
                    primary = new StallSnapshot(primary.GolemId, primary.Reason, primary.ResourceId, primary.Shortfall, golem.SteamShortage);
                }
                // In player names when the golem is known and names are wired (SandboxWorld):
                // "Scavenger #4 stalled: Crate (2,-1) is full", never the ids (Phase 2 critique).
                string description = golem != null && Names != null ? StallDiagnostics.Describe(golem, primary, Names) : null;
                return StallDiagnostics.ComposeStripText(_tracker.Count, primary, WaitingCount, description);
            }
        }
    }
}
