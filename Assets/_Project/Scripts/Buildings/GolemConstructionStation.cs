using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Sibling component alongside PlaceableBuilding on its prefab (PlaceableBuilding is
    // sealed, so this can't subclass it). Spends Scrap/Brass -- finally consuming
    // ChassisDefinition.scrapCost/brassCost, which every authored chassis asset has carried
    // since M3 but nothing read until now -- to spawn a bare-chassis GolemEntity and hand it
    // straight to the Workbench so the player programs it exactly like any other golem.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class GolemConstructionStation : MonoBehaviour
    {
        [SerializeField] private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        [SerializeField] private GolemEntity golemPrefab;
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private SimulationClockRunner clockRunner;
        [SerializeField] private WorkbenchController workbenchController;
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";

        // Facing-based spatial routing. Optional in exactly the same way GolemEntity's own
        // spatialEndpointHolder is: leave these unassigned (Main.unity never wires them) and
        // constructed golems route purely by the ids on their appendage cards, as before.
        [SerializeField] private SpatialEndpointRegistryHolder spatialEndpointHolder;
        [SerializeField] private GridMapHolder gridMapHolder;
        [SerializeField] private Vector2 cellSize = new Vector2(1f, 0.5f);

        private int _nextGolemNumber = 1;

        public ChassisDefinition[] ChassisRoster => chassisRoster;

        // Test/bootstrap-friendly setup mirroring GolemEntity.Configure/ConfigureEconomy.
        public void Configure(
            ChassisDefinition[] roster, GolemEntity prefab, ConveyorSystemHolder conveyor,
            ResourceNodeRegistryHolder nodes, StorageBufferRegistryHolder buffers,
            SimulationClockRunner clock, WorkbenchController workbench, string bufferId)
        {
            chassisRoster = roster ?? new ChassisDefinition[0];
            golemPrefab = prefab;
            conveyorHolder = conveyor;
            nodeRegistryHolder = nodes;
            bufferRegistryHolder = buffers;
            clockRunner = clock;
            workbenchController = workbench;
            stockpileBufferId = bufferId;
        }

        /// <summary>
        /// Turns on facing-based routing for every golem this station builds. Split out from
        /// Configure for the same reason ConfigureEconomy is -- every existing call site keeps
        /// working by simply never calling it, and a station with no spatial registry keeps
        /// producing purely id-routed golems.
        /// </summary>
        public void ConfigureSpatial(
            SpatialEndpointRegistryHolder endpoints, GridMapHolder gridMap, Vector2 gridCellSize)
        {
            spatialEndpointHolder = endpoints;
            gridMapHolder = gridMap;
            cellSize = gridCellSize;
        }

        /// <summary>
        /// Which way this station points, and therefore the tile its golem steps out onto and
        /// the direction that golem starts facing. Read from the sibling PlaceableBuilding the
        /// player oriented with R at placement time.
        /// </summary>
        public Facing StationFacing
        {
            get
            {
                PlaceableBuilding building = GetComponent<PlaceableBuilding>();
                return building != null ? building.Facing : Facing.North;
            }
        }

        /// <summary>
        /// Current Scrap/Brass in the buffer this station spends from. Returns false when no
        /// buffer registry is wired, so the panel can say "stockpile unavailable" rather than
        /// silently printing a confident zero.
        /// </summary>
        public bool TryGetStockpile(out int scrapStock, out int brassStock)
        {
            scrapStock = 0;
            brassStock = 0;
            if (bufferRegistryHolder == null ||
                !bufferRegistryHolder.Registry.TryGetBuffer(stockpileBufferId, out StorageBuffer buffer))
            {
                return false;
            }

            scrapStock = buffer.GetQuantity(ItemType.Scrap);
            brassStock = buffer.GetQuantity(ItemType.Brass);
            return true;
        }

        /// <summary>
        /// Whether <see cref="TryConstructGolem"/> would currently succeed on cost grounds.
        /// Routed through ConstructionCostPolicy -- the same arithmetic the panel prints -- so
        /// the preview and the actual withdrawal can never disagree. A missing buffer registry
        /// reads as unaffordable, matching TryConstructGolem's own early-out.
        /// </summary>
        public bool CanAfford(ChassisDefinition chassis)
        {
            if (chassis == null)
            {
                return false;
            }

            int scrapStock;
            int brassStock;
            if (!TryGetStockpile(out scrapStock, out brassStock))
            {
                // A buffer that has never been deposited into doesn't exist yet, but a
                // genuinely free chassis is still affordable against it -- the same
                // zero-cost case StorageBufferRegistry.TryWithdrawScrapAndBrass guards.
                return bufferRegistryHolder != null &&
                       ConstructionCostPolicy.CanAfford(0, 0, chassis.scrapCost, chassis.brassCost);
            }

            return ConstructionCostPolicy.CanAfford(scrapStock, brassStock, chassis.scrapCost, chassis.brassCost);
        }

        // Withdraws the chassis's cost, instantiates a bare-chassis golem (no logic core or
        // appendages yet -- the player fits those via the Workbench, matching "feed
        // resources to build golem parts"), registers it with the clock so it sits Idle
        // until programmed, and retargets the Workbench onto it immediately.
        public bool TryConstructGolem(ChassisDefinition chassis, out GolemEntity golem)
        {
            golem = null;
            if (chassis == null || golemPrefab == null || bufferRegistryHolder == null)
            {
                return false;
            }

            if (!bufferRegistryHolder.Registry.TryWithdrawScrapAndBrass(stockpileBufferId, chassis.scrapCost, chassis.brassCost))
            {
                return false;
            }

            // Resolved BEFORE Instantiate, deliberately: GolemVisual caches its base position in
            // Awake (which Instantiate runs synchronously) and drives the idle bob from it, so a
            // transform moved afterwards gets dragged straight back. Spawning at the final
            // position sidesteps that entirely instead of adding a re-sync call.
            Vector2Int spawnCell;
            Facing spawnFacing;
            Vector3 spawnPosition;
            ResolveConstructedGolemPlacement(out spawnCell, out spawnFacing, out spawnPosition);

            golem = Instantiate(golemPrefab, spawnPosition, Quaternion.identity);
            golem.Configure($"PlayerGolem-{_nextGolemNumber:D3}", conveyorHolder);
            _nextGolemNumber++;
            golem.ConfigureEconomy(nodeRegistryHolder, bufferRegistryHolder);
            golem.Program.TryAssignChassis(chassis);

            if (spatialEndpointHolder != null)
            {
                golem.ConfigureSpatial(spatialEndpointHolder, spawnCell, spawnFacing);
            }

            GolemVisual visual = golem.GetComponent<GolemVisual>();
            if (visual != null)
            {
                visual.RefreshSpriteFromChassis();
            }

            if (clockRunner != null)
            {
                clockRunner.Register(golem);
            }

            if (workbenchController != null)
            {
                workbenchController.RetargetGolem(golem);
            }

            return true;
        }

        // Stands the new golem on a real tile and points it the same way the station points, so
        // "where you built it" IS its routing. With no spatial registry wired this degrades to
        // the pre-existing behaviour exactly: spawn on the station's own position, no facing.
        private void ResolveConstructedGolemPlacement(
            out Vector2Int spawnCell, out Facing spawnFacing, out Vector3 spawnPosition)
        {
            spawnFacing = StationFacing;

            if (spatialEndpointHolder == null)
            {
                spawnCell = Vector2Int.zero;
                spawnPosition = transform.position;
                return;
            }

            var converter = new GridCoordinateConverter(cellSize);
            Vector2Int stationCell = converter.WorldToCell(transform.position);

            GridMap map = gridMapHolder != null ? gridMapHolder.Map : null;
            spawnCell = GolemSpawnPlacement.ResolveSpawnCell(
                stationCell, spawnFacing, map != null ? (System.Func<Vector2Int, bool>)map.IsOccupied : null);

            // A golem whose sprite sits on one tile while its routing reads another is the exact
            // illegibility this whole pass exists to remove.
            spawnPosition = converter.CellToWorldCenter(spawnCell);
        }
    }
}
