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

        // Left null in Main.unity today (Inspector default) -- placement there stays exactly
        // as free as it always was. Sandbox.unity wires this to the shared stockpile buffer,
        // which is what actually turns scrapCost/brassCost on.
        [SerializeField] private StorageBufferRegistryHolder _stockpileHolder;
        [SerializeField] private string _stockpileBufferId = "FactoryStockpile";
        [SerializeField] private PlaceableBuilding[] _availablePrefabs;

        private GridCoordinateConverter _converter;
        private InputAction _clickAction;
        private Vector2Int _hoveredCell;

        private static readonly Color RefusedPopupColor = new Color(1f, 0.52f, 0.40f, 1f);
        private static readonly Color SpentPopupColor = new Color(0.72f, 0.75f, 0.78f, 1f);

        private int ReadStock(string itemType)
        {
            if (_stockpileHolder == null ||
                !_stockpileHolder.Registry.TryGetBuffer(_stockpileBufferId, out StorageBuffer buffer))
            {
                return 0;
            }

            return buffer.GetQuantity(itemType);
        }

        // Skipped outside Play mode: FloatingPopup drives itself from Update, so in an EditMode
        // test it would never tick and never be destroyed.
        private static void SpawnPopup(Vector3 worldPosition, string text, Color color)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(text))
            {
                return;
            }

            GolemFactory.UI.FloatingPopup.Spawn(worldPosition + new Vector3(0f, 0.3f, 0f), text, color);
        }

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

        /// <summary>
        /// State the ghost is currently showing. Exposed so a test can assert what the player
        /// is being told without reading a Color off a SpriteRenderer.
        /// </summary>
        public BuildGhostState GhostState { get; private set; } = BuildGhostState.Valid;

        private void UpdateGhost()
        {
            if (_ghost == null)
            {
                return;
            }

            _ghost.transform.position = _converter.CellToWorldCenter(_hoveredCell);
            bool occupied = _gridMapHolder != null && _gridMapHolder.Map.IsOccupied(_hoveredCell);
            GhostState = BuildGhostVisuals.Classify(occupied, CanAffordActivePrefab());
            // Colours and the blocked pulse come from BuildGhostVisuals, which documents the
            // measurements behind them -- the old inline green/red pair was tuned against the
            // pre-reskin cold grey floor and composited to a 1.06:1 contrast ratio against
            // each other on the warm plank floor that replaced it.
            _ghost.color = BuildGhostVisuals.Evaluate(GhostState, Time.time);
            _ghost.gameObject.SetActive(_buildingPrefab != null);
        }

        /// <summary>
        /// Whether the active prefab's cost is currently payable. With no stockpile wired (as
        /// in Main.unity) placement is free, so this is always true there -- exactly matching
        /// PlaceOrRemove's own cost check, so the ghost can never promise something placement
        /// will then refuse.
        /// </summary>
        public bool CanAffordActivePrefab()
        {
            if (_buildingPrefab == null || _stockpileHolder == null)
            {
                return true;
            }

            if (!_stockpileHolder.Registry.TryGetBuffer(_stockpileBufferId, out StorageBuffer buffer))
            {
                return GolemFactory.UI.ConstructionCostPolicy.CanAfford(
                    0, 0, _buildingPrefab.ScrapCost, _buildingPrefab.BrassCost);
            }

            return GolemFactory.UI.ConstructionCostPolicy.CanAfford(
                buffer.GetQuantity(ItemType.Scrap), buffer.GetQuantity(ItemType.Brass),
                _buildingPrefab.ScrapCost, _buildingPrefab.BrassCost);
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
                // The refusal has to appear at the cursor. Until now this string was set and
                // never rendered anywhere, so a click that could not be paid for was
                // indistinguishable from a click that did not register.
                SpawnPopup(_converter.CellToWorldCenter(cell),
                    GolemFactory.UI.ConstructionCostPolicy.FormatShortfall(
                        ReadStock(ItemType.Scrap), ReadStock(ItemType.Brass),
                        _buildingPrefab.ScrapCost, _buildingPrefab.BrassCost),
                    RefusedPopupColor);
                return;
            }

            LastStatusMessage = "";
            PlaceableBuilding instance = Instantiate(_buildingPrefab, _converter.CellToWorldCenter(cell), Quaternion.identity);
            instance.Cell = cell;
            map.TryOccupy(cell, instance);
            if (_stockpileHolder != null && (_buildingPrefab.ScrapCost > 0 || _buildingPrefab.BrassCost > 0))
            {
                SpawnPopup(_converter.CellToWorldCenter(cell),
                    "-" + GolemFactory.UI.ConstructionCostPolicy.FormatCost(
                        _buildingPrefab.ScrapCost, _buildingPrefab.BrassCost),
                    SpentPopupColor);
            }
        }
    }
}
