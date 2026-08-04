using UnityEngine;
using TMPro;

namespace GolemFactory.UI
{
    /// <summary>
    /// A short-lived world-space caption that rises and fades -- the "+1 Scrap" confirmation a
    /// harvest used to give only as a status string nobody rendered.
    /// <para>
    /// Built entirely from code via <see cref="Spawn"/> with no prefab and no scene wiring,
    /// the same self-assembling world-space Canvas idiom as GolemStallIndicator, so both
    /// Sandbox.unity and Main.unity get it without either scene having to carry a reference to
    /// anything. It destroys its own GameObject when finished, so nothing has to track it.
    /// </para>
    /// </summary>
    public sealed class FloatingPopup : MonoBehaviour
    {
        public const float DefaultDuration = 0.9f;
        public const float DefaultRise = 0.55f;

        private const float CanvasScale = 0.01f;
        private const int SortingOrder = 5200;

        private TextMeshProUGUI _label;
        private Vector3 _origin;
        private float _elapsed;
        private float _duration = DefaultDuration;
        private float _rise = DefaultRise;
        private Color _color = Color.white;

        /// <summary>
        /// Creates and starts a popup at <paramref name="worldPosition"/>. Returns the
        /// instance so a test can drive it, but callers are expected to ignore it.
        /// </summary>
        public static FloatingPopup Spawn(
            Vector3 worldPosition, string text, Color color,
            float duration = DefaultDuration, float rise = DefaultRise)
        {
            var go = new GameObject("FloatingPopup");
            go.transform.position = worldPosition;
            FloatingPopup popup = go.AddComponent<FloatingPopup>();
            popup.Initialize(worldPosition, text, color, duration, rise);
            return popup;
        }

        private void Initialize(Vector3 worldPosition, string text, Color color, float duration, float rise)
        {
            _origin = worldPosition;
            _color = color;
            _duration = duration;
            _rise = rise;
            _elapsed = 0f;
            Build(text);
        }

        private void Build(string text)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localScale = Vector3.one * CanvasScale;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // Above the stall badge: a popup is a momentary confirmation the player is
            // actively looking for, and it lasts under a second.
            canvas.sortingOrder = SortingOrder;

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(260f, 56f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(canvasGo.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _label = labelGo.GetComponent<TextMeshProUGUI>();
            _label.text = text;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = _color;
            _label.fontSize = 30;
            _label.fontStyle = FontStyles.Bold;
            _label.raycastTarget = false;
            // A dark outline, not a drop shadow: the popup floats over a warm mid-luminance
            // floor and over golem sprites, and an outline is the only thing that keeps light
            // text legible against both without knowing which it landed on.
            _label.outlineWidth = 0.22f;
            _label.outlineColor = new Color32(20, 12, 8, 255);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            transform.position = _origin + Vector3.up * FeedbackMotion.RiseOffset(_elapsed, _duration, _rise);
            if (_label != null)
            {
                Color c = _color;
                c.a = FeedbackMotion.FadeOutAlpha(_elapsed, _duration);
                _label.color = c;
            }

            if (_elapsed >= _duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
