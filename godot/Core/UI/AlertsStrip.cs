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
        }

        public StallTracker Tracker => _tracker;

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
            foreach (GolemEntity golem in _roster?.Invoke() ?? Array.Empty<GolemEntity>())
            {
                if (golem == null || golem.IsRemoved || golem.Program == null)
                {
                    continue;
                }
                if (golem.Program.State == GolemState.Stalled)
                {
                    _snapshots.Add(new StallSnapshot(golem.GolemId, golem.StallReason, golem.StallResourceId, golem.StallShortfall, golem.SteamShortage));
                }
            }
            _tracker.Reconcile(_snapshots);
        }

        /// <summary>What the strip says now.</summary>
        public string Text
        {
            get
            {
                _tracker.TryGetPrimaryStall(out StallSnapshot primary);
                return StallDiagnostics.ComposeStripText(_tracker.Count, primary);
            }
        }
    }
}
