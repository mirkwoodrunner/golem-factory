using UnityEngine;
using GolemFactory.Events;

namespace GolemFactory.Golems
{
    // Graphics-demo visual layer for a golem: assigns its placeholder sprite once and tints it
    // while Stalled, same EventBus.GolemStalled/GolemResumed subscription idiom
    // UI/GolemStallIndicator.cs already uses -- so the demo visibly communicates simulation
    // state, not just static placement. Reads GolemEntity only for its id; never writes to it.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class GolemVisual : MonoBehaviour
    {
        private static readonly Color StalledTint = new Color(0.85f, 0.35f, 0.35f, 1f);

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
        private bool _isStalled;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _basePosition = transform.position;
            ApplySprite();
        }

        // Idle bob while running, a decaying shake jolt right when a stall lands, then a
        // held-still pose while stalled -- reuses the same GolemStalled/GolemResumed
        // subscription as the tint below rather than polling Program.State every frame.
        private void Update()
        {
            if (_shakeTimeRemaining > 0f)
            {
                _shakeTimeRemaining -= Time.deltaTime;
                float shakeX = GolemAnimationUtility.ComputeShakeOffset(
                    Time.time, _shakeTimeRemaining, shakeDuration, shakeAmplitude, shakeFrequency);
                transform.position = _basePosition + new Vector3(shakeX, 0f, 0f);
            }
            else if (!_isStalled)
            {
                float bobY = GolemAnimationUtility.ComputeIdleBobOffset(Time.time, bobAmplitude, bobFrequency);
                transform.position = _basePosition + new Vector3(0f, bobY, 0f);
            }
            else
            {
                transform.position = _basePosition;
            }
        }

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

        private void OnEnable()
        {
            EventBus.GolemStalled += OnGolemStalled;
            EventBus.GolemResumed += OnGolemResumed;
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
                _spriteRenderer.color = StalledTint;
                _isStalled = true;
                _shakeTimeRemaining = shakeDuration;
            }
        }

        private void OnGolemResumed(GolemResumedEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _spriteRenderer.color = Color.white;
                _isStalled = false;
            }
        }
    }
}
