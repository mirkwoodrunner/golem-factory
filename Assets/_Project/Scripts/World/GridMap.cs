using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.World
{
    // Simulation truth for grid occupancy, decoupled from any Tilemap/rendering.
    public sealed class GridMap
    {
        private readonly Dictionary<Vector2Int, object> _occupants = new Dictionary<Vector2Int, object>();

        public bool IsOccupied(Vector2Int cell) => _occupants.ContainsKey(cell);

        public bool TryGetOccupant(Vector2Int cell, out object occupant) => _occupants.TryGetValue(cell, out occupant);

        public bool TryOccupy(Vector2Int cell, object occupant)
        {
            if (_occupants.ContainsKey(cell))
            {
                return false;
            }

            _occupants[cell] = occupant;
            return true;
        }

        public void Free(Vector2Int cell) => _occupants.Remove(cell);
    }
}
