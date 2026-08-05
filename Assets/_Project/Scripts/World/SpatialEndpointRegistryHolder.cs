using UnityEngine;

namespace GolemFactory.World
{
    // Thin scene-resident owner for the plain-C# SpatialEndpointRegistry, mirroring
    // GridMapHolder / ConveyorSystemHolder / StorageBufferRegistryHolder /
    // ResourceNodeRegistryHolder. Keeps the routing logic engine-decoupled and unit-testable
    // while still giving it a scene presence a GolemEntity can be pointed at.
    public sealed class SpatialEndpointRegistryHolder : MonoBehaviour
    {
        public SpatialEndpointRegistry Registry { get; } = new SpatialEndpointRegistry();
    }
}
