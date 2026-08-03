using System.Collections.Generic;
using GolemFactory.Events;

namespace GolemFactory.UI
{
    // One golem's current stall, as plain data so StallTracker stays unit-testable without a
    // GameObject (the reason AlertsPanel's bookkeeping was factored out here in the first place).
    public readonly struct StallSnapshot
    {
        public readonly string GolemId;
        public readonly StallReason Reason;
        public readonly string ResourceId;

        public StallSnapshot(string golemId, StallReason reason, string resourceId)
        {
            GolemId = golemId;
            Reason = reason;
            ResourceId = resourceId;
        }
    }

    // Plain C# tracker of "currently stalled" golem ids, driven by
    // EventBus.GolemStalled/GolemResumed. Factored out of AlertsPanel so the add/remove
    // bookkeeping is unit-testable without a GameObject or OnGUI.
    //
    // Events alone cannot be the source of truth here. A listener only hears stalls that
    // happen *after* it subscribes, so anything already stalled when the strip enabled stayed
    // invisible to it forever -- which is exactly how the alerts strip came to read "All
    // golems running." while two golems sat Stalled. Events keep the strip responsive within
    // a frame; Reconcile re-derives the set from the golems' actual State so it cannot drift.
    public sealed class StallTracker
    {
        private readonly Dictionary<string, StallSnapshot> _stalled = new Dictionary<string, StallSnapshot>();

        public IReadOnlyCollection<string> StalledGolemIds => _stalled.Keys;
        public int Count => _stalled.Count;

        public void Subscribe()
        {
            EventBus.GolemStalled += OnGolemStalled;
            EventBus.GolemResumed += OnGolemResumed;
        }

        public void Unsubscribe()
        {
            EventBus.GolemStalled -= OnGolemStalled;
            EventBus.GolemResumed -= OnGolemResumed;
        }

        public bool IsStalled(string golemId) => _stalled.ContainsKey(golemId);

        public bool TryGetStall(string golemId, out StallSnapshot snapshot) =>
            _stalled.TryGetValue(golemId, out snapshot);

        // Replaces the tracked set wholesale from live golem state. Called on an interval by
        // AlertsPanel rather than every frame -- correctness only needs it to run
        // occasionally, since the event stream already handles the common case promptly.
        public void Reconcile(IReadOnlyList<StallSnapshot> currentlyStalled)
        {
            _stalled.Clear();
            if (currentlyStalled == null)
            {
                return;
            }

            for (int i = 0; i < currentlyStalled.Count; i++)
            {
                StallSnapshot snapshot = currentlyStalled[i];
                if (!string.IsNullOrEmpty(snapshot.GolemId))
                {
                    _stalled[snapshot.GolemId] = snapshot;
                }
            }
        }

        // The single stall to name when the strip has room for one. Lowest StallReason wins so
        // the choice is deterministic rather than dependent on dictionary ordering; ties break
        // on golem id for the same reason.
        public bool TryGetPrimaryStall(out StallSnapshot primary)
        {
            primary = default(StallSnapshot);
            bool found = false;
            foreach (KeyValuePair<string, StallSnapshot> entry in _stalled)
            {
                StallSnapshot candidate = entry.Value;
                if (!found ||
                    candidate.Reason < primary.Reason ||
                    (candidate.Reason == primary.Reason &&
                     string.CompareOrdinal(candidate.GolemId, primary.GolemId) < 0))
                {
                    primary = candidate;
                    found = true;
                }
            }

            return found;
        }

        private void OnGolemStalled(GolemStalledEvent e) =>
            _stalled[e.GolemId] = new StallSnapshot(e.GolemId, e.Reason, e.ResourceId);

        private void OnGolemResumed(GolemResumedEvent e) => _stalled.Remove(e.GolemId);
    }
}
