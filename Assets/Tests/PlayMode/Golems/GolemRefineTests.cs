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
    public class GolemRefineTests
    {
        private GameObject _root;
        private StorageBufferRegistryHolder _bufferRegistry;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [UnityTest]
        public IEnumerator Refine_NoInputAvailable_GolemStalls_NoOutputProduced()
        {
            GolemEntity golem = Build();
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 3));

            golem.Tick(1);
            yield return null;

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(0, golem.Program.StepProgressTicks);
            Assert.AreEqual(0, _bufferRegistry.Registry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
        }

        [UnityTest]
        public IEnumerator Refine_WithInput_WithdrawsImmediately_StaysRunningWhileProcessing()
        {
            GolemEntity golem = Build();
            _bufferRegistry.Registry.Deposit("ScrapBuffer", "Scrap", 1);
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 3));

            golem.Tick(1);
            yield return null;

            // Input is withdrawn up front (start-of-processing commit), but output hasn't
            // appeared yet -- still mid-cycle, not stalled.
            Assert.AreEqual(0, _bufferRegistry.Registry.GetOrCreate("ScrapBuffer").GetQuantity("Scrap"));
            Assert.AreEqual(0, _bufferRegistry.Registry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Assert.AreEqual(1, golem.Program.StepProgressTicks);
        }

        [UnityTest]
        public IEnumerator Refine_CompletesAfterDurationTicks_DepositsOutput_AdvancesStep_PublishesCompleted()
        {
            GolemEntity golem = Build();
            _bufferRegistry.Registry.Deposit("ScrapBuffer", "Scrap", 1);
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 3));

            string completedGolemId = null;
            void OnCompleted(GolemCompletedEvent e) => completedGolemId = e.GolemId;
            EventBus.GolemCompleted += OnCompleted;
            try
            {
                golem.Tick(1);
                golem.Tick(2);
                Assert.AreEqual(0, _bufferRegistry.Registry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));

                golem.Tick(3);
                yield return null;
            }
            finally
            {
                EventBus.GolemCompleted -= OnCompleted;
            }

            Assert.AreEqual(1, _bufferRegistry.Registry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.AreEqual(0, golem.Program.StepProgressTicks);
            Assert.AreEqual(golem.GolemId, completedGolemId);
        }

        [UnityTest]
        public IEnumerator Refine_StalledThenInputArrives_ResumesFromBeginAndCompletes()
        {
            GolemEntity golem = Build();
            golem.Program.appendages.Add(RefineStep("ScrapBuffer", "BrassBuffer", "Scrap", "Brass", 2));

            golem.Tick(1);
            Assert.AreEqual(GolemState.Stalled, golem.Program.State);

            _bufferRegistry.Registry.Deposit("ScrapBuffer", "Scrap", 1);
            golem.Tick(2);
            Assert.AreEqual(GolemState.Running, golem.Program.State);

            golem.Tick(3);
            yield return null;

            Assert.AreEqual(1, _bufferRegistry.Registry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        private GolemEntity Build()
        {
            _root = new GameObject("Root");

            _bufferRegistry = new GameObject("Buffers").AddComponent<StorageBufferRegistryHolder>();
            _bufferRegistry.transform.SetParent(_root.transform);

            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", null);
            golem.ConfigureEconomy(null, _bufferRegistry);
            golem.Program.logicCore = AlwaysOnCore();

            return golem;
        }

        private static LogicCoreDefinition AlwaysOnCore()
        {
            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.AlwaysOn;
            return core;
        }

        private static AppendageActionDefinition RefineStep(
            string sourceId, string destinationId, string inputItemType, string outputItemType, int durationTicks)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
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
