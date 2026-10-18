using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A depot: a crate on its cell, so a routed chain has somewhere to end. Ported from Unity's
    /// sibling component (G2b).
    ///
    /// <para>
    /// <b>A depot holds its own goods</b> (docs/crates-design.md §4.1). With no authored
    /// <c>bufferId</c> it publishes its own crate, <c>Crate(x,y)</c>, which golems on its
    /// neighbouring tiles reach and nothing else does. An explicit id publishes that shared buffer
    /// instead: a Post crate (§10), or a test keeping the old one-stockpile behaviour.
    /// </para>
    ///
    /// <para>
    /// <b>A labelled depot publishes a different endpoint.</b> With an empty filter it publishes
    /// the plain <see cref="StorageBufferEndpoint"/>; with one set, a
    /// <see cref="FilteredBufferEndpoint"/> over the same crate that accepts and dispenses only
    /// that good. Changing the label re-publishes on the same cell.
    /// </para>
    /// </summary>
    public sealed class PlaceableDepot : IBuildingPart, ICrate
    {
        private string bufferId = "";

        // Empty means this depot accepts and dispenses anything. Set to an ItemType id to make
        // it a sorter: it takes only that good, and hands out only that good.
        private string filterItemType = "";

        private SpatialEndpointRegistry _endpoints;
        private StorageBufferRegistry _buffers;
        private Vector2Int _registeredCell;
        private bool _isRegistered;

        /// <summary>The authored id: empty for a depot with its own crate.</summary>
        public string BufferId => bufferId;

        /// <summary>The id of the crate a cell's own: <c>Crate(3,-2)</c>, the shape <c>Mast(x,y)</c> has.</summary>
        public static string CrateIdFor(Vector2Int cell) => "Crate(" + cell.x + "," + cell.y + ")";

        public bool OwnsBuffer => string.IsNullOrEmpty(bufferId);

        public string CrateId => !_isRegistered ? null : OwnsBuffer ? CrateIdFor(_registeredCell) : bufferId;

        // Read, never created: a demolished depot's crate is removed from the registry, and a
        // reader must not bring it back. Placement creates it (PublishEndpoint).
        public StorageBuffer Buffer => _buffers != null && _buffers.TryGetBuffer(CrateId, out StorageBuffer b) ? b : null;

        public bool CanLabel => true;

        public string FilterItemType => string.IsNullOrEmpty(filterItemType) ? null : filterItemType;

        public string FilterLabel => DepotFilterOptions.Describe(FilterItemType);

        /// <summary>Authoring setter (Unity set these in the Inspector).</summary>
        public void Configure(string buffer, string filter = "")
        {
            bufferId = buffer ?? "";
            filterItemType = filter ?? "";
        }

        public IBuildingPart CloneForInstance() =>
            new PlaceableDepot { bufferId = bufferId, filterItemType = filterItemType };

        /// <summary>
        /// Registers this depot's crate on <paramref name="cell"/>. Called by build mode when
        /// the depot is placed.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistry endpoints, StorageBufferRegistry buffers, Vector2Int cell)
        {
            if (endpoints == null || buffers == null)
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

        private void PublishEndpoint()
        {
            if (!_isRegistered || _endpoints == null)
            {
                return;
            }

            StorageBuffer buffer = _buffers.GetOrCreate(CrateId);
            string filter = FilterItemType;
            IItemEndpoint endpoint = filter == null
                ? (IItemEndpoint)new StorageBufferEndpoint(buffer)
                : new FilteredBufferEndpoint(buffer, filter);
            _endpoints.Register(_registeredCell, endpoint);
        }
    }
}
