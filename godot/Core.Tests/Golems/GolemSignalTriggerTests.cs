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
    }
}
