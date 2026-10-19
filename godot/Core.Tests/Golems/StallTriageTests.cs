using System;
using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Belts;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Grey for waiting, red for stuck (docs/onboarding-feedback-design.md §3): the triage table,
    // the probe that reads a golem's neighbours, the mood and the alerts strip.
    public class StallTriageTests
    {
        private static StallFacts Facts(AppendageActionType action = AppendageActionType.Haul, bool gives = false,
            bool fed = false, bool drained = false, bool truckload = false, string good = null) =>
            new StallFacts(action, gives, fed, drained, truckload, good);

        [Test]
        public void EveryStallReason_HasAnAnswer()
        {
            foreach (StallReason reason in Enum.GetValues(typeof(StallReason)))
            {
                Assert.DoesNotThrow(() => StallTriage.Urgency(reason, default),
                    reason + " has no triage: decide who must act on it (§3.3)");
            }
        }

        [Test]
        public void TheTable_BothWays()
        {
            Assert.AreEqual(StallUrgency.Waiting, StallTriage.Urgency(StallReason.NodeEmpty, Facts(truckload: true)));
            Assert.AreEqual(StallUrgency.Stuck, StallTriage.Urgency(StallReason.NodeEmpty, Facts()));
            Assert.AreEqual(StallUrgency.Waiting, StallTriage.Urgency(StallReason.BeltFull, Facts(drained: true)));
            Assert.AreEqual(StallUrgency.Stuck, StallTriage.Urgency(StallReason.BeltFull, Facts()));
            Assert.AreEqual(StallUrgency.Waiting, StallTriage.Urgency(StallReason.BufferEmpty, Facts(gives: true, fed: true)));
            Assert.AreEqual(StallUrgency.Stuck, StallTriage.Urgency(StallReason.BufferEmpty, Facts(gives: true)));
            Assert.AreEqual(StallUrgency.Stuck, StallTriage.Urgency(StallReason.BeltEmpty, Facts(gives: false, fed: true)));
            Assert.AreEqual(StallUrgency.Waiting, StallTriage.Urgency(StallReason.MissingItem, Facts(gives: true, fed: true)));
            Assert.AreEqual(StallUrgency.Stuck, StallTriage.Urgency(StallReason.MissingItem, Facts(gives: true)));
            Assert.AreEqual(StallUrgency.Stuck,
                StallTriage.Urgency(StallReason.MissingItem, Facts(AppendageActionType.Assemble, true, true)),
                "an Assemble's hold won't refill: only the Hauls before it fill it");
            foreach (StallReason never in new[] { StallReason.Unconfigured, StallReason.NoSourceAtTile,
                StallReason.NoTargetAtTile, StallReason.InputFull, StallReason.OutputFull, StallReason.NoSteam,
                StallReason.NodeCrowded, StallReason.FilterMismatch })
            {
                Assert.AreEqual(StallUrgency.Stuck,
                    StallTriage.Urgency(never, Facts(gives: true, fed: true, drained: true, truckload: true)), never.ToString());
            }
        }

        [Test]
        public void TheMood_WaitingFromAWaitingUrgency_AndSteamStaysStarved()
        {
            Assert.AreEqual(6, (int)GolemMood.Waiting);
            Assert.AreEqual(GolemMood.Waiting, GolemMoodRules.Classify(
                GolemState.Stalled, StallReason.BufferEmpty, true, 0, StallUrgency.Waiting));
            Assert.AreEqual(GolemMood.Stalled, GolemMoodRules.Classify(
                GolemState.Stalled, StallReason.BufferEmpty, true, 0));
            Assert.AreEqual(GolemMood.Starved, GolemMoodRules.Classify(
                GolemState.Stalled, StallReason.NoSteam, true, 0, StallUrgency.Waiting));
            Assert.IsFalse(GolemMoodRules.IsStopped(GolemMood.Waiting));
        }

        [Test]
        public void Presented_HoldsTheOldMoodThroughTheDwell_AndRedIsImmediate()
        {
            Assert.AreEqual(GolemMood.Working, GolemMoodRules.Presented(GolemMood.Waiting, 1.9f, GolemMood.Working));
            Assert.AreEqual(GolemMood.Waiting,
                GolemMoodRules.Presented(GolemMood.Waiting, GolemMoodRules.WaitingDwellSeconds, GolemMood.Working));
            Assert.AreEqual(GolemMood.Stalled, GolemMoodRules.Presented(GolemMood.Stalled, 0f, GolemMood.Waiting));
        }

        [Test]
        public void Straining_NeverShowsABadge()
        {
            Assert.IsFalse(GolemMoodRules.ShouldShowBadge(GolemMood.Straining, 100f));
            Assert.IsTrue(GolemMoodRules.ShouldShowBadge(GolemMood.Waiting, GolemMoodRules.WaitingDwellSeconds));
        }

        // --- The probe ----------------------------------------------------------------------

        private sealed class Floor
        {
            public readonly SpatialEndpointRegistry Endpoints = new SpatialEndpointRegistry();
            public readonly BeltNetwork Belts = new BeltNetwork();
            public readonly List<GolemEntity> Golems = new List<GolemEntity>();
            public readonly StallFactsProbe Probe;

            public Floor()
            {
                Belts.Configure(new ConveyorSystem(), Endpoints, 4);
                Probe = new StallFactsProbe(() => Golems, Endpoints, Belts);
            }

            public StorageBuffer Crate(Vector2Int cell, string id = null)
            {
                var buffer = new StorageBuffer(id ?? "Crate" + cell, 50);
                Endpoints.Register(cell, new StorageBufferEndpoint(buffer));
                return buffer;
            }

            /// <summary>A golem at <paramref name="cell"/> facing north: takes from south, gives north.</summary>
            public GolemEntity Golem(Vector2Int cell, params (AppendageActionType type, string good)[] steps)
            {
                var golem = new GolemEntity();
                golem.Configure("G" + Golems.Count, null);
                golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
                for (int i = 0; i < steps.Length; i++)
                {
                    golem.Program.appendages.Add(new AppendageActionDefinition { actionType = steps[i].type, durationTicks = 1 });
                    golem.Program.SetItemTypeAt(i, steps[i].good ?? "");
                }
                golem.ConfigureSpatial(Endpoints, cell, Facing.North);
                golem.ConfigureStallFacts(Probe.Read);
                Golems.Add(golem);
                return golem;
            }
        }

        private static readonly (AppendageActionType, string) HaulScrap = (AppendageActionType.Haul, ItemType.Scrap);
        private static readonly (AppendageActionType, string) Push = (AppendageActionType.Push, null);

        [Test]
        public void ACrateFedByAGolem_ReadsGrey_AndTheFeederCarriedAwayTurnsItRed()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));                 // the feeder's source
            floor.Crate(new Vector2Int(0, 2));                 // between them
            GolemEntity feeder = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity presser = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);

            presser.Tick(0);
            Assert.AreEqual(StallReason.BufferEmpty, presser.StallReason);
            Assert.AreEqual(GolemMood.Waiting, presser.Mood);

            feeder.SetHeld(true);
            Assert.AreEqual(GolemMood.Stalled, presser.Mood, "a carried feeder feeds nothing");
        }

        [Test]
        public void TheGolemItself_NeverFeeds()
        {
            // Chapter 4's Scavenger turned round to face its own depot.
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            GolemEntity golem = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            floor.Endpoints.Register(new Vector2Int(0, 2), floor.Endpoints.TryGetEndpoint(new Vector2Int(0, 0), out IItemEndpoint same) ? same : null);

            golem.Tick(0);
            Assert.AreEqual(GolemMood.Stalled, golem.Mood);
        }

        [Test]
        public void AFeederThatCantPushTheGood_DoesNotCount()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            floor.Crate(new Vector2Int(0, 2));
            floor.Golem(new Vector2Int(0, 1), (AppendageActionType.Haul, ItemType.Coal), Push);
            GolemEntity presser = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);

            presser.Tick(0);
            Assert.AreEqual(GolemMood.Stalled, presser.Mood);
        }

        [Test]
        public void APostCrate_IsFedThroughAnother()
        {
            var floor = new Floor();
            var post = new StorageBuffer(SandboxWorld.PostBufferId, 200);
            floor.Endpoints.Register(new Vector2Int(5, 2), new StorageBufferEndpoint(post));
            floor.Endpoints.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(post));
            floor.Crate(new Vector2Int(5, 0));
            floor.Golem(new Vector2Int(5, 1), HaulScrap, Push);
            GolemEntity taker = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);

            taker.Tick(0);
            Assert.AreEqual(GolemMood.Waiting, taker.Mood);
        }

        [Test]
        public void ABelt_IsFedUpstream()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            floor.Belts.TryPlace(new Vector2Int(0, 2), Facing.North, out _);
            floor.Belts.TryPlace(new Vector2Int(0, 3), Facing.North, out _);
            floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);    // pushes onto (0,2)
            GolemEntity unloader = floor.Golem(new Vector2Int(0, 4), HaulScrap, Push); // takes from (0,3)

            unloader.Tick(0);
            Assert.AreEqual(StallReason.BeltEmpty, unloader.StallReason);
            Assert.AreEqual(GolemMood.Waiting, unloader.Mood);
        }

        [Test]
        public void ABelt_IsDrainedDownstream()
        {
            var floor = new Floor();
            floor.Belts.TryPlace(new Vector2Int(0, 2), Facing.North, out _);
            floor.Belts.TryPlace(new Vector2Int(0, 3), Facing.North, out _);
            GolemEntity pusher = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);    // pushes onto (0,2)
            Assert.IsFalse(floor.Probe.Read(pusher).TargetDrained);

            floor.Golem(new Vector2Int(0, 4), HaulScrap, Push);                          // takes from (0,3)
            Assert.IsTrue(floor.Probe.Read(pusher).TargetDrained);
        }

        [Test]
        public void ABoiler_NeverGives_AndAlwaysDrains()
        {
            var floor = new Floor();
            var boiler = new SteamBoiler("B", new Vector2Int(9, 9), 0);
            floor.Endpoints.Register(new Vector2Int(0, 0), new BoilerFuelEndpoint(boiler, "Boiler"));
            GolemEntity golem = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            StallFacts facts = floor.Probe.Read(golem);
            Assert.IsFalse(facts.SourceGives);

            floor.Endpoints.Register(new Vector2Int(0, 2), new BoilerFuelEndpoint(boiler, "Boiler"));
            Assert.IsTrue(floor.Probe.Read(golem).TargetDrained);
        }

        [Test]
        public void AFullCrateNothingHaulsFrom_IsRed_AndAGolemHaulingFromItMakesItGrey()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0)).Deposit(ItemType.Scrap, 50);
            floor.Crate(new Vector2Int(0, 2)).Deposit(ItemType.Scrap, 50);
            GolemEntity golem = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            for (int t = 0; t < 10; t++)
            {
                golem.Tick(t);
            }
            Assert.AreEqual(StallReason.BeltFull, golem.StallReason);
            Assert.AreEqual(GolemMood.Stalled, golem.Mood);

            floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);
            Assert.AreEqual(GolemMood.Waiting, golem.Mood);
        }

        [Test]
        public void AnEmptyStall_WithNoTruckloadComing_IsRed()
        {
            var floor = new Floor();
            var node = new ResourceNode("CoalNode", ItemType.Coal, 0);
            floor.Endpoints.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            var probe = new StallFactsProbe(() => floor.Golems, floor.Endpoints, floor.Belts, () => null);
            GolemEntity golem = floor.Golem(new Vector2Int(0, 1), (AppendageActionType.ExtractFromNode, null), Push);
            golem.ConfigureStallFacts(probe.Read);

            golem.Tick(0);
            Assert.AreEqual(StallReason.NodeEmpty, golem.StallReason);
            Assert.AreEqual(GolemMood.Stalled, golem.Mood);
            Assert.IsFalse(probe.Read(golem).TruckloadOnTheWay);
        }

        [Test]
        public void AGolemWithNoFactsSource_ClassifiesAsBefore()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            floor.Crate(new Vector2Int(0, 2));
            floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity presser = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);
            presser.ConfigureStallFacts(null);

            presser.Tick(0);
            Assert.AreEqual(GolemMood.Stalled, presser.Mood);
        }

        // --- Phase 2 critique: false grey -------------------------------------------------

        /// <summary>One crate's store on two cells, as the Post crates share one.</summary>
        private static StorageBuffer SharedCrate(Floor floor, string id, Vector2Int a, Vector2Int b)
        {
            var buffer = new StorageBuffer(id, 50);
            floor.Endpoints.Register(a, new StorageBufferEndpoint(buffer));
            floor.Endpoints.Register(b, new StorageBufferEndpoint(buffer));
            return buffer;
        }

        [Test]
        public void AnEmptyLoop_OfGolemsEachWaitingOnTheOther_IsRed()
        {
            // A hauls X → Y, B hauls Y → X, and both crates are empty. Each "feeds" the other,
            // so both read grey forever, but nothing will ever start the loop: the player must.
            var floor = new Floor();
            StorageBuffer x = SharedCrate(floor, "X", new Vector2Int(0, 0), new Vector2Int(5, 2));
            SharedCrate(floor, "Y", new Vector2Int(0, 2), new Vector2Int(5, 0));
            GolemEntity a = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity b = floor.Golem(new Vector2Int(5, 1), HaulScrap, Push);

            a.Tick(0);
            Assert.AreEqual(GolemMood.Waiting, a.Mood, "B hasn't run yet: it may still deliver");
            b.Tick(0);
            Assert.AreEqual(StallReason.BufferEmpty, a.StallReason);
            Assert.AreEqual(StallReason.BufferEmpty, b.StallReason);
            Assert.AreEqual(GolemMood.Stalled, a.Mood, "an empty loop never starts on its own");
            Assert.AreEqual(GolemMood.Stalled, b.Mood);

            // One good in the loop and it runs: A takes it and is no longer stalled, so B waits on A.
            x.Deposit(ItemType.Scrap, 1);
            a.Tick(1);
            Assert.AreNotEqual(GolemState.Stalled, a.Program.State);
            Assert.AreEqual(GolemMood.Waiting, b.Mood);
        }

        [Test]
        public void AFeederItselfStuckWithNothingFeedingIt_StillMakesTheGolemBehindItGrey()
        {
            // §3.2's one red at the root, grey behind it: kept by the loop rule.
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            floor.Crate(new Vector2Int(0, 2));
            GolemEntity feeder = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity presser = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);

            feeder.Tick(0);
            presser.Tick(0);
            Assert.AreEqual(GolemMood.Stalled, feeder.Mood);
            Assert.AreEqual(GolemMood.Waiting, presser.Mood);
        }

        [Test]
        public void AGolemHaulingAnotherGood_DoesNotDrainAFullCrate()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 2)).Deposit(ItemType.Scrap, 50);
            GolemEntity pusher = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            pusher.Inventory.AddInput(ItemType.Scrap, 3);
            GolemEntity coal = floor.Golem(new Vector2Int(0, 3), (AppendageActionType.Haul, ItemType.Coal), Push);
            Assert.IsFalse(floor.Probe.Read(pusher).TargetDrained, "a Coal hauler never takes the Scrap that blocks it");

            coal.Program.SetItemTypeAt(0, ItemType.Scrap);
            Assert.IsTrue(floor.Probe.Read(pusher).TargetDrained);
        }

        // --- The strip ----------------------------------------------------------------------

        [Test]
        public void TheStrip_CountsOnlyStoppedGolems_AndSaysHowManyWait()
        {
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            floor.Crate(new Vector2Int(0, 2));
            floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity presser = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);
            var strip = new AlertsStrip(() => floor.Golems);
            strip.Attach();
            try
            {
                presser.Tick(0);
                Assert.AreEqual(0, strip.Tracker.Count, "the event path counted a waiting golem");
                strip.Reconcile();
                Assert.AreEqual(0, strip.Tracker.Count);
                Assert.AreEqual("No golem needs you · 1 waiting for goods.", strip.Text);

                floor.Golems[0].SetHeld(true);
                strip.Reconcile();
                Assert.AreEqual(1, strip.Tracker.Count, "a golem nothing feeds is stopped, and counted");
            }
            finally
            {
                strip.Detach();
            }
        }

        [Test]
        public void TheStrip_PortedTextUnchanged_WithNothingWaiting()
        {
            Assert.AreEqual("All golems running.", StallDiagnostics.ComposeStripText(0, default));
            Assert.AreEqual("No golem needs you · 3 waiting for goods.", StallDiagnostics.ComposeStripText(0, default, 3));
        }
    }
}
