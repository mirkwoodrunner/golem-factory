using UnityEngine;

namespace GolemFactory.Golems
{
    // Wires the M2 hardcoded demo program onto a GolemEntity and starts the clock so the
    // milestone is playable without pre-authored .asset files (see HardcodedDemoProgram).
    // A real play/pause/speed HUD and authored ScriptableObject programs replace this at M3/M8.
    public sealed class GolemDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        [SerializeField] private SimulationClockRunner clockRunner;

        private void Start()
        {
            GolemProgram demo = HardcodedDemoProgram.ExtractAndDeposit();
            golem.Program.logicCore = demo.logicCore;
            golem.Program.appendages.AddRange(demo.appendages);

            clockRunner.Register(golem);
            clockRunner.Play();
        }
    }
}
