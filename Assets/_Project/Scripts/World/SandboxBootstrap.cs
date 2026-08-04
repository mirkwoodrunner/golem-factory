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
        [SerializeField] private int startingAetherQuantity = 40;

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

        private void Start()
        {
            // ScrapNode/BrassNode are directly harvestable (both by the player's own
            // Interact and by a player-programmed golem's ExtractFromNode step) so a fresh
            // save can afford every chassis's scrapCost/brassCost without first wiring a
            // refining chain -- AssemblyBayStructure's tier/refine loop stays available for
            // later, it's just not required to bootstrap the very first golem.
            nodeRegistryHolder.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            nodeRegistryHolder.Registry.Register(new ResourceNode("BrassNode", ItemType.Brass));
            nodeRegistryHolder.Registry.Register(new ResourceNode("AetherNode", ItemType.Aether, startingAetherQuantity));

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

            RegisterSpatialEndpoints();
            WireSpatialGameplay();
        }

        // Hands the spatial layer to the systems the player actually drives. Every one of these
        // is a Configure*(...) call rather than Inspector-only state, so this scene can turn
        // facing-based routing on without Main.unity (which never runs this) being touched.
        private void WireSpatialGameplay()
        {
            var cellSize = grid != null ? (Vector2)grid.cellSize : new Vector2(1f, 0.5f);

            if (buildModeController != null && beltNetworkHolder != null)
            {
                buildModeController.ConfigureBelts(beltNetworkHolder, spatialEndpointHolder, conveyorHolder);
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
