using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemExecutionTests
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

        private GolemEntity CreateEntity()
        {
            var go = new GameObject("TestGolem");
            _spawned.Add(go);
            return go.AddComponent<GolemEntity>();
        }

        [Test]
        public void NewProgram_StartsIdleAtStepZero()
        {
            var program = new GolemProgram();

            Assert.AreEqual(GolemState.Idle, program.State);
            Assert.AreEqual(0, program.CurrentStepIndex);
        }

        [Test]
        public void AdvanceStep_WrapsAroundToZero()
        {
            var program = new GolemProgram();
            program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());

            program.AdvanceStep();

            Assert.AreEqual(0, program.CurrentStepIndex);
        }

        [Test]
        public void Tick_AlwaysOnTrigger_MovesIdleGolemToRunningAndAdvancesStep()
        {
            GolemEntity entity = CreateEntity();
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());
            entity.Program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());

            entity.Tick(1);

            Assert.AreEqual(GolemState.Running, entity.Program.State);
            Assert.AreEqual(1, entity.Program.CurrentStepIndex);
        }

        [Test]
        public void Tick_CompletingLastStep_WrapsToIdleAndPublishesGolemCompleted()
        {
            GolemEntity entity = CreateEntity();
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());
            entity.Program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());

            string completedGolemId = null;
            void OnCompleted(GolemCompletedEvent e) => completedGolemId = e.GolemId;
            EventBus.GolemCompleted += OnCompleted;
            try
            {
                entity.Tick(1);
                entity.Tick(2);
            }
            finally
            {
                EventBus.GolemCompleted -= OnCompleted;
            }

            Assert.AreEqual(GolemState.Idle, entity.Program.State);
            Assert.AreEqual(0, entity.Program.CurrentStepIndex);
            Assert.AreEqual(entity.GolemId, completedGolemId);
        }

        [Test]
        public void Tick_IntervalTrigger_OnlyFiresOnMultiplesOfIntervalTicks()
        {
            GolemEntity entity = CreateEntity();
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.Interval;
            logicCore.intervalTicks = 3;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());

            entity.Tick(1);
            Assert.AreEqual(GolemState.Idle, entity.Program.State);

            entity.Tick(2);
            Assert.AreEqual(GolemState.Idle, entity.Program.State);

            entity.Tick(3);
            Assert.AreEqual(GolemState.Idle, entity.Program.State);
            Assert.AreEqual(0, entity.Program.CurrentStepIndex);
        }
    }
}
