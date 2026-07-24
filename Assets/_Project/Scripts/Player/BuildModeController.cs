using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using GolemFactory.Buildings;
using GolemFactory.Economy;
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

        // Left null in Main.unity today (Inspector default) -- placement there stays exactly
        // as free as it always was. Sandbox.unity wires this to the shared stockpile buffer,
        // which is what actually turns scrapCost/brassCost on.
        [SerializeField] private StorageBufferRegistryHolder _stockpileHolder;
        [SerializeField] private string _stockpileBufferId = "FactoryStockpile";
        [SerializeField] private PlaceableBuilding[] _availablePrefabs;

        private GridCoordinateConverter _converter;
        private InputAction _clickAction;
        private Vector2Int _hoveredCell;

        // Set by PlaceOrRemove on a failed cost check, for a BuildMenuPanel (or a test) to
        // surface -- mirrors UI/GolemProgrammingPanel's own _statusMessage field.
        public string LastStatusMessage { get; private set; } = "";

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

        // Wires the economy side separately from Configure above so existing callers (and
        // Main.unity, which never calls this) are unaffected -- placement there stays free.
        public void ConfigureEconomy(StorageBufferRegistryHolder stockpileHolder, string stockpileBufferId, PlaceableBuilding[] availablePrefabs)
        {
            _stockpileHolder = stockpileHolder;
            _stockpileBufferId = stockpileBufferId;
            _availablePrefabs = availablePrefabs;
        }

        // Called by UI/BuildMenuPanel when the player picks a different placeable type.
        public void SetActivePrefab(PlaceableBuilding prefab) => _buildingPrefab = prefab;

        public PlaceableBuilding ActivePrefab => _buildingPrefab;
        public IReadOnlyList<PlaceableBuilding> AvailablePrefabs => _availablePrefabs;

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

            if (_stockpileHolder != null &&
                !_stockpileHolder.Registry.TryWithdrawScrapAndBrass(_stockpileBufferId, _buildingPrefab.ScrapCost, _buildingPrefab.BrassCost))
            {
                LastStatusMessage = $"Not enough resources to build {_buildingPrefab.name} " +
                                     $"(needs {_buildingPrefab.ScrapCost} Scrap, {_buildingPrefab.BrassCost} Brass).";
                return;
            }

            LastStatusMessage = "";
            PlaceableBuilding instance = Instantiate(_buildingPrefab, _converter.CellToWorldCenter(cell), Quaternion.identity);
            instance.Cell = cell;
            map.TryOccupy(cell, instance);
        }
    }
}
