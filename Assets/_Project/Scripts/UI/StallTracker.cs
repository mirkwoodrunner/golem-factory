using System.Collections.Generic;
using GolemFactory.Events;

namespace GolemFactory.UI
{
    // Plain C# tracker of "currently stalled" golem ids, driven by
    // EventBus.GolemStalled/GolemResumed. Factored out of AlertsPanel so the add/remove
    // bookkeeping is unit-testable without a GameObject or OnGUI.
    public sealed class StallTracker
    {
        private readonly HashSet<string> _stalledGolemIds = new HashSet<string>();
        public IReadOnlyCollection<string> StalledGolemIds => _stalledGolemIds;

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

        public bool IsStalled(string golemId) => _stalledGolemIds.Contains(golemId);

        private void OnGolemStalled(GolemStalledEvent e) => _stalledGolemIds.Add(e.GolemId);
        private void OnGolemResumed(GolemResumedEvent e) => _stalledGolemIds.Remove(e.GolemId);
    }
}
