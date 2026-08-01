using UnityEngine;
using UnityEngine.InputSystem;
using GolemFactory.World;

namespace GolemFactory.Player
{
    // Analog movement, not grid-locked -- only golems are grid-locked per
    // docs/game-design.md. Reads the Move action each Update and applies
    // PlayerMovement.ComputeDisplacement directly to transform.position (no physics).
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private float _moveSpeed = 4f;

        private InputAction _moveAction;
        private GridCoordinateConverter _floorBoundsConverter;
        private int _floorBoundsHalfExtent;
        private bool _hasFloorBounds;

        // Programmatic setup used by tests (and available for runtime bootstrapping),
        // mirroring BuildModeController.Configure -- avoids requiring Inspector-assigned
        // references.
        public void Configure(InputActionAsset actions, float moveSpeed)
        {
            _actions = actions;
            _moveSpeed = moveSpeed;
        }

        // Separate from Configure (not a required param there) since this is a Sandbox-only
        // concern -- SandboxBootstrap calls this once; Main.unity's player never does, so its
        // movement and every existing test/call site are unaffected.
        public void SetFloorBounds(GridCoordinateConverter converter, int halfExtent)
        {
            _floorBoundsConverter = converter;
            _floorBoundsHalfExtent = halfExtent;
            _hasFloorBounds = true;
        }

        private void Awake()
        {
            if (GetComponent<YSortSpriteRenderer>() == null)
            {
                gameObject.AddComponent<YSortSpriteRenderer>();
            }

            if (_actions != null)
            {
                _moveAction = _actions.FindActionMap("Gameplay")?.FindAction("Move");
            }
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
        }

        private void Update()
        {
            if (_moveAction == null)
            {
                return;
            }

            MoveBy(_moveAction.ReadValue<Vector2>(), Time.deltaTime);
        }

        // Exposed separately from Update so tests can drive movement directly without
        // simulating Input System events, same pattern as BuildModeController.PlaceOrRemove.
        public void MoveBy(Vector2 moveInput, float deltaTime)
        {
            transform.position += PlayerMovement.ComputeDisplacement(moveInput, _moveSpeed, deltaTime);
            if (_hasFloorBounds)
            {
                transform.position = FloorLayout.ClampToFloor(transform.position, _floorBoundsConverter, _floorBoundsHalfExtent);
            }
        }
    }
}
