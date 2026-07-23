using UnityEngine;

namespace GolemFactory.World
{
    // Thin scene-resident owner for the plain-C# ResourceNodeRegistry, mirroring
    // GridMapHolder's ownership of GridMap.
    public sealed class ResourceNodeRegistryHolder : MonoBehaviour
    {
        public ResourceNodeRegistry Registry { get; } = new ResourceNodeRegistry();
    }
}
