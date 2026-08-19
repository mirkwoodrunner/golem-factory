using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// The far end of §6's Freight Link: a mast a Zeppelin launches its hold at, from anywhere
    /// on the map. 20 Brass + 10 Casing, placed like any other building.
    ///
    /// <para>
    /// <b>It is a depot with a registry entry.</b> The goods have to land somewhere real --
    /// "onto the mast's tile" means a tile a belt or another golem can then work from -- so the
    /// mast publishes a <see cref="StorageBufferEndpoint"/> on its cell exactly as
    /// <see cref="PlaceableDepot"/> does, and a launch is a <c>Push</c> that happens to reach
    /// across the map. Inventing a second kind of receiving tile would have meant a second set
    /// of capacity, endpoint and save rules for the same idea.
    /// </para>
    ///
    /// <para>
    /// What it adds beyond a depot is the <see cref="FreightMastRegistry"/> entry, which is what
    /// a Zeppelin binds to at placement.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableFreightMast : MonoBehaviour
    {
        /// <summary>§6: 20 Brass + 10 Casing. Recorded here beside the behaviour it prices.</summary>
        public const int BrassCost = 20;
        public const int CasingCost = 10;

        [SerializeField] private string bufferId = "FactoryStockpile";

        // Blank on the prefab and stamped from the cell at placement, exactly as a boiler's id
        // is: two masts must never share a name.
        [SerializeField] private string mastId = "";

        public string BufferId => bufferId;
        public string MastId => mastId;

        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistryHolder endpointHolder,
            StorageBufferRegistryHolder bufferHolder,
            Vector2Int cell)
        {
            if (endpointHolder == null || bufferHolder == null || string.IsNullOrEmpty(bufferId))
            {
                return false;
            }

            // GetOrCreate for the reason PlaceableDepot records: a buffer springs into existence
            // on first deposit, so a mast placed before anything has been stored would otherwise
            // publish nothing at all.
            StorageBuffer buffer = bufferHolder.Registry.GetOrCreate(bufferId);
            endpointHolder.Registry.Register(cell, new StorageBufferEndpoint(buffer));
            return true;
        }

        public bool RegisterWithMastNetwork(FreightMastRegistryHolder holder, Vector2Int cell)
        {
            if (holder == null)
            {
                return false;
            }

            mastId = "Mast(" + cell.x + "," + cell.y + ")";
            holder.Registry.Register(cell, mastId);
            return true;
        }

        public bool UnregisterFromMastNetwork(FreightMastRegistryHolder holder, Vector2Int cell) =>
            holder != null && holder.Registry.Unregister(cell);
    }
}
