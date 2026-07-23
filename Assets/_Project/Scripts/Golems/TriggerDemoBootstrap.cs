using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Golems
{
    // M7 demo: the "vertical slice" trigger chain from the design doc -- Golem A hauls
    // scrap until [a buffer] hits a threshold -> triggers a refine -> triggers a load into
    // a sell/ship building -- adapted to this project's buffer-based economy:
    // Golem E continuously hauls Scrap (via ExtractThenLoad) into a dedicated
    // TriggerScrapBuffer; once that crosses thresholdQuantity, Golem F (Threshold
    // trigger) refines it into Brass; Golem F completing (Signal trigger) fires Golem G,
    // which "ships" the Brass into a final ShippedBuffer.
    // Additive alongside BeltDemoBootstrap -- reuses its shared Conveyor/Nodes/Buffers
    // GameObjects (in particular the infinite "ScrapNode" it registers) rather than
    // duplicating that infrastructure, but registers its own dedicated belt segment and
    // buffers so this demo's pacing isn't drowned out by the M4/M5 Scrap chain already
    // running on the shared ScrapBuffer.
    public sealed class TriggerDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private GolemEntity golemE;
        [SerializeField] private GolemEntity golemF;
        [SerializeField] private GolemEntity golemG;
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistry;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistry;
        [SerializeField] private SimulationClockRunner clockRunner;
        [SerializeField] private int segmentLengthTicks = 3;
        [SerializeField] private int thresholdQuantity = 10;

        private void Start()
        {
            conveyorHolder.System.Register(new BeltSegment("TriggerScrapBelt", segmentLengthTicks));

            // Golem E is the only one of the three that touches a belt (ExtractFromNode/
            // LoadIntoBuffer) -- F and G are buffer-to-buffer only, so they need
            // ConfigureEconomy but not Configure's conveyorHolder.
            golemE.Configure(golemE.GolemId, conveyorHolder);
            golemE.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programE = HardcodedDemoProgram.ExtractThenLoad("ScrapNode", "TriggerScrapBelt", "TriggerScrapBuffer");
            golemE.Program.logicCore = programE.logicCore;
            golemE.Program.appendages.AddRange(programE.appendages);

            golemF.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programF = HardcodedDemoProgram.ThresholdRefine(
                "TriggerScrapBuffer", "TriggerBrassBuffer", ItemType.Scrap, ItemType.Brass,
                durationTicks: 3, thresholdQuantity: thresholdQuantity);
            golemF.Program.logicCore = programF.logicCore;
            golemF.Program.appendages.AddRange(programF.appendages);

            golemG.ConfigureEconomy(nodeRegistry, bufferRegistry);
            GolemProgram programG = HardcodedDemoProgram.SignalShip(
                golemF.GolemId, "TriggerBrassBuffer", "ShippedBuffer", ItemType.Brass);
            golemG.Program.logicCore = programG.logicCore;
            golemG.Program.appendages.AddRange(programG.appendages);

            // Note: conveyorHolder.System is already registered as an ITickable by
            // BeltDemoBootstrap -- registering it again here would double-tick every
            // segment (including the M4/M5 ones), so only the golems are registered.
            clockRunner.Register(golemE);
            clockRunner.Register(golemF);
            clockRunner.Register(golemG);
        }
    }
}
