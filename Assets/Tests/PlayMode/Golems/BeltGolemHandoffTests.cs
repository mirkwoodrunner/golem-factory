using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Belts;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.PlayMode
{
    public class BeltGolemHandoffTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            DemoBuffer.ResetAll();
        }

        [UnityTest]
        public IEnumerator ExtractFromNode_PushesItemOntoNamedBelt_AndAdvancesStep()
        {
            (GolemEntity golem, ConveyorSystemHolder holder) = Build();
            holder.System.Register(new BeltSegment("Belt", 5));
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(ExtractStep("Node", "Belt"));

            golem.Tick(1);
            yield return null;

            Assert.IsTrue(holder.System.TryGetSegment("Belt", out BeltSegment segment));
            Assert.AreEqual(1, segment.Items.Count);
            Assert.AreEqual("Node", segment.Items[0].ItemType);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        [UnityTest]
        public IEnumerator ExtractFromNode_BeltFull_GolemStalls_PublishesGolemStalledEvent()
        {
            (GolemEntity golem, ConveyorSystemHolder holder) = Build();
            var full = new BeltSegment("Belt", 1);
            full.TryEnqueue(new ItemStack { ItemType = "Blocker1" });
            full.Advance(1f);
            full.TryEnqueue(new ItemStack { ItemType = "Blocker2" });
            holder.System.Register(full);
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(ExtractStep("Node", "Belt"));

            string stalledGolemId = null;
            void OnStalled(GolemStalledEvent e) => stalledGolemId = e.GolemId;
            EventBus.GolemStalled += OnStalled;
            try
            {
                golem.Tick(1);
                yield return null;
            }
            finally
            {
                EventBus.GolemStalled -= OnStalled;
            }

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(golem.GolemId, stalledGolemId);
        }

        [UnityTest]
        public IEnumerator LoadIntoBuffer_BeltEmptyOrHeadNotYetAtEnd_GolemStalls()
        {
            (GolemEntity golem, ConveyorSystemHolder holder) = Build();
            holder.System.Register(new BeltSegment("Belt", 5));
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(LoadStep("Belt", "Buffer"));

            golem.Tick(1);
            yield return null;

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(0, DemoBuffer.GetCount("Buffer"));
        }

        [UnityTest]
        public IEnumerator EndToEnd_TwoGolemsAcrossTwoChainedSegments_ItemReachesDemoBuffer()
        {
            _root = new GameObject("Root");
            var holder = new GameObject("Conveyor").AddComponent<ConveyorSystemHolder>();
            holder.transform.SetParent(_root.transform);

            var beltA = new BeltSegment("BeltA", 2);
            var beltB = new BeltSegment("BeltB", 2);
            beltA.Next = beltB;
            holder.System.Register(beltA);
            holder.System.Register(beltB);

            var golemA = new GameObject("GolemA").AddComponent<GolemEntity>();
            golemA.transform.SetParent(_root.transform);
            golemA.Configure("GolemA", holder);
            golemA.Program.logicCore = AlwaysOnCore();
            golemA.Program.appendages.Add(ExtractStep("Node", "BeltA"));

            var golemB = new GameObject("GolemB").AddComponent<GolemEntity>();
            golemB.transform.SetParent(_root.transform);
            golemB.Configure("GolemB", holder);
            golemB.Program.logicCore = AlwaysOnCore();
            golemB.Program.appendages.Add(LoadStep("BeltB", "Buffer"));

            var tickables = new List<GolemEntity> { golemA, golemB };
            for (long tick = 1; tick <= 10; tick++)
            {
                holder.System.Tick(tick);
                foreach (GolemEntity entity in tickables)
                {
                    entity.Tick(tick);
                }
            }
            yield return null;

            // Multiple items may have crossed by tick 10 (both golems keep re-triggering
            // AlwaysOn); the flow-reaches-the-far-end behavior is what's under test here,
            // not an exact throughput count.
            Assert.GreaterOrEqual(DemoBuffer.GetCount("Buffer"), 1);
        }

        private (GolemEntity golem, ConveyorSystemHolder holder) Build()
        {
            _root = new GameObject("Root");

            var holder = new GameObject("Conveyor").AddComponent<ConveyorSystemHolder>();
            holder.transform.SetParent(_root.transform);

            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", holder);

            return (golem, holder);
        }

        private static LogicCoreDefinition AlwaysOnCore()
        {
            var core = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            core.triggerType = TriggerType.AlwaysOn;
            return core;
        }

        private static AppendageActionDefinition ExtractStep(string sourceId, string destinationId)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.ExtractFromNode;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            return step;
        }

        private static AppendageActionDefinition LoadStep(string sourceId, string destinationId)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.LoadIntoBuffer;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            return step;
        }
    }
}
