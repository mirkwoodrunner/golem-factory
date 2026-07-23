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

        private SpriteRenderer _spriteRenderer;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                _spriteRenderer.sprite = sprite;
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
            }
        }

        private void OnGolemResumed(GolemResumedEvent e)
        {
            if (golem != null && e.GolemId == golem.GolemId)
            {
                _spriteRenderer.color = Color.white;
            }
        }
    }
}
