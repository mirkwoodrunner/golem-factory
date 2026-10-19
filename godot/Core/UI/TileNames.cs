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
    /// "Crate (3,4)", "Freight Mast (9,1)", "Belt (0,-12)", "Scrap stall", "the floor at (3,2)".
    /// Named from what stands there and its cell, never from an id: ids are keys
    /// ("Belt(0,-12)#3") and a key on screen reads as a bug. Cells print as (x,y) in Core's
    /// frame, no space, as the crate headers do.
    /// </summary>
    public sealed class TileNames
    {
        private readonly SpatialEndpointRegistry _endpoints;
        private readonly FreightMastRegistry _masts;
        private readonly string _postBufferId;

        public TileNames(SpatialEndpointRegistry endpoints, FreightMastRegistry masts = null,
            string postBufferId = SandboxWorld.PostBufferId)
        {
            _endpoints = endpoints;
            _masts = masts;
            _postBufferId = postBufferId;
        }

        /// <summary>The name of whatever stands on <paramref name="cell"/>.</summary>
        public string Name(Vector2Int cell)
        {
            IItemEndpoint endpoint = null;
            _endpoints?.TryGetEndpoint(cell, out endpoint);
            return Describe(endpoint, cell, _masts != null && _masts.Contains(cell), _postBufferId);
        }

        /// <summary>The good a labelled crate on <paramref name="cell"/> takes, or null.</summary>
        public string Label(Vector2Int cell)
        {
            IItemEndpoint endpoint = null;
            _endpoints?.TryGetEndpoint(cell, out endpoint);
            return (endpoint as IFilteredEndpoint)?.AcceptedItemType;
        }

        public static string Cell(Vector2Int cell) => "(" + cell.x + "," + cell.y + ")";

        public static string Describe(IItemEndpoint endpoint, Vector2Int cell, bool isMast = false,
            string postBufferId = SandboxWorld.PostBufferId)
        {
            switch (endpoint)
            {
                case null:
                    return "the floor at " + Cell(cell);
                case ResourceNodeEndpoint node:
                    return node.Node != null ? ItemTiers.DisplayName(node.Node.ItemType) + " stall" : "a stall";
                case BeltSegmentEndpoint _:
                    return "Belt " + Cell(cell);
                case BoilerFuelEndpoint _:
                    return "Boiler " + Cell(cell);
                case SlagHeapEndpoint _:
                    return "Slag Heap " + Cell(cell);
                case ScrapRecyclerEndpoint _:
                    return "Scrap Recycler " + Cell(cell);
                case ClockTowerInputEndpoint _:
                    return "the Clock Tower";
            }

            if (isMast)
            {
                return "Freight Mast " + Cell(cell);
            }
            string bufferId = (endpoint as StorageBufferEndpoint)?.Buffer?.BufferId
                              ?? (endpoint as FilteredBufferEndpoint)?.Buffer?.BufferId;
            if (bufferId != null && bufferId == postBufferId)
            {
                return "Pneumatic Post";
            }
            return bufferId != null ? "Crate " + Cell(cell) : endpoint.DisplayName;
        }
    }
}
