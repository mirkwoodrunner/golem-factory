using UnityEngine;

namespace GolemFactory.Economy
{
    // Thin scene-resident owner for the plain-C# StorageBufferRegistry, mirroring
    // Belts/ConveyorSystemHolder's ownership of ConveyorSystem.
    public sealed class StorageBufferRegistryHolder : MonoBehaviour
    {
        public StorageBufferRegistry Registry { get; } = new StorageBufferRegistry();
    }
}
