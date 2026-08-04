using System.Collections.Generic;

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
    public sealed class SpatialEndpointRegistry
    {
        private readonly Dictionary<Vector2IntKey, IItemEndpoint> _endpoints =
            new Dictionary<Vector2IntKey, IItemEndpoint>();

        public int Count => _endpoints.Count;

        public void Register(UnityEngine.Vector2Int cell, IItemEndpoint endpoint)
        {
            // A null endpoint would register a cell that reports occupied but yields nothing,
            // which reads to the player as "the tile is broken". Treat it as an unregister.
            if (endpoint == null)
            {
                Unregister(cell);
                return;
            }

            _endpoints[new Vector2IntKey(cell)] = endpoint;
        }

        public void Unregister(UnityEngine.Vector2Int cell) => _endpoints.Remove(new Vector2IntKey(cell));

        public bool TryGetEndpoint(UnityEngine.Vector2Int cell, out IItemEndpoint endpoint) =>
            _endpoints.TryGetValue(new Vector2IntKey(cell), out endpoint);

        public bool HasEndpoint(UnityEngine.Vector2Int cell) => _endpoints.ContainsKey(new Vector2IntKey(cell));

        public void Clear() => _endpoints.Clear();

        // Vector2Int is a fine dictionary key (it implements IEquatable), but it boxes through
        // the default comparer on some Unity versions; this wrapper pins a struct comparer path
        // and keeps the hash stable and obvious.
        private readonly struct Vector2IntKey : System.IEquatable<Vector2IntKey>
        {
            private readonly int _x;
            private readonly int _y;

            public Vector2IntKey(UnityEngine.Vector2Int cell)
            {
                _x = cell.x;
                _y = cell.y;
            }

            public bool Equals(Vector2IntKey other) => _x == other._x && _y == other._y;

            public override bool Equals(object obj) => obj is Vector2IntKey && Equals((Vector2IntKey)obj);

            public override int GetHashCode() => (_x * 397) ^ _y;
        }
    }
}
