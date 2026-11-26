using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A slag heap: publishes its tile, which accepts Slag to void and Coke to burn it with --
    /// §5.3(c)'s costed sink. Without a published tile it is a building the smelter cannot
    /// reach, which is the whole of its job. Ported from Unity's sibling component (G2b).
    /// </summary>
    public sealed class PlaceableSlagHeap : IBuildingPart, ICokeHolder
    {
        public const int ScrapCost = 20;
        public const int IronPlateCost = 10;

        private string heapId = "";
        private int startingCoke;
        private int slagPerCoke = SlagHeap.DefaultSlagPerCoke;
        private SlagHeap _heap;

        /// <summary>Slag voided per Coke burned (placeables.json), for every heap this prefab places.</summary>
        public int SlagPerCoke => slagPerCoke;

        public SlagHeap Heap
        {
            get
            {
                if (_heap == null)
                {
                    _heap = new SlagHeap(heapId, startingCoke, slagPerCoke);
                }
                return _heap;
            }
        }

        // --- ICokeHolder (the player's [E] and [I]) ---
        public string CokeHolderName => "Slag Heap";
        public string CokeHolderInSentence => "the Slag Heap";
        public bool HasCokeStore => true;
        public int CokeStock => Heap.CokeStock;
        public int CokeCapacity => SlagHeap.CokeCapacity;
        public int CokeRoom => Heap.CokeRoom;
        public int AddCoke(int quantity) => Heap.AddCoke(quantity);
        public int TakeCoke(int quantity) => Heap.TakeCoke(quantity);

        public void Configure(string id, int coke, int slagPerCokeBurned = SlagHeap.DefaultSlagPerCoke)
        {
            heapId = id ?? "";
            startingCoke = coke;
            slagPerCoke = slagPerCokeBurned < 1 ? 1 : slagPerCokeBurned;
        }

        public IBuildingPart CloneForInstance() =>
            new PlaceableSlagHeap { heapId = heapId, startingCoke = startingCoke, slagPerCoke = slagPerCoke };

        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistry endpoints, Vector2Int cell)
        {
            if (endpoints == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(heapId))
            {
                heapId = "SlagHeap(" + cell.x + "," + cell.y + ")";
                _heap = new SlagHeap(heapId, startingCoke, slagPerCoke);
            }

            endpoints.Register(cell, new SlagHeapEndpoint(Heap));
            return true;
        }
    }
}
