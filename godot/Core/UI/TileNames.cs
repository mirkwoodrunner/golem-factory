using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.UI
{
    /// <summary>
    /// What the player calls the thing on a tile (docs/onboarding-feedback-design.md §4.3):
    /// "Crate (3,4)", "Freight Mast (9,1)", "Belt (0,-12)", "Scrap cart", "the floor at (3,2)".
    /// Named from what stands there and its cell, never from an id: ids are keys
    /// ("Belt(0,-12)#3") and a key on screen reads as a bug. Cells print as (x,y) in Core's
    /// frame, no space, as the crate headers do.
    /// </summary>
    public sealed class TileNames
    {
        private readonly SpatialEndpointRegistry _endpoints;
        private readonly FreightMastRegistry _masts;

        public TileNames(SpatialEndpointRegistry endpoints, FreightMastRegistry masts = null)
        {
            _endpoints = endpoints;
            _masts = masts;
        }

        /// <summary>The good a labelled crate on <paramref name="cell"/> takes, or null.</summary>
        public string Label(Vector2Int cell) => (EndpointAt(cell) as IFilteredEndpoint)?.AcceptedItemType;

        public static string Cell(Vector2Int cell) => "(" + cell.x + "," + cell.y + ")";

        /// <summary>The name of whatever stands on <paramref name="cell"/>.</summary>
        public string Name(Vector2Int cell)
        {
            IItemEndpoint endpoint = EndpointAt(cell);
            switch (endpoint)
            {
                case null:
                    return "the floor at " + Cell(cell);
                case ResourceNodeEndpoint node:
                    return node.Node != null ? ItemTiers.DisplayName(node.Node.ItemType) + " cart" : "a cart";
                case BeltSegmentEndpoint _:
                    return "Belt " + Cell(cell);
                case BoilerFuelEndpoint _:
                    return "Boiler " + Cell(cell);
                case SlagHeapEndpoint _:
                    return "Slag Heap " + Cell(cell);
                case ScrapRecyclerEndpoint _:
                    return "Scrap Recycler " + Cell(cell);
                case ClockTowerInputEndpoint tower:
                    return tower.DisplayName == "Clock Tower" ? "the Clock Tower" : tower.DisplayName;
            }

            if (_masts != null && _masts.Contains(cell))
            {
                return "Freight Mast " + Cell(cell);
            }
            string bufferId = (endpoint as StorageBufferEndpoint)?.Buffer?.BufferId
                              ?? (endpoint as FilteredBufferEndpoint)?.Buffer?.BufferId;
            if (bufferId == SandboxWorld.PostBufferId)
            {
                return "Pneumatic Post";
            }
            if (bufferId == Contracts.ContractBoard.OfficeBufferId)
            {
                return "the City Office";
            }
            return bufferId != null ? "Crate " + Cell(cell) : endpoint.DisplayName;
        }

        private IItemEndpoint EndpointAt(Vector2Int cell)
        {
            IItemEndpoint endpoint = null;
            _endpoints?.TryGetEndpoint(cell, out endpoint);
            return endpoint;
        }
    }
}
