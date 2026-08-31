using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Publishes a StorageBuffer onto the cell this building occupies, so a golem facing it can
    // push items into it without naming it by id. Sibling component alongside PlaceableBuilding,
    // the same arrangement GolemConstructionStation and PlaceableBelt use.
    //
    // This is what actually closes the player's loop. The Depot prefab was previously pure
    // decoration -- a building with no behaviour at all -- so a spatially routed chain had
    // nowhere to *end*: a golem could pull from a node and push onto a belt, and the belt ran
    // into nothing. Pointing the depot at the same FactoryStockpile the construction station
    // spends from means a finished chain visibly pays for the next golem.
    //
    // --- The label (docs/cozy-automation-design.md §1) -----------------------------------
    // A depot can be told what it is for. Empty means "anything", which is byte for byte what
    // every depot did before this existed -- the opt-in-by-null rule every cross-cutting system
    // in this project follows, and the reason no authored depot and no existing test needed
    // touching. A label makes a crate a sorter on the way in and a typed source on the way out.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableDepot : MonoBehaviour
    {
        [SerializeField] private string bufferId = "FactoryStockpile";

        [Tooltip("Empty means this depot accepts and dispenses anything. Set to an ItemType id " +
                 "to make it a sorter: it takes only that good, and hands out only that good.")]
        [SerializeField] private string filterItemType = "";

        // Remembered from the registration so a filter changed at runtime can re-publish the
        // endpoint. Without this, cycling the label would update a field nobody reads: the
        // endpoint object on the cell is built once at placement and captures the filter it was
        // built with.
        private SpatialEndpointRegistryHolder _endpointHolder;
        private StorageBufferRegistryHolder _bufferHolder;
        private Vector2Int _registeredCell;
        private bool _isRegistered;

        public string BufferId => bufferId;

        /// <summary>
        /// The good this crate is labelled for, or null for "anything". Normalised, so a
        /// serialized <c>""</c> and a code-written <c>null</c> are never two different answers.
        /// </summary>
        public string FilterItemType =>
            string.IsNullOrEmpty(filterItemType) ? null : filterItemType;

        /// <summary>What the prompt and the crate's readout call the current label.</summary>
        public string FilterLabel => DepotFilterOptions.Describe(FilterItemType);

        /// <summary>
        /// Registers this depot's buffer on <paramref name="cell"/>. Called by
        /// BuildModeController after placement (and by SandboxBootstrap for any depot authored
        /// directly into the scene), for the same reason belts are registered there: one place
        /// owns "placed in the world" so a building can never exist visually but not logically.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistryHolder endpointHolder,
            StorageBufferRegistryHolder bufferHolder,
            Vector2Int cell)
        {
            if (endpointHolder == null || bufferHolder == null || string.IsNullOrEmpty(bufferId))
            {
                return false;
            }

            _endpointHolder = endpointHolder;
            _bufferHolder = bufferHolder;
            _registeredCell = cell;
            _isRegistered = true;

            PublishEndpoint();
            return true;
        }

        /// <summary>
        /// Labels this crate, re-publishing its tile so the change is live immediately rather
        /// than at the next placement.
        ///
        /// <para>
        /// Called by the player's <c>[E]</c> and by the save's restore pass. The restore case is
        /// why this re-publishes rather than only writing the field: a rebuilt building is placed
        /// first and has its per-type state applied afterwards, so by the time the filter arrives
        /// an unfiltered endpoint is already sitting on the cell.
        /// </para>
        /// </summary>
        public void SetFilter(string itemType)
        {
            filterItemType = string.IsNullOrEmpty(itemType) ? "" : itemType;
            PublishEndpoint();
        }

        /// <summary>
        /// Advances to the next label in this depot's live cycle and returns what it landed on.
        ///
        /// <para>
        /// The cycle is built from what the stockpile has actually handled (see
        /// <see cref="DepotFilterOptions.BuildCycle"/>), so it stays short and grows with the
        /// factory. With no buffer registry wired the cycle is just "any goods", and cycling is
        /// a no-op -- which is the honest behaviour for a depot that is not connected to an
        /// economy yet.
        /// </para>
        /// </summary>
        public string CycleFilter()
        {
            string next = DepotFilterOptions.Next(BuildFilterCycle(), FilterItemType);
            SetFilter(next);
            return FilterItemType;
        }

        /// <summary>
        /// The labels this depot could wear right now. Exposed so the interaction prompt can say
        /// how many there are without rebuilding the list a second way.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> BuildFilterCycle()
        {
            StorageBuffer buffer = ResolveBuffer();
            return DepotFilterOptions.BuildCycle(
                buffer != null ? buffer.Quantities.Keys : null, FilterItemType);
        }

        private StorageBuffer ResolveBuffer()
        {
            if (_bufferHolder == null || string.IsNullOrEmpty(bufferId))
            {
                return null;
            }

            // GetOrCreate, not TryGetBuffer: a buffer springs into existence on first deposit,
            // so a depot placed before anything has ever been stored would otherwise find
            // nothing to publish and silently register no endpoint.
            return _bufferHolder.Registry.GetOrCreate(bufferId);
        }

        // The one place that decides which KIND of endpoint a depot puts on its tile. A labelled
        // crate publishes FilteredBufferEndpoint; an unlabelled one publishes exactly the
        // StorageBufferEndpoint it always did, so nothing about the default path changed.
        private void PublishEndpoint()
        {
            if (!_isRegistered || _endpointHolder == null)
            {
                return;
            }

            StorageBuffer buffer = ResolveBuffer();
            if (buffer == null)
            {
                return;
            }

            string filter = FilterItemType;
            IItemEndpoint endpoint = filter == null
                ? (IItemEndpoint)new StorageBufferEndpoint(buffer)
                : new FilteredBufferEndpoint(buffer, filter);

            _endpointHolder.Registry.Register(_registeredCell, endpoint);
        }
    }
}
