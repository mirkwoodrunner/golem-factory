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
    // GolemConstructionStation to build a golem at, or a GolemEntity to (re)program) within
    // range and acts on it via the new Interact action. The three interactable kinds don't
    // share a common interface, so they're cached as three separate arrays (populated once,
    // like the plan calls for) rather than forcing an artificial abstraction over them.
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private float _interactRange = 1.5f;
        [SerializeField] private StorageBufferRegistryHolder _stockpileHolder;
        [SerializeField] private string _stockpileBufferId = "FactoryStockpile";
        [SerializeField] private GolemConstructionPanel _constructionPanel;
        [SerializeField] private WorkbenchController _workbenchController;

        private InputAction _interactAction;
        private ResourceNodeMarker[] _nodeMarkers = new ResourceNodeMarker[0];
        private GolemConstructionStation[] _stations = new GolemConstructionStation[0];
        private GolemEntity[] _golems = new GolemEntity[0];

        // Set by Interact()/the Try* methods on failure, for an OnGUI prompt or a test to
        // surface -- mirrors BuildModeController.LastStatusMessage.
        public string LastStatusMessage { get; private set; } = "";

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

        private void Awake()
        {
            if (_actions != null)
            {
                _interactAction = _actions.FindActionMap("Gameplay")?.FindAction("Interact");
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
        }

        private void OnDisable()
        {
            if (_interactAction != null)
            {
                _interactAction.performed -= OnInteractPerformed;
                _interactAction.Disable();
            }
        }

        private void OnInteractPerformed(InputAction.CallbackContext context) => Interact();

        // Re-scans the scene for interactables. Called once on enable; also public so a
        // GolemConstructionStation built mid-session (or a test) can make itself/new golems
        // interactable without waiting for a re-enable.
        public void RefreshInteractables()
        {
            _nodeMarkers = FindObjectsByType<ResourceNodeMarker>(FindObjectsSortMode.None);
            _stations = FindObjectsByType<GolemConstructionStation>(FindObjectsSortMode.None);
            _golems = FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
        }

        // Finds the single nearest interactable of any kind within range and acts on it.
        // Returns false (with LastStatusMessage explaining why) if nothing was in range or
        // the action itself failed (e.g. an empty node, or a station the player can't afford).
        public bool Interact()
        {
            float bestDistanceSqr = _interactRange * _interactRange;
            ResourceNodeMarker nearestMarker = FindNearest(_nodeMarkers, ref bestDistanceSqr);
            if (nearestMarker != null)
            {
                return TryHarvest(nearestMarker);
            }

            bestDistanceSqr = _interactRange * _interactRange;
            GolemConstructionStation nearestStation = FindNearest(_stations, ref bestDistanceSqr);
            if (nearestStation != null)
            {
                return TryOpenConstruction(nearestStation);
            }

            bestDistanceSqr = _interactRange * _interactRange;
            GolemEntity nearestGolem = FindNearest(_golems, ref bestDistanceSqr);
            if (nearestGolem != null)
            {
                return TryProgram(nearestGolem);
            }

            LastStatusMessage = "Nothing in range to interact with.";
            return false;
        }

        // Exposed separately from Interact() so tests (and a future prompt UI) can target a
        // specific interactable directly, same pattern as BuildModeController.PlaceOrRemove.
        public bool TryHarvest(ResourceNodeMarker marker)
        {
            if (marker == null || !marker.TryHarvest(out ItemStack item))
            {
                LastStatusMessage = "Nothing left to harvest here.";
                return false;
            }

            if (_stockpileHolder != null)
            {
                _stockpileHolder.Registry.Deposit(_stockpileBufferId, item.ItemType);
            }

            LastStatusMessage = $"Harvested {item.ItemType}.";
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

            _workbenchController.RetargetGolem(golem);
            LastStatusMessage = $"Programming {golem.GolemId}.";
            return true;
        }

        private T FindNearest<T>(T[] candidates, ref float bestDistanceSqr) where T : Component
        {
            T nearest = null;
            foreach (T candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                float distanceSqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr <= bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    nearest = candidate;
                }
            }

            return nearest;
        }
    }
}
