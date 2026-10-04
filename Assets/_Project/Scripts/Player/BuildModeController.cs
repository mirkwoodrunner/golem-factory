using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
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
        private InputAction _cancelAction;
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

        /// <summary>
        /// Whether the wrecking bar is in hand: clicks remove buildings and place nothing.
        ///
        /// <para>
        /// <b>Why this had to exist.</b> Removal was only ever reachable by clicking an occupied
        /// cell <em>while holding a placeable</em> -- see <see cref="OnClickPerformed"/>, which
        /// gates every click on <c>BuildClickPolicy.ShouldPlace</c>. That was invisible while
        /// there was no way out of build mode, because a placeable was then always in hand. The
        /// moment Escape / right-click / re-clicking the row shipped, "click a depot to take it
        /// back" quietly stopped working for anyone who had put their tool down -- found in
        /// playtest as "the ability to pick up depots goes away at some point".
        /// </para>
        ///
        /// <para>
        /// A mode rather than a modifier key, and rather than simply letting any click demolish:
        /// demolition is destructive and the cursor is already overloaded (harvest, interact,
        /// place). It sits in the build menu beside the things it undoes.
        /// </para>
        /// </summary>
        public bool IsDemolishActive { get; private set; }

        /// <summary>
        /// Whether a click belongs to build mode at all -- either tool counts. Distinct from
        /// <see cref="IsPlacementActive"/>, which arbitrates <c>R</c>: there is nothing to
        /// rotate while demolishing, so R must still reach a bench or a golem.
        /// </summary>
        public bool IsBuildToolActive => IsPlacementActive || IsDemolishActive;

        /// <summary>
        /// Picks up the wrecking bar, putting down any placeable first -- the two are one
        /// cursor and cannot both be held.
        /// </summary>
        public void EnterDemolishMode()
        {
            _buildingPrefab = null;
            IsDemolishActive = true;
            LastStatusMessage = "";
            EndDrag();
        }

        /// <summary>Turns the ghost one step clockwise. Public so a test can drive it directly.</summary>
        public void RotatePlacement() => PlacementFacing = FacingUtility.RotateClockwise(PlacementFacing);

        private static readonly Color RefusedPopupColor = new Color(1f, 0.52f, 0.40f, 1f);
        private static readonly Color SpentPopupColor = new Color(0.72f, 0.75f, 0.78f, 1f);

        // Green where SpentPopupColor is grey: goods coming back read as a gain, and the
        // refund popup fires on the same tile a "-15 Scrap" did when it was built.
        private static readonly Color RefundPopupColor = new Color(0.56f, 0.86f, 0.50f, 1f);

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

        // §6's Freight Mast. Optional in the same additive way steam is: unwired, a mast prefab
        // places as a plain building that publishes its tile but joins no mast network, so no
        // Zeppelin can bind to it.
        [SerializeField] private FreightMastRegistryHolder _mastRegistryHolder;

        public void ConfigureFreight(FreightMastRegistryHolder mastRegistryHolder) =>
            _mastRegistryHolder = mastRegistryHolder;

        // A placed GolemConstructionStation needs SCENE references a prefab cannot carry, so
        // the scene's bootstrap hands itself over here and this asks it to wire each station
        // as it is built. Runtime-only (no [SerializeField]) because an interface reference
        // is not serializable and this is exactly the Configure(...) case the project's idiom
        // exists for. Unwired, a station places as a plain building -- which is what it did
        // before, decoratively.
        private GolemFactory.Buildings.IPlacedStationConfigurator _stationConfigurator;

        public void ConfigureStationWiring(GolemFactory.Buildings.IPlacedStationConfigurator configurator) =>
            _stationConfigurator = configurator;

        // --- The buildable area (docs/progression-design.md §3.3) -------------------------
        // -1 means UNBOUNDED, following ResourceNode.Infinite and StorageBuffer.Unlimited's
        // sentinel idiom, and it is the default: Main.unity and every existing test rig never
        // call ConfigurePlacementBounds and place exactly where they always could. Sandbox
        // turns it on from SandboxBootstrap, next to the identical call that bounds the player.
        private int _placementHalfExtent = -1;
        private int _placementStreetDepth;

        /// <summary>
        /// Bounds placement to the ground that is actually drawn. Separate from
        /// <see cref="Configure"/> for the same reason ConfigureBelts is -- a scene that never
        /// calls this keeps building anywhere.
        ///
        /// <para>
        /// The bound is the WORLD (workshop + street), not the workshop: the five traders stand
        /// out on the street, so bounding to the room would forbid the belts and depots that
        /// reach them. See <see cref="FloorLayout.IsInsideWorld"/>.
        /// </para>
        /// </summary>
        public void ConfigurePlacementBounds(int halfExtent, int streetDepth)
        {
            _placementHalfExtent = halfExtent;
            _placementStreetDepth = streetDepth;
        }

        // The live room, for Floor Expansion (§11 item 15). Held rather than copied: new floor
        // has to be buildable the moment it is paid for, without a second call to re-bound.
        private FloorBounds _placementBounds;

        public void ConfigurePlacementBounds(FloorBounds bounds, int streetDepth)
        {
            _placementBounds = bounds;
            if (bounds != null)
            {
                _placementHalfExtent = bounds.HalfExtent;
                _placementStreetDepth = streetDepth;
            }
        }

        /// <summary>
        /// Whether a building may stand on <paramref name="cell"/> at all. Public so the ghost,
        /// the click and a test all read one answer -- a ghost that promises a tile placement
        /// then refuses is the bug this replaced, not an improvement on it.
        /// </summary>
        public bool IsCellBuildable(Vector2Int cell)
        {
            if (_placementHalfExtent < 0)
            {
                return true;
            }

            if (_placementBounds != null)
            {
                return FloorLayout.IsInsideWorld(
                    cell, _placementBounds.HalfExtent, _placementStreetDepth,
                    FloorLayout.StreetHalfExtent, _placementBounds.NorthExtent);
            }

            return FloorLayout.IsInsideWorld(cell, _placementHalfExtent, _placementStreetDepth);
        }

        /// <summary>
        /// Called by UI/BuildMenuPanel when the player picks a placeable type. Takes the wrecking
        /// bar out of the player's hand, because <b>the two are one cursor and cannot both be
        /// held</b> — the exact mirror of what <see cref="EnterDemolishMode"/> already does to a
        /// placeable.
        ///
        /// <para>
        /// <b>That mirror was missing, and it did far more than leave a row highlighted.</b>
        /// Every mode question in this file is asked as <c>IsDemolishActive</c> FIRST, so a
        /// player who clicked Demolish and then clicked Depot got: no placement at all
        /// (<c>PlaceOrRemove</c> answers an empty tile with "nothing here" and returns before it
        /// ever reaches <c>PlaceInternal</c>), an inverted ghost that greens on occupied tiles,
        /// no facing arrow — so <c>R</c> went silently invisible again — and any drag turned into
        /// a demolition sweep. The build menu highlighted both rows, which was the only visible
        /// symptom of four broken behaviours.
        /// </para>
        ///
        /// <para>
        /// Fixed HERE rather than in the menu that reported it. The panel is one caller; the
        /// invariant belongs to the state, or the next caller — a hotkey, a test, the assembly
        /// line — reintroduces it.
        /// </para>
        /// </summary>
        public void SetActivePrefab(PlaceableBuilding prefab)
        {
            _buildingPrefab = prefab;
            IsDemolishActive = false;
            LastStatusMessage = "";
            // A run in progress belonged to the tool being put down, exactly as it does when the
            // bar is picked up.
            EndDrag();
        }

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
                _cancelAction = gameplay?.FindAction("CancelBuild");
            }
        }

        private void OnEnable()
        {
            if (_clickAction != null)
            {
                _clickAction.Enable();
                _clickAction.performed += OnClickPerformed;
                _clickAction.canceled += OnClickCanceled;
            }

            if (_rotateAction != null)
            {
                _rotateAction.Enable();
                _rotateAction.performed += OnRotatePerformed;
            }

            if (_cancelAction != null)
            {
                _cancelAction.Enable();
                _cancelAction.performed += OnCancelPerformed;
            }
        }

        private void OnDisable()
        {
            if (_clickAction != null)
            {
                _clickAction.performed -= OnClickPerformed;
                _clickAction.canceled -= OnClickCanceled;
                _clickAction.Disable();
            }

            EndDrag();

            if (_rotateAction != null)
            {
                _rotateAction.performed -= OnRotatePerformed;
                _rotateAction.Disable();
            }

            if (_cancelAction != null)
            {
                _cancelAction.performed -= OnCancelPerformed;
                _cancelAction.Disable();
            }
        }

        private void OnCancelPerformed(InputAction.CallbackContext context) => CancelPlacement();

        /// <summary>
        /// Puts the held placeable down -- leaves build mode. Public so the build menu, a test
        /// and the key binding all take the same path.
        ///
        /// <para>
        /// <b>Why this had to exist.</b> There was no way out of build mode at all: the menu only
        /// ever called <see cref="SetActivePrefab"/>, so once a row was clicked
        /// <see cref="IsPlacementActive"/> stayed true for the rest of the session. Every left
        /// click went on placing or demolishing, and -- because R is arbitrated on exactly that
        /// flag -- <b>R could never again reach the Hand-Crank Bench or a golem</b>. A player who
        /// opened the build menu once could no longer change a bench recipe or turn a golem,
        /// which is how this was found in play.
        /// </para>
        /// </summary>
        public bool CancelPlacement()
        {
            if (_buildingPrefab == null && !IsDemolishActive)
            {
                return false;
            }

            _buildingPrefab = null;
            EndDrag();
            // The wrecking bar leaves by the same three doors the placeables do. Anything else
            // reintroduces the trap this method exists to close, with a more destructive tool
            // stuck in the player's hand.
            IsDemolishActive = false;
            LastStatusMessage = "";

            // The ghost hides itself on the next Update (BuildClickPolicy sees nothing in hand),
            // but hiding it here too means the feedback lands on the frame of the key press
            // rather than one frame later.
            if (_ghost != null)
            {
                _ghost.gameObject.SetActive(false);
            }

            return true;
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

            // Belt-and-braces against a drag that outlives its button -- see OnClickCanceled.
            if (_dragActive && !Pointer.current.press.isPressed)
            {
                EndDrag();
            }

            ExtendDrag(_hoveredCell);
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
            if (IsDemolishActive)
            {
                // The wrecking bar inverts the ghost: an OCCUPIED tile is the good one. Reusing
                // Blocked's red for "this will be destroyed" would be the same colour meaning
                // opposite things one mode apart, so removal gets its own steady red and an
                // empty tile gets the inert steel that already means "this click does nothing".
                GhostState = BuildGhostVisuals.ClassifyRemoval(HasRemovableThing(_hoveredCell));
            }
            else
            {
                // Off the ground reads as Blocked rather than as a fourth state: the player's
                // move is the same one an occupied tile asks for -- put the cursor somewhere
                // else.
                bool refused = occupied || !IsCellBuildable(_hoveredCell);
                GhostState = BuildGhostVisuals.Classify(refused, CanAffordActivePrefab());
            }
            // Colours and the blocked pulse come from BuildGhostVisuals, which documents the
            // measurements behind them -- the old inline green/red pair was tuned against the
            // pre-reskin cold grey floor and composited to a 1.06:1 contrast ratio against
            // each other on the warm plank floor that replaced it.
            _ghost.color = BuildGhostVisuals.Evaluate(GhostState, Time.time);
            // Hidden over UI as well as with nothing in hand, so the ghost and the click agree:
            // a tile that will not be built on must not be showing a "valid placement" square
            // under an open menu.
            _ghost.gameObject.SetActive(BuildClickPolicy.ShouldPlace(IsBuildToolActive, _pointerOverUi));
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

            // Nothing to aim while demolishing: an arrow on the wrecking bar would say the
            // removal has a direction.
            if (IsDemolishActive)
            {
                _ghostArrow.gameObject.SetActive(false);
                return;
            }

            if (!_ghostArrow.gameObject.activeSelf)
            {
                _ghostArrow.gameObject.SetActive(true);
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
        /// <summary>
        /// Whether this cell holds something the wrecking bar can actually take. A golem
        /// occupies the grid too and is not a building, so "occupied" is the wrong question.
        /// </summary>
        public bool HasRemovableBuilding(Vector2Int cell) =>
            _gridMapHolder != null
            && _gridMapHolder.Map.TryGetOccupant(cell, out object occupant)
            && occupant is PlaceableBuilding;

        /// <summary>
        /// The golem standing on <paramref name="cell"/>, if any.
        ///
        /// <para>
        /// <b>Golems are NOT <c>GridMap</c> occupants</b> — only buildings are (see the two
        /// <c>TryOccupy</c> calls in this file, which are the only ones in the project). A golem
        /// knows its own cell instead, so finding one is a scan rather than a lookup. That is
        /// affordable here because this runs on a click and on the hovered cell, never per golem
        /// per frame; <c>RoutingFocusController</c> and <c>PlayerInteractor</c> make the same
        /// trade and cache, which this deliberately does not — a cache would have to be
        /// invalidated by every dismantle, and the thing it would be caching is one comparison.
        /// </para>
        /// </summary>
        public bool TryFindGolemAt(Vector2Int cell, out GolemEntity golem)
        {
            golem = null;
            var golems = Object.FindObjectsByType<GolemEntity>(
                FindObjectsSortMode.None);
            for (int i = 0; i < golems.Length; i++)
            {
                if (golems[i] != null && golems[i].Cell == cell)
                {
                    golem = golems[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the wrecking bar has something to take back at this cell.</summary>
        public bool HasRemovableThing(Vector2Int cell) =>
            HasRemovableBuilding(cell)
            || (_golemDismantler != null && TryFindGolemAt(cell, out _));

        // The scene's golem dismantler. Optional in the same additive way every other holder on
        // this component is: unwired, the wrecking bar behaves exactly as it did before golems
        // were removable, refusing a golem's tile with "not a building".
        private IGolemDismantler _golemDismantler;

        /// <summary>
        /// Late-wired by the scene bootstrap, for the same reason <c>ConfigureStationWiring</c>
        /// is: the implementor is a component in the scene and this one is on a prefab.
        /// </summary>
        public void ConfigureGolemDismantling(IGolemDismantler dismantler) =>
            _golemDismantler = dismantler;

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
            if (!BuildClickPolicy.ShouldPlace(IsBuildToolActive, _pointerOverUi))
            {
                return;
            }

            PlaceOrRemove(_hoveredCell);
            BeginDrag(_hoveredCell);
        }

        // Release ends the run. Button actions cancel on release, but Update carries a second
        // check against the pointer's own state as well: a drag that outlived its button would
        // keep laying belts wherever the cursor went, which is the one failure mode of this
        // feature a player could not undo in a single gesture.
        private void OnClickCanceled(InputAction.CallbackContext context) => EndDrag();

        // ===================================================================================
        // Click-and-drag: laying a RUN.
        // ===================================================================================
        //
        // Belts and steam pipes are the two things a player lays fifteen of in a row, and until
        // now that was fifteen clicks with an R in the middle of it. A drag lays the run and
        // POINTS IT ALONG ITSELF, which is the half that matters: the facing of every belt but
        // the last is only knowable once the drag has reached the next cell, so each cell is laid
        // facing the cursor and then turned as the run goes on (PlaceableBelt.Reface /
        // BeltNetwork.TrySetFacing, neither of which disturbs the lane or its cargo).
        //
        // Which placeables answer to this is a per-prefab flag, not a component test -- see
        // PlaceableBuilding.IsDragPlaceable for why the question is about the gesture rather than
        // about the thing.
        //
        // THE WRECKING BAR DRAGS TOO, and its run is deliberately not the mirror image of a
        // placement run: it never stops, and it never takes a golem. Both differences have
        // reasons, and both are argued at DemolishDragged rather than here.
        private bool _dragActive;

        /// <summary>
        /// Whether this run is the wrecking bar's rather than a placeable's. The two runs are
        /// deliberately NOT symmetric -- see <see cref="DemolishDragged"/>.
        /// </summary>
        private bool _dragDemolishing;

        private Vector2Int _dragLastCell;
        private readonly List<Vector2Int> _dragStepScratch = new List<Vector2Int>();

        // Cells this drag laid. Purely so the player can wiggle back over their own run without
        // it counting as "blocked": every OTHER occupied cell ends the drag, because a run that
        // silently skipped a wall would leave the belt before the gap pointing into it.
        private readonly HashSet<Vector2Int> _dragPlacedCells = new HashSet<Vector2Int>();

        /// <summary>Whether a run is being laid right now. Public so a test can assert it.</summary>
        public bool IsDragging => _dragActive;

        /// <summary>
        /// Starts a run at the cell the click just landed on. Public so a test can lay a run
        /// without simulating a pointer, matching how <c>PlaceOrRemove</c> is already reachable.
        /// </summary>
        public void BeginDrag(Vector2Int anchor)
        {
            if (IsDemolishActive)
            {
                // The wrecking bar drags unconditionally -- there is no prefab to carry a flag,
                // and the tool IS the flag. Safe to make destructive-by-the-gesture for the same
                // reason removal is a full refund: a swept building comes back for nothing, so
                // an over-long drag costs the player a re-place and not a single unit of goods.
                _dragActive = true;
                _dragDemolishing = true;
                _dragLastCell = anchor;
                _dragPlacedCells.Clear();
                return;
            }

            if (_buildingPrefab == null || !_buildingPrefab.IsDragPlaceable)
            {
                return;
            }

            _dragActive = true;
            _dragDemolishing = false;
            _dragLastCell = anchor;
            _dragPlacedCells.Clear();
            // The anchor counts as ours whether or not the click actually placed anything: if it
            // did, the run may need to turn it; if it did not (no room, no goods), the very first
            // step will find it occupied or refused and end the drag there.
            _dragPlacedCells.Add(anchor);
        }

        private void EndDrag()
        {
            _dragActive = false;
            _dragDemolishing = false;
            _dragPlacedCells.Clear();
        }

        /// <summary>
        /// Carries a live drag up to <paramref name="cell"/>, laying every cell on the way.
        /// Public so a test can drive a run without simulating a pointer.
        /// </summary>
        public void ExtendDrag(Vector2Int cell)
        {
            if (!_dragActive || cell == _dragLastCell || _gridMapHolder == null)
            {
                return;
            }

            _dragStepScratch.Clear();
            BuildDragPath.AppendCells(_dragLastCell, cell, _dragStepScratch);

            for (int i = 0; i < _dragStepScratch.Count; i++)
            {
                Vector2Int next = _dragStepScratch[i];
                if (_dragDemolishing)
                {
                    DemolishDragged(next);
                    _dragLastCell = next;
                    continue;
                }

                Facing stepFacing = BuildDragPath.StepFacing(_dragLastCell, next, PlacementFacing);

                if (_dragPlacedCells.Contains(next))
                {
                    // Wiggled back over our own run. Re-anchor and carry on, laying nothing --
                    // and pointedly NOT re-facing the cell we came from, which would turn the
                    // run backwards into itself. The next forward step re-faces it correctly.
                    _dragLastCell = next;
                    continue;
                }

                if (!TryPlaceDragged(next, stepFacing))
                {
                    // Blocked, off the ground, or unaffordable. The run stops where it stopped
                    // rather than skipping the obstruction, because a run with a hole in it
                    // leaves the belt before the hole pointing at nothing.
                    EndDrag();
                    return;
                }

                // Only now does the cell we came from know which way the run leaves it -- after
                // the next cell is really there, so a refusal never leaves a belt aimed at a wall.
                RefaceDragged(_dragLastCell, stepFacing);

                _dragPlacedCells.Add(next);
                _dragLastCell = next;

                // The ghost keeps the direction the run is going, so letting go and clicking
                // again continues the line rather than restarting it at whatever R last chose.
                PlacementFacing = stepFacing;
            }
        }

        /// <summary>
        /// One cell of a wrecking-bar run: takes back the building standing here, if any.
        ///
        /// <para>
        /// <b>It does NOT stop at an empty cell, and that is the deliberate asymmetry with the
        /// placement run.</b> A run of belts must stop at an obstruction because a run with a
        /// hole in it leaves the belt before the hole pointing at nothing -- a placed run has to
        /// be CONTINUOUS to mean anything. A demolition has no such requirement: sweeping the bar
        /// across a corner of the factory is meant to clear what is there and pass over what is
        /// not, and an L-shaped drag crosses empty floor as a matter of course. Stopping on the
        /// first gap would make the tool useless for the one gesture it exists for.
        /// </para>
        ///
        /// <para>
        /// <b>It does not take GOLEMS, and a click still does.</b> This is the one place the drag
        /// is narrower than the click it repeats, and the reason is that the full refund does not
        /// actually make a golem whole: demolishing a building and re-placing it restores it
        /// exactly, while a dismantled golem hands back its chassis and its cargo and loses <em>the
        /// program</em> -- six cards the player dragged one at a time, gone to a gesture aimed at
        /// the crate beside it. (A patented program survives in the Patents tab; an unpatented one
        /// does not.) Sweeping up a building is an undo away; sweeping up a golem is not, so a
        /// golem still costs one deliberate click.
        /// </para>
        ///
        /// <para>
        /// Every other rule is the click's, unchanged, because this calls the same
        /// <see cref="DemolishBuilding"/>: full refund, runtime-placed only, and refused outright
        /// if the stockpile has no room to take the goods back. A refusal does not end the run --
        /// it leaves that one building standing and says so, and the sweep carries on.
        /// </para>
        /// </summary>
        private void DemolishDragged(Vector2Int cell)
        {
            object occupant;
            if (_gridMapHolder.Map.TryGetOccupant(cell, out occupant)
                && occupant is PlaceableBuilding building)
            {
                DemolishBuilding(building, cell, refund: true);
            }
        }

        // One cell of a run. Deliberately NOT PlaceOrRemove: that method is the player's CLICK,
        // and a click on an occupied cell demolishes what is there. Dragging a belt run across
        // your own depot must never eat the depot.
        private bool TryPlaceDragged(Vector2Int cell, Facing facing)
        {
            GridMap map = _gridMapHolder.Map;
            if (map.IsOccupied(cell) || !IsCellBuildable(cell) || _buildingPrefab == null)
            {
                return false;
            }

            Facing previous = PlacementFacing;
            PlacementFacing = facing;
            PlaceInternal(cell, map);
            PlacementFacing = previous;

            // PlaceInternal reports a refusal by leaving the cell unoccupied (it withdraws the
            // cost atomically and returns early on a shortfall), so occupancy is the honest
            // answer to "did that work" without giving the method a return value its other two
            // callers would have to start ignoring.
            return map.IsOccupied(cell);
        }

        // Turns a cell the current drag already laid, so the run points along itself.
        private void RefaceDragged(Vector2Int cell, Facing facing)
        {
            object occupant;
            if (!_dragPlacedCells.Contains(cell) || _gridMapHolder == null
                || !_gridMapHolder.Map.TryGetOccupant(cell, out occupant))
            {
                return;
            }

            var building = occupant as PlaceableBuilding;
            if (building == null || building.Facing == facing)
            {
                return;
            }

            building.Facing = facing;

            // A belt's facing IS its routing, so the lane graph has to be told before the
            // picture is. Both are no-ops for a pipe, which has no direction to speak of --
            // its Facing is read only as the orientation of an ISOLATED stub.
            PlaceableBelt belt = building.GetComponent<PlaceableBelt>();
            if (belt != null && _beltNetworkHolder != null)
            {
                _beltNetworkHolder.Network.TrySetFacing(cell, facing);
                belt.Reface(facing);
            }

            RefreshConnectedShapes();
        }

        /// <summary>
        /// Re-derives every belt and pipe picture from the networks that own them. Called
        /// wherever the built world changes, and a no-op for a scene that wired neither.
        /// </summary>
        private void RefreshConnectedShapes()
        {
            PlaceableSteamPipe.RefreshAllShapes(_steamNetworkHolder);
            PlaceableBelt.RefreshAllShapes(_beltNetworkHolder);
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
                    // The player's own click at a cell: a sale being reversed, so it pays back.
                    DemolishBuilding(building, cell, refund: true);
                }
                else if (IsDemolishActive && !TryDismantleGolemAt(cell))
                {
                    // Not a building, and not a golem either. Says so rather than doing nothing,
                    // because a silent click on the wrecking bar reads as the tool being broken.
                    LastStatusMessage = "Nothing to remove there -- that tile is not a building.";
                    SpawnPopup(_converter.CellToWorldCenter(cell), "not a building", RefusedPopupColor);
                }

                return;
            }

            // A golem standing on an UNoccupied tile, which is the normal case: golems are not
            // GridMap occupants, so the branch above only fires when a golem happens to share a
            // tile with a building. Checked before the empty-tile refusal below, or the wrecking
            // bar would say "nothing here" while the player is looking straight at a golem.
            if (IsDemolishActive && TryDismantleGolemAt(cell))
            {
                return;
            }

            if (IsDemolishActive)
            {
                SpawnPopup(_converter.CellToWorldCenter(cell), "nothing here", RefusedPopupColor);
                return;
            }

            if (_buildingPrefab == null)
            {
                return;
            }

            // REMOVAL is checked above and is deliberately NOT bounded: a building standing off
            // the ground (an old save, a bound that moved) must always be removable, or it is
            // litter the player cannot clear.
            if (!IsCellBuildable(cell))
            {
                LastStatusMessage = "Can't build there -- that is outside the workshop and the street.";
                SpawnPopup(_converter.CellToWorldCenter(cell), "off the ground", RefusedPopupColor);
                return;
            }

            PlaceInternal(cell, map);
        }

        /// <summary>
        /// Rebuilds a building a save file describes, at the cell and facing it was saved with.
        ///
        /// <para>
        /// DOES NOT CHARGE THE COST, for the same reason
        /// <c>GolemConstructionStation.TryRespawnGolem</c> does not: the player paid for this
        /// building in the session that placed it, and charging on load would be a tax that
        /// simply fails for anyone who has since spent their stockpile -- turning "load my game"
        /// into "lose the half of my factory I can no longer afford".
        /// </para>
        ///
        /// <para>
        /// Everything else goes through the same occupancy and endpoint registration a placement
        /// does, because a restored belt that never reached BeltNetwork would be a lane the
        /// player can see and items cannot use.
        /// </para>
        /// </summary>
        public bool TryRebuildSavedBuilding(
            string prefabKey, Vector2Int cell, Facing facing, out PlaceableBuilding instance)
        {
            instance = null;
            PlaceableBuilding prefab = FindPrefab(prefabKey);
            if (prefab == null || _gridMapHolder == null)
            {
                return false;
            }

            GridMap map = _gridMapHolder.Map;
            if (map.IsOccupied(cell))
            {
                return false;
            }

            instance = Instantiate(prefab, _converter.CellToWorldCenter(cell), Quaternion.identity);
            instance.Cell = cell;
            instance.Facing = facing;
            instance.MarkRuntimePlaced(prefabKey);
            map.TryOccupy(cell, instance);
            RegisterPlacedEndpoints(instance, cell, facing);

            // Its own publish: a save restore builds the instance here rather than through
            // PlaceInternal, so it would otherwise be the one path that left a loaded factory
            // full of buildings the player could not interact with.
            EventBus.Publish(new WorldInteractablesChangedEvent("building restored"));
            return true;
        }

        /// <summary>
        /// Removes every building the player placed, leaving the ones authored into the scene
        /// alone. The counterpart of <c>StorageBufferRegistry.Clear()</c>: a load must REPLACE
        /// the built world, not merge into it, or loading twice stacks two factories on one
        /// floor -- and the second copy would silently fail to place, since the first already
        /// occupies every cell.
        /// </summary>
        public int ClearRuntimePlacedBuildings()
        {
            var placed = new List<PlaceableBuilding>();
            foreach (PlaceableBuilding building in
                     FindObjectsByType<PlaceableBuilding>(FindObjectsSortMode.None))
            {
                if (building != null && building.IsRuntimePlaced)
                {
                    placed.Add(building);
                }
            }

            foreach (PlaceableBuilding building in placed)
            {
                // refund: false, and this is NOT a detail. A load REPLACES the built world, and
                // it sweeps exactly the buildings a refund pays out on -- the runtime-placed
                // ones. Paying them back here would hand the player their whole factory's cost
                // in goods on every single load, on top of the buffers the save is about to
                // restore: save, load, save, load is an infinite resource duplicator. Only a
                // player's own click at a cell is a sale being reversed.
                DemolishBuilding(building, building.Cell, refund: false);
            }

            return placed.Count;
        }

        private PlaceableBuilding FindPrefab(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey) || _availablePrefabs == null)
            {
                return null;
            }

            foreach (PlaceableBuilding prefab in _availablePrefabs)
            {
                if (prefab != null && prefab.name == prefabKey)
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>
        /// The wrecking bar's golem branch. Returns whether this click was <em>about</em> a
        /// golem at all — true even when the dismantle was refused, because a refusal that
        /// reported "that tile is not a building" over a visible golem would be a lie.
        ///
        /// <para>
        /// Every rule here lives on the other side of <c>IGolemDismantler</c>: this method owns
        /// the cursor, the status line and the popups, and nothing else. That split is why the
        /// refund cannot diverge between a golem removed with the wrecking bar and a golem
        /// removed by anything added later.
        /// </para>
        /// </summary>
        private bool TryDismantleGolemAt(Vector2Int cell)
        {
            GolemEntity golem;
            if (_golemDismantler == null || !TryFindGolemAt(cell, out golem))
            {
                return false;
            }

            IReadOnlyList<RecipeIngredient> refunded;
            string refusalReason;
            if (!_golemDismantler.TryDismantleGolem(golem, out refunded, out refusalReason))
            {
                LastStatusMessage = refusalReason;
                SpawnPopup(_converter.CellToWorldCenter(cell), "kept", RefusedPopupColor);
                return true;
            }

            LastStatusMessage = "";
            // A golem that cost nothing and carried nothing still has to say something happened,
            // or the click reads as the tool failing. "dismantled" is that floor.
            SpawnPopup(
                _converter.CellToWorldCenter(cell),
                refunded != null && refunded.Count > 0
                    ? "+" + GolemFactory.UI.ConstructionCostPolicy.FormatCost(refunded)
                    : "dismantled",
                RefundPopupColor);
            return true;
        }

        // Every registration a placement made, undone in one place, so removal and a save's
        // "replace the built world" sweep can never drift apart.
        private void DemolishBuilding(PlaceableBuilding building, Vector2Int cell, bool refund)
        {
            // COZY RULE: never take goods away as the price of tidying up. If the refund will
            // not fit, the building stays standing and says so -- a demolition that destroyed
            // what it could not hand back would be exactly the punishment a full refund exists
            // to remove. Checked before anything is torn down, so the refusal leaves the world
            // untouched rather than half-dismantled.
            if (refund && !RefundWouldFit(building))
            {
                LastStatusMessage =
                    "Not demolished: the stockpile has no room for the refund. Make space first.";
                SpawnPopup(_converter.CellToWorldCenter(cell), "no room for refund", RefusedPopupColor);
                return;
            }

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
                 building.GetComponent<PlaceableBoiler>() != null ||
                 building.GetComponent<PlaceableFreightMast>() != null ||
                 building.GetComponent<PlaceableSlagHeap>() != null ||
                 building.GetComponent<PlaceableScrapRecycler>() != null))
            {
                _spatialEndpointHolder.Registry.Unregister(cell);
            }

            // A demolished mast must leave the registry too, or a Zeppelin stays bound to a cell
            // with nothing on it. GolemEntity re-binds when its target vanishes, but only if the
            // registry has stopped offering the dead mast.
            PlaceableFreightMast removedMast = building.GetComponent<PlaceableFreightMast>();
            if (removedMast != null)
            {
                removedMast.UnregisterFromMastNetwork(_mastRegistryHolder, cell);
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

            // Read the price back BEFORE the GameObject goes, and pay it back in full.
            if (refund)
            {
                RefundBuilding(building, cell);
            }

            if (_gridMapHolder != null)
            {
                _gridMapHolder.Map.Free(cell);
            }
            Destroy(building.gameObject);

            // Same sweep as after a placement, and it matters MORE here: a tee whose third arm
            // has just been lifted has to stop drawing an arm into an empty cell. The object
            // being destroyed is still alive for the rest of the frame, which is why the two
            // refreshes both skip anything that has already left its network.
            RefreshConnectedShapes();

            // Removal matters as much as placement: PlayerInteractor's cached arrays would
            // otherwise keep offering a demolished depot. FillPositions parks destroyed entries
            // at infinity so a stale cache is never *wrong*, but it stays one entry longer than
            // it should and the prompt can still name a building that is gone.
            EventBus.Publish(new WorldInteractablesChangedEvent("building demolished"));
        }

        /// <summary>
        /// Pays a demolished building's cost back into the stockpile, in full.
        ///
        /// <para>
        /// <b>Full, not a percentage, and that is a settled design call rather than a starting
        /// value.</b> It was raised as a tuning question in playtest -- does free relocation take
        /// the sting out of placing badly? -- and answered: <b>the game is cozy, so placing and
        /// reorganising must not be punitive.</b> A salvage fraction is a tax on changing your
        /// mind, in a game whose whole loop is laying something down, watching it be wrong, and
        /// moving it. Do not reintroduce one as "balance". §10's rule against soft-locks points
        /// the same way: a player who walls their stockpile into a building they can only destroy
        /// is stuck for a reason that is not a decision. It also makes "move a building" something
        /// the existing tools already do -- remove, then place -- at no cost, which is why there
        /// is no separate pick-up-and-carry mode.
        /// </para>
        ///
        /// <para>
        /// <b>Only what the player actually paid for.</b> <see cref="PlaceableBuilding
        /// .IsRuntimePlaced"/> is false for anything authored into the scene, and refunding those
        /// would mint goods out of the furniture -- demolish the scene's own depots and the
        /// stockpile grows. A restored save re-marks its buildings as runtime-placed, so loading
        /// does not lose you the refund on something you did buy.
        /// </para>
        /// </summary>
        private void RefundBuilding(PlaceableBuilding building, Vector2Int cell)
        {
            if (_stockpileHolder == null || building == null || !building.IsRuntimePlaced)
            {
                return;
            }

            IReadOnlyList<RecipeIngredient> cost = building.Cost;
            if (cost == null || cost.Count == 0)
            {
                return;
            }

            // Room was checked before a single registration was torn down (RefundWouldFit), so
            // every unit lands. Nothing here has to cope with a partial payout -- a refund that
            // can silently come up short is the punishment this whole feature removes.
            for (int i = 0; i < cost.Count; i++)
            {
                _stockpileHolder.Registry.Deposit(_stockpileBufferId, cost[i].itemType, cost[i].quantity);
            }

            LastStatusMessage = "";
            SpawnPopup(_converter.CellToWorldCenter(cell),
                "+" + GolemFactory.UI.ConstructionCostPolicy.FormatCost(cost), RefundPopupColor);
        }

        /// <summary>
        /// Whether the stockpile can take back everything this building cost. Asked BEFORE the
        /// demolition so a refusal is a no-op rather than a half-dismantled building and a hole
        /// in the player's goods.
        ///
        /// <para>
        /// Answers <c>true</c> for anything that would not be refunded anyway -- no stockpile
        /// wired, scene-authored furniture, a free building -- so the check never blocks a
        /// removal it has no stake in. Sandbox's stockpile is Unlimited, so in the shipping
        /// scene this always passes; it exists for the capped buffers a later scene may use.
        /// </para>
        /// </summary>
        private bool RefundWouldFit(PlaceableBuilding building)
        {
            if (_stockpileHolder == null || building == null || !building.IsRuntimePlaced)
            {
                return true;
            }

            IReadOnlyList<RecipeIngredient> cost = building.Cost;
            if (cost == null || cost.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < cost.Count; i++)
            {
                if (_stockpileHolder.Registry.RoomFor(_stockpileBufferId, cost[i].itemType)
                    < cost[i].quantity)
                {
                    return false;
                }
            }

            return true;
        }

        private void PlaceInternal(Vector2Int cell, GridMap map)
        {
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
            // What tells the save system this building is rebuildable, and which prefab to
            // rebuild it from. Recorded here, at the one place a building is ever placed.
            instance.MarkRuntimePlaced(_buildingPrefab.name);
            map.TryOccupy(cell, instance);
            RegisterPlacedEndpoints(instance, cell, PlacementFacing);
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
        //
        // Takes the facing explicitly rather than reading PlacementFacing, so the save system can
        // rebuild a belt pointing the way it pointed when it was placed instead of the way the
        // cursor happens to point now. A belt's facing IS its routing, so inheriting the
        // cursor's would silently re-plumb a restored factory.
        private void RegisterPlacedEndpoints(PlaceableBuilding instance, Vector2Int cell, Facing facing)
        {
            PlaceableBelt belt = instance.GetComponent<PlaceableBelt>();
            if (belt != null && _beltNetworkHolder != null)
            {
                // A splitter is placed through the identical call with one flag -- it IS a belt
                // as far as the lane, the capacity and the handoff pass are concerned, and only
                // its outputs differ.
                bool isSplitter = instance.GetComponent<PlaceableBeltSplitter>() != null;
                PlacedBelt placed;
                if (_beltNetworkHolder.Network.TryPlace(cell, facing, isSplitter, out placed))
                {
                    belt.BindSegment(placed.Segment, facing, _conveyorHolder, _cellSize);
                }
            }

            PlaceableDepot depot = instance.GetComponent<PlaceableDepot>();
            if (depot != null)
            {
                depot.RegisterAsSpatialEndpoint(_spatialEndpointHolder, _stockpileHolder, cell);
            }

            //   * a construction station: gets the scene registries, clock and Workbench that
            //     let it actually build a golem. Without this a station the player PAID for
            //     was inert -- TryConstructGolem early-outs on a null buffer registry and says
            //     nothing, so the money went and the building did not work. Stations were only
            //     ever wired by the bootstrap's one-shot startup sweep, which by definition
            //     cannot see one built afterwards.
            GolemConstructionStation station = instance.GetComponent<GolemConstructionStation>();
            if (station != null && _stationConfigurator != null)
            {
                _stationConfigurator.ConfigureStation(station);
            }

            //   * a clock tower: publishes its input tile, giving the megaproject somewhere for
            //     a golem to Push into. Without this a placed tower was inert -- the win
            //     condition could be built and then never delivered to.
            PlaceableClockTower tower = instance.GetComponent<PlaceableClockTower>();
            if (tower != null)
            {
                tower.RegisterAsSpatialEndpoint(_spatialEndpointHolder, tower.SiteHolder, cell);
            }

            //   * a slag heap: publishes its tile, which accepts Slag to void and Coke to burn
            //     it with. §5.3(c)'s costed sink -- without a published tile it is a building
            //     the smelter cannot reach, which is the whole of its job.
            PlaceableSlagHeap slagHeap = instance.GetComponent<PlaceableSlagHeap>();
            if (slagHeap != null)
            {
                slagHeap.RegisterAsSpatialEndpoint(_spatialEndpointHolder, cell);
            }

            //   * a scrap recycler: publishes its tile, which takes junk and Coke in and hands
            //     Scrap back out. BOTH directions matter -- it is a machine in the middle of the
            //     logistics graph rather than a sink, so a golem has to be able to haul from it.
            PlaceableScrapRecycler recycler = instance.GetComponent<PlaceableScrapRecycler>();
            if (recycler != null)
            {
                recycler.RegisterAsSpatialEndpoint(_spatialEndpointHolder, cell);
            }

            //   * a freight mast: publishes its tile like a depot AND joins the mast registry,
            //     which is the half a Zeppelin binds to. Both, because §6's link has to deliver
            //     goods somewhere real as well as be findable.
            PlaceableFreightMast mast = instance.GetComponent<PlaceableFreightMast>();
            if (mast != null)
            {
                mast.RegisterAsSpatialEndpoint(_spatialEndpointHolder, _stockpileHolder, cell);
                mast.RegisterWithMastNetwork(_mastRegistryHolder, cell);
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

            // Neighbours change shape when something is laid beside them: a straight pipe
            // becomes a tee, a belt fed from its flank becomes a corner. Swept here rather than
            // patched per cell for the reason BeltNetwork.Relink is recomputed wholesale -- see
            // PlaceableSteamPipe's roster note. Both are no-ops with their network unwired.
            RefreshConnectedShapes();

            // LAST, after every registration above. A listener re-scans the scene on this, and
            // the thing it will find must already be fully wired -- a depot found before
            // RegisterAsSpatialEndpoint would be interactable and routing-invisible at once.
            //
            // This is the same class of bug the station wiring above fixed, one level out: a
            // depot the player placed could never be labelled with [E], a boiler never fuelled,
            // a placed station never built from, because the interactor had cached its lists at
            // startup and nothing ever told it the world had changed.
            EventBus.Publish(new WorldInteractablesChangedEvent("building placed"));
        }
    }
}
