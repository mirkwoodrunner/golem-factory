using GolemFactory.Belts;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// A resource node standing in the world: the spatial proxy that forwards to the same
    /// <see cref="ResourceNodeRegistry.TryExtract"/> a golem's ExtractFromNode step calls, so
    /// player harvesting and golem extraction genuinely compete for the same stock.
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (G2c) -- the logic half. Unity's
    /// version also drove its own sprite's tint, scale and harvest pulse; the Godot layer
    /// draws those now, from <see cref="Visual"/> and <see cref="HarvestCount"/>. Node identity
    /// is the SPRITE (root CLAUDE.md: a tint never survives Play), which is the scene's call.
    /// </para>
    /// </summary>
    public sealed class ResourceNodeMarker
    {
        private ResourceNodeRegistry nodeRegistry;
        private string nodeId;
        private int _peakQuantity;
        private Vector2Int _spatialCell;
        private bool _hasSpatialCell;

        public string NodeId => nodeId;

        /// <summary>
        /// Where the marker stands, in world units. Free rather than a cell, as Unity's transform
        /// was: a marker is placed by hand and its cell is derived from it.
        /// </summary>
        public Vector3 Position { get; set; }

        public Vector2Int CellOn(GridCoordinateConverter converter) => converter.WorldToCell(Position);

        public void Configure(ResourceNodeRegistry registry, string id)
        {
            nodeRegistry = registry;
            nodeId = id;
            _peakQuantity = 0;
            RefreshVisualState();
        }

        /// <summary>
        /// Publishes the node on the cell under it, which is what lets a golem facing away from
        /// it ExtractFromNode.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistry endpoints, GridCoordinateConverter converter)
        {
            if (endpoints == null || nodeRegistry == null || !nodeRegistry.TryGetNode(nodeId, out ResourceNode node))
            {
                return false;
            }

            _spatialCell = CellOn(converter);
            _hasSpatialCell = true;
            endpoints.Register(_spatialCell, new ResourceNodeEndpoint(node));
            return true;
        }

        public Vector2Int SpatialCell => _spatialCell;
        public bool IsSpatiallyRegistered => _hasSpatialCell;

        public int RemainingQuantity =>
            nodeRegistry != null && nodeRegistry.TryGetNode(nodeId, out ResourceNode node) ? node.RemainingQuantity : 0;

        public string ItemType =>
            nodeRegistry != null && nodeRegistry.TryGetNode(nodeId, out ResourceNode node) ? node.ItemType : "";

        /// <summary>The most this node has held, the reference its depletion readout scales to.</summary>
        public int PeakQuantity => _peakQuantity;

        public bool IsDepleted => RemainingQuantity == 0;

        /// <summary>Harvests made through this marker -- the scene plays its pulse when it rises.</summary>
        public int HarvestCount { get; private set; }

        /// <summary>The depletion readout, as Unity's sprite drew it.</summary>
        public ResourceNodeVisual Visual { get; private set; }

        /// <summary>Whether the last readout said "depleted".</summary>
        public bool LastRenderedAsDepleted => Visual.IsDepleted;

        public bool TryHarvest(out ItemStack item)
        {
            item = default;
            bool harvested = nodeRegistry != null && nodeRegistry.TryExtract(nodeId, out item);
            if (harvested)
            {
                HarvestCount++;
            }

            RefreshVisualState();
            return harvested;
        }

        /// <summary>Re-reads the node and updates <see cref="Visual"/>.</summary>
        public void RefreshVisualState()
        {
            int remaining = RemainingQuantity;
            if (remaining > _peakQuantity)
            {
                _peakQuantity = remaining;
            }

            Visual = ResourceNodeVisualState.Evaluate(remaining, _peakQuantity);
        }
    }
}
