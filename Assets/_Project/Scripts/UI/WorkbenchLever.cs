using UnityEngine;
using UnityEngine.UI;

namespace GolemFactory.UI
{
    // The visual half of the "Engage Gears" lever: drives a handle RectTransform down its
    // track and back. Deliberately presentation-only -- it never reads or writes program
    // state, so the draft/commit semantics are untouched.
    //
    // Crucially it is *not* self-triggering. An earlier pass registered Pull() on this
    // GameObject's own Button.onClick, alongside WorkbenchController.EngageGears, so the
    // lever ran a full satisfying throw on every failure path -- positive feedback for a
    // no-op. WorkbenchController now drives it from the commit *result* instead
    // (Pull() on success, Refuse() on rejection), which is why there is no
    // onClick.AddListener here.
    //
    // The motion curves live in the engine-free WorkbenchLeverMotion so the throw/hold/
    // return and refusal shapes are unit-testable without a scene (GolemAnimationUtility
    // idiom); this MonoBehaviour is the thin applier.
    [RequireComponent(typeof(Button))]
    public sealed class WorkbenchLever : MonoBehaviour
    {
        [SerializeField] private RectTransform handle;
        [SerializeField] private float travelPixels = 74f;

        private Vector2 _restPosition;
        private float _elapsed = -1f;
        private bool _refusing;

        // Test/bootstrap-friendly setup, same Configure* idiom as WorkbenchController.
        public void ConfigureHandle(RectTransform handleRect, float travel)
        {
            handle = handleRect;
            travelPixels = travel;
            CacheRest();
        }

        private void Awake() => CacheRest();

        private void OnEnable()
        {
            // Re-showing the screen mid-animation would otherwise leave the handle stuck
            // partway down its track.
            _elapsed = -1f;
            _refusing = false;
            ApplyNormalized(0f);
        }

        private void CacheRest()
        {
            if (handle != null)
            {
                _restPosition = handle.anchoredPosition;
            }
        }

        // A committed pull: the full throw/hold/spring-back.
        public void Pull()
        {
            _refusing = false;
            _elapsed = 0f;
        }

        // A rejected pull: a short judder that never reaches the bottom stop.
        public void Refuse()
        {
            _refusing = true;
            _elapsed = 0f;
        }

        // Exposed for tests/diagnostics: 0 at rest, >0 while either animation runs.
        public bool IsAnimating => _elapsed >= 0f;
        public bool IsRefusing => _refusing && _elapsed >= 0f;

        private void Update()
        {
            if (_elapsed < 0f)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float duration = _refusing ? WorkbenchLeverMotion.RefuseSeconds : WorkbenchLeverMotion.TotalSeconds;
            if (_elapsed >= duration)
            {
                _elapsed = -1f;
                _refusing = false;
                ApplyNormalized(0f);
                return;
            }

            ApplyNormalized(_refusing
                ? WorkbenchLeverMotion.ComputeRefusedNormalized(_elapsed)
                : WorkbenchLeverMotion.ComputeHandleNormalized(_elapsed));
        }

        private void ApplyNormalized(float normalized)
        {
            if (handle == null)
            {
                return;
            }

            handle.anchoredPosition = _restPosition + new Vector2(0f, -travelPixels * normalized);
        }
    }
}
