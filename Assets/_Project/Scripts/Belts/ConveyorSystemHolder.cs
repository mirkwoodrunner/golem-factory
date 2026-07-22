using UnityEngine;

namespace GolemFactory.Belts
{
    // Thin scene-resident owner for the plain-C# ConveyorSystem, mirroring GridMapHolder's
    // ownership of GridMap and SimulationClockRunner's ownership of SimulationClock.
    public sealed class ConveyorSystemHolder : MonoBehaviour
    {
        public ConveyorSystem System { get; } = new ConveyorSystem();
    }
}
