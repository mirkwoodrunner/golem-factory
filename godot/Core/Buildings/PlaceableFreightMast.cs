using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// §6's Freight Mast: publishes its tile like a depot AND joins the mast registry, which is
    /// the half a Zeppelin binds to. Both, because the link has to deliver goods somewhere real
    /// as well as be findable. Ported from Unity's sibling component (G2b).
    /// </summary>
    public sealed class PlaceableFreightMast : IBuildingPart
    {
        public const int BrassCost = 20;
        public const int CasingCost = 10;

        private string bufferId = "FactoryStockpile";
        private string mastId = "";

        public string BufferId => bufferId;
        public string MastId => mastId;

        public IBuildingPart CloneForInstance() => new PlaceableFreightMast { bufferId = bufferId, mastId = mastId };

        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistry endpoints, StorageBufferRegistry buffers, Vector2Int cell)
        {
            if (endpoints == null || buffers == null || string.IsNullOrEmpty(bufferId))
            {
                return false;
            }

            StorageBuffer buffer = buffers.GetOrCreate(bufferId);
            endpoints.Register(cell, new StorageBufferEndpoint(buffer));
            return true;
        }

        public bool RegisterWithMastNetwork(FreightMastRegistry registry, Vector2Int cell)
        {
            if (registry == null)
            {
                return false;
            }

            mastId = "Mast(" + cell.x + "," + cell.y + ")";
            registry.Register(cell, mastId);
            return true;
        }

        public bool UnregisterFromMastNetwork(FreightMastRegistry registry, Vector2Int cell) =>
            registry != null && registry.Unregister(cell);
    }
}
