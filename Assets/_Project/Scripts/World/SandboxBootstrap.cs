using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Player;

namespace GolemFactory.World
{
    // Sandbox.unity's front-door bootstrap, directly analogous to Golems/BeltDemoBootstrap.cs
    // -- but there's no pre-programmed golem roster here: the world starts empty, and the
    // player builds/programs golems themselves via GolemConstructionStation + the Workbench.
    // Only responsible for seeding state that has to exist before the player can act on it:
    // the starting ResourceNodes (ids matched by hand-placed ResourceNodeMarkers already in
    // the scene) and starting the clock, so a player-programmed golem runs the instant Engage
    // Gears is pulled with no separate "start simulation" step.
    public sealed class SandboxBootstrap : MonoBehaviour
    {
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private SimulationClockRunner clockRunner;

        // The 2-extractor-per-node cap (docs/progression-design.md §3.2). Optional like every
        // other holder here: leave it unassigned and this falls back to finding the scene's
        // single registry, and if there isn't one the cap simply never applies -- exactly the
        // state Main.unity is in, since it never runs this bootstrap.
        [SerializeField] private NodeExtractorRegistryHolder nodeExtractorHolder;

        // --- Buffer backpressure (docs/progression-design.md §11 item 3, §5.3(c)) -----------
        // Optional, like every other holder here: leave it unassigned and this falls back to
        // finding the scene's single registry, and if there isn't one the capacity policy below
        // simply never applies. Main.unity never runs this bootstrap, so its demo economy keeps
        // the Unlimited default and is provably unaffected.
        [SerializeField] private Economy.StorageBufferRegistryHolder bufferRegistryHolder;

        // BOTH OF THESE ARE TUNING NUMBERS, NOT DERIVED ONES. The progression design gives no
        // figure for either; these are a first pass to be playtested, not a computed result.
        //
        // 100 per item type for ordinary production buffers: at stage 4 iron smelting produces
        // ~96 Slag/min (§5.3(c)), so a 100-cap Slag slot backs up in roughly a minute. That is
        // the right order of magnitude for "route it every cycle or the line stalls" without
        // making the early game fiddly, where a single golem cycle moves a handful of units.
        [SerializeField] private int productionBufferCapacityPerType = 100;

        // The player's stockpile stays UNLIMITED. It is the player's wallet, not a production
        // tile -- the construction station and the build menu spend from it, and the player's
        // own hand-harvesting pays into it. §10's soft-lock audit does not contemplate a capped
        // stockpile, so capping it would risk a soft-lock the design never sanctioned (a full
        // Scrap slot that stops the player banking the Scrap they need to build the golem that
        // would drain it). The id is BuildModeController/PlayerInteractor/GolemConstructionStation
        // /PlaceableDepot's shared default, taken from the code rather than guessed.
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";

        // The two hardcoded "ScrapBeltA"/"ScrapBeltB" segments this used to register are GONE.
        // They existed only because the Workbench's belt-facing appendage cards named those ids
        // and would otherwise always stall -- a scaffold for id routing. Belts are now placed by
        // the player (BeltNetwork registers a real segment and a spatial endpoint per cell), and
        // a spatially placed golem never consults an appendage's ids at all, so the scaffold was
        // routing items into two invisible lanes that nothing could see or reach.
        //
        // Nothing else depended on them: they were created and registered here and referenced
        // only by the provisional demonstration endpoint below, which is also gone. Main.unity's
        // own belts come from Golems/BeltDemoBootstrap and are untouched.

        // Wires the camera to follow the player -- CameraRigController.SetFollowTarget isn't
        // a [SerializeField] (it's set programmatically, same as every other Configure(...)
        // method in this project), so something has to call it once at scene start. This is
        // the scene's only front-door bootstrap, so it's the natural place, rather than adding
        // an editor-only serialized field to CameraRigController that Main.unity would never use.
        [SerializeField] private CameraRigController cameraRig;
        [SerializeField] private Transform playerTransform;

        // Same "no editor-only serialized field on the shared component" reasoning as
        // cameraRig above -- PlayerController.SetFloorBounds isn't a [SerializeField] so
        // Main.unity's player (which never calls this) is provably unaffected.
        [SerializeField] private GolemFactory.Player.PlayerController player;
        [SerializeField] private Grid grid;

        // Facing-based spatial routing (docs/digital-design.md, "Grid & Movement Mechanics").
        // Optional on purpose: leave it unassigned and every golem falls back to the bare-string
        // id routing that Main.unity's demos use, which is exactly what makes this additive.
        [SerializeField] private SpatialEndpointRegistryHolder spatialEndpointHolder;

        // Player-placeable belts, the golem construction station, and the routing highlight.
        // All optional in the same additive way everything else here is.
        [SerializeField] private BeltNetworkHolder beltNetworkHolder;
        [SerializeField] private GridMapHolder gridMapHolder;
        [SerializeField] private GolemFactory.Player.BuildModeController buildModeController;
        [SerializeField] private GolemFactory.Player.PlayerInteractor playerInteractor;
        [SerializeField] private RoutingFocusController routingFocusController;
        [SerializeField] private Sprite facingArrowSprite;
        [SerializeField] private Sprite routingTileSprite;

        // --- Steam power (docs/progression-design.md §3.1, §11 item 4) ----------------------
        // Optional like every other holder here. When assigned, boilers and steam pipes already
        // standing in the scene are published into the network and the clock ticks it, so the
        // Coke burn and the fuel gauge are LIVE -- but golems are only gated on power if the
        // switch below is on.
        [SerializeField] private GolemFactory.Steam.SteamNetworkHolder steamNetworkHolder;

        // THE SWITCH. Off, deliberately, and the reason is a hard dependency rather than
        // caution: COKE HAS NO SOURCE UNTIL §1.5 authors the coal node and the coking recipe.
        // Turned on today, the starting boiler would burn its 240 Coke down, every golem in the
        // scene would stall NoSteam, and there would be no way to make more -- a soft-lock in
        // the one playable scene, and precisely the "total blackout with no golems to recover"
        // row §10's audit clears only because the Hand-Crank Bench (§11 item 7, also unbuilt)
        // can always hand-crank Coke.
        //
        // This matches the state §1.2's buffer capacity is already in: built, wired, tested, and
        // not yet biting. Flipping it is a one-line change here plus the §1.5 content it waits
        // on -- see the §1.4 entry in docs/open-items.md.
        [SerializeField] private bool requireSteamPower;

        // --- The Clock Tower (docs/progression-design.md §7) --------------------------------
        // Optional like every other holder here. When assigned, any Clock Tower standing in the
        // scene publishes its input tile and the clock ticks the site, so stage progress accrues.
        // Unassigned, nothing happens at all -- Main.unity has no tower and never runs this.
        [SerializeField] private GolemFactory.ClockTower.ClockTowerSiteHolder clockTowerSiteHolder;

        // --- The two §8 readouts ------------------------------------------------------------
        // Both live on WorkbenchCanvas.prefab and both need a holder that lives on
        // ManagerHolders.prefab. A PREFAB CANNOT HOLD A REFERENCE INTO ANOTHER PREFAB -- it
        // resolves to null on instantiation -- so the link is made here at runtime rather than
        // authored, which is exactly what the Configure(...) idiom exists for.
        [SerializeField] private GolemFactory.UI.SteamFuelGaugeView steamFuelGaugeView;
        [SerializeField] private GolemFactory.UI.ClockTowerPanelView clockTowerPanelView;
        [SerializeField] private GolemFactory.UI.HandCrankPanelView handCrankPanelView;

        private void Start()
        {
            RegisterStartingNodes();

            clockRunner.Register(conveyorHolder.System);
            clockRunner.Play();

            if (cameraRig != null && playerTransform != null)
            {
                cameraRig.SetFollowTarget(playerTransform);
            }

            if (player != null && grid != null)
            {
                player.SetFloorBounds(new GridCoordinateConverter(grid.cellSize), FloorLayout.HalfExtent);
            }

            ApplyBufferCapacityPolicy();
            RegisterSpatialEndpoints();
            RegisterSteamNetwork();
            RegisterClockTower();
            RegisterHandCrankBenches();
            WireSpatialGameplay();
            WireHudReadouts();
        }

        /// <summary>
        /// Wires every Hand-Crank Bench in the scene to the player's stockpile and the clock
        /// (docs/progression-design.md §11 item 7). Same sweep idiom as the boiler and tower
        /// passes, so a bench the player builds later configures itself the same way.
        ///
        /// <para>
        /// DELIBERATELY NOT GATED ON <c>requireSteamPower</c>, and never handed a steam network.
        /// §10 clears the "total blackout with no golems to recover" row only because the bench
        /// runs R1 without steam, so the player can always hand-crank coke back from nothing.
        /// </para>
        /// </summary>
        private void RegisterHandCrankBenches()
        {
            Economy.StorageBufferRegistryHolder buffers = bufferRegistryHolder != null
                ? bufferRegistryHolder
                : FindAnyObjectByType<Economy.StorageBufferRegistryHolder>(FindObjectsInactive.Include);

            GolemFactory.Buildings.HandCrankBench[] benches =
                FindObjectsByType<GolemFactory.Buildings.HandCrankBench>(FindObjectsInactive.Exclude);

            for (int i = 0; i < benches.Length; i++)
            {
                // Recipes come from the prefab; only the stockpile has to be resolved per scene,
                // because a prefab cannot hold a reference to a scene object.
                if (buffers != null)
                {
                    benches[i].ConfigureStockpile(buffers, stockpileBufferId);
                }

                if (clockRunner != null)
                {
                    clockRunner.Register(benches[i]);
                }
            }
        }

        /// <summary>
        /// Hands the two §8 readouts the holders they report on. Same find-it-in-the-scene
        /// fallback as everything else here, so the panels work whether or not an Inspector pass
        /// has assigned them, and a scene with neither view simply does nothing.
        ///
        /// <para>
        /// §12 calls these load-bearing rather than polish: steam upkeep ends up ~41 % of the
        /// endgame factory, and a running cost the player cannot see is one they cannot plan
        /// against.
        /// </para>
        /// </summary>
        private void WireHudReadouts()
        {
            GolemFactory.UI.SteamFuelGaugeView gauge = steamFuelGaugeView != null
                ? steamFuelGaugeView
                : FindAnyObjectByType<GolemFactory.UI.SteamFuelGaugeView>(FindObjectsInactive.Include);
            if (gauge != null && steamNetworkHolder != null)
            {
                gauge.Configure(steamNetworkHolder);
            }

            GolemFactory.UI.ClockTowerPanelView panel = clockTowerPanelView != null
                ? clockTowerPanelView
                : FindAnyObjectByType<GolemFactory.UI.ClockTowerPanelView>(FindObjectsInactive.Include);
            if (panel != null && clockTowerSiteHolder != null)
            {
                panel.Configure(clockTowerSiteHolder);
            }

            GolemFactory.UI.HandCrankPanelView crank = handCrankPanelView != null
                ? handCrankPanelView
                : FindAnyObjectByType<GolemFactory.UI.HandCrankPanelView>(FindObjectsInactive.Include);
            if (crank != null && playerInteractor != null)
            {
                Economy.StorageBufferRegistryHolder buffers = bufferRegistryHolder != null
                    ? bufferRegistryHolder
                    : FindAnyObjectByType<Economy.StorageBufferRegistryHolder>(FindObjectsInactive.Include);
                crank.Configure(playerInteractor, buffers, stockpileBufferId);
            }
        }

        /// <summary>
        /// Publishes every Clock Tower already standing in the scene onto its cell and starts
        /// ticking the site -- the same sweep-the-scene idiom the boiler, node-marker and
        /// construction-station passes use.
        ///
        /// <para>
        /// Registered with the clock for the reason <c>ClockTowerSiteHolder</c> documents: §7's
        /// rates are per unit of WORK DONE, so a factory run at 4x must reach a stage's nominal
        /// duration after the same number of ticks, not the same number of wall seconds.
        /// </para>
        /// </summary>
        private void RegisterClockTower()
        {
            if (clockTowerSiteHolder == null)
            {
                return;
            }

            if (grid != null && spatialEndpointHolder != null)
            {
                var converter = new GridCoordinateConverter(grid.cellSize);
                GolemFactory.Buildings.PlaceableClockTower[] towers =
                    FindObjectsByType<GolemFactory.Buildings.PlaceableClockTower>(FindObjectsInactive.Exclude);
                for (int i = 0; i < towers.Length; i++)
                {
                    towers[i].RegisterAsSpatialEndpoint(
                        spatialEndpointHolder,
                        clockTowerSiteHolder,
                        converter.WorldToCell(towers[i].transform.position));
                }
            }

            // Ticked whether or not a tower has been placed yet. An unbuilt tower has no stages
            // running and accrues nothing, so this is inert until there is something to measure
            // -- exactly as an idle boiler burns nothing.
            if (clockRunner != null)
            {
                clockRunner.Register(clockTowerSiteHolder);
            }
        }

        /// <summary>
        /// The world's raw deposits (docs/progression-design.md §5.1).
        ///
        /// <para>
        /// EVERY NODE IS INFINITE, including AetherNode, which used to hold a finite
        /// <c>startingAetherQuantity</c>. §5.1 is explicit: "All nodes are infinite but capped
        /// at 2 extractors (§3.2), so scarcity is *access*, not depletion -- which cannot
        /// soft-lock." A finite Aether node is §10's node-depletion row: Aether is the sole
        /// input of R12, which is the sole route to Aether Cells, Regulators, Conduits and
        /// therefore the Chronometer Core and the win condition. Draining it would end the run
        /// with nothing to point at.
        /// </para>
        ///
        /// <para>
        /// BRASSNODE IS GONE. §5.1: Brass is manufactured (R7, 2 Copper Ingot + 1 Zinc Ingot ->
        /// 3 Brass) from Phase 4 on and is never dug up. It was the game's one un-earned Tier-2
        /// good, and leaving it in would have made the entire copper/zinc line -- two node
        /// types, two smelting recipes and the alloying recipe that is Brass's only source --
        /// optional content next to a hole in the ground.
        /// </para>
        ///
        /// <para>
        /// Coal, Copper Ore and Zinc Ore are added: R1 (coking) is what finally gives Coke a
        /// source, and Coke is what <c>requireSteamPower</c> has been waiting on.
        /// </para>
        ///
        /// <para>
        /// MISSING ART: there is no sprite for a coal seam, a copper vein or a zinc vein, and
        /// the ResourceNodeMarkers that would place them in Sandbox.unity are scene objects, not
        /// code. Registering a node here makes it real to the simulation and reachable by id;
        /// putting one on the floor where the player can see it is an Editor pass
        /// (Tools/Art/generate_placeholder_art.py plus a texture import) that a text diff
        /// cannot do.
        /// </para>
        /// </summary>
        private void RegisterStartingNodes()
        {
            ResourceNodeRegistry registry = nodeRegistryHolder.Registry;

            // Scrap is directly harvestable by the player's own Interact as well as by a
            // golem's ExtractFromNode, so a fresh save can always afford the 12-Scrap
            // Clockwork Scavenger without first wiring anything -- §10's "all Scrap spent on
            // belts/floor" row depends on that staying true.
            registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            registry.Register(new ResourceNode("CoalNode", ItemType.Coal));
            registry.Register(new ResourceNode("CopperOreNode", ItemType.CopperOre));
            registry.Register(new ResourceNode("ZincOreNode", ItemType.ZincOre));
            registry.Register(new ResourceNode("AetherNode", ItemType.Aether));
        }

        /// <summary>
        /// Publishes every Boiler and Steam Pipe already standing in the scene into the steam
        /// network and starts ticking it, the same sweep-the-scene idiom the ResourceNodeMarker
        /// and GolemConstructionStation passes use. Runs whether or not
        /// <c>requireSteamPower</c> is on: the burn accounting and the fuel gauge are honest
        /// readouts of the boilers that exist, and a boiler with nothing drawing on it burns
        /// nothing anyway (§3.1).
        /// </summary>
        private void RegisterSteamNetwork()
        {
            if (steamNetworkHolder == null || grid == null)
            {
                return;
            }

            var converter = new GridCoordinateConverter(grid.cellSize);

            // Boilers first, then pipes -- not for correctness (SteamNetwork re-derives reach
            // from scratch on any topology change, so order cannot matter) but because it makes
            // a mid-registration breakpoint read the way the player would expect.
            GolemFactory.Buildings.PlaceableBoiler[] boilers =
                FindObjectsByType<GolemFactory.Buildings.PlaceableBoiler>(FindObjectsInactive.Exclude);
            for (int i = 0; i < boilers.Length; i++)
            {
                Vector2Int boilerCell = converter.WorldToCell(boilers[i].transform.position);
                boilers[i].RegisterWithSteamNetwork(steamNetworkHolder, boilerCell);

                // ...and its fuel hatch, so golems can Push Coke into it. Without this a boiler
                // is a sealed tank holding whatever it was built with, which makes §9's arc
                // unreachable past the opening 240 Coke -- see Steam/BoilerFuelEndpoint.
                boilers[i].RegisterAsSpatialEndpoint(spatialEndpointHolder, boilerCell);
            }

            GolemFactory.Buildings.PlaceableSteamPipe[] pipes =
                FindObjectsByType<GolemFactory.Buildings.PlaceableSteamPipe>(FindObjectsInactive.Exclude);
            for (int i = 0; i < pipes.Length; i++)
            {
                pipes[i].RegisterWithSteamNetwork(
                    steamNetworkHolder, converter.WorldToCell(pipes[i].transform.position));
            }

            // Registered with the clock so the burn happens on simulation ticks -- Coke is spent
            // per unit of work done, so it has to follow Play/Pause and the speed multiplier.
            if (clockRunner != null)
            {
                clockRunner.Register(steamNetworkHolder);
            }
        }

        /// <summary>
        /// Turns on real per-item-type backpressure for this scene: ordinary production buffers
        /// get a finite cap, the player's stockpile stays unlimited.
        ///
        /// Runs BEFORE RegisterSpatialEndpoints and before any golem or player deposit, because
        /// a buffer takes its capacity at creation and the stockpile must be created uncapped
        /// rather than picking up the default from whichever deposit happens to touch it first.
        /// </summary>
        private void ApplyBufferCapacityPolicy()
        {
            Economy.StorageBufferRegistryHolder holder = bufferRegistryHolder;
            if (holder == null)
            {
                // Same idiom as the ResourceNodeMarker/GolemConstructionStation sweeps below:
                // find it in the scene rather than requiring an Inspector pass that a text-only
                // change cannot perform. An explicit assignment still wins when one is made.
                holder = FindAnyObjectByType<Economy.StorageBufferRegistryHolder>(FindObjectsInactive.Include);
            }

            if (holder == null)
            {
                return;
            }

            holder.Registry.DefaultCapacityPerType = productionBufferCapacityPerType;

            if (!string.IsNullOrEmpty(stockpileBufferId))
            {
                holder.Registry.SetCapacity(stockpileBufferId, Economy.StorageBuffer.Unlimited);
            }
        }

        // Hands the spatial layer to the systems the player actually drives. Every one of these
        // is a Configure*(...) call rather than Inspector-only state, so this scene can turn
        // facing-based routing on without Main.unity (which never runs this) being touched.
        private void WireSpatialGameplay()
        {
            var cellSize = grid != null ? (Vector2)grid.cellSize : new Vector2(1f, 1f);

            // Same find-it-in-the-scene fallback as ApplyBufferCapacityPolicy, and for the same
            // reason: adding a holder to ManagerHolders.prefab is an Editor pass, and until one
            // happens this keeps the cap wired if a holder is present and inert if it isn't.
            NodeExtractorRegistryHolder cap = nodeExtractorHolder != null
                ? nodeExtractorHolder
                : FindAnyObjectByType<NodeExtractorRegistryHolder>(FindObjectsInactive.Include);

            if (buildModeController != null && beltNetworkHolder != null)
            {
                buildModeController.ConfigureBelts(beltNetworkHolder, spatialEndpointHolder, conveyorHolder);
            }

            // Placement wiring is UNCONDITIONAL: a boiler or pipe the player builds should join
            // the network and show up on the fuel gauge whether or not golems are gated on it.
            // requireSteamPower governs only whether a golem STALLS without steam.
            if (buildModeController != null && steamNetworkHolder != null)
            {
                buildModeController.ConfigureSteam(steamNetworkHolder);
            }

            if (playerInteractor != null)
            {
                playerInteractor.ConfigureBuildMode(buildModeController);
                playerInteractor.ConfigureGolemPlacement(gridMapHolder, cellSize);
            }

            // Every station in the scene, not just one: stations are themselves placeable, so
            // the player can build more of them, and each has to produce spatially placed
            // golems. Newly built stations configure themselves via PlaceableBuilding's own
            // wiring path -- see GolemConstructionStation.ConfigureSpatial.
            GolemFactory.Buildings.GolemConstructionStation[] stations =
                FindObjectsByType<GolemFactory.Buildings.GolemConstructionStation>(FindObjectsInactive.Include);
            for (int i = 0; i < stations.Length; i++)
            {
                stations[i].ConfigureSpatial(spatialEndpointHolder, gridMapHolder, cellSize);

                // THE ONE LINE THE SWITCH GATES. Handing a station the steam network is what
                // makes every golem it builds steam-gated; withholding it leaves them exempt,
                // which is how this scene ships until §1.5 gives Coke a source.
                if (requireSteamPower && steamNetworkHolder != null)
                {
                    stations[i].ConfigureSteam(steamNetworkHolder);
                }

                // NOT behind the switch. §3.2's cap consumes nothing and cannot soft-lock: a
                // refused golem stalls naming the seam, and the fix is to walk it to another
                // node. It is on from the moment there is a registry to hold the claims.
                if (cap != null)
                {
                    stations[i].ConfigureNodeExtractorCap(cap);
                }
            }

            if (routingFocusController != null && playerTransform != null)
            {
                routingFocusController.Configure(
                    playerTransform, facingArrowSprite, routingTileSprite, cellSize, 3.5f);
                routingFocusController.Rescan();
            }
        }

        // Publishes the world's item-bearing things onto the cells they physically occupy, so a
        // golem can route by facing instead of by the bare-string ids baked into its appendage
        // cards. Everything here is additive: with spatialEndpointHolder unassigned this method
        // does nothing at all and the scene behaves exactly as it did before.
        private void RegisterSpatialEndpoints()
        {
            if (spatialEndpointHolder == null || grid == null)
            {
                return;
            }

            var converter = new GridCoordinateConverter(grid.cellSize);

            // The three hand-placed ResourceNodeMarkers already carry both a nodeId and a world
            // position; each one resolves its own cell (it owns the Transform) and publishes its
            // backing ResourceNode there.
            //
            // Nodes are the only endpoints seeded here now. Belts are placed by the player and
            // register themselves through BeltNetwork; the buffer a golem loads into is a placed
            // Depot. The old provisional "publish ScrapBeltA two tiles north of ScrapNode" hack
            // is gone with the hardcoded segments it depended on.
            ResourceNodeMarker[] markers = FindObjectsByType<ResourceNodeMarker>(FindObjectsInactive.Exclude);
            for (int i = 0; i < markers.Length; i++)
            {
                markers[i].RegisterAsSpatialEndpoint(spatialEndpointHolder, converter);
            }
        }
    }
}
