using System.Collections.Generic;
using GolemFactory.Belts;

namespace GolemFactory.World
{
    // Plain C# registry of ResourceNodes keyed by id, mirroring ConveyorSystem's
    // segment dictionary. Owned by a scene holder (see ResourceNodeRegistryHolder).
    public sealed class ResourceNodeRegistry
    {
        private readonly Dictionary<string, ResourceNode> _nodes = new Dictionary<string, ResourceNode>();

        public void Register(ResourceNode node) => _nodes[node.NodeId] = node;

        /// <summary>Forgets a node: a supply cart taken off the street. False for an unknown id.</summary>
        public bool Remove(string nodeId) => nodeId != null && _nodes.Remove(nodeId);

        /// <summary>Every node, in the order they were registered: what a save walks.</summary>
        public IEnumerable<ResourceNode> Nodes => _nodes.Values;

        public bool TryGetNode(string nodeId, out ResourceNode node)
        {
            if (nodeId == null)
            {
                node = null;
                return false;
            }

            return _nodes.TryGetValue(nodeId, out node);
        }

        public bool TryExtract(string nodeId, out ItemStack item)
        {
            item = default;
            return TryGetNode(nodeId, out ResourceNode node) && node.TryExtract(out item);
        }
    }
}
