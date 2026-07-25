using UnityEngine;
using GolemFactory.Belts;

namespace GolemFactory.World
{
    // Thin spatial proxy for exactly one logical ResourceNode -- ResourceNode/
    // ResourceNodeRegistry stay pure logic with no Transform/world position at all
    // (unchanged since M5); this is what lets a player walk up to one. TryHarvest is a
    // one-line forward to ResourceNodeRegistry.TryExtract, the exact same call
    // Golems/GolemEntity.cs's TryExtractFromNode already makes via step.sourceId -- a
    // player harvesting and a golem extracting from the same nodeId genuinely compete
    // for the same RemainingQuantity, which is a fair emergent property, not something
    // this class needs to reconcile. Depositing the harvested item into a buffer is the
    // caller's job (Player/PlayerInteractor.cs), not this class's -- keeps this a pure
    // spatial+extraction proxy, matching GolemEntity's own extract/deposit split.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ResourceNodeMarker : MonoBehaviour
    {
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private string nodeId;

        public string NodeId => nodeId;

        public void Configure(ResourceNodeRegistryHolder registryHolder, string id)
        {
            nodeRegistryHolder = registryHolder;
            nodeId = id;
        }

        private void Awake()
        {
            if (GetComponent<YSortSpriteRenderer>() == null)
            {
                gameObject.AddComponent<YSortSpriteRenderer>();
            }
        }

        public bool TryHarvest(out ItemStack item)
        {
            item = default;
            return nodeRegistryHolder != null && nodeRegistryHolder.Registry.TryExtract(nodeId, out item);
        }
    }
}
