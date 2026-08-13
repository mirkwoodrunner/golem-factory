using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        [SerializeField] private Vector2 _cellSize = new Vector2(1f, 1f);

        // Left null in Main.unity today (Inspector default) -- placement there stays exactly
        // as free as it always was. Sandbox.unity wires this to the shared stockpile buffer,
        // which is what actually turns scrapCost/brassCost on.
        [SerializeField] private StorageBufferRegistryHolder _stockpileHolder;
        [SerializeField] private string _stockpileBufferId = "FactoryStockpile";
        [SerializeField] private PlaceableBuilding[] _availablePrefabs;

        // Belt placement. Optional: with no network wired, a PlaceableBelt prefab still places
        // as a plain building (it just never registers a lane), and Main.unity -- which offers
        // no belt at all -- is untouched.
        [SerializeField] private BeltNetworkHolder _beltNetworkHolder;
        [SerializeField] private SpatialEndpointRegistryHolder _spatialEndpointHolder;
        [SerializeField] private GolemFactory.Belts.ConveyorSystemHolder _conveyorHolder;

        // Steam power (docs/progression-design.md §3.1). Optional in exactly the same additive
        // way belts are: with no network wired, a Boiler or Steam Pipe prefab still places as a
        // plain building -- it just never publishes itself into the steam grid -- and Main.unity,
        // which offers neither, is untouched.
        [SerializeField] private GolemFactory.Steam.SteamNetworkHolder _steamNetworkHolder;

        private GridCoordinateConverter _converter;
        private InputAction _clickAction;
        private InputAction _rotateAction;
        private Vector2Int _hoveredCell;

        /// <summary>
        /// Direction the next placed building/belt will face. Cycled with R. Held on the
        /// controller rather than the prefab because it must persist between placements --
        /// laying a run of belts means orienting once and clicking several times.
        /// </summary>
        public Facing PlacementFacing { get; private set; } = Facing.North;

        /// <summary>
        /// Whether the player is currently holding a placeable. Used to arbitrate the shared R
        /// key: in build mode R turns the ghost, otherwise PlayerInteractor uses it to turn the
        /// nearest golem. One binding, two meanings, decided by whether a tool is in hand.
        /// </summary>
        public bool IsPlacementActive => _buildingPrefab != null;

        /// <summary>Turns the ghost one step clockwise. Public so a test can drive it directly.</summary>
        public void RotatePlacement() => PlacementFacing = FacingUtility.RotateClockwise(PlacementFacing);

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

        /// <summary>
        /// Wires belt placement. Separate again for the same reason ConfigureEconomy is: a
        /// scene with no belts (Main.unity) never calls it and nothing changes there.
        /// </summary>
        public void ConfigureBelts(
            BeltNetworkHolder beltNetworkHolder, SpatialEndpointRegistryHolder spatialEndpointHolder,
            GolemFactory.Belts.ConveyorSystemHolder conveyorHolder)
        {
            _beltNetworkHolder = beltNetworkHolder;
            _spatialEndpointHolder = spatialEndpointHolder;
            _conveyorHolder = conveyorHolder;
        }

        /// <summary>
        /// Wires steam placement. Separate again, for the same reason ConfigureBelts is: a
        /// scene that offers no Boiler or Steam Pipe never calls it and nothing changes there.
        /// </summary>
        public void ConfigureSteam(GolemFactory.Steam.SteamNetworkHolder steamNetworkHolder) =>
            _steamNetworkHolder = steamNetworkHolder;

        // Called by UI/BuildMenuPanel when the player picks a different placeable type.
        public void SetActivePrefab(PlaceableBuilding prefab) => _buildingPrefab = prefab;

        public PlaceableBuilding ActivePrefab => _buildingPrefab;
        public IReadOnlyList<PlaceableBuilding> AvailablePrefabs => _availablePrefabs;

        private void Awake()
        {
            _converter = new GridCoordinateConverter(_cellSize);
            if (_actions != null)
            {
                InputActionMap gameplay = _actions.FindActionMap("Gameplay");
                _clickAction = gameplay?.FindAction("Click");
                _rotateAction = gameplay?.FindAction("Rotate");
            }
        }

        private void OnEnable()
        {
            if (_clickAction != null)
            {
                _clickAction.Enable();
                _clickAction.performed += OnClickPerformed;
            }

            if (_rotateAction != null)
            {
                _rotateAction.Enable();
                _rotateAction.performed += OnRotatePerformed;
            }
        }

        private void OnDisable()
        {
            if (_clickAction != null)
            {
                _clickAction.performed -= OnClickPerformed;
                _clickAction.Disable();
            }

            if (_rotateAction != null)
            {
                _rotateAction.performed -= OnRotatePerformed;
                _rotateAction.Disable();
            }
        }

        // R only turns the ghost when a placeable is actually in hand; otherwise it belongs to
        // PlayerInteractor, which uses it to rotate the nearest already-placed golem.
        private void OnRotatePerformed(InputAction.CallbackContext context)
        {
            if (IsPlacementActive)
            {
                RotatePlacement();
            }
        }

        /// <summary>
        /// Whether the cursor was over UI as of this frame's update. Sampled here rather than read
        /// inside <see cref="OnClickPerformed"/>, because Unity hit-tests the UI once per frame
        /// and querying it from within input-event processing answers with the PREVIOUS frame's
        /// state -- which for a panel that just opened is exactly the wrong answer.
        /// </summary>
        private bool _pointerOverUi;

        private void Update()
        {
            if (_camera == null || Pointer.current == null)
            {
                // No camera or pointer (tests, headless) -- leave _pointerOverUi false so
                // PlaceOrRemove stays directly callable exactly as it always was.
                return;
            }

            _pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

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

            // The ghost has to show which way the thing will point, or R is an invisible mode
            // switch: the player rotates, sees no change, and finds out only after placing.
            // The arrow is a child object so the ghost's own sprite stays upright.
            UpdateGhostFacingArrow();

            bool occupied = _gridMapHolder != null && _gridMapHolder.Map.IsOccupied(_hoveredCell);
            GhostState = BuildGhostVisuals.Classify(occupied, CanAffordActivePrefab());
            // Colours and the blocked pulse come from BuildGhostVisuals, which documents the
            // measurements behind them -- the old inline green/red pair was tuned against the
            // pre-reskin cold grey floor and composited to a 1.06:1 contrast ratio against
            // each other on the warm plank floor that replaced it.
            _ghost.color = BuildGhostVisuals.Evaluate(GhostState, Time.time);
            // Hidden over UI as well as with nothing in hand, so the ghost and the click agree:
            // a tile that will not be built on must not be showing a "valid placement" square
            // under an open menu.
            _ghost.gameObject.SetActive(BuildClickPolicy.ShouldPlace(_buildingPrefab != null, _pointerOverUi));
        }

        // The ghost's facing arrow, created on demand as a child of the ghost so no scene or
        // prefab wiring is required for it to appear -- matching how FloatingPopup and
        // InteractionPromptView build their own presentation rather than needing authored slots.
        [SerializeField] private Sprite _facingArrowSprite;
        private SpriteRenderer _ghostArrow;

        private void UpdateGhostFacingArrow()
        {
            if (_ghostArrow == null)
            {
                if (_facingArrowSprite == null)
                {
                    return;
                }

                var go = new GameObject("FacingArrow");
                go.transform.SetParent(_ghost.transform, false);
                _ghostArrow = go.AddComponent<SpriteRenderer>();
                _ghostArrow.sprite = _facingArrowSprite;
                _ghostArrow.sortingLayerID = _ghost.sortingLayerID;
                _ghostArrow.sortingOrder = _ghost.sortingOrder + 1;
            }

            _ghostArrow.transform.localRotation =
                Quaternion.Euler(0f, 0f, FacingVisuals.ScreenAngleDegrees(PlacementFacing, _cellSize));
            // Nudged along the facing so it reads as "out of this tile, that way" rather than
            // as a decoration sitting on the tile.
            Vector2 direction = FacingVisuals.ScreenDirection(PlacementFacing, _cellSize);
            _ghostArrow.transform.localPosition = new Vector3(direction.x * 0.28f, direction.y * 0.28f, 0f);
            _ghostArrow.color = _ghost.color;
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

            return GolemFactory.UI.ConstructionCostPolicy.CanAfford(ReadStock, _buildingPrefab.Cost);
        }

        // A click over UI belongs to that UI, not to the world underneath it. Without this,
        // closing the golem construction panel also tried to build a Depot on whatever tile the
        // panel was covering -- and so did clicking a row in the build menu, so *selecting* a
        // placeable immediately tried to place one.
        private void OnClickPerformed(InputAction.CallbackContext context)
        {
            if (!BuildClickPolicy.ShouldPlace(IsPlacementActive, _pointerOverUi))
            {
                return;
            }

            PlaceOrRemove(_hoveredCell);
        }

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
                    // Tear the lane down BEFORE destroying the GameObject. BeltNetwork.TryRemove
                    // is what clears any upstream belt's Next pointer; skipping it would leave a
                    // live belt handing items to an unregistered segment that never ticks, so
                    // they would pile into a lane the player can no longer see.
                    if (_beltNetworkHolder != null && building.GetComponent<PlaceableBelt>() != null)
                    {
                        _beltNetworkHolder.Network.TryRemove(cell);
                    }

                    // A removed depot must stop being an endpoint too, or golems keep pushing
                    // into a building that is no longer there. The same is true of the two
                    // other things that publish an input tile -- a Clock Tower and a Boiler's
                    // fuel hatch -- so the test is "did this building publish an endpoint",
                    // not "was it a depot".
                    if (_spatialEndpointHolder != null &&
                        (building.GetComponent<PlaceableDepot>() != null ||
                         building.GetComponent<PlaceableClockTower>() != null ||
                         building.GetComponent<PlaceableBoiler>() != null))
                    {
                        _spatialEndpointHolder.Registry.Unregister(cell);
                    }

                    // Steam has to come out of the grid before the GameObject goes, for the same
                    // reason a belt does. A paved-over pipe that stayed registered would keep
                    // carrying steam through a gap the player can see -- progression-design §9's
                    // Phase 6 beat is precisely that gap biting, and it cannot bite if the
                    // network never hears about it.
                    if (_steamNetworkHolder != null)
                    {
                        PlaceableSteamPipe removedPipe = building.GetComponent<PlaceableSteamPipe>();
                        if (removedPipe != null)
                        {
                            removedPipe.UnregisterFromSteamNetwork(_steamNetworkHolder);
                        }

                        PlaceableBoiler removedBoiler = building.GetComponent<PlaceableBoiler>();
                        if (removedBoiler != null)
                        {
                            removedBoiler.UnregisterFromSteamNetwork(_steamNetworkHolder);
                        }
                    }

                    map.Free(cell);
                    Destroy(building.gameObject);
                }

                return;
            }

            if (_buildingPrefab == null)
            {
                return;
            }

            // Atomic with a full refund on shortfall (StorageBufferRegistry.TryWithdrawBundle):
            // a Boiler is 30 Scrap + 10 Iron Plate, and taking the Scrap for a placement that
            // then refuses would be a straight theft at the cursor.
            if (_stockpileHolder != null &&
                !_stockpileHolder.Registry.TryWithdrawBundle(_stockpileBufferId, _buildingPrefab.Cost))
            {
                LastStatusMessage = $"Not enough resources to build {_buildingPrefab.name} " +
                                     $"(needs {GolemFactory.UI.ConstructionCostPolicy.FormatCost(_buildingPrefab.Cost)}).";
                // The refusal has to appear at the cursor. Until now this string was set and
                // never rendered anywhere, so a click that could not be paid for was
                // indistinguishable from a click that did not register.
                SpawnPopup(_converter.CellToWorldCenter(cell),
                    GolemFactory.UI.ConstructionCostPolicy.FormatShortfall(ReadStock, _buildingPrefab.Cost),
                    RefusedPopupColor);
                return;
            }

            LastStatusMessage = "";
            PlaceableBuilding instance = Instantiate(_buildingPrefab, _converter.CellToWorldCenter(cell), Quaternion.identity);
            instance.Cell = cell;
            instance.Facing = PlacementFacing;
            map.TryOccupy(cell, instance);
            RegisterPlacedEndpoints(instance, cell);
            if (_stockpileHolder != null && _buildingPrefab.Cost != null && _buildingPrefab.Cost.Count > 0)
            {
                SpawnPopup(_converter.CellToWorldCenter(cell),
                    "-" + GolemFactory.UI.ConstructionCostPolicy.FormatCost(_buildingPrefab.Cost),
                    SpentPopupColor);
            }
        }

        // Two placeables also have to exist in the *simulation*, not just the scene, and both
        // are published here so one place owns "this building is now really in the world".
        //
        //   * A belt: BeltNetwork creates the BeltSegment, registers it with the ConveyorSystem
        //     so it ticks, publishes a spatial endpoint on the cell, and auto-chains it to
        //     whatever it points into. The scene component is only told the result.
        //   * A depot: publishes its StorageBuffer on the cell, giving a routed chain somewhere
        //     to actually end.
        private void RegisterPlacedEndpoints(PlaceableBuilding instance, Vector2Int cell)
        {
            PlaceableBelt belt = instance.GetComponent<PlaceableBelt>();
            if (belt != null && _beltNetworkHolder != null)
            {
                PlacedBelt placed;
                if (_beltNetworkHolder.Network.TryPlace(cell, PlacementFacing, out placed))
                {
                    belt.BindSegment(placed.Segment, PlacementFacing, _conveyorHolder, _cellSize);
                }
            }

            PlaceableDepot depot = instance.GetComponent<PlaceableDepot>();
            if (depot != null)
            {
                depot.RegisterAsSpatialEndpoint(_spatialEndpointHolder, _stockpileHolder, cell);
            }

            //   * a clock tower: publishes its input tile, giving the megaproject somewhere for
            //     a golem to Push into. Without this a placed tower was inert -- the win
            //     condition could be built and then never delivered to.
            PlaceableClockTower tower = instance.GetComponent<PlaceableClockTower>();
            if (tower != null)
            {
                tower.RegisterAsSpatialEndpoint(_spatialEndpointHolder, tower.SiteHolder, cell);
            }

            //   * a boiler or a steam pipe: publishes itself into the SteamNetwork, which
            //     re-derives every boiler's reach on the spot. A PIPE is not an IItemEndpoint --
            //     steam is not a good that travels on tiles -- but a BOILER is, on its fuel
            //     side: Coke reaches it in a golem's hold like any other good. See
            //     Steam/BoilerFuelEndpoint for why that had to exist at all.
            if (_steamNetworkHolder != null)
            {
                PlaceableBoiler boiler = instance.GetComponent<PlaceableBoiler>();
                if (boiler != null)
                {
                    boiler.RegisterWithSteamNetwork(_steamNetworkHolder, cell);
                    boiler.RegisterAsSpatialEndpoint(_spatialEndpointHolder, cell);
                }

                PlaceableSteamPipe pipe = instance.GetComponent<PlaceableSteamPipe>();
                if (pipe != null)
                {
                    pipe.RegisterWithSteamNetwork(_steamNetworkHolder, cell);
                }
            }
        }
    }
}
