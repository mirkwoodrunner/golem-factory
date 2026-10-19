using System;
using System.Collections.Generic;
using GolemFactory.Golems;

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

        /// <summary>Golems stalled but waiting on the factory (grey), as of the last reconcile.</summary>
        public int WaitingCount { get; private set; }

        public void Attach() => _tracker.Subscribe();

        public void Detach() => _tracker.Unsubscribe();

        /// <summary>Per frame (real time): reconcile on the interval.</summary>
        public void Update(float seconds)
        {
            _untilReconcile -= seconds;
            if (_untilReconcile <= 0f)
            {
                _untilReconcile = ReconcileIntervalSeconds;
                Reconcile();
            }
        }

        /// <summary>Re-reads every golem's state into the tracker.</summary>
        public void Reconcile()
        {
            _snapshots.Clear();
            int waiting = 0;
            foreach (GolemEntity golem in _roster?.Invoke() ?? Array.Empty<GolemEntity>())
            {
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
        }

        private GolemEntity Find(string golemId)
        {
            foreach (GolemEntity golem in _roster?.Invoke() ?? Array.Empty<GolemEntity>())
            {
                if (golem != null && golem.GolemId == golemId)
                {
                    return golem;
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
                if (primary.Reason == Events.StallReason.NoSteam)
                {
                    GolemEntity golem = Find(primary.GolemId);
                    if (golem != null)
                    {
                        primary = new StallSnapshot(primary.GolemId, primary.Reason, primary.ResourceId, primary.Shortfall, golem.SteamShortage);
                    }
                }
                return StallDiagnostics.ComposeStripText(_tracker.Count, primary, WaitingCount);
            }
        }
    }
}
