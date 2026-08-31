using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Golems;

namespace GolemFactory.UI
{
    // The world-space badge floating over a golem. It began as a stall indicator and is now the
    // golem's MOOD badge (docs/cozy-automation-design.md §2) -- stalls are two of its six moods,
    // and the loudest two.
    //
    // --- WHY THE CLASS IS STILL CALLED GolemStallIndicator ------------------------------------
    // It is referenced by class name from GolemPrefab.prefab's serialized data. Renaming it
    // compiles perfectly and orphans that reference silently, which is exactly the class of bug
    // this project has written down twice already (a RectTransform with no Canvas ancestor; a
    // prefab value overridden by the scene). The name is the cheap part; the wiring is not.
    //
    // --- ONE BADGE, NOT SIX -------------------------------------------------------------------
    // A second floating component per mood would fight this one for the same anchor and the same
    // WorldHudRegistry slot, so the badge widened instead of multiplying. It draws NOTHING for
    // GolemMood.Working: a factory of forty golems each wearing a "working" icon is a factory
    // nobody can read. Silence is the state that means everything is fine.
    //
    // Built as a World Space Canvas child so it tracks the golem's position without manual
    // WorldToScreenPoint math -- the camera never rotates, so no billboarding is needed.
    public sealed class GolemStallIndicator : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        // Measured from the golem's TRANSFORM, which since the BottomCenter pivot pass means its
        // feet rather than its middle. A chassis sprite is 96px at PPU 64, so its head is 1.5
        // world units up; 1.75 leaves the same quarter-cell gap above it that 1.0 left when the
        // transform sat at the sprite's centre. Left at 1.0 the badge would have hung on the
        // golem's chest, which is the sort of thing a pivot change breaks quietly.
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.75f, 0f);
        [SerializeField] private Sprite badgeSprite;

        private Canvas _canvas;
        private Image _badgeImage;
        private TextMeshProUGUI _label;

        // Starts out of band so the first LateUpdate always applies whatever the golem is really
        // in, rather than trusting a default that happens to match.
        private GolemMood _mood = (GolemMood)(-1);
        private float _heldSeconds;

        // The caption inputs the badge was last rendered from. A stall's reason can change while
        // the mood does not (a golem going from BeltFull to NodeEmpty is Stalled either way), so
        // mood alone is not enough to decide whether the text needs rebuilding.
        private Events.StallReason _renderedReason;
        private string _renderedResourceId;
        private int _renderedShortfall;

        /// <summary>The mood currently drawn. Exposed so a test can assert it without a screen.</summary>
        public GolemMood Mood => _mood;

        /// <summary>Whether the badge is on screen right now.</summary>
        public bool IsBadgeVisible => _canvas != null && _canvas.gameObject.activeSelf;

        private void Awake() => BuildIndicator();

        private void OnEnable()
        {
            // Held time restarts on enable rather than persisting, so a golem re-enabled while
            // asleep serves its dwell again instead of popping a badge on frame one. The dwell
            // is about "has this been true long enough to be worth saying", and a fresh listener
            // genuinely does not know.
            _heldSeconds = 0f;
            _mood = (GolemMood)(-1);
            Refresh(0f);
        }

        private void LateUpdate()
        {
            Refresh(Time.deltaTime);

            if (!IsBadgeVisible || golem == null || _canvas == null)
            {
                return;
            }

            // Through the registry, so two golems standing a cell apart do not stack their
            // badges on one another -- a badge nobody can read is a stall nobody fixes. Keyed by
            // golem id rather than by instance, so the answer is stable across a respawn.
            _canvas.transform.position = WorldHudRegistry.Resolve(
                golem.transform.position + worldOffset, golem.GolemId);
        }

        /// <summary>
        /// One frame of mood tracking. Takes its delta explicitly so a test can advance the dwell
        /// clock without waiting on real time.
        /// </summary>
        public void Refresh(float deltaTime)
        {
            GolemMood mood = golem != null ? golem.Mood : GolemMood.Working;

            if (mood != _mood)
            {
                _mood = mood;
                _heldSeconds = 0f;
                ApplyMoodChrome(mood);
                RefreshCaption(mood);
            }
            else
            {
                _heldSeconds += deltaTime;
                // A stall that changes its REASON keeps its mood but must not keep its caption:
                // the whole reason StallDiagnostics exists is to name the blocking resource, and
                // a stale name sends the player to fix the wrong thing.
                if (GolemMoodRules.IsStopped(mood) && CaptionInputsChanged())
                {
                    RefreshCaption(mood);
                }
            }

            bool shouldShow = GolemMoodRules.ShouldShowBadge(mood, _heldSeconds);
            if (_canvas != null && _canvas.gameObject.activeSelf != shouldShow)
            {
                _canvas.gameObject.SetActive(shouldShow);
            }
        }

        private bool CaptionInputsChanged() =>
            golem != null &&
            (golem.StallReason != _renderedReason ||
             golem.StallResourceId != _renderedResourceId ||
             golem.StallShortfall != _renderedShortfall);

        private void ApplyMoodChrome(GolemMood mood)
        {
            if (_badgeImage != null)
            {
                _badgeImage.color = GolemMoodPalette.Badge(mood);
            }

            if (_canvas != null)
            {
                // The advisory moods are physically smaller, so a room of sleeping golems does
                // not shout as loudly as one stalled one. Scaled on the canvas rather than the
                // plate so the caption shrinks with it.
                _canvas.transform.localScale = Vector3.one * (CanvasScale * GolemMoodPalette.BadgeScale(mood));
            }
        }

        // The badge names the blocking resource, not just the golem. Under a strictly-linear
        // execution model the golem will retry the same step forever, so "GolemD is stalled"
        // isn't actionable on its own -- the resource that's blocking it is the only thing the
        // player can actually go and fix. The advisory moods carry no resource, so they get
        // GolemMoodRules' one-word captions instead.
        private void RefreshCaption(GolemMood mood)
        {
            if (_label == null)
            {
                return;
            }

            if (!GolemMoodRules.IsStopped(mood))
            {
                _label.text = GolemMoodRules.Caption(mood);
                _renderedReason = Events.StallReason.None;
                _renderedResourceId = null;
                _renderedShortfall = 0;
                return;
            }

            _renderedReason = golem != null ? golem.StallReason : Events.StallReason.None;
            _renderedResourceId = golem != null ? golem.StallResourceId : null;
            _renderedShortfall = golem != null ? golem.StallShortfall : 0;

            string who = golem != null ? golem.GolemId : "Golem";
            _label.text = "[!] " + who + "\n" +
                StallDiagnostics.DescribeShort(_renderedReason, _renderedResourceId, _renderedShortfall);
        }

        private const float CanvasScale = 0.008f;

        private void BuildIndicator()
        {
            var canvasGO = new GameObject("MoodBadgeCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localScale = Vector3.one * CanvasScale;

            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 5000;

            RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
            // Two lines for a stall (id + reason), so the badge is sized for the loudest case
            // and the quiet ones simply do not fill it.
            canvasRect.sizeDelta = new Vector2(300f, 84f);

            var badgeGO = new GameObject("Badge", typeof(RectTransform), typeof(Image));
            badgeGO.transform.SetParent(canvasGO.transform, false);
            RectTransform badgeRect = badgeGO.GetComponent<RectTransform>();
            badgeRect.anchorMin = Vector2.zero;
            badgeRect.anchorMax = Vector2.one;
            badgeRect.offsetMin = Vector2.zero;
            badgeRect.offsetMax = Vector2.zero;

            _badgeImage = badgeGO.GetComponent<Image>();
            _badgeImage.color = GolemMoodPalette.Badge(GolemMood.Stalled);
            if (badgeSprite != null)
            {
                _badgeImage.sprite = badgeSprite;
                _badgeImage.type = Image.Type.Sliced;
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
