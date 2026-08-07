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
    // sealed, so this can't subclass it). Spends ChassisDefinition.cost -- an item bundle
    // since §1.5 (docs/progression-design.md §6, §11 item 8), replacing the Scrap/Brass pair
    // that could not express a single §6 cost from the Presser on -- to spawn a bare-chassis
    // GolemEntity and hand it straight to the Workbench so the player programs it exactly like
    // any other golem.
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

        // Steam power (docs/progression-design.md §3.1). Optional in exactly the same way the
        // spatial holder above is: a station with no steam network builds golems that are
        // EXEMPT from the NoSteam precondition, which is the state Sandbox.unity ships in today
        // (SandboxBootstrap.requireSteamPower is off until §1.5 gives Coke a source).
        [SerializeField] private GolemFactory.Steam.SteamNetworkHolder steamNetworkHolder;

        // The 2-extractor-per-node cap (docs/progression-design.md §3.2). Optional in the same
        // way the steam holder above is: a station with no cap registry builds golems that may
        // work any node they face, which is the state Main.unity is in permanently.
        [SerializeField] private NodeExtractorRegistryHolder nodeExtractorHolder;

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
        /// Steam-gates every golem this station builds from here on. Split out for the same
        /// reason ConfigureSpatial is -- a station that never gets one keeps producing golems
        /// that run without a boiler, which is what the whole opt-in fork buys.
        /// </summary>
        public void ConfigureSteam(GolemFactory.Steam.SteamNetworkHolder steam) =>
            steamNetworkHolder = steam;

        /// <summary>
        /// Subjects every golem this station builds to §3.2's two-extractor-per-node cap.
        /// Split out for the same reason ConfigureSteam is.
        ///
        /// <para>
        /// UNLIKE steam, this is NOT behind a scene switch. The cap needs no consumable and
        /// cannot soft-lock: a refused golem stalls naming the seam and is fixed by walking it
        /// somewhere else, which is a five-second player action rather than a dependency on
        /// content that does not exist yet.
        /// </para>
        /// </summary>
        public void ConfigureNodeExtractorCap(NodeExtractorRegistryHolder cap) =>
            nodeExtractorHolder = cap;

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
        ///
        /// <para>
        /// Kept on the Scrap/Brass pair even though costs are now bundles: this feeds the
        /// panel's one-line "what's in the vault" readout, which is a summary of the two goods
        /// the player hand-harvests, not a cost preview. Affordability goes through
        /// <see cref="StockOf"/> instead.
        /// </para>
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
        /// How much of one good the station's stockpile holds -- the stock reader
        /// ConstructionCostPolicy's bundle arithmetic takes. Zero when nothing is wired, which
        /// makes an unwired station read as "you have none of anything", the same answer the
        /// withdrawal would give.
        /// </summary>
        public int StockOf(string itemType) =>
            bufferRegistryHolder == null
                ? 0
                : bufferRegistryHolder.Registry.GetQuantity(stockpileBufferId, itemType);

        /// <summary>
        /// Whether <see cref="TryConstructGolem"/> would currently succeed on cost grounds.
        /// Routed through ConstructionCostPolicy -- the same arithmetic the panel prints -- so
        /// the preview and the actual withdrawal can never disagree. A missing buffer registry
        /// reads as unaffordable, matching TryConstructGolem's own early-out.
        /// </summary>
        public bool CanAfford(ChassisDefinition chassis)
        {
            if (chassis == null || bufferRegistryHolder == null)
            {
                return false;
            }

            // A buffer that has never been deposited into doesn't exist yet, but a genuinely
            // free chassis is still affordable against it -- the same zero-cost case
            // StorageBufferRegistry.TryWithdrawBundle guards, and StockOf answers 0 for it
            // rather than failing.
            return ConstructionCostPolicy.CanAfford(StockOf, chassis.cost);
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

            // Atomic with a full refund on shortfall -- a chassis is the most expensive thing
            // the player buys and a partial charge would take four goods and hand back nothing.
            if (!bufferRegistryHolder.Registry.TryWithdrawBundle(stockpileBufferId, chassis.cost))
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

            // After ConfigureSpatial, deliberately: the steam grid is keyed by cell, and
            // ConfigureSteam registers the golem at whatever cell it is standing on. Wiring it
            // first would register the golem at (0,0) and leave it there.
            if (steamNetworkHolder != null)
            {
                golem.ConfigureSteam(steamNetworkHolder);
            }

            // After ConfigureSpatial for the same reason: the cap claim is filed against the
            // node behind the golem, which is only meaningful once it is standing somewhere.
            if (nodeExtractorHolder != null)
            {
                golem.ConfigureNodeExtractorCap(nodeExtractorHolder);
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
