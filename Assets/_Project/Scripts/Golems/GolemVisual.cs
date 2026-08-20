using UnityEngine;
using GolemFactory.Events;

namespace GolemFactory.Golems
{
    // The visual layer for a golem: assigns its sprite once, then drives its tint and its motion
    // from GolemEntity.Mood (docs/cozy-automation-design.md §2). Reads GolemEntity only; never
    // writes to it.
    //
    // --- WHY THIS POLLS RATHER THAN LISTENS ---------------------------------------------------
    // It used to subscribe to GolemStalled/GolemResumed and keep an _isStalled bool, which gave
    // the golem exactly two appearances: white and bobbing, or red and still. Moods need five,
    // and the four transitions that matter most -- Idle -> Running, stock filling toward the
    // cap, a program being erased, a golem enabled while ALREADY stalled -- publish no event at
    // all. GolemStallIndicator.OnEnable already carried a hand-written re-derivation for that
    // last case with a comment saying the event stream cannot cover it; polling a pure
    // classifier makes the whole class of gap go away.
    //
    // The stall event is still used for exactly one thing: the entry shake. A shake is a
    // TRANSITION, and a poller cannot see a transition it did not happen to sample.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class GolemVisual : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        [SerializeField] private Sprite sprite;

        [SerializeField] private float bobAmplitude = 0.04f;
        [SerializeField] private float bobFrequency = 2.2f;
        [SerializeField] private float shakeAmplitude = 0.08f;
        [SerializeField] private float shakeFrequency = 45f;
        [SerializeField] private float shakeDuration = 0.35f;

        private SpriteRenderer _spriteRenderer;
        private Vector3 _basePosition;
        private float _shakeTimeRemaining;

        // Cached so the tint is written on a CHANGE rather than every frame -- the same
        // "re-render from data, but only when the data moved" discipline WorkbenchController and
        // BeltSegmentVisual follow. Starts deliberately out of band so the first frame always
        // applies whatever the golem is actually in.
        private GolemMood _mood = (GolemMood)(-1);

        /// <summary>The mood currently being drawn. Exposed so a test can assert it.</summary>
        public GolemMood Mood => _mood;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _basePosition = transform.position;
            ApplySprite();
        }

        private void Update() => Refresh(Time.time, Time.deltaTime);

        /// <summary>
        /// One frame of mood, tint and motion. Split out of <c>Update</c> and given its clock
        /// explicitly so a test can drive it without waiting on real time -- the same reason
        /// <c>HandCrankBench.Tick</c> takes a tick rather than reading one.
        /// </summary>
        public void Refresh(float time, float deltaTime)
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            GolemMood mood = golem != null ? golem.Mood : GolemMood.Working;
            if (mood != _mood)
            {
                _mood = mood;
                _spriteRenderer.color = GolemMoodPalette.Tint(mood);
            }

            if (_shakeTimeRemaining > 0f)
            {
                _shakeTimeRemaining -= deltaTime;
                float shakeX = GolemAnimationUtility.ComputeShakeOffset(
                    time, _shakeTimeRemaining, shakeDuration, shakeAmplitude, shakeFrequency);
                transform.position = _basePosition + new Vector3(shakeX, 0f, 0f);
                return;
            }

            // No branch for "held still": the stopped moods resolve to a zero amplitude, and
            // ComputeIdleBobOffset returns 0 for that. See GolemAnimationUtility.BobFor.
            GolemAnimationUtility.BobParameters bob =
                GolemAnimationUtility.BobFor(mood, bobAmplitude, bobFrequency);
            float bobY = GolemAnimationUtility.ComputeIdleBobOffset(time, bob.Amplitude, bob.Frequency);
            transform.position = _basePosition + new Vector3(0f, bobY, 0f);
        }

        /// <summary>
        /// Re-reads the golem's transform as the anchor the bob/shake animate around.
        /// </summary>
        /// <remarks>
        /// Required by anything that MOVES a golem after Awake (the player summoning one onto
        /// their tile). Update rewrites transform.position from _basePosition every frame, so
        /// without this the golem would visibly snap straight back to where it was built while
        /// its simulation cell said otherwise -- sprite and routing disagreeing, which is
        /// exactly the illegibility this pass exists to remove.
        /// </remarks>
        public void SyncBasePosition() => _basePosition = transform.position;

        // Called by GolemConstructionStation right after TryAssignChassis, since chassis
        // assignment happens after Instantiate -- this component's Awake already ran with
        // no chassis yet. Falls back to the Inspector-set sprite field when the chassis has
        // no chassisSprite of its own, so hand-wired demo golems are unaffected.
        public void RefreshSpriteFromChassis()
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }
            ApplySprite();
        }

        private void ApplySprite()
        {
            Sprite resolved = golem != null && golem.Program?.chassis?.chassisSprite != null
                ? golem.Program.chassis.chassisSprite
                : sprite;
            if (resolved != null)
            {
                _spriteRenderer.sprite = resolved;
            }
        }

        private void OnEnable() => EventBus.GolemStalled += OnGolemStalled;

        private void OnDisable() => EventBus.GolemStalled -= OnGolemStalled;

        // The one thing left that genuinely needs an event: a single jolt on the frame a stall
        // lands. GolemStalledEvent is published on the TRANSITION into Stalled (and again if the
        // reason changes) rather than every tick, which is what stops the shake being re-armed
        // ten times a second and never decaying -- see the EventBus comment.
        private void OnGolemStalled(GolemStalledEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _shakeTimeRemaining = shakeDuration;
            }
        }
    }
}
