using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Player
{
    // Finds the nearest interactable (a ResourceNodeMarker to harvest, a
    // GolemConstructionStation to build a golem at, or a GolemEntity to (re)program) and acts
    // on it via the Interact action. The three interactable kinds don't share a common
    // interface, so they're cached as three separate arrays rather than forcing an artificial
    // abstraction over them.
    //
    // The *selection* itself is no longer done here: it moved to the pure
    // InteractionTargeting.SelectNearest, which fixed a real bug in the process (the old
    // inline version preferred kinds in a fixed order, so a node at the edge of range beat a
    // station the player was standing on). This class is now the thin MonoBehaviour that
    // gathers positions, applies the result, and drives the feedback -- the same
    // "pure function + thin applier" split as PlayerController/PlayerMovement.
    //
    // It also owns the always-on affordance (InteractionPromptView) and the harvest
    // confirmation (FloatingPopup). Both are optional: with no view wired the component
    // behaves exactly as it did before, which is what keeps a scene that never wires them
    // (or a test) working unchanged.
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private float _interactRange = 1.5f;
        [SerializeField] private StorageBufferRegistryHolder _stockpileHolder;
        [SerializeField] private string _stockpileBufferId = "FactoryStockpile";
        [SerializeField] private GolemConstructionPanel _constructionPanel;
        [SerializeField] private WorkbenchController _workbenchController;

        [Header("Affordance")]
        [SerializeField] private InteractionPromptView _promptView;
        [SerializeField] private ManagementPanel _managementPanel;
        [SerializeField] private string _interactKeyLabel = "E";

        // Harvest confirmations reuse the warm-amber "something good happened" colour the
        // Workbench and build ghost already use; the failure line is the same desaturated
        // steel the out-of-range ring uses, so "nothing happened" never reads as an alarm.
        private static readonly Color HarvestPopupColor = new Color(1f, 0.86f, 0.50f, 1f);
        private static readonly Color RefusedPopupColor = new Color(0.72f, 0.75f, 0.78f, 1f);

        // Rotating an already-placed golem. "Golems cannot pivot" is a rule about *runtime
        // execution* -- nothing in a program may turn the golem mid-cycle -- not about the
        // player repositioning one between runs, which is the core spatial puzzle. GolemEntity
        // already draws that distinction: SetPlacement is player-facing, and nothing on the
        // execution path calls it.
        [SerializeField] private BuildModeController _buildModeController;

        // Moving a placed golem. Rotation alone is not enough to make placement a real choice:
        // the construction station emits its golem onto the tile it faces, and without a way to
        // move one afterwards the only way to position a golem would be to build a station in
        // exactly the right spot -- where the station itself then occupies one of the two tiles
        // the golem needs. So the player walks to the tile they want and summons the golem to it.
        [SerializeField] private GridMapHolder _gridMapHolder;
        [SerializeField] private Vector2 _cellSize = new Vector2(1f, 0.5f);
        [SerializeField] private float _summonRange = 12f;

        private InputAction _interactAction;
        private InputAction _rotateAction;
        private InputAction _placeGolemAction;
        private ResourceNodeMarker[] _nodeMarkers = new ResourceNodeMarker[0];
        private GolemConstructionStation[] _stations = new GolemConstructionStation[0];
        private GolemEntity[] _golems = new GolemEntity[0];

        // Position buffers refilled each frame from the cached component arrays, so the
        // per-frame selection allocates nothing. Sized only when the arrays are re-scanned.
        private Vector3[] _nodePositions = new Vector3[0];
        private Vector3[] _stationPositions = new Vector3[0];
        private Vector3[] _golemPositions = new Vector3[0];

        // Set by Interact()/the Try* methods on failure, for the prompt UI or a test to
        // surface -- mirrors BuildModeController.LastStatusMessage.
        public string LastStatusMessage { get; private set; } = "";

        /// <summary>The pick the affordance is currently advertising. Exposed for tests.</summary>
        public InteractionPick CurrentPick { get; private set; } = InteractionPick.None;

        public InteractionAffordance CurrentAffordance { get; private set; } = InteractionAffordance.Hidden;

        /// <summary>The prompt text currently shown (empty when nothing is advertised).</summary>
        public string CurrentPrompt { get; private set; } = "";

        // Programmatic setup used by tests (and available for runtime bootstrapping), mirroring
        // BuildModeController.Configure/ConfigureEconomy -- avoids requiring Inspector-assigned
        // references.
        public void Configure(
            InputActionAsset actions, float interactRange, StorageBufferRegistryHolder stockpileHolder,
            string stockpileBufferId, GolemConstructionPanel constructionPanel, WorkbenchController workbenchController)
        {
            _actions = actions;
            _interactRange = interactRange;
            _stockpileHolder = stockpileHolder;
            _stockpileBufferId = stockpileBufferId;
            _constructionPanel = constructionPanel;
            _workbenchController = workbenchController;
        }

        /// <summary>
        /// Wires the affordance separately from Configure so existing callers (and every
        /// existing test) are unaffected and a scene that wants no prompt simply never calls
        /// it -- the same split BuildModeController uses for Configure/ConfigureEconomy.
        /// </summary>
        public void ConfigureAffordance(InteractionPromptView promptView, ManagementPanel managementPanel, string interactKeyLabel)
        {
            _promptView = promptView;
            _managementPanel = managementPanel;
            _interactKeyLabel = interactKeyLabel;
        }

        /// <summary>
        /// Wires the build-mode controller this interactor defers the shared R key to. Optional:
        /// with none wired, R always rotates the nearest golem.
        /// </summary>
        public void ConfigureBuildMode(BuildModeController buildModeController) =>
            _buildModeController = buildModeController;

        private void Awake()
        {
            if (_promptView == null)
            {
                _promptView = GetComponent<InteractionPromptView>();
            }

            if (_actions != null)
            {
                InputActionMap gameplay = _actions.FindActionMap("Gameplay");
                _interactAction = gameplay?.FindAction("Interact");
                _rotateAction = gameplay?.FindAction("Rotate");
                _placeGolemAction = gameplay?.FindAction("PlaceGolem");
            }
        }

        private void OnEnable()
        {
            RefreshInteractables();
            if (_interactAction != null)
            {
                _interactAction.Enable();
                _interactAction.performed += OnInteractPerformed;
            }

            if (_rotateAction != null)
            {
                _rotateAction.Enable();
                _rotateAction.performed += OnRotatePerformed;
            }

            if (_placeGolemAction != null)
            {
                _placeGolemAction.Enable();
                _placeGolemAction.performed += OnPlaceGolemPerformed;
            }
        }

        private void OnDisable()
        {
            if (_interactAction != null)
            {
                _interactAction.performed -= OnInteractPerformed;
                _interactAction.Disable();
            }

            if (_rotateAction != null)
            {
                _rotateAction.performed -= OnRotatePerformed;
                _rotateAction.Disable();
            }

            if (_placeGolemAction != null)
            {
                _placeGolemAction.performed -= OnPlaceGolemPerformed;
                _placeGolemAction.Disable();
            }

            if (_promptView != null)
            {
                _promptView.Hide();
            }
        }

        private void OnInteractPerformed(InputAction.CallbackContext context) => Interact();

        // R is shared with build mode. While a placeable is in hand it turns the ghost; only
        // with empty hands does it turn the golem you are standing next to.
        private void OnRotatePerformed(InputAction.CallbackContext context)
        {
            if (_buildModeController != null && _buildModeController.IsPlacementActive)
            {
                return;
            }

            RotateNearestGolem();
        }

        private void OnPlaceGolemPerformed(InputAction.CallbackContext context) => ToggleCarryGolem();

        /// <summary>
        /// Summons the nearest in-range golem onto the tile the player is standing on, keeping
        /// its facing. Together with rotation this is what makes a golem's position an actual
        /// decision instead of an accident of where its station happened to be.
        /// </summary>
        /// <remarks>
        /// Refuses a cell a building already occupies -- GridMap is the simulation truth for
        /// occupancy, and dropping a golem inside a depot would give it that depot's tile as
        /// both its own and its neighbour's.
        /// </remarks>
        /// <summary>The golem currently in the player's hands, or null.</summary>
        public GolemEntity CarriedGolem { get; private set; }

        /// <summary>
        /// The [G] key: pick up the golem you are standing next to, or -- if already carrying
        /// one -- put it down on the tile you are standing on.
        /// </summary>
        /// <remarks>
        /// Carry/drop rather than a single "summon the nearest golem here" action, which is
        /// what this originally was and which turned out to be genuinely ambiguous: with two
        /// golems in play, standing on the destination tile meant the golem *already placed*
        /// nearby was usually nearer than the new one at the station, so the wrong golem moved.
        /// Picking up explicitly, at arm's length, removes the guess entirely.
        /// </remarks>
        public bool ToggleCarryGolem()
        {
            return CarriedGolem != null ? TryDropCarriedGolem() : TryPickUpNearestGolem();
        }

        public bool TryPickUpNearestGolem()
        {
            // Arm's reach, like every other interaction here -- you pick up the golem you are
            // standing next to, which is the disambiguation.
            GolemEntity golem = SelectNearestGolem(_interactRange);
            if (golem == null)
            {
                LastStatusMessage = "No golem in range to pick up.";
                return false;
            }

            CarriedGolem = golem;
            golem.SetHeld(true);
            LastStatusMessage = $"Carrying {golem.GolemId}. [G] to set it down.";
            SpawnPopup(golem.transform.position, "Carrying " + golem.GolemId, HarvestPopupColor);
            return true;
        }

        public bool TryDropCarriedGolem()
        {
            GolemEntity golem = CarriedGolem;
            if (golem == null)
            {
                return false;
            }

            var converter = new GridCoordinateConverter(_cellSize);
            Vector2Int cell = converter.WorldToCell(transform.position);

            // GridMap is the simulation truth for occupancy. Dropping a golem inside a depot
            // would give it that depot's tile as its own, so its source/target would read the
            // depot's neighbours instead of the ones the player was aiming at.
            if (_gridMapHolder != null && _gridMapHolder.Map.IsOccupied(cell))
            {
                LastStatusMessage = "Something is already built on this tile.";
                SpawnPopup(transform.position, "Tile occupied", RefusedPopupColor);
                return false;
            }

            golem.SetPlacement(cell, golem.Facing);
            golem.transform.position = converter.CellToWorldCenter(cell);
            SyncGolemVisualAnchor(golem);
            golem.SetHeld(false);
            CarriedGolem = null;

            LastStatusMessage = $"{golem.GolemId} placed at {cell}.";
            SpawnPopup(transform.position, "Placed " + golem.GolemId, HarvestPopupColor);
            return true;
        }

        // GolemVisual rewrites transform.position from a cached anchor every frame, so any move
        // has to be told to it or the sprite snaps straight back to where it was built.
        private static void SyncGolemVisualAnchor(GolemEntity golem)
        {
            GolemVisual visual = golem.GetComponent<GolemVisual>();
            if (visual != null)
            {
                visual.SyncBasePosition();
            }
        }

        // A carried golem rides slightly above the player so it is obvious it is in hand and
        // not working. Driven here rather than by the golem so nothing in Golems/ has to know
        // the player exists.
        private void CarryHeldGolem()
        {
            if (CarriedGolem == null)
            {
                return;
            }

            // Destroyed mid-carry (e.g. the player removed it some other way).
            if (CarriedGolem == null || CarriedGolem.Equals(null))
            {
                CarriedGolem = null;
                return;
            }

            CarriedGolem.transform.position = transform.position + new Vector3(0f, 0.55f, 0f);
            SyncGolemVisualAnchor(CarriedGolem);
        }

        /// <summary>
        /// Wires golem repositioning. Optional, like every other Configure* here.
        /// </summary>
        public void ConfigureGolemPlacement(GridMapHolder gridMapHolder, Vector2 cellSize)
        {
            _gridMapHolder = gridMapHolder;
            _cellSize = cellSize;
        }

        // Nearest golem within a radius, reusing the same pure selector the routing highlight
        // uses so "the golem the game is talking about" is decided one way, not two.
        private GolemEntity SelectNearestGolem(float range)
        {
            FillPositions(_golems, _golemPositions);
            int index = GolemFactory.World.RoutingFocus.SelectNearestIndex(
                transform.position, _golemPositions, range);
            return index >= 0 && index < _golems.Length ? _golems[index] : null;
        }

        /// <summary>
        /// Turns the nearest in-range golem one step clockwise. This is the move that makes
        /// facing a puzzle rather than a fact about where you happened to build: aim the golem
        /// at the node, put a belt in front, and it runs; turn it away and it stalls naming the
        /// empty tile. Public so a test can drive it without synthesising Input System events.
        /// </summary>
        public bool RotateNearestGolem()
        {
            // The nearest GOLEM, not the winner of the multi-kind [E] pick. Those differ
            // constantly and the difference is fatal here: a golem is almost always placed
            // right next to the node it pulls from, so the node usually wins the combined pick
            // and rotation would refuse with "no golem in range" while the player is standing
            // next to one. Range is still arm's length, so this only ever turns what you are
            // standing beside.
            GolemEntity golem = SelectNearestGolem(_interactRange);
            if (golem == null)
            {
                LastStatusMessage = "No golem in range to rotate.";
                return false;
            }

            Facing rotated = FacingUtility.RotateClockwise(golem.Facing);
            golem.SetPlacement(golem.Cell, rotated);
            LastStatusMessage = $"{golem.GolemId} now faces {FacingVisuals.Describe(rotated)}.";
            SpawnPopup(golem.transform.position, "Facing " + FacingVisuals.Describe(rotated), HarvestPopupColor);
            return true;
        }

        private void Update()
        {
            CarryHeldGolem();
            RefreshAffordance();
        }

        // Re-scans the scene for interactables. Called once on enable; also public so a
        // GolemConstructionStation built mid-session (or a test) can make itself/new golems
        // interactable without waiting for a re-enable.
        public void RefreshInteractables()
        {
            _nodeMarkers = FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None);
            _stations = FindObjectsByType<GolemConstructionStation>(FindObjectsSortMode.None);
            _golems = FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);

            if (_nodePositions.Length != _nodeMarkers.Length)
            {
                _nodePositions = new Vector3[_nodeMarkers.Length];
            }

            if (_stationPositions.Length != _stations.Length)
            {
                _stationPositions = new Vector3[_stations.Length];
            }

            if (_golemPositions.Length != _golems.Length)
            {
                _golemPositions = new Vector3[_golems.Length];
            }
        }

        /// <summary>
        /// Re-selects the nearest interactable and updates the on-screen affordance. Public so
        /// a test can assert what the player is being told without waiting for an Update tick.
        /// </summary>
        public void RefreshAffordance()
        {
            InteractionPick pick = SelectNearestPick();
            CurrentPick = pick;
            CurrentAffordance = InteractionTargeting.ClassifyAffordance(pick, _interactRange);

            // A full screen (Workbench/Management/Construction) owns the player's attention and
            // dims the world behind it; a world-space prompt drawn under that dim is exactly
            // the kind of leftover HUD this pass is removing.
            if (!HudScreenPolicy.ShouldShowWorldHud(
                    _workbenchController != null && _workbenchController.IsOpen,
                    _managementPanel != null && _managementPanel.IsOpen,
                    _constructionPanel != null && _constructionPanel.IsOpen))
            {
                CurrentAffordance = InteractionAffordance.Hidden;
            }

            Component target = ResolveTarget(pick);
            if (target == null)
            {
                CurrentAffordance = InteractionAffordance.Hidden;
            }

            if (CurrentAffordance == InteractionAffordance.Hidden)
            {
                CurrentPrompt = "";
                if (_promptView != null)
                {
                    _promptView.Hide();
                }

                return;
            }

            // A depleted node is in range but cannot be harvested. Saying "[E] Harvest Aether
            // - depleted" told the player two contradictory things at once.
            if (CurrentAffordance == InteractionAffordance.Ready && IsUnavailable(pick, target))
            {
                CurrentAffordance = InteractionAffordance.Unavailable;
            }

            string targetName;
            string detail;
            DescribeTarget(pick, target, CarriedGolem != null, out targetName, out detail);
            CurrentPrompt = InteractionTargeting.BuildPrompt(
                pick.Kind, targetName, detail, CurrentAffordance, _interactKeyLabel);

            if (_promptView != null)
            {
                _promptView.Show(target.transform, CurrentAffordance, CurrentPrompt);
            }
        }

        private InteractionPick SelectNearestPick()
        {
            FillPositions(_nodeMarkers, _nodePositions);
            FillPositions(_stations, _stationPositions);
            FillPositions(_golems, _golemPositions);
            return InteractionTargeting.SelectNearest(
                transform.position, _nodePositions, _stationPositions, _golemPositions);
        }

        // Destroyed components leave null holes in the cached arrays (a removed building, a
        // golem the player deleted). Parking them at +infinity keeps them un-selectable
        // without reallocating the array or reordering the live indices.
        private static void FillPositions<T>(T[] source, Vector3[] destination) where T : Component
        {
            for (int i = 0; i < source.Length && i < destination.Length; i++)
            {
                destination[i] = source[i] != null
                    ? source[i].transform.position
                    : new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            }
        }

        private Component ResolveTarget(InteractionPick pick)
        {
            switch (pick.Kind)
            {
                case InteractionKind.Harvest:
                    return pick.Index >= 0 && pick.Index < _nodeMarkers.Length ? _nodeMarkers[pick.Index] : null;
                case InteractionKind.Construct:
                    return pick.Index >= 0 && pick.Index < _stations.Length ? _stations[pick.Index] : null;
                case InteractionKind.Program:
                    return pick.Index >= 0 && pick.Index < _golems.Length ? _golems[pick.Index] : null;
                default:
                    return null;
            }
        }

        private static bool IsUnavailable(InteractionPick pick, Component target) =>
            pick.Kind == InteractionKind.Harvest && ((ResourceNodeMarker)target).IsDepleted;

        private static void DescribeTarget(
            InteractionPick pick, Component target, bool isCarrying, out string targetName, out string detail)
        {
            targetName = "";
            detail = "";
            switch (pick.Kind)
            {
                case InteractionKind.Harvest:
                {
                    var marker = (ResourceNodeMarker)target;
                    string itemType = marker.ItemType;
                    targetName = string.IsNullOrEmpty(itemType) ? marker.NodeId : itemType;
                    detail = ResourceNodeVisualState.DescribeRemaining(marker.RemainingQuantity);
                    break;
                }
                case InteractionKind.Construct:
                    targetName = "";
                    detail = "";
                    break;
                case InteractionKind.Program:
                {
                    var golem = (GolemEntity)target;
                    targetName = golem.GolemId;
                    // Facing is in the caption because it is now routing, not decoration, and
                    // these are the only place the player is told [R] and [G] exist at all.
                    //
                    // The run state used to lead this line and has been dropped: it is already
                    // said louder by the stall badge floating over the same golem, and the two
                    // captions plus the badge were physically overlapping on screen. Keeping
                    // only what nothing else shows.
                    detail = "faces " + FacingVisuals.Describe(golem.Facing)
                        + " · [R] turn · [G] " + (isCarrying ? "drop" : "carry");
                    break;
                }
            }
        }

        // Finds the single nearest interactable of any kind within range and acts on it.
        // Returns false (with LastStatusMessage explaining why) if nothing was in range or
        // the action itself failed (e.g. an empty node, or a station the player can't afford).
        public bool Interact()
        {
            InteractionPick pick = SelectNearestPick();
            if (!pick.IsInRange(_interactRange))
            {
                LastStatusMessage = "Nothing in range to interact with.";
                return false;
            }

            Component target = ResolveTarget(pick);
            switch (pick.Kind)
            {
                case InteractionKind.Harvest:
                    return TryHarvest(target as ResourceNodeMarker);
                case InteractionKind.Construct:
                    return TryOpenConstruction(target as GolemConstructionStation);
                case InteractionKind.Program:
                    return TryProgram(target as GolemEntity);
                default:
                    LastStatusMessage = "Nothing in range to interact with.";
                    return false;
            }
        }

        // Exposed separately from Interact() so tests (and the prompt UI) can target a
        // specific interactable directly, same pattern as BuildModeController.PlaceOrRemove.
        public bool TryHarvest(ResourceNodeMarker marker)
        {
            if (marker == null)
            {
                LastStatusMessage = "Nothing left to harvest here.";
                return false;
            }

            if (!marker.TryHarvest(out ItemStack item))
            {
                LastStatusMessage = "Nothing left to harvest here.";
                // A refusal needs to say so where the player is looking. Without this the
                // only difference between "harvested" and "this node is spent" was a status
                // string nothing rendered -- the exact gap the depletion scope cut left open.
                SpawnPopup(marker.transform.position, "Depleted", RefusedPopupColor);
                return false;
            }

            if (_stockpileHolder != null)
            {
                _stockpileHolder.Registry.Deposit(_stockpileBufferId, item.ItemType);
            }

            LastStatusMessage = $"Harvested {item.ItemType}.";
            SpawnPopup(marker.transform.position, "+1 " + item.ItemType, HarvestPopupColor);
            return true;
        }

        public bool TryOpenConstruction(GolemConstructionStation station)
        {
            if (station == null || _constructionPanel == null)
            {
                LastStatusMessage = "No construction panel available.";
                return false;
            }

            _constructionPanel.Open(station);
            LastStatusMessage = "";
            return true;
        }

        public bool TryProgram(GolemEntity golem)
        {
            if (golem == null || _workbenchController == null)
            {
                LastStatusMessage = "No Workbench available.";
                return false;
            }

            _workbenchController.Open();
            _workbenchController.RetargetGolem(golem);
            LastStatusMessage = $"Programming {golem.GolemId}.";
            return true;
        }

        // Popups are skipped outside Play mode: FloatingPopup drives itself from Update and
        // would never tick (nor ever be destroyed) in an EditMode test.
        private static void SpawnPopup(Vector3 worldPosition, string text, Color color)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // Above the interaction caption, not on top of it: both anchor to the same target,
            // and at the caption's own height the two lines rendered straight through each
            // other ("+1 Aether" over "[E] Harvest Aether").
            FloatingPopup.Spawn(worldPosition + new Vector3(0f, 1.5f, 0f), text, color);
        }
    }
}
