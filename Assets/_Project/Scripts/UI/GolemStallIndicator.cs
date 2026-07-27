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

        private void Awake() => BuildIndicator();

        private void OnEnable()
        {
            EventBus.GolemStalled += OnGolemStalled;
            EventBus.GolemResumed += OnGolemResumed;
            _isStalled = golem != null && golem.Program.State == GolemState.Stalled;
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
            canvasRect.sizeDelta = new Vector2(220f, 60f);

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

            TextMeshProUGUI label = labelGO.GetComponent<TextMeshProUGUI>();
            // TMP's default SDF font atlas (LiberationSans SDF) doesn't include U+26A0
            // (legacy Text's dynamic OS font fallback rendered it fine, TMP's static atlas
            // renders it as a missing-glyph box) -- plain ASCII avoids the atlas gap.
            label.text = golem != null ? $"[!] {golem.GolemId}" : "[!]";
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontSize = 28;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;

            canvasGO.SetActive(false);
        }
    }
}
