using GolemFactory.Compat;

namespace GolemFactory.World
{
    // Which endpoint sits on which cell. The spatial counterpart to the three id-keyed
    // registries (ConveyorSystem's segment dictionary, ResourceNodeRegistry,
    // StorageBufferRegistry) and follows their conventions exactly: plain C#, owned by a
    // thin scene Holder, and every lookup returns false on a miss rather than throwing.
    //
    // One endpoint per cell. A tile is a physical place; two things cannot occupy it, and
    // allowing a list would immediately raise "which one does the golem pull from?", which
    // is precisely the ambiguity facing-based routing exists to remove.
    //
    // Backed by a SpatialHashGrid rather than a Dictionary: every golem asks for the tile behind
    // it and the tile in front at each step's Begin, so this lookup is the tick's most frequent
    // one. The grid answers it from flat arrays in under half the dictionary's time
    // (SimulationStressTests.TheSpatialHash_AgainstADictionary), with a hash that is the same
    // on every run.
    public sealed class SpatialEndpointRegistry
    {
        private readonly SpatialHashGrid<IItemEndpoint> _endpoints = new SpatialHashGrid<IItemEndpoint>(256);

        public int Count => _endpoints.Count;

        public void Register(Vector2Int cell, IItemEndpoint endpoint)
        {
            // A null endpoint would register a cell that reports occupied but yields nothing,
            // which reads to the player as "the tile is broken". Treat it as an unregister.
            if (endpoint == null)
            {
                Unregister(cell);
                return;
            }

            // One endpoint per cell: a second registration replaces the first where it stands.
            if (_endpoints.TryGetHandle(cell, out int handle))
            {
                _endpoints[handle] = endpoint;
                return;
            }

            _endpoints.Add(cell, endpoint);
        }

        public void Unregister(Vector2Int cell) => _endpoints.RemoveAt(cell);

        public bool TryGetEndpoint(Vector2Int cell, out IItemEndpoint endpoint) =>
            _endpoints.TryGet(cell, out endpoint);

        public bool HasEndpoint(Vector2Int cell) => _endpoints.Contains(cell);

        public void Clear() => _endpoints.Clear();
    }
}
