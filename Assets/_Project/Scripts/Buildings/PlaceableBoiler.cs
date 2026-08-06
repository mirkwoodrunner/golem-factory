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
