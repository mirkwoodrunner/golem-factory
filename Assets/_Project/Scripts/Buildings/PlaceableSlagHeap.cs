using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// §11 item 10's placeable sink. A thin wrapper around <see cref="SlagHeap"/> that publishes
    /// its tile, exactly as <c>PlaceableBoiler</c> wraps <c>SteamBoiler</c>.
    /// </summary>
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableSlagHeap : MonoBehaviour
    {
        // TUNING, not derived: §11 prices the Slag Heap at nothing at all, so these are a first
        // pass for the Game Director. Deliberately PRESSER-TIER goods, for the same structural
        // reason §8 prices the bay upgrade that way -- the heap unblocks the iron line, so a
        // heap that cost iron-line goods could be gated behind the jam it exists to clear.
        public const int ScrapCost = 20;
        public const int IronPlateCost = 10;

        [SerializeField] private string heapId = "";
        [SerializeField] private int startingCoke;

        private SlagHeap _heap;

        public SlagHeap Heap
        {
            get
            {
                if (_heap == null)
                {
                    _heap = new SlagHeap(heapId, startingCoke);
                }

                return _heap;
            }
        }

        /// <summary>
        /// Publishes the heap's tile so a golem can push Slag (and Coke) onto it. Called by
        /// BuildModeController on placement and by SandboxBootstrap for scene-authored heaps --
        /// the one-place-owns-"really in the world" rule every other placeable follows.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistryHolder endpointHolder, Vector2Int cell)
        {
            if (endpointHolder == null)
            {
                return false;
            }

            // Stamped from the cell like a boiler's id, so two heaps cannot share a name.
            if (string.IsNullOrEmpty(heapId))
            {
                heapId = "SlagHeap(" + cell.x + "," + cell.y + ")";
                _heap = new SlagHeap(heapId, startingCoke);
            }

            endpointHolder.Registry.Register(cell, new SlagHeapEndpoint(Heap));
            return true;
        }
    }
}
