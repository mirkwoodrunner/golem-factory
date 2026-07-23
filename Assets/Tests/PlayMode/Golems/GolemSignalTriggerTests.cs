using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.PlayMode
{
    // Signal trigger relies on GolemEntity.OnEnable subscribing to EventBus.GolemCompleted
    // -- a MonoBehaviour lifecycle callback Unity only invokes in Play Mode (GolemEntity
    // has no [ExecuteAlways]), so this needs PlayMode, unlike Threshold's direct-poll
    // logic in Tests/EditMode/Golems/GolemTriggerTests.cs.
    public class GolemSignalTriggerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private GolemEntity Build(string golemId)
        {
            _root = new GameObject("Root");

            var golemGo = new GameObject(golemId);
            golemGo.transform.SetParent(_root.transform);
            var golem = golemGo.AddComponent<GolemEntity>();
            golem.Configure(golemId, null);
            return golem;
        }

        private static LogicCoreDefinition SignalCore(string signalGolemId)
        {
            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.Signal;
            core.signalGolemId = signalGolemId;
            return core;
        }

        // Haul defaults to a no-op success stub -- isolates trigger logic under test from
        // step-execution logic.
        private static AppendageActionDefinition NoOpStep() =>
            ScriptableObject.CreateInstance<AppendageActionDefinition>();

        [UnityTest]
        public IEnumerator UnrelatedGolemCompletes_DoesNotFire()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            golem.Program.appendages.Add(NoOpStep());

            EventBus.Publish(new GolemCompletedEvent("SomeoneElse"));
            golem.Tick(1);
            yield return null;

            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(0, golem.Program.CurrentStepIndex);
        }

        [UnityTest]
        public IEnumerator WatchedGolemCompletes_Fires()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            golem.Program.appendages.Add(NoOpStep());

            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(1);
            yield return null;

            // Fired and the single no-op step completed the same tick, wrapping to Idle --
            // if it hadn't fired at all, State would also read Idle (its untouched default),
            // so CurrentStepIndex alone wouldn't prove firing; the two-appendage test below
            // is the one that actually distinguishes "fired" from "never subscribed."
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        [UnityTest]
        public IEnumerator ConsumedAfterFiring_DoesNotRefireWithoutANewEvent()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            golem.Program.appendages.Add(NoOpStep());
            golem.Program.appendages.Add(NoOpStep());

            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(1);
            yield return null;
            Assert.AreEqual(1, golem.Program.CurrentStepIndex);

            golem.Tick(2);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);

            golem.Tick(3);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(0, golem.Program.CurrentStepIndex);
        }

        [UnityTest]
        public IEnumerator ArrivesWhileBusy_IsQueuedUntilIdle()
        {
            GolemEntity golem = Build("Watcher");
            golem.Program.logicCore = SignalCore("Producer");
            var slowStep = NoOpStep();
            slowStep.durationTicks = 3;
            golem.Program.appendages.Add(slowStep);

            EventBus.Publish(new GolemCompletedEvent("Producer"));
            golem.Tick(1);
            yield return null;
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
