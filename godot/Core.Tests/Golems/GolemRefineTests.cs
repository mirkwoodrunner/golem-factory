using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemRefineTests
    {
        private StorageBufferRegistry _bufferRegistry;


        [Test]
        public void Refine_NoInputAvailable_GolemStalls_NoOutputProduced()
        {
            GolemEntity golem = Build();
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 3));

            golem.Tick(1);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(0, golem.Program.StepProgressTicks);
            Assert.AreEqual(0, _bufferRegistry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
        }

        [Test]
        public void Refine_WithInput_WithdrawsImmediately_StaysRunningWhileProcessing()
        {
            GolemEntity golem = Build();
            _bufferRegistry.Deposit("ScrapBuffer", "Scrap", 1);
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 3));

            golem.Tick(1);

            // Input is withdrawn up front (start-of-processing commit), but output hasn't
            // appeared yet -- still mid-cycle, not stalled.
            Assert.AreEqual(0, _bufferRegistry.GetOrCreate("ScrapBuffer").GetQuantity("Scrap"));
            Assert.AreEqual(0, _bufferRegistry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Assert.AreEqual(1, golem.Program.StepProgressTicks);
        }

        [Test]
        public void Refine_CompletesAfterDurationTicks_DepositsOutput_AdvancesStep_PublishesCompleted()
        {
            GolemEntity golem = Build();
            _bufferRegistry.Deposit("ScrapBuffer", "Scrap", 1);
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 3));

            string completedGolemId = null;
            void OnCompleted(GolemCompletedEvent e) => completedGolemId = e.GolemId;
            EventBus.GolemCompleted += OnCompleted;
            try
            {
                golem.Tick(1);
                golem.Tick(2);
                Assert.AreEqual(0, _bufferRegistry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));

                golem.Tick(3);
            }
            finally
            {
                EventBus.GolemCompleted -= OnCompleted;
            }

            Assert.AreEqual(1, _bufferRegistry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(0, golem.Program.StepProgressTicks);
            Assert.AreEqual(golem.GolemId, completedGolemId);
        }

        [Test]
        public void Refine_StalledThenInputArrives_ResumesFromBeginAndCompletes()
        {
            GolemEntity golem = Build();
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 2));

            int resumedCount = 0;
            string resumedGolemId = null;
            void OnResumed(GolemResumedEvent e) { resumedCount++; resumedGolemId = e.GolemId; }
            EventBus.GolemResumed += OnResumed;
            try
            {
                golem.Tick(1);
                Assert.AreEqual(GolemState.Stalled, golem.Program.State);
                Assert.AreEqual(0, resumedCount);

                _bufferRegistry.Deposit("ScrapBuffer", "Scrap", 1);
                golem.Tick(2);
                Assert.AreEqual(GolemState.Running, golem.Program.State);
                Assert.AreEqual(1, resumedCount);
                Assert.AreEqual(golem.GolemId, resumedGolemId);

                golem.Tick(3);

                // Resumed fires exactly once, at the stalled->unstalled transition -- not
                // again on the tick that completes the (already-resumed) cycle.
                Assert.AreEqual(1, resumedCount);
            }
            finally
            {
                EventBus.GolemResumed -= OnResumed;
            }

            Assert.AreEqual(1, _bufferRegistry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        [Test]
        public void Refine_NeverStalled_DoesNotPublishGolemResumedEvent()
        {
            GolemEntity golem = Build();
            _bufferRegistry.Deposit("ScrapBuffer", "Scrap", 1);
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 2));

            bool resumedFired = false;
            void OnResumed(GolemResumedEvent e) => resumedFired = true;
            EventBus.GolemResumed += OnResumed;
            try
            {
                golem.Tick(1);
                golem.Tick(2);
            }
            finally
            {
                EventBus.GolemResumed -= OnResumed;
            }

            Assert.IsFalse(resumedFired);
        }

        private GolemEntity Build()
        {

            _bufferRegistry = new StorageBufferRegistry();

            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            golem.ConfigureEconomy(null, _bufferRegistry);
            golem.Program.logicCore = AlwaysOnCore();

            return golem;
        }

        private static LogicCoreDefinition AlwaysOnCore()
        {
            var core = new LogicCoreDefinition();
            core.triggerType = TriggerType.AlwaysOn;
            return core;
        }

        private static AppendageActionDefinition RefineStep(
            string sourceId, string destinationId, string inputItemType, string outputItemType, int durationTicks)
        {
            var step = new AppendageActionDefinition();
            step.actionType = AppendageActionType.Refine;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            step.inputItemType = inputItemType;
            step.outputItemType = outputItemType;
            step.durationTicks = durationTicks;
            return step;
        }
    }
}
