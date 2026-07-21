using UnityEngine;

namespace GolemFactory.World
{
    // Thin scene-resident owner for the plain-C# GridMap, mirroring how SimulationClock
    // is owned by a MonoBehaviour wrapper (see Simulation/SimulationClock.cs).
    public sealed class GridMapHolder : MonoBehaviour
    {
        public GridMap Map { get; } = new GridMap();
    }
}
