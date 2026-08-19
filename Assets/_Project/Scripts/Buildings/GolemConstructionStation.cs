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
        [SerializeField] private Vector2 cellSize = new Vector2(1f, 1f);

        // Steam power (docs/progression-design.md §3.1). Optional in exactly the same way the
        // spatial holder above is: a station with no steam network builds golems that are
        // EXEMPT from the NoSteam precondition, which is the state Sandbox.unity ships in today
        // (SandboxBootstrap.requireSteamPower is off until §1.5 gives Coke a source).
        [SerializeField] private GolemFactory.Steam.SteamNetworkHolder steamNetworkHolder;

        // The 2-extractor-per-node cap (docs/progression-design.md §3.2). Optional in the same
        // way the steam holder above is: a station with no cap registry builds golems that may
        // work any node they face, which is the state Main.unity is in permanently.
        [SerializeField] private NodeExtractorRegistryHolder nodeExtractorHolder;

        // §8's concurrent-golem cap. Optional in exactly the same additive way the steam
        // network and the extractor cap are: a station with no bay builds without a limit,
        // which is what Main.unity's demos and every pre-existing test do.
        [SerializeField] private AssemblyBayStructure assemblyBay;

        private int _nextGolemNumber = 1;

        /// <summary>
        /// Why the last <see cref="TryConstructGolem"/> refused, when the reason was not the
        /// cost. Empty otherwise. Exists because "you are out of bay slots" and "you cannot
        /// afford this" need different actions from the player, and the panel previously read
        /// every refusal as a shortfall -- which would have printed "needs 0 more Scrap" at a
        /// player whose stockpile was full.
        /// </summary>
        public string LastRefusalReason { get; private set; } = "";

        /// <summary>The bay this station's golems occupy, or null when uncapped.</summary>
        public AssemblyBayStructure AssemblyBay => assemblyBay;

        /// <summary>
        /// Subjects every golem this station builds to §8's bay cap. Split out for the same
        /// reason ConfigureSteam is -- a station that never gets one keeps building without a
        /// limit, which is the state every scene but Sandbox is in.
        /// </summary>
        public void ConfigureAssemblyBay(AssemblyBayStructure bay) => assemblyBay = bay;

        // §6's Freight Link. Optional like every holder above it.
        [SerializeField] private FreightMastRegistryHolder mastRegistryHolder;

        public void ConfigureFreight(FreightMastRegistryHolder masts) => mastRegistryHolder = masts;

        public ChassisDefinition[] ChassisRoster => chassisRoster;

        /// <summary>What this station instantiates. Exposed so the scene's bootstrap can
        /// hand a station the player just built the same prefab the authored one uses,
        /// rather than a second serialized copy of that reference.</summary>
        public GolemEntity GolemPrefab => golemPrefab;

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
        /// The asset half of a station's wiring: what it can build, and what it builds them
        /// from. Split out of <see cref="Configure"/> so a station that came into the world
        /// after the scene bootstrap ran (one the player placed, one a save rebuilt) can be
        /// given a roster without also being handed a fresh set of scene holders.
        /// </summary>
        public void ConfigureBuildRoster(ChassisDefinition[] roster, GolemEntity prefab)
        {
            chassisRoster = roster ?? new ChassisDefinition[0];
            golemPrefab = prefab;
        }

        /// <summary>
        /// The scene half: the registries, clock and Workbench a station needs, and nothing
        /// else. DELIBERATELY DOES NOT TOUCH the roster or the golem prefab -- those are asset
        /// references a scene-authored station already carries, and overwriting them from a
        /// sweep would let one unconfigured caller blank a station that was working.
        /// </summary>
        public void ConfigureSceneServices(
            ConveyorSystemHolder conveyor, ResourceNodeRegistryHolder nodes,
            StorageBufferRegistryHolder buffers, SimulationClockRunner clock,
            WorkbenchController workbench, string bufferId)
        {
            conveyorHolder = conveyor;
            nodeRegistryHolder = nodes;
            bufferRegistryHolder = buffers;
            clockRunner = clock;
            workbenchController = workbench;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        /// <summary>
        /// Whether this station could build anything if asked. False on a freshly placed
        /// prefab, whose roster and golem prefab are both empty -- which is exactly the state
        /// that made a player-built station a decorative box.
        /// </summary>
        public bool HasBuildRoster =>
            golemPrefab != null && chassisRoster != null && chassisRoster.Length > 0;

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
            LastRefusalReason = "";
            if (chassis == null || golemPrefab == null || bufferRegistryHolder == null)
            {
                return false;
            }

            // §8: Assembly Bays cap concurrent golems. CHECKED BEFORE THE COST, so a refused
            // build never touches the stockpile -- charging four goods and then discovering
            // there is nowhere to put the golem would be the partial-charge bug the bundle
            // withdrawal exists to prevent, one level up.
            if (assemblyBay != null && !assemblyBay.HasFreeSlot)
            {
                LastRefusalReason =
                    $"All {assemblyBay.MaxGolemSlots} assembly bays are full. " +
                    "Upgrade the bays, or dismantle a golem.";
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

            golem = SpawnGolem(chassis, NextGolemId(), spawnCell, spawnFacing, spawnPosition);

            // Takes the slot the check above reserved. Assigned after the golem exists rather
            // than before, so a spawn that somehow failed cannot leave a bay slot held by
            // nothing -- the bay counts golems, and there was no golem to count.
            if (assemblyBay != null)
            {
                assemblyBay.TryAssignGolem(golem);
            }

            if (workbenchController != null)
            {
                workbenchController.RetargetGolem(golem);
            }

            return true;
        }

        /// <summary>
        /// Rebuilds a golem a save file describes but the scene no longer contains, standing on
        /// the cell it was saved on and facing the way it was saved facing.
        ///
        /// <para>
        /// TWO THINGS THIS DELIBERATELY DOES NOT DO, both of which <see cref="TryConstructGolem"/>
        /// does. It does not charge the chassis cost -- the player paid for this golem in the
        /// session that built it, and charging again would make loading a game a tax, or fail
        /// outright for a player who has since spent their stockpile. And it does not retarget
        /// the Workbench: loading a factory of nine golems would otherwise leave the programming
        /// screen pointed at whichever one happened to be last in the file.
        /// </para>
        ///
        /// <para>
        /// Everything else is the same wiring in the same order, because it goes through the same
        /// <c>SpawnGolem</c>. The order is load-bearing and documented there.
        /// </para>
        /// </summary>
        public bool TryRespawnGolem(
            string golemId, ChassisDefinition chassis, Vector2Int savedCell, Facing savedFacing,
            out GolemEntity golem)
        {
            golem = null;
            if (chassis == null || golemPrefab == null || string.IsNullOrEmpty(golemId))
            {
                return false;
            }

            // Its own cell, not a resolved spawn tile: a loaded factory has to come back the
            // shape it was saved in. ResolveConstructedGolemPlacement's walk past blocked
            // neighbours is for a NEW golem stepping out of a station door, and applying it here
            // would shuffle a restored factory's golems off their tiles -- which under spatial
            // routing means quietly rerouting them.
            Vector3 position = spatialEndpointHolder != null
                ? new GridCoordinateConverter(cellSize).CellToWorldCenter(savedCell)
                : transform.position;

            golem = SpawnGolem(chassis, golemId, savedCell, savedFacing, position);

            // FORCED, not checked. A save describes a factory that was legal when it was built,
            // and refusing part of it on load would silently delete golems the player owns
            // because a cap moved -- the same reasoning that stops a load re-charging costs.
            if (assemblyBay != null)
            {
                assemblyBay.ForceAssignGolem(golem);
            }

            // Keep the counter ahead of every id restored from the file, or the next golem the
            // player builds is handed a name a loaded golem already answers to -- and golem ids
            // are the key for save entries, stall events and the spatial/steam registries, so a
            // duplicate is not cosmetic. Parsed rather than tracked as a count because the file
            // is the only thing that knows which numbers were used.
            ReserveGolemNumber(golemId);
            return true;
        }

        // The whole birth sequence, shared so a respawned golem cannot drift from a built one.
        private GolemEntity SpawnGolem(
            ChassisDefinition chassis, string golemId, Vector2Int spawnCell, Facing spawnFacing,
            Vector3 spawnPosition)
        {
            GolemEntity golem = Instantiate(golemPrefab, spawnPosition, Quaternion.identity);
            golem.Configure(golemId, conveyorHolder);
            golem.ConfigureEconomy(nodeRegistryHolder, bufferRegistryHolder);
            golem.Program.TryAssignChassis(chassis);

            // What tells the save system this golem is reconstructible. Set here rather than at
            // either call site so it is impossible to add a third way to build a golem that
            // forgets it -- a golem that forgot would simply vanish on the next load, silently,
            // which is the bug this whole change exists to fix.
            golem.MarkRuntimeSpawned();

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

            // §6's Freight Link, and it must come after ConfigureSpatial too -- the binding is
            // "the mast nearest THIS golem", so it is meaningless until the golem has a cell.
            // This is the placement §6 binds at: one mast, chosen once, kept.
            if (mastRegistryHolder != null)
            {
                golem.ConfigureFreight(mastRegistryHolder);
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

            return golem;
        }

        // "PlayerGolem-007" -> reserve 7, so the next build is 008. Anything that does not match
        // the pattern (a golem the player renamed, an id from another scene's bootstrap) is
        // ignored rather than guessed at: an unparseable id cannot collide with a generated one.
        private void ReserveGolemNumber(string golemId)
        {
            const string prefix = "PlayerGolem-";
            if (golemId == null || !golemId.StartsWith(prefix))
            {
                return;
            }

            if (int.TryParse(golemId.Substring(prefix.Length), out int number) &&
                number >= _nextGolemNumber)
            {
                _nextGolemNumber = number + 1;
            }
        }

        private string NextGolemId()
        {
            string id = $"PlayerGolem-{_nextGolemNumber:D3}";
            _nextGolemNumber++;
            return id;
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
