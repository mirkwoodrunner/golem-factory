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

        // Sandbox registered a ConveyorSystem but never registered a single BeltSegment, so
        // every belt-facing appendage the Workbench offers (ExtractScrap -> a belt,
        // LoadIntoScrapBuffer <- a belt) could only ever stall: TryEnqueue/TryDequeueHead on an
        // unknown segment id returns false. The ids deliberately match the ones
        // Golems/BeltDemoBootstrap registers in Main.unity so ONE set of authored
        // AppendageActionDefinition assets drives both scenes.
        [SerializeField] private int beltSegmentLengthTicks = 5;

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

            var scrapBeltA = new BeltSegment("ScrapBeltA", beltSegmentLengthTicks);
            var scrapBeltB = new BeltSegment("ScrapBeltB", beltSegmentLengthTicks);
            scrapBeltA.Next = scrapBeltB;
            conveyorHolder.System.Register(scrapBeltA);
            conveyorHolder.System.Register(scrapBeltB);

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

            RegisterSpatialEndpoints(scrapBeltA);
        }

        // Publishes the world's item-bearing things onto the cells they physically occupy, so a
        // golem can route by facing instead of by the bare-string ids baked into its appendage
        // cards. Everything here is additive: with spatialEndpointHolder unassigned this method
        // does nothing at all and the scene behaves exactly as it did before.
        private void RegisterSpatialEndpoints(BeltSegment scrapBeltA)
        {
            if (spatialEndpointHolder == null || grid == null)
            {
                return;
            }

            var converter = new GridCoordinateConverter(grid.cellSize);

            // The three hand-placed ResourceNodeMarkers already carry both a nodeId and a world
            // position; each one resolves its own cell (it owns the Transform) and publishes its
            // backing ResourceNode there.
            ResourceNodeMarker[] markers = FindObjectsByType<ResourceNodeMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            ResourceNodeMarker scrapMarker = null;
            for (int i = 0; i < markers.Length; i++)
            {
                markers[i].RegisterAsSpatialEndpoint(spatialEndpointHolder, converter);
                if (markers[i].NodeId == "ScrapNode")
                {
                    scrapMarker = markers[i];
                }
            }

            // Belts have no scene presence yet -- BeltSegmentVisual exists but nothing in either
            // scene instantiates one, and player-placeable belts are a separate pass. So the
            // demonstration chain is anchored off the scrap node rather than authored by hand:
            // ScrapBeltA is published two tiles north of ScrapNode, leaving exactly one tile
            // between them for a north-facing golem to stand on and pull node -> belt.
            // Provisional: replace with the belt's own placed cell once belts are placeable.
            if (scrapMarker != null && scrapMarker.IsSpatiallyRegistered && scrapBeltA != null)
            {
                Vector2Int beltCell = scrapMarker.SpatialCell + new Vector2Int(0, 2);
                spatialEndpointHolder.Registry.Register(beltCell, new BeltSegmentEndpoint(scrapBeltA));
            }
        }
    }
}
