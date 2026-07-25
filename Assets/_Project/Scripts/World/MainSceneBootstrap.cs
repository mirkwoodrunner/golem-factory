using UnityEngine;
using GolemFactory.Player;

namespace GolemFactory.World
{
    // Main.unity's only player-facing bootstrap: everything else the scene needs
    // (golem programs, clock start) is already seeded by BeltDemoBootstrap/
    // TriggerDemoBootstrap/AssemblyLineDemoBootstrap. This just wires
    // CameraRigController.SetFollowTarget once at Start(), mirroring
    // SandboxBootstrap's identical one-liner for the same reason -- SetFollowTarget
    // isn't a [SerializeField] (same Configure()-not-Inspector idiom as the rest of
    // the project), so something has to call it.
    public sealed class MainSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private CameraRigController cameraRig;
        [SerializeField] private Transform playerTransform;

        private void Start()
        {
            if (cameraRig != null && playerTransform != null)
            {
                cameraRig.SetFollowTarget(playerTransform);
            }
        }
    }
}
