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
            bool fed = false, bool drained = false, bool refills = false, string good = null) =>
            new StallFacts(action, gives, fed, drained, refills, good);

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
            Assert.AreEqual(StallUrgency.Waiting, StallTriage.Urgency(StallReason.NodeEmpty, Facts(refills: true)));
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
                    StallTriage.Urgency(never, Facts(gives: true, fed: true, drained: true, refills: true)), never.ToString());
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
        public void AnEmptyNode_ThatNothingRefills_IsRed()
        {
            var floor = new Floor();
            var node = new ResourceNode("CoalNode", ItemType.Coal, 0);
            floor.Endpoints.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            var probe = new StallFactsProbe(() => floor.Golems, floor.Endpoints, floor.Belts, _ => false);
            GolemEntity golem = floor.Golem(new Vector2Int(0, 1), (AppendageActionType.ExtractFromNode, null), Push);
            golem.ConfigureStallFacts(probe.Read);

            golem.Tick(0);
            Assert.AreEqual(StallReason.NodeEmpty, golem.StallReason);
            Assert.AreEqual(GolemMood.Stalled, golem.Mood);
            Assert.IsFalse(probe.Read(golem).SourceRefills);
        }

        /// <summary>
        /// docs/supply-design.md §4: an empty cart reads as waiting, grey, because it refills
        /// itself. The same node read by a probe that knows of no refill is red, above.
        /// </summary>
        [Test]
        public void AnEmptyCart_ThatRefillsItself_IsGrey()
        {
            var floor = new Floor();
            var node = new ResourceNode("Cart(0,0)", ItemType.Coal, 0);
            floor.Endpoints.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            var probe = new StallFactsProbe(() => floor.Golems, floor.Endpoints, floor.Belts, id => id == "Cart(0,0)");
            GolemEntity golem = floor.Golem(new Vector2Int(0, 1), (AppendageActionType.ExtractFromNode, null), Push);
            golem.ConfigureStallFacts(probe.Read);

            golem.Tick(0);
            Assert.AreEqual(StallReason.NodeEmpty, golem.StallReason);
            Assert.IsTrue(probe.Read(golem).SourceRefills);
            Assert.AreEqual(GolemMood.Waiting, golem.Mood, "the cart will clear it");
        }

        /// <summary>The world's probe asks the street: a cart just added, empty, refills.</summary>
        [Test]
        public void TheWorld_ReadsAnEmptyNewCart_AsRefilling()
        {
            SandboxWorld world = CrateWorld.Compose();
            var coal = new Vector2Int(-4, -16);
            Assert.IsTrue(world.GrantCart(coal, ItemType.Coal).Done);
            string id = world.Supply.SpotAt(coal).NodeId;
            Assert.IsTrue(world.Supply.Refills(id));
            Assert.IsFalse(world.Supply.Refills("Cart(99,99)"), "no cart there");
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

        // --- §17: the feeders the hover card names ------------------------------------------

        /// <summary>
        /// §17.2's one answer: on every fixture, a golem's feeder list is non-empty exactly when
        /// its facts read SourceGives and SourceFed, so the card never names a feeder the colour
        /// doesn't follow, nor misses one it does.
        /// </summary>
        [Test]
        public void FeedersOf_AgreesWithSourceFed()
        {
            int fed = 0, notFed = 0;
            foreach ((string name, Floor floor) in AgreementFixtures())
            {
                foreach (GolemEntity golem in floor.Golems)
                {
                    StallFacts facts = floor.Probe.Read(golem);
                    bool expected = facts.SourceGives && facts.SourceFed;
                    Assert.AreEqual(expected, floor.Probe.FeedersOf(golem).Count > 0,
                        name + ": " + golem.GolemId + " (" + golem.StallReason + ")");
                    if (expected)
                    {
                        fed++;
                    }
                    else
                    {
                        notFed++;
                    }
                }
            }
            Assert.Greater(fed, 0, "no fixture had a fed golem");
            Assert.Greater(notFed, 0, "no fixture had an unfed golem");
        }

        private static IEnumerable<(string, Floor)> AgreementFixtures()
        {
            // The sleeper: a feeder on an Interval core that hasn't fired.
            var sleeper = new Floor();
            sleeper.Crate(new Vector2Int(0, 0));
            sleeper.Crate(new Vector2Int(0, 2));
            GolemEntity s = sleeper.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            s.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.Interval, intervalTicks = 1000003 };
            sleeper.Golem(new Vector2Int(0, 3), HaulScrap, Push).Tick(0);
            yield return ("sleeper", sleeper);

            // The empty loop: each stalled on the other.
            var loop = new Floor();
            SharedCrate(loop, "X", new Vector2Int(0, 0), new Vector2Int(5, 2));
            SharedCrate(loop, "Y", new Vector2Int(0, 2), new Vector2Int(5, 0));
            GolemEntity a = loop.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity b = loop.Golem(new Vector2Int(5, 1), HaulScrap, Push);
            a.Tick(0);
            b.Tick(0);
            yield return ("empty loop", loop);

            // The chain: S1 → S2 → P, every crate empty, so S1 is red at the root.
            var chain = new Floor();
            chain.Crate(new Vector2Int(0, 0));
            chain.Crate(new Vector2Int(0, 2));
            chain.Crate(new Vector2Int(0, 4));
            GolemEntity s1 = chain.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            GolemEntity s2 = chain.Golem(new Vector2Int(0, 3), HaulScrap, Push);
            GolemEntity p = chain.Golem(new Vector2Int(0, 5), HaulScrap, Push);
            s1.Tick(0);
            s2.Tick(0);
            p.Tick(0);
            yield return ("chain", chain);

            // The belt: fed by a pusher upstream.
            var belt = new Floor();
            belt.Crate(new Vector2Int(0, 0));
            belt.Belts.TryPlace(new Vector2Int(0, 2), Facing.North, out _);
            belt.Belts.TryPlace(new Vector2Int(0, 3), Facing.North, out _);
            belt.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            belt.Golem(new Vector2Int(0, 4), HaulScrap, Push).Tick(0);
            yield return ("belt", belt);

            // The labelled crate: a Scrap-labelled depot fed by a Scrap pusher, and a Coal taker
            // behind a Coal-labelled one nothing feeds.
            var labelled = new Floor();
            labelled.Crate(new Vector2Int(0, 0));
            labelled.Endpoints.Register(new Vector2Int(0, 2), new FilteredBufferEndpoint(new StorageBuffer("L", 50), ItemType.Scrap));
            labelled.Golem(new Vector2Int(0, 1), HaulScrap, Push);
            labelled.Golem(new Vector2Int(0, 3), HaulScrap, Push).Tick(0);
            labelled.Endpoints.Register(new Vector2Int(4, 2), new FilteredBufferEndpoint(new StorageBuffer("C", 50), ItemType.Coal));
            labelled.Golem(new Vector2Int(4, 3), (AppendageActionType.Haul, ItemType.Coal), Push).Tick(0);
            yield return ("labelled crate", labelled);

            // The wrong good: the feeder hauls Coal, the golem behind wants Scrap.
            var wrong = new Floor();
            wrong.Crate(new Vector2Int(0, 0));
            wrong.Crate(new Vector2Int(0, 2));
            wrong.Golem(new Vector2Int(0, 1), (AppendageActionType.Haul, ItemType.Coal), Push);
            wrong.Golem(new Vector2Int(0, 3), HaulScrap, Push).Tick(0);
            yield return ("wrong good", wrong);

            // The Post: fed through another Post crate.
            var post = new Floor();
            var store = new StorageBuffer(SandboxWorld.PostBufferId, 200);
            post.Endpoints.Register(new Vector2Int(5, 2), new StorageBufferEndpoint(store));
            post.Endpoints.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(store));
            post.Crate(new Vector2Int(5, 0));
            post.Golem(new Vector2Int(5, 1), HaulScrap, Push);
            post.Golem(new Vector2Int(0, 3), HaulScrap, Push).Tick(0);
            yield return ("post", post);

            // A carried feeder feeds nothing.
            var carried = new Floor();
            carried.Crate(new Vector2Int(0, 0));
            carried.Crate(new Vector2Int(0, 2));
            carried.Golem(new Vector2Int(0, 1), HaulScrap, Push).SetHeld(true);
            carried.Golem(new Vector2Int(0, 3), HaulScrap, Push).Tick(0);
            yield return ("carried feeder", carried);
        }

        [Test]
        public void FeedersOf_TwoFeedersOneInAnEmptyLoop_NamesOnlyTheDeliveringOne()
        {
            // §17.5: B and C both push into the crate P hauls from. B's own source is empty and
            // unfed (red at the root, so it counts); C hauls from X, which only P fills: an empty
            // loop through P, which never starts on its own.
            var floor = new Floor();
            floor.Crate(new Vector2Int(0, 0));
            SharedCrate(floor, "P-src", new Vector2Int(0, 2), new Vector2Int(3, 2));
            SharedCrate(floor, "X", new Vector2Int(0, 4), new Vector2Int(3, 0));
            GolemEntity b = floor.Golem(new Vector2Int(0, 1), HaulScrap, Push);   // (0,0) → P-src
            GolemEntity c = floor.Golem(new Vector2Int(3, 1), HaulScrap, Push);   // X → P-src
            GolemEntity p = floor.Golem(new Vector2Int(0, 3), HaulScrap, Push);   // P-src → X
            b.Tick(0);
            c.Tick(0);
            p.Tick(0);

            Assert.IsTrue(floor.Probe.Read(p).SourceFed);
            CollectionAssert.AreEqual(new[] { b }, floor.Probe.FeedersOf(p));
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
