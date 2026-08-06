using UnityEngine;
using GolemFactory.Steam;

namespace GolemFactory.Buildings
{
    // The Steam Pipe (docs/progression-design.md §3.1). One cell, no facing, no contents, no
    // capacity -- it is a length of pipe. Sibling component alongside PlaceableBuilding, same as
    // PlaceableBoiler/PlaceableDepot/PlaceableBelt.
    //
    // Deliberately far thinner than PlaceableBelt, and that is the §11-item-4 correction made
    // physical: a belt has a direction, a segment, a capacity and a link rule, while a pipe has
    // a cell and nothing else. Everything that makes a pipe useful is the undirected flood fill
    // in Steam/SteamPipeRules, which never asks a pipe which way it points.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableSteamPipe : MonoBehaviour
    {
        // §3.1's cost, RECORDED not charged -- see PlaceableBoiler for why (Iron Plate is a
        // §1.5 item, and §11 item 8 changes how costs are expressed at the same time).
        public const int IronPlateCost = SteamNetwork.SteamPipeIronPlateCost;

        /// <summary>The cell this pipe published, valid only while registered.</summary>
        public Vector2Int Cell { get; private set; }

        public bool IsRegistered { get; private set; }

        public bool RegisterWithSteamNetwork(SteamNetworkHolder holder, Vector2Int cell)
        {
            if (holder == null)
            {
                return false;
            }

            Cell = cell;
            IsRegistered = true;
            // AddPipe returns false on a cell that already has pipe; treat that as registered
            // anyway, since the network's state is what the caller asked for either way.
            holder.Network.AddPipe(cell);
            return true;
        }

        /// <summary>
        /// Pulls this pipe out of the network. Everything downstream of the gap loses steam
        /// immediately -- SteamNetwork re-derives reach wholesale rather than caching it, which
        /// is what makes progression-design §9's Phase 6 beat (a stage bar freezing because one
        /// extractor lost steam to a paved-over pipe) actually happen.
        /// </summary>
        public bool UnregisterFromSteamNetwork(SteamNetworkHolder holder)
        {
            if (holder == null || !IsRegistered)
            {
                return false;
            }

            IsRegistered = false;
            return holder.Network.RemovePipe(Cell);
        }
    }
}
