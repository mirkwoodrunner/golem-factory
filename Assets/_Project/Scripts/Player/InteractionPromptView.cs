using UnityEngine;
using TMPro;
using GolemFactory.UI;

namespace GolemFactory.Player
{
    /// <summary>
    /// The always-on interaction affordance: a ring under whatever <see cref="PlayerInteractor"/>
    /// is currently targeting, plus a caption above it saying what Interact would do.
    /// <para>
    /// Before this, the player-facing loop had no affordance at all -- nothing indicated what
    /// was interactable, what was in range, or what the key would do; you pressed Interact and
    /// found out. In Factorio/Satisfactory that feedback is constant and is the single biggest
    /// thing separating "a scene with objects in it" from a game.
    /// </para>
    /// <para>
    /// Three states, from <see cref="InteractionTargeting.ClassifyAffordance"/>: Ready draws a
    /// bright amber ring and an "[E] ..." caption; OutOfRange draws a dim steel ring and a
    /// "Move closer to ..." caption (this is what makes the interact radius discoverable
    /// without drawing a permanent circle around the player); Hidden draws nothing.
    /// </para>
    /// <para>
    /// Assembles its own children in Awake, so a scene only has to add the component and point
    /// it at a sprite -- no prefab, and no cross-prefab reference to go null on instantiation.
    /// </para>
    /// </summary>
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private Sprite ringSprite;
        [SerializeField] private Vector3 ringOffset = new Vector3(0f, -0.12f, 0f);
        [SerializeField] private Vector3 captionOffset = new Vector3(0f, 0.95f, 0f);
        [SerializeField] private string sortingLayerName = "Default";

        /// <summary>
        /// Warm amber, matching the build ghost's valid tint and the Workbench's "engaged"
        /// vocabulary. Measured at 5.23:1 against the plank floor -- brightness is what
        /// carries here, since every warm hue is at home on this background.
        /// </summary>
        private static readonly Color ReadyRingColor = new Color(1f, 0.82f, 0.42f, 1f);

        /// <summary>
        /// Cool desaturated steel at roughly half the alpha: present enough to be noticed in
        /// peripheral vision, inert enough that it never reads as actionable. 2.52:1 against
        /// the ready ring.
        /// </summary>
        private static readonly Color OutOfRangeRingColor = new Color(0.66f, 0.70f, 0.74f, 0.55f);

        private static readonly Color ReadyTextColor = new Color(1f, 0.93f, 0.80f, 1f);
        private static readonly Color OutOfRangeTextColor = new Color(0.74f, 0.76f, 0.78f, 1f);

        // The ring breathes only while Ready. A steady ring is easy to stop seeing; a slow
        // brightness pulse keeps it in peripheral vision without being a strobe. Out of range
        // it holds still on purpose -- stillness is itself the "not yet" signal.
        private const float PulsePeriod = 1.6f;
        private const float PulseDepth = 0.28f;
        private const float RingScaleMin = 0.92f;
        private const float RingScaleMax = 1f;

        /// <summary>Between the floor Tilemap (-30000) and every Y-sorted entity (~±400).</summary>
        public const int RingSortingOrder = -20000;

        private SpriteRenderer _ring;
        private Canvas _captionCanvas;
        private TextMeshProUGUI _caption;
        private Transform _target;
        private InteractionAffordance _affordance = InteractionAffordance.Hidden;

        /// <summary>Test/bootstrap-friendly setup, same Configure* idiom as the rest of the project.</summary>
        public void Configure(Sprite ring, Vector3 ringWorldOffset, Vector3 captionWorldOffset)
        {
            ringSprite = ring;
            ringOffset = ringWorldOffset;
            captionOffset = captionWorldOffset;
            if (_ring != null)
            {
                _ring.sprite = ringSprite;
            }
        }

        public InteractionAffordance CurrentAffordance => _affordance;
        public Transform CurrentTarget => _target;
        public string CurrentPrompt => _caption != null ? _caption.text : "";
        public bool IsVisible => _affordance != InteractionAffordance.Hidden;

        private void Awake() => Build();

        /// <summary>
        /// Points the affordance at <paramref name="target"/>. Called every frame by
        /// PlayerInteractor; cheap enough to be idempotent, so there is no "has it changed"
        /// bookkeeping to get wrong.
        /// </summary>
        public void Show(Transform target, InteractionAffordance affordance, string prompt)
        {
            if (target == null || affordance == InteractionAffordance.Hidden)
            {
                Hide();
                return;
            }

            _target = target;
            _affordance = affordance;

            // Ready is the only lit, pulsing state. OutOfRange and Unavailable both hold still
            // in dim steel: in both cases pressing the key does nothing, and stillness is
            // itself the "not yet / not here" signal.
            bool ready = affordance == InteractionAffordance.Ready;
            if (_ring != null)
            {
                _ring.gameObject.SetActive(true);
                _ring.color = ready ? ReadyRingColor : OutOfRangeRingColor;
            }

            if (_captionCanvas != null)
            {
                _captionCanvas.gameObject.SetActive(true);
            }

            if (_caption != null)
            {
                _caption.text = prompt;
                _caption.color = ready ? ReadyTextColor : OutOfRangeTextColor;
                _caption.fontStyle = ready ? FontStyles.Bold : FontStyles.Normal;
            }

            ApplyTransforms();
        }

        public void Hide()
        {
            _target = null;
            _affordance = InteractionAffordance.Hidden;
            if (_ring != null)
            {
                _ring.gameObject.SetActive(false);
            }

            if (_captionCanvas != null)
            {
                _captionCanvas.gameObject.SetActive(false);
            }
        }

        // LateUpdate so the ring lands on where the target actually ended the frame, not where
        // it was before the player's own movement was applied.
        private void LateUpdate()
        {
            if (_affordance == InteractionAffordance.Hidden || _target == null)
            {
                return;
            }

            ApplyTransforms();
        }

        private void ApplyTransforms()
        {
            if (_target == null)
            {
                return;
            }

            Vector3 basePosition = _target.position;

            if (_ring != null)
            {
                _ring.transform.position = basePosition + ringOffset;

                if (_affordance == InteractionAffordance.Ready)
                {
                    float breathe = FeedbackMotion.Breathe01(Time.time, PulsePeriod);
                    Color c = ReadyRingColor;
                    c.a = ReadyRingColor.a * (1f - PulseDepth + PulseDepth * breathe);
                    _ring.color = c;
                    _ring.transform.localScale = Vector3.one * Mathf.Lerp(RingScaleMin, RingScaleMax, breathe);
                }
                else
                {
                    _ring.transform.localScale = Vector3.one * RingScaleMin;
                }
            }

            if (_captionCanvas != null)
            {
                _captionCanvas.transform.position = basePosition + captionOffset;
            }
        }

        private void Build()
        {
            var ringGo = new GameObject("InteractionRing", typeof(SpriteRenderer));
            ringGo.transform.SetParent(transform, false);
            _ring = ringGo.GetComponent<SpriteRenderer>();
            _ring.sprite = ringSprite;
            // Drawn under every character but over the floor tilemap. Deliberately NOT
            // Y-sorted: the ring belongs to the floor plane, and letting YSortSpriteRenderer
            // reorder it would make it flicker in front of and behind its own target as the
            // player walks around it.
            //
            // The floor Tilemap renders at -30000 and every Y-sorted entity lands within a few
            // hundred of zero (YSortUtility multiplies world Y by 100), so -20000 is the band
            // between them. A "small negative" order like -100 would have put the ring in
            // front of anything standing further back than y = 1.
            _ring.sortingLayerName = sortingLayerName;
            _ring.sortingOrder = RingSortingOrder;

            var canvasGo = new GameObject("PromptCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localScale = Vector3.one * 0.008f;
            _captionCanvas = canvasGo.GetComponent<Canvas>();
            _captionCanvas.renderMode = RenderMode.WorldSpace;
            _captionCanvas.sortingOrder = 5100;

            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(460f, 52f);

            var labelGo = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(canvasGo.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            _caption = labelGo.GetComponent<TextMeshProUGUI>();
            _caption.alignment = TextAlignmentOptions.Center;
            _caption.fontSize = 24;
            _caption.color = ReadyTextColor;
            _caption.raycastTarget = false;
            _caption.outlineWidth = 0.2f;
            _caption.outlineColor = new Color32(20, 12, 8, 255);

            ringGo.SetActive(false);
            canvasGo.SetActive(false);
        }
    }
}
