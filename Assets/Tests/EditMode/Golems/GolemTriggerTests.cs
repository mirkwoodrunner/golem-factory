using NUnit.Framework;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // Threshold trigger only -- it's a direct poll of already-held state (no MonoBehaviour
    // lifecycle event subscription involved), so it works correctly in EditMode. Signal
    // trigger depends on GolemEntity.OnEnable subscribing to EventBus.GolemCompleted, which
    // Unity does not invoke for a plain MonoBehaviour outside Play Mode -- see
    // Tests/PlayMode/Golems/GolemSignalTriggerTests.cs for that coverage instead.
    public class GolemTriggerTests
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

        private GolemEntity Build(string golemId)
        {
            _root = new GameObject("Root");

            var bufferGo = new GameObject("Buffers");
            bufferGo.transform.SetParent(_root.transform);
            _bufferRegistry = bufferGo.AddComponent<StorageBufferRegistryHolder>();

            var golemGo = new GameObject(golemId);
            golemGo.transform.SetParent(_root.transform);
            var golem = golemGo.AddComponent<GolemEntity>();
            golem.Configure(golemId, null);
            golem.ConfigureEconomy(null, _bufferRegistry);
            return golem;
        }

        private static LogicCoreDefinition ThresholdCore(string bufferId, string itemType, int quantity)
        {
            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.Threshold;
            core.thresholdBufferId = bufferId;
            core.thresholdItemType = itemType;
            core.thresholdQuantity = quantity;
            return core;
        }

        // Haul defaults to a no-op success stub -- isolates trigger logic under test from
        // step-execution logic (Extract/Load/Refine), which is covered elsewhere.
        private static AppendageActionDefinition NoOpStep() =>
            ScriptableObject.CreateInstance<AppendageActionDefinition>();

        [Test]
        public void Threshold_BelowQuantity_DoesNotFire()
        {
            GolemEntity golem = Build("Golem");
            golem.Program.logicCore = ThresholdCore("Buffer", "Scrap", 10);
            golem.Program.appendages.Add(NoOpStep());
            _bufferRegistry.Registry.Deposit("Buffer", "Scrap", 5);

            golem.Tick(1);

            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        [Test]
        public void Threshold_AtOrAboveQuantity_Fires_PublishesThresholdCrossedEvent()
        {
            GolemEntity golem = Build("Golem");
            golem.Program.logicCore = ThresholdCore("Buffer", "Scrap", 10);
            golem.Program.appendages.Add(NoOpStep());
            _bufferRegistry.Registry.Deposit("Buffer", "Scrap", 10);

            ThresholdCrossedEvent? published = null;
            void OnCrossed(ThresholdCrossedEvent e) => published = e;
            EventBus.ThresholdCrossed += OnCrossed;
            try
            {
                golem.Tick(1);
            }
            finally
            {
                EventBus.ThresholdCrossed -= OnCrossed;
            }

            // Single no-op step completes the same tick it fires, wrapping back to Idle.
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
            Assert.IsTrue(published.HasValue);
            Assert.AreEqual("Buffer", published.Value.InventoryId);
            Assert.AreEqual(10, published.Value.Quantity);
        }

        [Test]
        public void Threshold_StaysAboveAfterFiring_DoesNotRefireEveryTick()
        {
            GolemEntity golem = Build("Golem");
            golem.Program.logicCore = ThresholdCore("Buffer", "Scrap", 10);
            golem.Program.appendages.Add(NoOpStep());
            _bufferRegistry.Registry.Deposit("Buffer", "Scrap", 10);

            golem.Tick(1);

            int firedCount = 0;
            void OnCrossed(ThresholdCrossedEvent e) => firedCount++;
            EventBus.ThresholdCrossed += OnCrossed;
            try
            {
                golem.Tick(2);
                golem.Tick(3);
            }
            finally
            {
                EventBus.ThresholdCrossed -= OnCrossed;
            }

            Assert.AreEqual(0, firedCount);
        }

        [Test]
        public void Threshold_DipsBelowThenReCrosses_FiresAgain()
        {
            GolemEntity golem = Build("Golem");
            golem.Program.logicCore = ThresholdCore("Buffer", "Scrap", 10);
            golem.Program.appendages.Add(NoOpStep());
            _bufferRegistry.Registry.Deposit("Buffer", "Scrap", 10);
            golem.Tick(1);

            _bufferRegistry.Registry.TryWithdraw("Buffer", "Scrap", 5);
            golem.Tick(2);
            _bufferRegistry.Registry.Deposit("Buffer", "Scrap", 5);

            int firedCount = 0;
            void OnCrossed(ThresholdCrossedEvent e) => firedCount++;
            EventBus.ThresholdCrossed += OnCrossed;
            try
            {
                golem.Tick(3);
            }
            finally
            {
                EventBus.ThresholdCrossed -= OnCrossed;
            }

            Assert.AreEqual(1, firedCount);
        }
    }
}
