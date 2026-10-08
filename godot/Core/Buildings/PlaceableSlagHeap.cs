using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A slag heap: publishes its tile, which accepts Slag to void and Coke to burn it with --
    /// §5.3(c)'s costed sink. Without a published tile it is a building the smelter cannot
    /// reach, which is the whole of its job. Ported from Unity's sibling component (G2b).
    /// </summary>
    public sealed class PlaceableSlagHeap : IBuildingPart
    {
        public const int ScrapCost = 20;
        public const int IronPlateCost = 10;

        private string heapId = "";
        private int startingCoke;
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

        public void Configure(string id, int coke)
        {
            heapId = id ?? "";
            startingCoke = coke;
        }

        public IBuildingPart CloneForInstance() => new PlaceableSlagHeap { heapId = heapId, startingCoke = startingCoke };

        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistry endpoints, Vector2Int cell)
        {
            if (endpoints == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(heapId))
            {
                heapId = "SlagHeap(" + cell.x + "," + cell.y + ")";
                _heap = new SlagHeap(heapId, startingCoke);
            }

            endpoints.Register(cell, new SlagHeapEndpoint(Heap));
            return true;
        }
    }
}
