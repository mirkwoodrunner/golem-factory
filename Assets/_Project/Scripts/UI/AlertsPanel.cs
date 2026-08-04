using System.Collections.Generic;
using GolemFactory.Golems;
using UnityEngine;
using TMPro;

namespace GolemFactory.UI
{
    // Always-on alerts strip: reports every golem currently Stalled, live, via
    // StallTracker (driven by GolemStalledEvent/GolemResumedEvent). A "current status"
    // view, not a history log. UGUI-based (converted from the original OnGUI panel as
    // part of the Management HUD consolidation) -- lives as an always-active sibling
    // (AlertsStrip) in the shared Workbench canvas, top-center, never hidden.
    //
    // Events keep it responsive, but they are not trusted as the source of truth: the strip
    // periodically re-derives the stalled set from the golems' actual Program.State. Purely
    // event-driven, it read "All golems running." while two golems sat Stalled, because a
    // listener never hears about stalls that happened before it subscribed.
    public sealed class AlertsPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI statusTextMeshProUGUI;
        [SerializeField] private float reconcileIntervalSeconds = 0.5f;

        private readonly StallTracker _tracker = new StallTracker();
        private readonly List<StallSnapshot> _snapshotBuffer = new List<StallSnapshot>();
        private GolemEntity[] _golems;
        private float _nextReconcileTime;

        public StallTracker Tracker => _tracker;

        public void ConfigureUI(TextMeshProUGUI status) => statusTextMeshProUGUI = status;

        private void OnEnable()
        {
            _tracker.Subscribe();
            // Reconcile immediately on enable rather than waiting out the first interval --
            // enabling late is precisely the case the event stream cannot cover.
            _nextReconcileTime = 0f;
            RefreshGolemCache();
        }

        private void OnDisable() => _tracker.Unsubscribe();

        private void Update()
        {
            if (Time.unscaledTime >= _nextReconcileTime)
            {
                _nextReconcileTime = Time.unscaledTime + Mathf.Max(0.1f, reconcileIntervalSeconds);
                Reconcile();
            }

            if (statusTextMeshProUGUI == null)
            {
                return;
            }

            StallSnapshot primary;
            _tracker.TryGetPrimaryStall(out primary);
            statusTextMeshProUGUI.text = StallDiagnostics.ComposeStripText(_tracker.Count, primary);
        }

        // Golems can be constructed at runtime (GolemConstructionStation), so the cache is
        // rebuilt on every reconcile rather than only when an entry turns out to be destroyed.
        // An earlier version tried to detect staleness and only rebuild then; it could not see
        // *additions* at all, so in Sandbox -- which starts with zero golems -- the cache stayed
        // empty forever and Reconcile's wholesale clear actively erased stalls the event path
        // had correctly recorded, leaving the strip permanently claiming all golems were fine.
        // This runs on a 0.5s interval over a handful of golems, so the scan is not worth
        // optimising into a correctness hazard.
        private void RefreshGolemCache() =>
            _golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);

        private void Reconcile()
        {
            RefreshGolemCache();

            _snapshotBuffer.Clear();
            for (int i = 0; i < _golems.Length; i++)
            {
                GolemEntity golem = _golems[i];
                if (golem == null || golem.Program == null)
                {
                    continue;
                }

                if (golem.Program.State == GolemState.Stalled)
                {
                    _snapshotBuffer.Add(new StallSnapshot(
                        golem.GolemId, golem.StallReason, golem.StallResourceId));
                }
            }

            _tracker.Reconcile(_snapshotBuffer);
        }
    }
}
