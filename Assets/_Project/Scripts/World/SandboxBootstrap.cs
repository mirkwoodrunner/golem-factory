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

        // Wires the camera to follow the player -- CameraRigController.SetFollowTarget isn't
        // a [SerializeField] (it's set programmatically, same as every other Configure(...)
        // method in this project), so something has to call it once at scene start. This is
        // the scene's only front-door bootstrap, so it's the natural place, rather than adding
        // an editor-only serialized field to CameraRigController that Main.unity would never use.
        [SerializeField] private CameraRigController cameraRig;
        [SerializeField] private Transform playerTransform;

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
        }
    }
}
