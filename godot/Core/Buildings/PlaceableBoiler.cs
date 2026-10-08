using GolemFactory.Compat;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A boiler: registers a <see cref="SteamBoiler"/> with the steam network, and publishes
    /// its fuel hatch on its cell so Coke can reach it in a golem's hold like any other good
    /// (see <see cref="BoilerFuelEndpoint"/>). Ported from Unity's sibling component (G2b).
    /// </summary>
    public sealed class PlaceableBoiler : IBuildingPart
    {
        public const int DefaultStartingCoke = 240;
        public const int ScrapCost = SteamNetwork.BoilerScrapCost;
        public const int IronPlateCost = SteamNetwork.BoilerIronPlateCost;

        private string boilerId;
        private int startingCoke = DefaultStartingCoke;

        public SteamBoiler Boiler { get; private set; }
        public string BoilerId => boilerId;
        public int StartingCoke => startingCoke;
        public BoilerFuelEndpoint FuelEndpoint { get; private set; }

        public void Configure(string id, int coke)
        {
            boilerId = id;
            startingCoke = coke;
        }

        public IBuildingPart CloneForInstance() =>
            new PlaceableBoiler { boilerId = boilerId, startingCoke = startingCoke };

        public bool RegisterWithSteamNetwork(SteamNetwork network, Vector2Int cell)
        {
            if (network == null)
            {
                return false;
            }

            // Cell-stamped when unauthored, so two placed boilers never share an id -- the
            // network keys boilers by it.
            if (string.IsNullOrEmpty(boilerId))
            {
                boilerId = "Boiler(" + cell.x + "," + cell.y + ")";
            }

            Boiler = network.RegisterBoiler(boilerId, cell, startingCoke);
            return Boiler != null;
        }

        /// <summary>
        /// Publishes the fuel hatch. Must be called AFTER <see cref="RegisterWithSteamNetwork"/>
        /// -- the endpoint wraps the boiler that call created.
        /// </summary>
        public bool RegisterAsSpatialEndpoint(SpatialEndpointRegistry endpoints, Vector2Int cell)
        {
            if (endpoints == null || Boiler == null)
            {
                return false;
            }

            FuelEndpoint = new BoilerFuelEndpoint(Boiler);
            endpoints.Register(cell, FuelEndpoint);
            return true;
        }

        public bool UnregisterFromSpatialEndpoints(SpatialEndpointRegistry endpoints, Vector2Int cell)
        {
            if (endpoints == null)
            {
                return false;
            }

            endpoints.Unregister(cell);
            FuelEndpoint = null;
            return true;
        }

        public bool UnregisterFromSteamNetwork(SteamNetwork network)
        {
            if (network == null || string.IsNullOrEmpty(boilerId))
            {
                return false;
            }

            Boiler = null;
            return network.RemoveBoiler(boilerId);
        }
    }
}
