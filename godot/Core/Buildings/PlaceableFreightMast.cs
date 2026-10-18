using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// §6's Freight Mast: publishes its tile like a depot AND joins the mast registry, which is
    /// the half a Zeppelin binds to. Both, because the link has to deliver goods somewhere real
    /// as well as be findable. Ported from Unity's sibling component (G2b).
    ///
    /// <para>
    /// What a Zeppelin lands goes into the mast's own crate, <c>Mast(x,y)</c>, the id it already
    /// registers with the mast network (docs/crates-design.md §4.1): the player or a golem empties
    /// it. An authored <c>bufferId</c> publishes that shared buffer instead, as a depot's does.
    /// A mast's crate takes no label.
    /// </para>
    /// </summary>
    public sealed class PlaceableFreightMast : IBuildingPart, ICrate
    {
        public const int BrassCost = 20;
        public const int CasingCost = 10;

        private string bufferId = "";
        private string mastId = "";
        private StorageBufferRegistry _buffers;
        private Vector2Int _registeredCell;
        private bool _isRegistered;

        /// <summary>The authored id: empty for a mast with its own crate.</summary>
        public string BufferId => bufferId;
        public string MastId => mastId;

        public static string MastIdFor(Vector2Int cell) => "Mast(" + cell.x + "," + cell.y + ")";

        public bool OwnsBuffer => string.IsNullOrEmpty(bufferId);

        public string CrateId => !_isRegistered ? null : OwnsBuffer ? MastIdFor(_registeredCell) : bufferId;

        // Read, never created, for the reason PlaceableDepot.Buffer gives.
        public StorageBuffer Buffer => _buffers != null && _buffers.TryGetBuffer(CrateId, out StorageBuffer b) ? b : null;

        public string FilterItemType => null;

        public bool CanLabel => false;

        public void SetFilter(string itemType) { }

        /// <summary>Authoring setter (placeables.json).</summary>
        public void Configure(string buffer) => bufferId = buffer ?? "";

        public IBuildingPart CloneForInstance() => new PlaceableFreightMast { bufferId = bufferId, mastId = mastId };

        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistry endpoints, StorageBufferRegistry buffers, Vector2Int cell)
        {
            if (endpoints == null || buffers == null)
            {
                return false;
            }

            _buffers = buffers;
            _registeredCell = cell;
            _isRegistered = true;
            endpoints.Register(cell, new StorageBufferEndpoint(buffers.GetOrCreate(CrateId)));
            return true;
        }

        public bool RegisterWithMastNetwork(FreightMastRegistry registry, Vector2Int cell)
        {
            if (registry == null)
            {
                return false;
            }

            mastId = MastIdFor(cell);
            registry.Register(cell, mastId);
            return true;
        }

        public bool UnregisterFromMastNetwork(FreightMastRegistry registry, Vector2Int cell) =>
            registry != null && registry.Unregister(cell);
    }
}
