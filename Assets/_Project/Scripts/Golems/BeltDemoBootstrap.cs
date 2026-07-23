using UnityEngine;
using GolemFactory.Belts;

namespace GolemFactory.Golems
{
    // M4 demo: Golem A extracts onto a belt, the belt visibly carries the item across two
    // chained segments, Golem B pulls it off the far end into the DemoBuffer placeholder.
    // Additive to (not replacing) M2/M3's GolemDemoBootstrap -- disable that GameObject in
    // the scene when running this demo (see M4 manual setup).
    public sealed class BeltDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private GolemEntity golemA;
        [SerializeField] private GolemEntity golemB;
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private SimulationClockRunner clockRunner;
        [SerializeField] private int segmentLengthTicks = 5;

        private void Start()
        {
            var beltA = new BeltSegment("ScrapBeltA", segmentLengthTicks);
            var beltB = new BeltSegment("ScrapBeltB", segmentLengthTicks);
            beltA.Next = beltB;
            conveyorHolder.System.Register(beltA);
            conveyorHolder.System.Register(beltB);

            GolemProgram programA = HardcodedDemoProgram.ExtractOntoBelt("ScrapBeltA");
            golemA.Program.logicCore = programA.logicCore;
            golemA.Program.appendages.AddRange(programA.appendages);

            GolemProgram programB = HardcodedDemoProgram.LoadFromBelt("ScrapBeltB", "ScrapBuffer");
            golemB.Program.logicCore = programB.logicCore;
            golemB.Program.appendages.AddRange(programB.appendages);

            clockRunner.Register(conveyorHolder.System);
            clockRunner.Register(golemA);
            clockRunner.Register(golemB);
            clockRunner.Play();
        }
    }
}
