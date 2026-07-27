using UnityEngine;
using TMPro;

namespace GolemFactory.UI
{
    // Always-on alerts strip: reports every golem currently Stalled, live, via
    // StallTracker (driven by GolemStalledEvent/GolemResumedEvent). A "current status"
    // view, not a history log. UGUI-based (converted from the original OnGUI panel as
    // part of the Management HUD consolidation) -- lives as an always-active sibling
    // (AlertsStrip) in the shared Workbench canvas, top-center, never hidden.
    public sealed class AlertsPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI statusTextMeshProUGUI;

        private readonly StallTracker _tracker = new StallTracker();

        public void ConfigureUI(TextMeshProUGUI status) => statusTextMeshProUGUI = status;

        private void OnEnable() => _tracker.Subscribe();

        private void OnDisable() => _tracker.Unsubscribe();

        private void Update()
        {
            if (statusTextMeshProUGUI == null)
            {
                return;
            }

            statusTextMeshProUGUI.text = _tracker.StalledGolemIds.Count == 0
                ? "All golems running."
                // Plain ASCII, not the ⚠ glyph -- TMP's default SDF font atlas
                // (LiberationSans SDF) doesn't include U+26A0, unlike legacy Text's
                // dynamic OS font fallback, so it would render as a missing-glyph box.
                : $"[!] {_tracker.StalledGolemIds.Count} golem(s) stalled";
        }
    }
}
