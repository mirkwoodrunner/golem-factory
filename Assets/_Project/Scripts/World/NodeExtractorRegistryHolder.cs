using UnityEngine;

namespace GolemFactory.World
{
    // Thin scene wrapper owning one NodeExtractorRegistry, per the Holder pattern
    // (GridMapHolder, ConveyorSystemHolder, SpatialEndpointRegistryHolder, SteamNetworkHolder).
    // All the logic -- the claim ordering and the cap -- lives in plain C# so it is unit-
    // testable without a scene; this exists only to give it a scene presence a golem can be
    // handed in the Inspector or by a bootstrap's Configure call.
    //
    // NOT ITickable, unlike SteamNetworkHolder: the crew is re-derived lazily on the first
    // question asked after something changed, and there is no per-tick cost to accrue. A golem
    // asks on the tick it wants to extract, which is the only moment the answer matters.
    public sealed class NodeExtractorRegistryHolder : MonoBehaviour
    {
        private readonly NodeExtractorRegistry _registry = new NodeExtractorRegistry();

        public NodeExtractorRegistry Registry => _registry;
    }
}
