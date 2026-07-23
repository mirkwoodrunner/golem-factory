using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Golems
{
    // M4 demo (Scrap chain): Golem A extracts onto a belt, the belt visibly carries the
    // item across two chained segments, Golem B pulls it off the far end into the real
    // ScrapBuffer (M5 -- was the DemoBuffer placeholder through M4).
    // M5 extends the same bootstrap (rather than adding a new additive one, since this is
    // a direct continuation of the Scrap flow, not an independent demo) with: Golem C,
    // which refines that Scrap into Brass over several ticks, and Golem D, which runs an
    // independent Aether node -> belt -> buffer chain to demonstrate a second, finite
    // resource alongside Scrap's infinite one ("multiple resource chains").
    // Additive to (not replacing) M2/M3's GolemDemoBootstrap -- disable that GameObject in
    // the scene when running this demo (see M4 manual setup).
    public sealed class BeltDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private GolemEntity golemA;
        [SerializeField] private GolemEntity golemB;
        [SerializeField] private GolemEntity golemC;
        [SerializeField] private GolemEntity golemD;
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistry;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistry;
        [SerializeField] private SimulationClockRunner clockRunner;
        [SerializeField] private int segmentLengthTicks = 5;
        [SerializeField] private int aetherNodeQuantity = 20;

        private void Start()
        {
            nodeRegistry.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            nodeRegistry.Registry.Register(new ResourceNode("AetherNode", ItemType.Aether, aetherNodeQuantity));

            var beltA = new BeltSegment("ScrapBeltA", segmentLengthTicks);
            var beltB = new BeltSegment("ScrapBeltB", segmentLengthTicks);
            beltA.Next = beltB;
            conveyorHolder.System.Register(beltA);
            conveyorHolder.System.Register(beltB);
            conveyorHolder.System.Register(new BeltSegment("AetherBelt", segmentLengthTicks));

            golemA.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programA = HardcodedDemoProgram.ExtractOntoBelt("ScrapBeltA");
            golemA.Program.logicCore = programA.logicCore;
            golemA.Program.appendages.AddRange(programA.appendages);

            golemB.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programB = HardcodedDemoProgram.LoadFromBelt("ScrapBeltB", "ScrapBuffer");
            golemB.Program.logicCore = programB.logicCore;
            golemB.Program.appendages.AddRange(programB.appendages);

            golemC.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programC = HardcodedDemoProgram.Refine(
                "ScrapBuffer", "BrassBuffer", ItemType.Scrap, ItemType.Brass, durationTicks: 3);
            golemC.Program.logicCore = programC.logicCore;
            golemC.Program.appendages.AddRange(programC.appendages);

            golemD.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programD = HardcodedDemoProgram.ExtractThenLoad("AetherNode", "AetherBelt", "AetherBuffer");
            golemD.Program.logicCore = programD.logicCore;
            golemD.Program.appendages.AddRange(programD.appendages);

            clockRunner.Register(conveyorHolder.System);
            clockRunner.Register(golemA);
            clockRunner.Register(golemB);
            clockRunner.Register(golemC);
            clockRunner.Register(golemD);
            clockRunner.Play();
        }
    }
}
