using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Events;
using GolemFactory.Golems;

namespace GolemFactory.UI
{
    // UGUI replacement for the original OnGUI stall indicator: a small world-space badge
    // floating above the golem while it's Stalled, driven by GolemStalled/GolemResumed
    // (filtered to this golem's id) rather than polling Program.State every frame. Built as
    // a World Space Canvas child so it tracks the golem's position without manual
    // WorldToScreenPoint math -- the isometric camera never rotates, so no billboarding is
    // needed for it to face the camera correctly.
    public sealed class GolemStallIndicator : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private Sprite badgeSprite;

        private bool _isStalled;
        private Canvas _canvas;
        private TextMeshProUGUI _label;

        private void Awake() => BuildIndicator();

        private void OnEnable()
        {
            EventBus.GolemStalled += OnGolemStalled;
            EventBus.GolemResumed += OnGolemResumed;
            _isStalled = golem != null && golem.Program.State == GolemState.Stalled;
            // Re-derive the caption from live state, not just from the event: enabling after a
            // golem has already stalled is exactly the case the event stream cannot cover
            // (the same gap that left the alerts strip reading "All golems running.").
            if (_isStalled)
            {
                RefreshCaption(golem.StallReason, golem.StallResourceId);
            }
            RefreshVisibility();
        }

        private void OnDisable()
        {
            EventBus.GolemStalled -= OnGolemStalled;
            EventBus.GolemResumed -= OnGolemResumed;
        }

        private void LateUpdate()
        {
            if (!_isStalled || golem == null || _canvas == null)
            {
                return;
            }

            _canvas.transform.position = golem.transform.position + worldOffset;
        }

        private void OnGolemStalled(GolemStalledEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _isStalled = true;
                RefreshCaption(e.Reason, e.ResourceId);
                RefreshVisibility();
            }
        }

        private void OnGolemResumed(GolemResumedEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _isStalled = false;
                RefreshVisibility();
            }
        }

        // The badge names the blocking resource, not just the golem. Under a strictly-linear
        // execution model the golem will retry the same step forever, so "GolemD is stalled"
        // isn't actionable on its own -- the resource that's blocking it is the only thing the
        // player can actually go and fix.
        private void RefreshCaption(StallReason reason, string resourceId)
        {
            if (_label == null)
            {
                return;
            }

            string who = golem != null ? golem.GolemId : "Golem";
            _label.text = "[!] " + who + "\n" + StallDiagnostics.DescribeShort(reason, resourceId);
        }

        private void RefreshVisibility()
        {
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(_isStalled);
            }
        }

        private void BuildIndicator()
        {
            var canvasGO = new GameObject("StallBadgeCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localScale = Vector3.one * 0.008f;

            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 5000;

            RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
            // Two lines now (id + reason), so the badge is taller and wider than the
            // single-line version it replaces.
            canvasRect.sizeDelta = new Vector2(300f, 84f);

            var badgeGO = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeGO.transform.SetParent(canvasGO.transform, false);
            RectTransform badgeRect = badgeGO.GetComponent<RectTransform>();
            badgeRect.anchorMin = Vector2.zero;
            badgeRect.anchorMax = Vector2.one;
            badgeRect.offsetMin = Vector2.zero;
            badgeRect.offsetMax = Vector2.zero;

            Image badgeImage = badgeGO.GetComponent<Image>();
            badgeImage.color = new Color(0.75f, 0.15f, 0.1f, 0.92f);
            if (badgeSprite != null)
            {
                badgeImage.sprite = badgeSprite;
                badgeImage.type = Image.Type.Sliced;
            }

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(badgeGO.transform, false);
            RectTransform labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _label = labelGO.GetComponent<TextMeshProUGUI>();
            // TMP's default SDF font atlas (LiberationSans SDF) doesn't include U+26A0
            // (legacy Text's dynamic OS font fallback rendered it fine, TMP's static atlas
            // renders it as a missing-glyph box) -- plain ASCII avoids the atlas gap.
            _label.text = golem != null ? $"[!] {golem.GolemId}" : "[!]";
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = Color.white;
            _label.fontSize = 22;
            _label.fontStyle = FontStyles.Bold;
            _label.raycastTarget = false;

            canvasGO.SetActive(false);
        }
    }
}
