using UnityEngine;

namespace GolemFactory.Blueprints
{
    // Thin scene-resident owner for the plain-C# PatentRegistry, mirroring
    // ConveyorSystemHolder/StorageBufferRegistryHolder.
    public sealed class PatentRegistryHolder : MonoBehaviour
    {
        public PatentRegistry Registry { get; } = new PatentRegistry();
    }
}
