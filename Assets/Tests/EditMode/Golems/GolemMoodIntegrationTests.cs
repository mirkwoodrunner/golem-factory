using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// <c>GolemEntity.Mood</c> against a golem that is actually ticking, rather than against
    /// hand-supplied arguments -- so the wiring between the state machine and the classifier is
    /// pinned, not just the classifier.
    /// </summary>
    public class GolemMoodIntegrationTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                Object.DestroyImmediate(go);
            }

            _spawned.Clear();
        }

        private T AddHolder<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private GolemEntity NewGolem(string id = "Moody")
        {
            var go = new GameObject(id);
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure(id, null);
            return entity;
        }

        private static AppendageActionDefinition Step(AppendageActionType type, int durationTicks = 1)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = type;
            step.durationTicks = durationTicks;
            return step;
        }

        private static LogicCoreDefinition AlwaysOn()
        {
            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.AlwaysOn;
            return core;
        }

        [Test]
        public void AGolemWithNoLogicCoreIsUnprogrammed()
        {
            // The exact state a golem sits in between leaving the construction station and
            // being committed at the Workbench -- which is when the player most needs telling.
            GolemEntity golem = NewGolem();
            golem.Program.appendages.Add(Step(AppendageActionType.Push));

            Assert.IsFalse(golem.HasRunnableProgram);
            Assert.AreEqual(GolemMood.Unprogrammed, golem.Mood);
        }

        [Test]
        public void AGolemWithNoStepsIsUnprogrammed()
        {
            GolemEntity golem = NewGolem();
            golem.Program.logicCore = AlwaysOn();

            Assert.IsFalse(golem.HasRunnableProgram);
            Assert.AreEqual(GolemMood.Unprogrammed, golem.Mood);
        }

        [Test]
        public void AChassisIsNotWhatMakesAProgramRunnable()
        {
            // Deliberate: a chassis governs CAPACITY, and a golem with steps and no chassis
            // executes them perfectly well. Every hand-wired test golem in this suite is one.
            GolemEntity golem = NewGolem();
            golem.Program.logicCore = AlwaysOn();
            golem.Program.appendages.Add(Step(AppendageActionType.Push));

            Assert.IsNull(golem.Program.chassis);
            Assert.IsTrue(golem.HasRunnableProgram);
            Assert.AreNotEqual(GolemMood.Unprogrammed, golem.Mood);
        }

        [Test]
        public void AGolemFacingNothingReadsAsStalled()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            GolemEntity golem = NewGolem();
            golem.Program.logicCore = AlwaysOn();
            golem.Program.appendages.Add(Step(AppendageActionType.Push));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, 1);

            golem.Tick(0);

            Assert.AreEqual(StallReason.NoTargetAtTile, golem.StallReason);
            Assert.AreEqual(GolemMood.Stalled, golem.Mood);
        }

        [Test]
        public void AFullHoldOnARunningGolemReadsAsStraining()
        {
            var stockpile = new StorageBuffer("Stockpile");
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            // A source that will never run out, so the golem keeps Hauling and its input fills.
            var source = new StorageBuffer("Source");
            source.Deposit(ItemType.Scrap, 500);
            endpoints.Registry.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(source));
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(stockpile));

            GolemEntity golem = NewGolem();
            golem.Program.logicCore = AlwaysOn();
            golem.Program.appendages.Add(Step(AppendageActionType.Haul, 40));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);
            golem.Inventory.AddInput(ItemType.Scrap, GolemMoodRules.StrainingUnits);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Assert.AreEqual(GolemMood.Straining, golem.Mood, "a long Haul with a nearly-full hold");
        }

        [Test]
        public void AFinishedCycleReadsAsSleeping()
        {
            // An Interval trigger that will not fire again for a long time, which is the case
            // "asleep" is genuinely the right word for.
            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.Interval;
            core.intervalTicks = 1000;

            GolemEntity golem = NewGolem();
            golem.Program.logicCore = core;
            golem.Program.appendages.Add(Step(AppendageActionType.Haul));

            // Tick 0 satisfies 0 % 1000 == 0 and runs the one step, wrapping to Idle.
            golem.Tick(0);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(GolemMood.Sleeping, golem.Mood);

            // And it stays asleep across ticks that do not admit a cycle.
            golem.Tick(1);
            Assert.AreEqual(GolemMood.Sleeping, golem.Mood);
        }

        [Test]
        public void MoodIsReadOnly_AndSurvivesAGolemWithNoProgramObject()
        {
            // Defensive: Mood is presentation and must never be the thing that throws in a
            // frame loop.
            GolemEntity golem = NewGolem();
            Assert.AreEqual(GolemMood.Unprogrammed, golem.Mood);
            Assert.AreEqual(0, golem.Inventory.FullestTypeUnits);
        }

        [Test]
        public void FullestTypeUnitsSpansBothStocks()
        {
            // Both, because both can stall the golem and for different reasons: a full input
            // slot stops the next Haul, a full output slot stops the next Assemble.
            GolemEntity golem = NewGolem();
            golem.Inventory.AddInput(ItemType.Scrap, 3);
            golem.Inventory.AddOutput(ItemType.IronPlate, 7);

            Assert.AreEqual(7, golem.Inventory.FullestTypeUnits);

            golem.Inventory.AddInput(ItemType.Coke, 11);
            Assert.AreEqual(11, golem.Inventory.FullestTypeUnits);
        }
    }
}
