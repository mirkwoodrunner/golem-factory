using UnityEngine;
using UnityEngine.UI;

namespace GolemFactory.UI
{
    // Always-on alerts strip: reports every golem currently Stalled, live, via
    // StallTracker (driven by GolemStalledEvent/GolemResumedEvent). A "current status"
    // view, not a history log. UGUI-based (converted from the original OnGUI panel as
    // part of the Management HUD consolidation) -- lives as an always-active sibling
    // (AlertsStrip) in the shared Workbench canvas, top-center, never hidden.
    public sealed class AlertsPanel : MonoBehaviour
    {
        [SerializeField] private Text statusText;

        private readonly StallTracker _tracker = new StallTracker();

        public void ConfigureUI(Text status) => statusText = status;

        private void OnEnable() => _tracker.Subscribe();

        private void OnDisable() => _tracker.Unsubscribe();

        private void Update()
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = _tracker.StalledGolemIds.Count == 0
                ? "All golems running."
                : $"⚠ {_tracker.StalledGolemIds.Count} golem(s) stalled";
        }
    }
}
