using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // Ported from Tests/PlayMode/Golems/GolemSignalTriggerTests.cs. In Unity this had to be
    // a PlayMode suite: the Signal subscription lived in GolemEntity.OnEnable, which Unity
    // only invokes in Play Mode. GolemEntity is a plain class now and the subscription is the
    // explicit Attach()/Detach() pair, so the same four cases run as ordinary unit tests.
    //
    // Changes from the original are setup only: Attach() where Unity ran OnEnable, Detach()
    // in TearDown where Unity destroyed the object (EventBus is static, so a golem left
    // attached would hear the NEXT test's events), [Test] for [UnityTest], and no
    // `yield return null` -- the frame it waited for did nothing the assertions read.
    public class GolemSignalTriggerTests
    {
        private readonly List<GolemEntity> _attached = new List<GolemEntity>();

        [TearDown]
        public void TearDown()
        {
            foreach (GolemEntity golem in _attached)
            {
                golem.Detach();
            }
            _attached.Clear();
        }

        private GolemEntity Build(string golemId)
        {
            var golem = new GolemEntity();
            golem.Configure(golemId, null);
            golem.Attach();
            _attached.Add(golem);
            return golem;
        }

        private static LogicCoreDefinition SignalCore(string signalGolemId)
        {
            var core = new LogicCoreDefinition();
            core.triggerType = TriggerType.Signal;
            core.signalGolemId = signalGolemId;
            return core;
        }

        // Haul defaults to a no-op success stub -- isolates trigger logic under test from
        // step-execution logic.
        private static AppendageActionDefinition NoOpStep() => new AppendageActionDefinition();

        [Test]
        public void UnrelatedGolemCompletes_DoesNotFire()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            golem.Program.appendages.Add(NoOpStep());

            EventBus.Publish(new GolemCompletedEvent("SomeoneElse"));
            golem.Tick(1);

            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(0, golem.Program.CurrentStepIndex);
        }

        [Test]
        public void WatchedGolemCompletes_Fires()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            golem.Program.appendages.Add(NoOpStep());

            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(1);

            // Fired and the single no-op step completed the same tick, wrapping to Idle --
            // if it hadn't fired at all, State would also read Idle (its untouched default),
            // so CurrentStepIndex alone wouldn't prove firing; the two-appendage test below
            // is the one that actually distinguishes "fired" from "never subscribed."
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        [Test]
        public void ConsumedAfterFiring_DoesNotRefireWithoutANewEvent()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            golem.Program.appendages.Add(NoOpStep());
            golem.Program.appendages.Add(NoOpStep());

            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(1);
            Assert.AreEqual(1, golem.Program.CurrentStepIndex);

            golem.Tick(2);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);

            golem.Tick(3);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(0, golem.Program.CurrentStepIndex);
        }

        [Test]
        public void ArrivesWhileBusy_IsQueuedUntilIdle()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            var slowStep = NoOpStep();
            slowStep.durationTicks = 3;
            golem.Program.appendages.Add(slowStep);

            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(1);
            Assert.AreEqual(GolemState.Running, golem.Program.State);

            // A second signal arrives mid-cycle; queued rather than affecting the
            // currently-running step.
            EventBus.Publish(new GolemCompletedEvent("Producer"));

            golem.Tick(2);
            golem.Tick(3);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);

            golem.Tick(4);
            Assert.AreEqual(GolemState.Running, golem.Program.State);
        }

        // --- The subscription follows the core ------------------------------------------------
        // Only a golem with a Signal core is on the bus (GolemEntity.RefreshSignalSubscription).

        [Test]
        public void AGolemWithNoSignalCore_IsNotOnTheBus()
        {
            int before = EventBus.ListenerCount;

            GolemEntity golem = Build("Worker");
            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };

            Assert.AreEqual(before, EventBus.ListenerCount, "attached, but with nothing to hear");
        }

        [Test]
        public void ASignalCoreAssignedAfterAttach_IsHeardAtOnce_AndDroppedWhenReplaced()
        {
            int before = EventBus.ListenerCount;
            GolemEntity golem = Build("Watcher");
            golem.Program.appendages.Add(NoOpStep());

            golem.Program.logicCore = SignalCore("Producer");
            Assert.AreEqual(before + 1, EventBus.ListenerCount);
            EventBus.Publish(new GolemCompletedEvent("Producer")); // no tick in between
            Assert.IsTrue(golem.Program.PendingSignal);

            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
            Assert.AreEqual(before, EventBus.ListenerCount, "no Signal core, no listener");

            golem.Program.logicCore = SignalCore("Producer");
            golem.Detach();
            Assert.AreEqual(before, EventBus.ListenerCount, "detached, it hears nothing");
        }

        [Test]
        public void ACoreEditedInPlaceIntoASignal_IsHeardFromItsNextTriggerCheck()
        {
            GolemEntity golem = Build("Watcher");
            var core = new LogicCoreDefinition { triggerType = TriggerType.Interval, intervalTicks = 0 };
            golem.Program.logicCore = core;
            golem.Program.appendages.Add(NoOpStep());
            golem.Program.appendages.Add(NoOpStep());

            core.triggerType = TriggerType.Signal; // edited where it stands: nothing is told
            core.signalGolemId = "Producer";
            golem.Tick(1);                         // an idle golem checks its trigger, and subscribes
            Assert.AreEqual(0, golem.Program.CurrentStepIndex, "no signal yet");
            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(2);

            Assert.AreEqual(1, golem.Program.CurrentStepIndex, "fired: the first of its two steps ran");
        }

        [Test]
        public void AGolemCountsItsOwnCyclesAndStalls_AsItPublishesThem()
        {
            int completed = 0, stalled = 0;
            System.Action<GolemCompletedEvent> onCompleted = e => completed += e.GolemId == "Counter" ? 1 : 0;
            System.Action<GolemStalledEvent> onStalled = e => stalled += e.GolemId == "Counter" ? 1 : 0;
            EventBus.GolemCompleted += onCompleted;
            EventBus.GolemStalled += onStalled;
            try
            {
                GolemEntity golem = Build("Counter");
                golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
                golem.Program.appendages.Add(NoOpStep());
                for (int tick = 1; tick <= 5; tick++)
                {
                    golem.Tick(tick);
                }
                Assert.Greater(completed, 0);
                Assert.AreEqual(completed, golem.CompletedCycles);

                // A Push with no tile in front: one stall, however many ticks it is retried.
                golem.Program.appendages.Clear();
                golem.Program.appendages.Add(new AppendageActionDefinition { actionType = AppendageActionType.Push });
                golem.Program.CurrentStepIndex = 0;
                golem.Program.StepProgressTicks = 0;
                for (int tick = 6; tick <= 10; tick++)
                {
                    golem.Tick(tick);
                }
                Assert.AreEqual(1, stalled);
                Assert.AreEqual(stalled, golem.StallIncidents);
            }
            finally
            {
                EventBus.GolemCompleted -= onCompleted;
                EventBus.GolemStalled -= onStalled;
            }
        }
    }
}
