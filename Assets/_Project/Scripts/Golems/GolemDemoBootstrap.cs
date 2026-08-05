using UnityEngine;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    // Wires the M2 hardcoded demo program onto a GolemEntity and starts the clock so the
    // milestone is playable without pre-authored .asset files (see HardcodedDemoProgram).
    // A real play/pause/speed HUD and authored ScriptableObject programs replace this at M3/M8.
    public sealed class GolemDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        [SerializeField] private SimulationClockRunner clockRunner;

        // The chassis the demo program is fitted to. This used to be left unassigned,
        // which made the demo golem an *invalid* configuration: appendages are appended
        // straight onto the list below, bypassing GolemProgram.TryAddAppendage and its
        // "no chassis means no capacity" guard, so the golem ended up holding steps it had
        // no chassis to hold them in. That is what the Workbench then rendered as
        // "CHASSIS -- none --  SLOTS 1/0" over a viewport that drew no steps at all --
        // the first thing a player saw on this screen, and incoherent. Fitting a real
        // chassis whose slot count matches the demo program fixes it at the source.
        [SerializeField] private ChassisDefinition chassis;

        private void Start()
        {
            HardcodedDemoProgram.ApplyTo(golem, chassis, HardcodedDemoProgram.ExtractAndDeposit());

            clockRunner.Register(golem);
            clockRunner.Play();
        }
    }
}
