using UnityEngine;
using GolemFactory.Events;
using GolemFactory.Golems;

namespace GolemFactory.UI
{
    // Minimal M6 stall indicator: an OnGUI label projected above the golem's world
    // position while it's Stalled, driven by GolemStalled/GolemResumed (filtered to this
    // golem's id) rather than polling Program.State every frame. No sprite/art asset --
    // that's later visual polish, not M6's job.
    public sealed class GolemStallIndicator : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1f, 0f);

        private bool _isStalled;

        private void OnEnable()
        {
            EventBus.GolemStalled += OnGolemStalled;
            EventBus.GolemResumed += OnGolemResumed;
            _isStalled = golem != null && golem.Program.State == GolemState.Stalled;
        }

        private void OnDisable()
        {
            EventBus.GolemStalled -= OnGolemStalled;
            EventBus.GolemResumed -= OnGolemResumed;
        }

        private void OnGolemStalled(GolemStalledEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _isStalled = true;
            }
        }

        private void OnGolemResumed(GolemResumedEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _isStalled = false;
            }
        }

        private void OnGUI()
        {
            if (!_isStalled || golem == null || Camera.main == null)
            {
                return;
            }

            Vector3 screenPoint = Camera.main.WorldToScreenPoint(golem.transform.position + worldOffset);
            if (screenPoint.z < 0f)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.red }
            };
            GUI.Label(new Rect(screenPoint.x - 30f, Screen.height - screenPoint.y - 10f, 120f, 20f),
                $"⚠ {golem.GolemId}", style);
        }
    }
}
