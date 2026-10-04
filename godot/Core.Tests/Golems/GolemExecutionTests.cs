using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemExecutionTests
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

        private GolemEntity CreateEntity()
        {
            return new GolemEntity();
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
            program.appendages.Add(new AppendageActionDefinition());

            program.AdvanceStep();

            Assert.AreEqual(0, program.CurrentStepIndex);
        }

        [Test]
        public void Tick_AlwaysOnTrigger_MovesIdleGolemToRunningAndAdvancesStep()
        {
            GolemEntity entity = CreateEntity();
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(new AppendageActionDefinition());
            entity.Program.appendages.Add(new AppendageActionDefinition());

            entity.Tick(1);

            Assert.AreEqual(GolemState.Running, entity.Program.State);
            Assert.AreEqual(1, entity.Program.CurrentStepIndex);
        }

        [Test]
        public void Tick_CompletingLastStep_WrapsToIdleAndPublishesGolemCompleted()
        {
            GolemEntity entity = CreateEntity();
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(new AppendageActionDefinition());
            entity.Program.appendages.Add(new AppendageActionDefinition());

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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.Interval;
            logicCore.intervalTicks = 3;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(new AppendageActionDefinition());

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
