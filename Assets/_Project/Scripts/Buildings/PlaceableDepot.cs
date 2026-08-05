using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Publishes a StorageBuffer onto the cell this building occupies, so a golem facing it can
    // push items into it without naming it by id. Sibling component alongside PlaceableBuilding,
    // the same arrangement GolemConstructionStation and PlaceableBelt use.
    //
    // This is what actually closes the player's loop. The Depot prefab was previously pure
    // decoration -- a building with no behaviour at all -- so a spatially routed chain had
    // nowhere to *end*: a golem could pull from a node and push onto a belt, and the belt ran
    // into nothing. Pointing the depot at the same FactoryStockpile the construction station
    // spends from means a finished chain visibly pays for the next golem.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableDepot : MonoBehaviour
    {
        [SerializeField] private string bufferId = "FactoryStockpile";

        public string BufferId => bufferId;

        /// <summary>
        /// Registers this depot's buffer on <paramref name="cell"/>. Called by
        /// BuildModeController after placement (and by SandboxBootstrap for any depot authored
        /// directly into the scene), for the same reason belts are registered there: one place
        /// owns "placed in the world" so a building can never exist visually but not logically.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistryHolder endpointHolder,
            StorageBufferRegistryHolder bufferHolder,
            Vector2Int cell)
        {
            if (endpointHolder == null || bufferHolder == null || string.IsNullOrEmpty(bufferId))
            {
                return false;
            }

            // GetOrCreate, not TryGetBuffer: a buffer springs into existence on first deposit,
            // so a depot placed before anything has ever been stored would otherwise find
            // nothing to publish and silently register no endpoint.
            StorageBuffer buffer = bufferHolder.Registry.GetOrCreate(bufferId);
            endpointHolder.Registry.Register(cell, new StorageBufferEndpoint(buffer));
            return true;
        }
    }
}
