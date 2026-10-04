using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Steam power at the golem's edge (docs/progression-design.md §3.1): NoSteam is a
    // precondition on the existing rigidity rule, not a new kind of behaviour.
    //
    // The load-bearing property here is the SAME one GolemSpatialRoutingTests guards, one layer
    // up: steam is OPT-IN. A golem that is never handed a network is exempt and always runs, so
    // Main.unity's seven hand-wired demos and every pre-existing test are untouched by the
    // largest addition in the progression design. If that fork ever breaks, the very first
    // symptom is hundreds of unrelated tests going red at once -- so it is asserted directly
    // here rather than being left as an emergent property.
    public class GolemSteamPowerTests
    {
        private readonly List<object> _spawned = new List<object>();

        [TearDown]
        public void TearDown()
        {
            foreach (object go in _spawned)
            {
            }

            _spawned.Clear();
        }

        private T AddHolder<T>() where T : new()
        {
            return new T();
        }

        private static Vector2Int C(int x, int y) => new Vector2Int(x, y);

        private GolemEntity CreateGolem(string id, params AppendageActionDefinition[] steps)
        {
            GolemEntity entity = new GolemEntity();

            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            foreach (AppendageActionDefinition step in steps)
            {
                entity.Program.appendages.Add(step);
            }

            entity.Configure(id, null);
            return entity;
        }

        private static AppendageActionDefinition Step(
            AppendageActionType type, string sourceId = null, string destinationId = null)
        {
            var step = new AppendageActionDefinition();
            step.actionType = type;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            step.durationTicks = 1;
            return step;
        }

        // --- The opt-in fork ------------------------------------------------------------------

        [Test]
        public void AGolemWithNoSteamNetworkConfigured_RunsNormally()
        {
            // The Main.unity demo shape: no ConfigureSteam call anywhere. It must not merely be
            // "powered by an empty network" -- it must be OUTSIDE the mechanic, which is what
            // makes the addition genuinely additive.
            ConveyorSystem conveyor = AddHolder<ConveyorSystem>();
            ResourceNodeRegistry nodes = AddHolder<ResourceNodeRegistry>();
            var belt = new BeltSegment("ScrapBeltA", 4);
            conveyor.Register(belt);
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 5));

            GolemEntity golem = CreateGolem(
                "Demo", Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Demo", conveyor);
            golem.ConfigureEconomy(nodes, null);

            golem.Tick(0);

            Assert.AreEqual(StallReason.None, golem.StallReason);
            Assert.AreEqual(1, belt.Items.Count, "an exempt golem does its work");
        }

        [Test]
        public void AGolemOnAWorkingBoiler_RunsNormally()
        {
            ConveyorSystem conveyor = AddHolder<ConveyorSystem>();
            ResourceNodeRegistry nodes = AddHolder<ResourceNodeRegistry>();
            SteamNetwork steam = AddHolder<SteamNetwork>();
            var belt = new BeltSegment("ScrapBeltA", 4);
            conveyor.Register(belt);
            nodes.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 5));

            steam.RegisterBoiler("B", C(0, 0), 240);

            GolemEntity golem = CreateGolem(
                "Powered", Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Powered", conveyor);
            golem.ConfigureEconomy(nodes, null);
            golem.SetPlacement(C(0, 1), Facing.North);
            golem.ConfigureSteam(steam);

            golem.Tick(1);

            Assert.AreEqual(StallReason.None, golem.StallReason);
            Assert.AreEqual(1, belt.Items.Count);
        }

        // --- NoSteam as a precondition ----------------------------------------------------------

        [Test]
        public void AGolemWithNoBoilerInReach_StallsNoSteam_NamingItsOwnTile()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            steam.RegisterBoiler("B", C(0, 0), 240);

            GolemEntity golem = CreateGolem("Stranded", Step(AppendageActionType.Haul));
            golem.SetPlacement(C(7, 9), Facing.North);
            golem.ConfigureSteam(steam);

            golem.Tick(1);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.NoSteam, golem.StallReason);
            Assert.AreEqual(C(7, 9).ToString(), golem.StallResourceId,
                "§3.1: NoSteam names the tile");
        }

        [Test]
        public void AnUnpoweredGolem_PublishesGolemStalledEvent_LikeAnyOtherStall()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            steam.RegisterBoiler("B", C(0, 0), 240);

            GolemEntity golem = CreateGolem("Stranded", Step(AppendageActionType.Haul));
            golem.SetPlacement(C(7, 9), Facing.North);
            golem.ConfigureSteam(steam);

            var received = new List<GolemStalledEvent>();
            System.Action<GolemStalledEvent> handler = e => received.Add(e);
            EventBus.GolemStalled += handler;
            try
            {
                golem.Tick(1);
                // Edge-triggered like every other stall: the reason has not changed, so no
                // second event on the retry tick.
                golem.Tick(2);
            }
            finally
            {
                EventBus.GolemStalled -= handler;
            }

            Assert.AreEqual(1, received.Count, "one incident, one event");
            Assert.AreEqual(StallReason.NoSteam, received[0].Reason);
            Assert.AreEqual("Stranded", received[0].GolemId);
        }

        [Test]
        public void AnUnpoweredGolem_DoesNotConsumeTheStepsSideEffect()
        {
            // The rule that makes NoSteam safe to add: it is checked BEFORE any verb runs, so an
            // unpowered extractor cannot drain a node it will then be unable to push from.
            ConveyorSystem conveyor = AddHolder<ConveyorSystem>();
            ResourceNodeRegistry nodes = AddHolder<ResourceNodeRegistry>();
            SteamNetwork steam = AddHolder<SteamNetwork>();
            var belt = new BeltSegment("ScrapBeltA", 4);
            conveyor.Register(belt);
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 5);
            nodes.Register(node);

            steam.RegisterBoiler("B", C(0, 0), 240);

            GolemEntity golem = CreateGolem(
                "Stranded", Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Stranded", conveyor);
            golem.ConfigureEconomy(nodes, null);
            golem.SetPlacement(C(7, 9), Facing.North);
            golem.ConfigureSteam(steam);

            for (long tick = 1; tick <= 20; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(StallReason.NoSteam, golem.StallReason);
            Assert.AreEqual(5, node.RemainingQuantity, "the node was never touched");
            Assert.AreEqual(0, belt.Items.Count, "and nothing was produced");
            Assert.AreEqual(0, golem.Program.CurrentStepIndex, "it never advanced past the step");
        }

        [Test]
        public void LosingSteamAndGettingItBack_StallsThenResumes()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            steam.RegisterBoiler("B", C(0, 0), 240);
            steam.AddPipe(C(0, 1));

            GolemEntity golem = CreateGolem("Worker", Step(AppendageActionType.Haul));
            golem.SetPlacement(C(0, 2), Facing.North);
            golem.ConfigureSteam(steam);

            golem.Tick(1);
            Assert.AreEqual(StallReason.None, golem.StallReason, "precondition: it is powered");

            // Pave over the pipe. progression-design §9's Phase 6 beat.
            steam.RemovePipe(C(0, 1));
            golem.Tick(2);
            Assert.AreEqual(StallReason.NoSteam, golem.StallReason);

            var resumed = new List<GolemResumedEvent>();
            System.Action<GolemResumedEvent> handler = e => resumed.Add(e);
            EventBus.GolemResumed += handler;
            try
            {
                steam.AddPipe(C(0, 1));
                golem.Tick(3);
            }
            finally
            {
                EventBus.GolemResumed -= handler;
            }

            Assert.AreEqual(StallReason.None, golem.StallReason, "relaying the pipe fixes it");
            Assert.AreEqual(1, resumed.Count, "and it resumes through the ordinary machinery");
        }

        [Test]
        public void AGolemBeyondTheBoilersEightGolemCap_StallsNoSteam()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            steam.RegisterBoiler("B", C(0, 0), 10000);
            for (int x = 0; x <= 8; x++)
            {
                steam.AddPipe(C(x, 1));
            }

            // Nine golems in reach of one boiler; the ninth in cell order goes without.
            var golems = new List<GolemEntity>();
            for (int x = 0; x <= 8; x++)
            {
                GolemEntity golem = CreateGolem("G" + x, Step(AppendageActionType.Haul));
                golem.SetPlacement(C(x, 2), Facing.North);
                golem.ConfigureSteam(steam);
                golems.Add(golem);
            }

            foreach (GolemEntity golem in golems)
            {
                golem.Tick(1);
            }

            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(StallReason.None, golems[i].StallReason, "G" + i + " should be powered");
            }

            Assert.AreEqual(StallReason.NoSteam, golems[8].StallReason,
                "the ninth is over the cap");
            Assert.AreEqual(C(8, 2).ToString(), golems[8].StallResourceId);
        }

        // --- Registration lifecycle ----------------------------------------------------------

        [Test]
        public void ConfigureSteam_RegistersTheGolemAtItsCurrentCell()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            GolemEntity golem = CreateGolem("G", Step(AppendageActionType.Haul));
            golem.SetPlacement(C(3, 4), Facing.North);
            golem.ConfigureSteam(steam);

            Assert.IsTrue(steam.HasConsumer("G"));
            Assert.AreEqual(1, steam.ConsumerCount);
        }

        [Test]
        public void MovingAGolem_MovesWhatItDrawsFrom()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            steam.RegisterBoiler("B", C(0, 0), 240);

            GolemEntity golem = CreateGolem("G", Step(AppendageActionType.Haul));
            golem.SetPlacement(C(0, 1), Facing.North);
            golem.ConfigureSteam(steam);
            Assert.IsTrue(steam.IsPowered("G", 1));

            golem.SetPlacement(C(20, 20), Facing.North);

            Assert.IsFalse(steam.IsPowered("G", 2),
                "a golem carried away from its boiler loses power");
            Assert.AreEqual(1, steam.ConsumerCount, "and is not duplicated");
        }

        [Test]
        public void AHeldGolem_CostsNothingAndFreesItsBoilerSlot()
        {
            SteamNetwork steam = AddHolder<SteamNetwork>();
            SteamBoiler boiler = steam.RegisterBoiler("B", C(0, 0), 1000);

            GolemEntity golem = CreateGolem("G", Step(AppendageActionType.Haul));
            golem.SetPlacement(C(0, 1), Facing.North);
            golem.ConfigureSteam(steam);
            Assert.IsTrue(steam.IsPowered("G", 1));

            golem.SetHeld(true);

            Assert.IsFalse(steam.HasConsumer("G"),
                "a golem in the player's hands has a stale cell and must not draw steam");

            for (long tick = 2; tick <= 500; tick++)
            {
                steam.Tick(tick);
            }

            Assert.AreEqual(1000, boiler.CokeStock, "and costs nothing while held");

            golem.SetHeld(false);
            Assert.IsTrue(steam.IsPowered("G", 501), "putting it back down repowers it");
        }
    }
}
