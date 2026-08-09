using UnityEngine;
using GolemFactory.Steam;

namespace GolemFactory.Buildings
{
    // The Boiler (docs/progression-design.md §3.1). Sibling component alongside
    // PlaceableBuilding -- which is sealed, so every distinct building's behaviour is a sibling
    // rather than a subclass -- exactly the arrangement PlaceableDepot and PlaceableBelt use.
    //
    // The scene component owns none of the mechanic: it publishes itself into the SteamNetwork
    // on placement and withdraws on removal, and everything after that (reach, assignment, burn)
    // is plain C# in Steam/. Same division of labour as PlaceableBelt, which holds no items.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableBoiler : MonoBehaviour
    {
        // progression-design §9 Phase 1: the player starts beside "a Boiler holding 240 Coke".
        // That is the opening fuel budget the whole first phase is paced against -- 240 Coke is
        // 10 minutes of 4 golems, or 5 of 8 -- so it is the authored default rather than a
        // number the scene has to remember to set.
        public const int DefaultStartingCoke = 240;

        // §3.1's costs, RECORDED, not charged. Iron Plate does not exist as an ItemType until
        // §1.5 authors it, and §11 item 8 replaces PlaceableBuilding's scrapCost/brassCost pair
        // with a general item bundle in the same pass -- so wiring a cost now would mean
        // inventing an item and then re-expressing the cost twice. PlaceableBuilding.ScrapCost
        // is what BuildModeController actually charges today; when the bundle lands, these are
        // the two figures it takes.
        public const int ScrapCost = SteamNetwork.BoilerScrapCost;
        public const int IronPlateCost = SteamNetwork.BoilerIronPlateCost;

        [SerializeField] private string boilerId;
        [SerializeField] private int startingCoke = DefaultStartingCoke;

        /// <summary>The boiler this component published, or null before/after registration.</summary>
        public SteamBoiler Boiler { get; private set; }

        public string BoilerId => boilerId;
        public int StartingCoke => startingCoke;

        /// <summary>Test/bootstrap-friendly setter, matching the Configure(...) idiom.</summary>
        public void Configure(string id, int coke)
        {
            boilerId = id;
            startingCoke = coke;
        }

        /// <summary>
        /// Publishes this boiler into <paramref name="holder"/>'s network at
        /// <paramref name="cell"/>. Called by BuildModeController after placement, and by
        /// SandboxBootstrap for any boiler authored directly into the scene -- the same "one
        /// place owns 'placed in the world'" rule belts and depots follow, so a boiler can never
        /// exist visually but not logically.
        /// </summary>
        public bool RegisterWithSteamNetwork(SteamNetworkHolder holder, Vector2Int cell)
        {
            if (holder == null)
            {
                return false;
            }

            // Cell-stamped id when the prefab left it blank, exactly as BeltNetwork names its
            // segments "Belt(3,-2)#1": the player never authors a name, but a stall message, a
            // save file and the fuel gauge all need to tell two boilers apart.
            if (string.IsNullOrEmpty(boilerId))
            {
                boilerId = "Boiler(" + cell.x + "," + cell.y + ")";
            }

            Boiler = holder.Network.RegisterBoiler(boilerId, cell, startingCoke);
            return Boiler != null;
        }

        /// <summary>The fuel hatch this boiler published, or null before/after registration.</summary>
        public BoilerFuelEndpoint FuelEndpoint { get; private set; }

        /// <summary>
        /// Publishes the boiler's fuel hatch onto <paramref name="cell"/> so golems can
        /// <c>Push</c> Coke into it, exactly as PlaceableDepot and PlaceableClockTower publish
        /// theirs. Must be called AFTER <see cref="RegisterWithSteamNetwork"/> -- the endpoint
        /// wraps the <see cref="SteamBoiler"/> that call creates.
        ///
        /// <para>
        /// Separate from the steam registration above rather than folded into it, because the
        /// two answer to different systems: the SteamNetwork decides who gets powered, and the
        /// SpatialEndpointRegistry decides what a golem finds on the tile. A scene with no
        /// spatial routing at all (Main.unity) registers the first and not the second, and a
        /// boiler there keeps working exactly as it did.
        /// </para>
        /// </summary>
        public bool RegisterAsSpatialEndpoint(
            GolemFactory.World.SpatialEndpointRegistryHolder endpointHolder, Vector2Int cell)
        {
            if (endpointHolder == null || Boiler == null)
            {
                return false;
            }

            FuelEndpoint = new BoilerFuelEndpoint(Boiler);
            endpointHolder.Registry.Register(cell, FuelEndpoint);
            return true;
        }

        /// <summary>
        /// Withdraws the fuel hatch. A golem still facing the tile stalls NoTargetAtTile on its
        /// next push, which is the honest outcome of demolishing what it was delivering to.
        /// </summary>
        public bool UnregisterFromSpatialEndpoints(
            GolemFactory.World.SpatialEndpointRegistryHolder endpointHolder, Vector2Int cell)
        {
            if (endpointHolder == null)
            {
                return false;
            }

            endpointHolder.Registry.Unregister(cell);
            FuelEndpoint = null;
            return true;
        }

        /// <summary>
        /// Withdraws this boiler from the network. Everything it was powering loses steam on the
        /// next evaluation -- there is no grace period, which is the honest outcome of demolishing
        /// the thing making the steam.
        /// </summary>
        public bool UnregisterFromSteamNetwork(SteamNetworkHolder holder)
        {
            if (holder == null || string.IsNullOrEmpty(boilerId))
            {
                return false;
            }

            Boiler = null;
            return holder.Network.RemoveBoiler(boilerId);
        }
    }
}
