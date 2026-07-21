using UnityEngine;
using UnityEngine.InputSystem;
using GolemFactory.Buildings;
using GolemFactory.World;

namespace GolemFactory.Player
{
    // Click-to-place/click-to-remove against GridMap. Ghost preview and hover tracking are
    // MonoBehaviour concerns; PlaceOrRemove itself only touches GridMap + PlaceableBuilding,
    // so it's callable directly from tests without simulating Input System events.
    public sealed class BuildModeController : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private GridMapHolder _gridMapHolder;
        [SerializeField] private PlaceableBuilding _buildingPrefab;
        [SerializeField] private SpriteRenderer _ghost;
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Vector2 _cellSize = new Vector2(1f, 0.5f);
        [SerializeField] private Color _validColor = new Color(0.4f, 1f, 0.4f, 0.6f);
        [SerializeField] private Color _blockedColor = new Color(1f, 0.4f, 0.4f, 0.6f);

        private GridCoordinateConverter _converter;
        private InputAction _clickAction;
        private Vector2Int _hoveredCell;

        // Programmatic setup used by tests (and available for runtime bootstrapping) so this
        // component doesn't strictly require Inspector-assigned references to be exercised.
        public void Configure(Camera camera, GridMapHolder gridMapHolder, PlaceableBuilding buildingPrefab, Vector2 cellSize)
        {
            _camera = camera;
            _gridMapHolder = gridMapHolder;
            _buildingPrefab = buildingPrefab;
            _cellSize = cellSize;
            _converter = new GridCoordinateConverter(_cellSize);
        }

        private void Awake()
        {
            _converter = new GridCoordinateConverter(_cellSize);
            if (_actions != null)
            {
                _clickAction = _actions.FindActionMap("Gameplay")?.FindAction("Click");
            }
        }

        private void OnEnable()
        {
            if (_clickAction != null)
            {
                _clickAction.Enable();
                _clickAction.performed += OnClickPerformed;
            }
        }

        private void OnDisable()
        {
            if (_clickAction != null)
            {
                _clickAction.performed -= OnClickPerformed;
                _clickAction.Disable();
            }
        }

        private void Update()
        {
            if (_camera == null || Pointer.current == null)
            {
                return;
            }

            Vector3 worldPos = _camera.ScreenToWorldPoint(Pointer.current.position.ReadValue());
            worldPos.z = 0f;
            _hoveredCell = _converter.WorldToCell(worldPos);
            UpdateGhost();
        }

        private void UpdateGhost()
        {
            if (_ghost == null)
            {
                return;
            }

            _ghost.transform.position = _converter.CellToWorldCenter(_hoveredCell);
            bool occupied = _gridMapHolder != null && _gridMapHolder.Map.IsOccupied(_hoveredCell);
            _ghost.color = occupied ? _blockedColor : _validColor;
        }

        private void OnClickPerformed(InputAction.CallbackContext context) => PlaceOrRemove(_hoveredCell);

        public void PlaceOrRemove(Vector2Int cell)
        {
            if (_gridMapHolder == null)
            {
                return;
            }

            GridMap map = _gridMapHolder.Map;
            if (map.IsOccupied(cell))
            {
                if (map.TryGetOccupant(cell, out object occupant) && occupant is PlaceableBuilding building)
                {
                    map.Free(cell);
                    Destroy(building.gameObject);
                }

                return;
            }

            if (_buildingPrefab == null)
            {
                return;
            }

            PlaceableBuilding instance = Instantiate(_buildingPrefab, _converter.CellToWorldCenter(cell), Quaternion.identity);
            instance.Cell = cell;
            map.TryOccupy(cell, instance);
        }
    }
}
