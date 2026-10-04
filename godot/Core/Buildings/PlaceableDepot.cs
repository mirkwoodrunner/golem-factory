using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A depot: publishes a <see cref="StorageBuffer"/> on its cell so a routed chain has
    /// somewhere to end. Ported from Unity's sibling component (G2b).
    ///
    /// <para>
    /// <b>A labelled depot publishes a different endpoint.</b> With an empty filter it publishes
    /// the plain <see cref="StorageBufferEndpoint"/>; with one set, a
    /// <see cref="FilteredBufferEndpoint"/> that accepts and dispenses only that good. Changing
    /// the label re-publishes on the same cell.
    /// </para>
    /// </summary>
    public sealed class PlaceableDepot : IBuildingPart
    {
        private string bufferId = "FactoryStockpile";

        // Empty means this depot accepts and dispenses anything. Set to an ItemType id to make
        // it a sorter: it takes only that good, and hands out only that good.
        private string filterItemType = "";

        private SpatialEndpointRegistry _endpoints;
        private StorageBufferRegistry _buffers;
        private Vector2Int _registeredCell;
        private bool _isRegistered;

        public string BufferId => bufferId;

        public string FilterItemType => string.IsNullOrEmpty(filterItemType) ? null : filterItemType;

        public string FilterLabel => DepotFilterOptions.Describe(FilterItemType);

        /// <summary>Authoring setter (Unity set these in the Inspector).</summary>
        public void Configure(string buffer, string filter = "")
        {
            bufferId = buffer;
            filterItemType = filter ?? "";
        }

        public IBuildingPart CloneForInstance() =>
            new PlaceableDepot { bufferId = bufferId, filterItemType = filterItemType };

        /// <summary>
        /// Registers this depot's buffer on <paramref name="cell"/>. Called by build mode when
        /// the depot is placed.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistry endpoints, StorageBufferRegistry buffers, Vector2Int cell)
        {
            if (endpoints == null || buffers == null || string.IsNullOrEmpty(bufferId))
            {
                return false;
            }

            _endpoints = endpoints;
            _buffers = buffers;
            _registeredCell = cell;
            _isRegistered = true;
            PublishEndpoint();
            return true;
        }

        /// <summary>Labels the depot (null or empty clears it) and re-publishes its endpoint.</summary>
        public void SetFilter(string itemType)
        {
            filterItemType = string.IsNullOrEmpty(itemType) ? "" : itemType;
            PublishEndpoint();
        }

        /// <summary>Advances the label to the next option in the cycle and returns it.</summary>
        public string CycleFilter()
        {
            string next = DepotFilterOptions.Next(BuildFilterCycle(), FilterItemType);
            SetFilter(next);
            return FilterItemType;
        }

        public IReadOnlyList<string> BuildFilterCycle()
        {
            StorageBuffer buffer = ResolveBuffer();
            return DepotFilterOptions.BuildCycle(
                buffer != null ? buffer.Quantities.Keys : null, FilterItemType);
        }

        private StorageBuffer ResolveBuffer()
        {
            if (_buffers == null || string.IsNullOrEmpty(bufferId))
            {
                return null;
            }
            return _buffers.GetOrCreate(bufferId);
        }

        private void PublishEndpoint()
        {
            if (!_isRegistered || _endpoints == null)
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
            _endpoints.Register(_registeredCell, endpoint);
        }
    }
}
