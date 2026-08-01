using UnityEngine;
using UnityEngine.UI;

namespace GolemFactory.UI
{
    // The visual half of the "Engage Gears" lever: drives a handle RectTransform down its
    // track and back whenever the lever is pulled. Deliberately presentation-only -- the
    // Button on the same GameObject still raises WorkbenchController.EngageGears through
    // the normal onClick path, so the draft/commit semantics are untouched; this component
    // never reads or writes program state.
    //
    // The motion curve itself lives in the engine-free WorkbenchLeverMotion so it can be
    // unit-tested without a scene (GolemAnimationUtility idiom); this MonoBehaviour is the
    // thin applier.
    [RequireComponent(typeof(Button))]
    public sealed class WorkbenchLever : MonoBehaviour
    {
        [SerializeField] private RectTransform handle;
        [SerializeField] private float travelPixels = 74f;

        private Vector2 _restPosition;
        private float _elapsed = -1f;

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
            ApplyNormalized(0f);
        }

        private void Start()
        {
            GetComponent<Button>().onClick.AddListener(Pull);
        }

        private void CacheRest()
        {
            if (handle != null)
            {
                _restPosition = handle.anchoredPosition;
            }
        }

        public void Pull() => _elapsed = 0f;

        private void Update()
        {
            if (_elapsed < 0f)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed >= WorkbenchLeverMotion.TotalSeconds)
            {
                _elapsed = -1f;
                ApplyNormalized(0f);
                return;
            }

            ApplyNormalized(WorkbenchLeverMotion.ComputeHandleNormalized(_elapsed));
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
