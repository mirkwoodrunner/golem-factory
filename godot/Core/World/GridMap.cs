using GolemFactory.Compat;

namespace GolemFactory.World
{
    // Simulation truth for grid occupancy, decoupled from any Tilemap/rendering. One occupant
    // to a cell, held in a SpatialHashGrid: build mode asks it for every cell of a footprint
    // on every frame the ghost moves.
    public sealed class GridMap
    {
        private readonly SpatialHashGrid<object> _occupants = new SpatialHashGrid<object>(256);

        public bool IsOccupied(Vector2Int cell) => _occupants.Contains(cell);

        public bool TryGetOccupant(Vector2Int cell, out object occupant) => _occupants.TryGet(cell, out occupant);

        public bool TryOccupy(Vector2Int cell, object occupant)
        {
            if (_occupants.Contains(cell))
            {
                return false;
            }

            _occupants.Add(cell, occupant);
            return true;
        }

        public void Free(Vector2Int cell) => _occupants.RemoveAt(cell);
    }
}
