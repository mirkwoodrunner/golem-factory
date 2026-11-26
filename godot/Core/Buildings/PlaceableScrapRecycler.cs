using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A scrap recycler: publishes its tile, which takes junk and Coke in and hands Scrap back
    /// out. BOTH directions matter -- it is a machine in the middle of the logistics graph
    /// rather than a sink, so a golem has to be able to haul from it. Ported from Unity's
    /// sibling component (G2b).
    /// </summary>
    public sealed class PlaceableScrapRecycler : IBuildingPart, ICokeHolder
    {
        public const int ScrapCost = 20;
        public const int IronPlateCost = 10;

        private string recyclerId = "";
        private int startingCoke;
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

        // --- ICokeHolder (the player's [E] and [I]) ---
        public string CokeHolderName => "Scrap Recycler";
        public string CokeHolderInSentence => "the Scrap Recycler";
        public bool HasCokeStore => true;
        public int CokeStock => Recycler.CokeStock;
        public int CokeRoom => Recycler.CokeRoom;
        public int AddCoke(int quantity) => Recycler.AddCoke(quantity);
        public int TakeCoke(int quantity) => Recycler.TakeCoke(quantity);

        public void Configure(string id, int coke)
        {
            recyclerId = id ?? "";
            startingCoke = coke;
        }

        public IBuildingPart CloneForInstance() =>
            new PlaceableScrapRecycler { recyclerId = recyclerId, startingCoke = startingCoke };

        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistry endpoints, Vector2Int cell)
        {
            if (endpoints == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(recyclerId))
            {
                recyclerId = "ScrapRecycler(" + cell.x + "," + cell.y + ")";
                _recycler = new ScrapRecycler(recyclerId, startingCoke);
            }

            endpoints.Register(cell, new ScrapRecyclerEndpoint(Recycler));
            return true;
        }
    }
}
