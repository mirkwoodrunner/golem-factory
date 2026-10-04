using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// <c>Main.unity</c>'s seven demos, rehoused as tests.
    ///
    /// <para>
    /// <b>Why this exists.</b> The backlog wants Main.unity retired -- it is a diorama, the two
    /// scenes keep diverging, and it is a second scene to keep authored. The reason it could not
    /// be retired is that it is the only place the ID-ROUTED semantics are exercised: a golem
    /// with no spatial endpoint holder routes by the bare-string <c>sourceId</c>/
    /// <c>destinationId</c> on its cards and charges the AUTHORED <c>durationTicks</c>, and every
    /// pre-machine-model behaviour rides that fork. Deleting the scene without this suite would
    /// delete the coverage with it.
    /// </para>
    ///
    /// <para>
    /// <b>Driven from the scene's own program builders.</b> Every test below calls
    /// <see cref="HardcodedDemoProgram"/> -- the same static the demo bootstraps call -- rather
    /// than restating the programs. A transcription would be a second copy free to drift from
    /// the thing it claims to pin, which is exactly the trap the tech tree catalogue avoided by
    /// reading the assets.
    /// </para>
    ///
    /// <para>
    /// What this does NOT rehouse: the scene's camera rig, its hand-placed sprites and the
    /// visual demonstration itself. Those are what a diorama is FOR, and they are not
    /// regressions -- nothing asserts them today either.
    /// </para>
    /// </summary>
    public class IdRoutedDemoRegressionTests
    {

        [TearDown]
        public void TearDown()
        {
        }

        private GolemEntity BuildGolem(
            GolemProgram program, ConveyorSystem conveyor = null,
            ResourceNodeRegistry nodes = null, StorageBufferRegistry buffers = null)
        {
            var golem = new GolemEntity();

            var chassis = new ChassisDefinition();
            chassis.maxAppendageSlots = 6;
            HardcodedDemoProgram.ApplyTo(golem, chassis, program);

            golem.Configure("DemoGolem", conveyor);
            golem.ConfigureEconomy(nodes, buffers);

            // DELIBERATELY NEVER ConfigureSpatial'd. That absence IS the fork under test: it is
            // what makes this golem route by id and charge its authored duration, and it is the
            // state every golem in Main.unity is in.
            return golem;
        }

        private (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
            StorageBufferRegistry buffers) BuildWorld()
        {

            var conveyor = new ConveyorSystem();
            var nodes = new ResourceNodeRegistry();
            var buffers = new StorageBufferRegistry();

            return (conveyor, nodes, buffers);
        }

        // --- Golem A: THE DEMO THAT NOW WORKS ------------------------------------------------

        [Test]
        public void ExtractAndDeposit_CarriesTheWholeChain_NodeToBeltToBuffer()
        {
            // THIS PROGRAM USED TO BE A FICTION. Written in M2 with no belt, it could never run:
            // the id-routed ExtractFromNode extracts ONTO a belt named by the card's
            // destinationId, and the card named none, so the step refused at CanEnqueue(null)
            // before it ever touched the node -- every tick, forever. GolemDemoBootstrap applied
            // exactly this to a golem standing in Main.unity, which is why that golem visibly did
            // nothing for several milestones.
            //
            // It was pinned broken rather than repaired while the scene existed, because a belt
            // would have changed what the diorama demonstrated. The scene is retired, so it takes
            // a belt now -- as a REQUIRED argument, so the fiction cannot be rebuilt by accident
            // -- and this test pins the two-step id-routed chain end to end instead of pinning a
            // jam. Nothing else covered node -> belt -> buffer in one program.
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            conveyor.Register(new BeltSegment("ScrapBelt", 4));

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.ExtractAndDeposit("ScrapBelt"), conveyor, nodes, buffers);

            // Long enough for a unit to be extracted, travel the belt's whole length, and be
            // carried off its head -- LoadIntoBuffer goes through TryPeekHead, which refuses an
            // item still in transit.
            for (long tick = 0; tick < 40; tick++)
            {
                golem.Tick(tick);
                conveyor.Tick(tick);
            }

            Assert.Greater(
                buffers.GetQuantity("ScrapBuffer", ItemType.Scrap), 0,
                "the chain should deliver: node -> belt -> buffer");
        }

        [Test]
        public void AnExtractCardNamingNoBelt_StillRefusesBeforeItTouchesTheNode()
        {
            // The invariant the old fiction was accidentally documenting, kept deliberately now
            // that the demo no longer demonstrates it. This is the no-item-loss rule at the
            // extract end: a producer pulling from an irreversible source must confirm the
            // destination has room BEFORE it consumes, so a card with no belt takes nothing out
            // of the ground rather than extracting into nowhere.
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 10);
            nodes.Register(node);

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.ExtractAndDeposit(null), conveyor, nodes, buffers);

            for (long tick = 0; tick < 8; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.BeltFull, golem.StallReason,
                "refused at the destination belt -- which is null, because the card names none");
            Assert.AreEqual(10, node.RemainingQuantity, "and the seam was never touched");
            Assert.AreEqual(0, buffers.GetQuantity("ScrapBuffer", ItemType.Scrap));
        }

        [Test]
        public void AnIdRoutedGolem_NeverConsultsATile()
        {
            // The property that makes the fork safe. If this golem ever started reading the cell
            // in front of it, Main.unity's demos would break the moment a scene had endpoints --
            // and a golem with no tile under it would stall NoTargetAtTile instead of running.
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            conveyor.Register(new BeltSegment("ScrapBelt", 4));

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.ExtractOntoBelt("ScrapBelt"), conveyor, nodes, buffers);

            for (long tick = 0; tick < 8; tick++)
            {
                golem.Tick(tick);
            }

            // It fills the belt and then stalls BeltFull, because nothing is unloading it --
            // which is the RIGHT stall, and the point: an id-routed golem's complaints are about
            // named belts and buffers, never about the cell it is standing on.
            Assert.AreNotEqual(StallReason.NoTargetAtTile, golem.StallReason,
                "it is not routing by tile at all");
            conveyor.TryGetSegment("ScrapBelt", out BeltSegment belt);
            Assert.Greater(belt.Items.Count, 0, "and it really did move scrap by id");
        }

        // --- Golem B/C: the belt chain -------------------------------------------------------

        [Test]
        public void ExtractOntoBelt_ThenLoadFromBelt_CarriesScrapAcross()
        {
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            conveyor.Register(new BeltSegment("ScrapBelt", 4));

            GolemEntity loader = BuildGolem(
                HardcodedDemoProgram.ExtractOntoBelt("ScrapBelt"), conveyor, nodes, buffers);
            GolemEntity unloader = BuildGolem(
                HardcodedDemoProgram.LoadFromBelt("ScrapBelt", "ScrapBuffer"), conveyor, nodes, buffers);

            for (long tick = 0; tick < 40; tick++)
            {
                loader.Tick(tick);
                conveyor.Tick(tick);
                unloader.Tick(tick);
            }

            Assert.Greater(
                buffers.GetQuantity("ScrapBuffer", ItemType.Scrap), 0,
                "two golems handing off across a named belt segment, no tiles involved");
        }

        // --- Golem D: Refine, and the authored duration --------------------------------------

        [Test]
        public void Refine_ConvertsBetweenNamedBuffers()
        {
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            buffers.Deposit("ScrapBuffer", ItemType.Scrap, 4);

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.Refine("ScrapBuffer", "BrassBuffer", ItemType.Scrap, ItemType.Brass, 3),
                conveyor, nodes, buffers);

            for (long tick = 0; tick < 3; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(1, buffers.GetQuantity("BrassBuffer", ItemType.Brass));
            Assert.AreEqual(3, buffers.GetQuantity("ScrapBuffer", ItemType.Scrap),
                "input withdrawn up front, so processing time is committed work");
        }

        [Test]
        public void AnIdRoutedStep_ChargesItsAuthoredDuration()
        {
            // §1.1's fork, stated exactly: an id-routed golem keeps step.durationTicks as its
            // duration, where a spatially placed one derives it. A 9-tick Refine must take nine
            // ticks -- not the 2 + unitCount a machine-model Push would charge.
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            buffers.Deposit("ScrapBuffer", ItemType.Scrap, 2);

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.Refine("ScrapBuffer", "BrassBuffer", ItemType.Scrap, ItemType.Brass, 9),
                conveyor, nodes, buffers);

            for (long tick = 0; tick < 8; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(0, buffers.GetQuantity("BrassBuffer", ItemType.Brass),
                "eight ticks is not nine");

            golem.Tick(8);

            Assert.AreEqual(1, buffers.GetQuantity("BrassBuffer", ItemType.Brass));
        }

        // --- Golem E: a multi-step program that self-stalls -----------------------------------

        [Test]
        public void ExtractThenLoad_StallsOnStepTwoUntilTheBeltDelivers()
        {
            // M5's demonstration: one golem doing both halves waits on its own belt.
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap));
            conveyor.Register(new BeltSegment("SoloBelt", 6));

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.ExtractThenLoad("ScrapNode", "SoloBelt", "ScrapBuffer"),
                conveyor, nodes, buffers);

            golem.Tick(0);
            golem.Tick(1);

            // Step 2 cannot run yet: the item it just put on the belt is still travelling.
            Assert.AreEqual(0, buffers.GetQuantity("ScrapBuffer", ItemType.Scrap));

            for (long tick = 2; tick < 30; tick++)
            {
                conveyor.Tick(tick);
                golem.Tick(tick);
            }

            Assert.Greater(buffers.GetQuantity("ScrapBuffer", ItemType.Scrap), 0,
                "and resumes on its own once the belt has carried it, with no reprogramming");
        }

        // --- Golem F: the Threshold trigger ---------------------------------------------------

        [Test]
        public void ThresholdRefine_FiresOnTheCrossingRatherThanEveryTickAbove()
        {
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.ThresholdRefine(
                    "ScrapBuffer", "BrassBuffer", ItemType.Scrap, ItemType.Brass,
                    durationTicks: 1, thresholdQuantity: 4),
                conveyor, nodes, buffers);

            // Below the threshold: nothing happens however long it runs.
            buffers.Deposit("ScrapBuffer", ItemType.Scrap, 2);
            for (long tick = 0; tick < 10; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(0, buffers.GetQuantity("BrassBuffer", ItemType.Brass));

            // Crossing it fires exactly once per crossing -- edge-triggered, not level-triggered.
            buffers.Deposit("ScrapBuffer", ItemType.Scrap, 2);
            for (long tick = 10; tick < 20; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(1, buffers.GetQuantity("BrassBuffer", ItemType.Brass),
                "one crossing, one cycle");
        }

        // --- Golem G: the Signal trigger ------------------------------------------------------

        [Test]
        public void SignalShip_WaitsForAnotherGolemsCompletion()
        {
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();
            buffers.Deposit("BrassBuffer", ItemType.Brass, 4);
            conveyor.Register(new BeltSegment("ShipBelt", 4));

            GolemEntity golem = BuildGolem(
                HardcodedDemoProgram.SignalShip(
                    "UpstreamGolem", "BrassBuffer", "ShipBelt", ItemType.Brass),
                conveyor, nodes, buffers);

            // NOTE: the Signal trigger subscribes in OnEnable, which does not run in EditMode --
            // the gotcha CLAUDE.md records. So this pins the PROGRAM's shape, and the live
            // subscription stays covered by the PlayMode trigger suite.
            Assert.AreEqual(TriggerType.Signal, golem.Program.logicCore.triggerType);
            Assert.AreEqual(1, golem.Program.appendages.Count);
            // A same-item-type Refine, deliberately: there is no buffer-to-buffer appendage,
            // and a 1:1 recipe is a legitimate degenerate case of one rather than a new verb.
            Assert.AreEqual(AppendageActionType.Refine, golem.Program.appendages[0].actionType);
            Assert.AreEqual("BrassBuffer", golem.Program.appendages[0].sourceId);
            Assert.AreEqual("ShipBelt", golem.Program.appendages[0].destinationId);
        }

        // --- the fork itself -------------------------------------------------------------------

        [Test]
        public void TheMachineModelDoesNotReachAnIdRoutedGolem()
        {
            // §1.1: "an id-routed one keeps the old semantics byte for byte". A Push with no
            // spatial holder is Unconfigured rather than quietly emptying an internal stock that
            // the demos never fill.
            (ConveyorSystem conveyor, ResourceNodeRegistry nodes,
                StorageBufferRegistry buffers) = BuildWorld();

            var push = new AppendageActionDefinition();
            push.actionType = AppendageActionType.Push;
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var program = new GolemProgram { logicCore = logicCore };
            program.appendages.Add(push);

            GolemEntity golem = BuildGolem(program, conveyor, nodes, buffers);
            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason);
        }
    }
}
