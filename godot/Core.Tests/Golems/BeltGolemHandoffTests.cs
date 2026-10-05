using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class BeltGolemHandoffTests
    {
        private StorageBufferRegistry _bufferRegistry;


        [Test]
        public void ExtractFromNode_PushesItemOntoNamedBelt_AndAdvancesStep()
        {
            (GolemEntity golem, ConveyorSystem holder) = Build();
            holder.Register(new BeltSegment("Belt", 5));
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(ExtractStep("Node", "Belt"));

            golem.Tick(1);

            Assert.IsTrue(holder.TryGetSegment("Belt", out BeltSegment segment));
            Assert.AreEqual(1, segment.Items.Count);
            Assert.AreEqual("Node", segment.Items[0].ItemType);
            Assert.AreEqual(GolemState.Idle, golem.Program.State);
        }

        [Test]
        public void ExtractFromNode_BeltFull_GolemStalls_PublishesGolemStalledEvent()
        {
            (GolemEntity golem, ConveyorSystem holder) = Build();
            var full = new BeltSegment("Belt", 1);
            full.TryEnqueue(new ItemStack { ItemType = "Blocker1" });
            full.Advance(1f);
            full.TryEnqueue(new ItemStack { ItemType = "Blocker2" });
            holder.Register(full);
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(ExtractStep("Node", "Belt"));

            string stalledGolemId = null;
            void OnStalled(GolemStalledEvent e) => stalledGolemId = e.GolemId;
            EventBus.GolemStalled += OnStalled;
            try
            {
                golem.Tick(1);
            }
            finally
            {
                EventBus.GolemStalled -= OnStalled;
            }

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(golem.GolemId, stalledGolemId);
        }

        [Test]
        public void ExtractFromNode_UnknownNodeId_GolemStalls()
        {
            (GolemEntity golem, ConveyorSystem holder) = Build();
            holder.Register(new BeltSegment("Belt", 5));
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(ExtractStep("NoSuchNode", "Belt"));

            golem.Tick(1);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
        }

        [Test]
        public void LoadIntoBuffer_BeltEmptyOrHeadNotYetAtEnd_GolemStalls()
        {
            (GolemEntity golem, ConveyorSystem holder) = Build();
            holder.Register(new BeltSegment("Belt", 5));
            golem.Program.logicCore = AlwaysOnCore();
            golem.Program.appendages.Add(LoadStep("Belt", "Buffer"));

            golem.Tick(1);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.IsFalse(_bufferRegistry.TryGetBuffer("Buffer", out _));
        }

        [Test]
        public void EndToEnd_TwoGolemsAcrossTwoChainedSegments_ItemReachesStorageBuffer()
        {
            var holder = new ConveyorSystem();
            var nodeRegistry = new ResourceNodeRegistry();
            nodeRegistry.Register(new ResourceNode("Node", "Node"));
            _bufferRegistry = new StorageBufferRegistry();

            var beltA = new BeltSegment("BeltA", 2);
            var beltB = new BeltSegment("BeltB", 2);
            beltA.Next = beltB;
            holder.Register(beltA);
            holder.Register(beltB);

            var golemA = new GolemEntity();
            golemA.Configure("GolemA", holder);
            golemA.ConfigureEconomy(nodeRegistry, _bufferRegistry);
            golemA.Program.logicCore = AlwaysOnCore();
            golemA.Program.appendages.Add(ExtractStep("Node", "BeltA"));

            var golemB = new GolemEntity();
            golemB.Configure("GolemB", holder);
            golemB.ConfigureEconomy(nodeRegistry, _bufferRegistry);
            golemB.Program.logicCore = AlwaysOnCore();
            golemB.Program.appendages.Add(LoadStep("BeltB", "Buffer"));

            var tickables = new List<GolemEntity> { golemA, golemB };
            for (long tick = 1; tick <= 10; tick++)
            {
                holder.Tick(tick);
                foreach (GolemEntity entity in tickables)
                {
                    entity.Tick(tick);
                }
            }

            // Multiple items may have crossed by tick 10 (both golems keep re-triggering
            // AlwaysOn); the flow-reaches-the-far-end behavior is what's under test here,
            // not an exact throughput count.
            Assert.GreaterOrEqual(_bufferRegistry.GetOrCreate("Buffer").GetQuantity("Node"), 1);
        }

        private (GolemEntity golem, ConveyorSystem holder) Build()
        {

            var holder = new ConveyorSystem();

            var nodeRegistry = new ResourceNodeRegistry();
            nodeRegistry.Register(new ResourceNode("Node", "Node"));

            _bufferRegistry = new StorageBufferRegistry();

            var golem = new GolemEntity();
            golem.Configure("Golem", holder);
            golem.ConfigureEconomy(nodeRegistry, _bufferRegistry);

            return (golem, holder);
        }

        private static LogicCoreDefinition AlwaysOnCore()
        {
            var core = new LogicCoreDefinition();
            core.triggerType = TriggerType.AlwaysOn;
            return core;
        }

        private static AppendageActionDefinition ExtractStep(string sourceId, string destinationId)
        {
            var step = new AppendageActionDefinition();
            step.actionType = AppendageActionType.ExtractFromNode;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            return step;
        }

        private static AppendageActionDefinition LoadStep(string sourceId, string destinationId)
        {
            var step = new AppendageActionDefinition();
            step.actionType = AppendageActionType.LoadIntoBuffer;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            return step;
        }
    }
}
