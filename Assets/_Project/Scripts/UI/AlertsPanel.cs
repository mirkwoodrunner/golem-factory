using UnityEngine;

namespace GolemFactory.UI
{
    // Simple M6 alerts panel: lists every golem currently Stalled, live, via StallTracker
    // (driven by GolemStalledEvent/GolemResumedEvent). A "current status" view, not a
    // history log -- OnGUI-based like GolemProgrammingPanel/InventoryPanel, no Canvas/
    // UGUI scene wiring required.
    public sealed class AlertsPanel : MonoBehaviour
    {
        private readonly StallTracker _tracker = new StallTracker();
        private Vector2 _scroll;

        private void OnEnable() => _tracker.Subscribe();

        private void OnDisable() => _tracker.Unsubscribe();

        private void OnGUI()
        {
            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(10f, Screen.height - 150f, 320f, 140f), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Alerts", boldLabel);
            if (_tracker.StalledGolemIds.Count == 0)
            {
                GUILayout.Label("All golems running.");
            }
            else
            {
                foreach (string golemId in _tracker.StalledGolemIds)
                {
                    GUILayout.Label($"⚠ {golemId} is stalled");
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
