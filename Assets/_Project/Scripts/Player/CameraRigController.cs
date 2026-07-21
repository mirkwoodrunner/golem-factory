using UnityEngine;
using UnityEngine.InputSystem;

namespace GolemFactory.Player
{
    // Drives the plain Camera directly for M1 (pan via transform, zoom via orthographicSize).
    // Cinemachine is installed but not wired in yet - see the M1 implementation notes in
    // docs/unity-implementation-plan.md for why, and why swapping it in later is camera-only.
    public sealed class CameraRigController : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private float _panSpeed = 10f;
        [SerializeField] private float _zoomSpeed = 5f;
        [SerializeField] private float _minOrthographicSize = 3f;
        [SerializeField] private float _maxOrthographicSize = 15f;

        private InputAction _panAction;
        private InputAction _zoomAction;

        private void Awake()
        {
            if (_actions == null)
            {
                return;
            }

            var gameplay = _actions.FindActionMap("Gameplay");
            _panAction = gameplay?.FindAction("Pan");
            _zoomAction = gameplay?.FindAction("Zoom");
        }

        private void OnEnable()
        {
            _panAction?.Enable();
            _zoomAction?.Enable();
        }

        private void OnDisable()
        {
            _panAction?.Disable();
            _zoomAction?.Disable();
        }

        private void Update()
        {
            if (_camera == null)
            {
                return;
            }

            if (_panAction != null)
            {
                Vector2 pan = _panAction.ReadValue<Vector2>();
                if (pan != Vector2.zero)
                {
                    _camera.transform.position += new Vector3(pan.x, pan.y, 0f) * (_panSpeed * Time.deltaTime);
                }
            }

            if (_zoomAction != null)
            {
                float zoom = _zoomAction.ReadValue<float>();
                if (zoom != 0f)
                {
                    _camera.orthographicSize = Mathf.Clamp(
                        _camera.orthographicSize - zoom * _zoomSpeed * Time.deltaTime,
                        _minOrthographicSize,
                        _maxOrthographicSize);
                }
            }
        }
    }
}
