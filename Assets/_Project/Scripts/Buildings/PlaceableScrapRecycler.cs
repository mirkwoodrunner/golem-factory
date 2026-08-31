using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// §4b's junk hopper as a placeable. A thin wrapper around <see cref="ScrapRecycler"/>,
    /// exactly as <see cref="PlaceableSlagHeap"/> wraps <c>SlagHeap</c> and
    /// <c>PlaceableBoiler</c> wraps <c>SteamBoiler</c>.
    /// </summary>
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableScrapRecycler : MonoBehaviour
    {
        // TUNING. Deliberately the SAME Presser-tier bundle as the Slag Heap, and for the same
        // structural reason §8 prices the bay upgrade that way: the recycler exists to clear
        // jams, so a recycler priced in goods downstream of a jam could be gated behind the very
        // problem it solves.
        public const int ScrapCost = 20;
        public const int IronPlateCost = 10;

        [SerializeField] private string recyclerId = "";
        [SerializeField] private int startingCoke;

        private ScrapRecycler _recycler;

        public ScrapRecycler Recycler
        {
            get
            {
                if (_recycler == null)
                {
                    _recycler = new ScrapRecycler(recyclerId, startingCoke);
                }

                return _recycler;
            }
        }

        /// <summary>
        /// Publishes the hopper's tile so a golem can push junk (and Coke) onto it and haul Scrap
        /// back off it. Called by BuildModeController on placement and by SandboxBootstrap for
        /// scene-authored recyclers -- the one-place-owns-"really in the world" rule every other
        /// placeable follows.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistryHolder endpointHolder, Vector2Int cell)
        {
            if (endpointHolder == null)
            {
                return false;
            }

            // Stamped from the cell like a boiler's or a heap's id, so two hoppers cannot share
            // a name.
            if (string.IsNullOrEmpty(recyclerId))
            {
                recyclerId = "ScrapRecycler(" + cell.x + "," + cell.y + ")";
                _recycler = new ScrapRecycler(recyclerId, startingCoke);
            }

            endpointHolder.Registry.Register(cell, new ScrapRecyclerEndpoint(Recycler));
            return true;
        }
    }
}
