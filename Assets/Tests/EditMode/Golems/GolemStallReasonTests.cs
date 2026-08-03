using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class GolemStallReasonTests
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

        private GolemEntity CreateExtractingGolem(
            ConveyorSystemHolder conveyor, ResourceNodeRegistryHolder nodes,
            string nodeId, string beltId)
        {
            var go = new GameObject("TestGolem");
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure("TestGolem", conveyor);
            entity.ConfigureEconomy(nodes, null);

            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;

            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.ExtractFromNode;
            step.sourceId = nodeId;
            step.destinationId = beltId;
            entity.Program.appendages.Add(step);

            return entity;
        }

        // --- The item-loss bug --------------------------------------------------------------
        // ExtractFromNode used to decrement the finite ResourceNode and *then* enqueue. A full
        // destination belt made the enqueue fail, so the extracted unit was dropped on the
        // floor -- material permanently destroyed out of a finite node, once per blocked tick.

        [Test]
        public void ExtractOntoAFullBelt_DoesNotConsumeFromTheFiniteNode()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();

            var segment = new BeltSegment("ScrapBeltA", 1);
            conveyor.System.Register(segment);
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 5));

            // Fill the belt so nothing more can be enqueued.
            while (segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap, Progress = 0f }))
            {
            }
            Assert.IsFalse(segment.CanEnqueue(), "precondition: belt must be full");

            GolemEntity entity = CreateExtractingGolem(conveyor, nodes, "ScrapNode", "ScrapBeltA");

            for (int tick = 0; tick < 10; tick++)
            {
                entity.Tick(tick);
            }

            ResourceNode node;
            Assert.IsTrue(nodes.Registry.TryGetNode("ScrapNode", out node));
            Assert.AreEqual(5, node.RemainingQuantity,
                "a blocked extract consumed from the node and dropped the item");
            Assert.AreEqual(GolemState.Stalled, entity.Program.State);
        }

        [Test]
        public void ExtractOntoAFullBelt_ReportsBeltFullAgainstTheDestinationId()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();

            var segment = new BeltSegment("ScrapBeltA", 1);
            conveyor.System.Register(segment);
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 5));
            while (segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap, Progress = 0f }))
            {
            }

            GolemEntity entity = CreateExtractingGolem(conveyor, nodes, "ScrapNode", "ScrapBeltA");
            entity.Tick(0);

            Assert.AreEqual(StallReason.BeltFull, entity.StallReason);
            Assert.AreEqual("ScrapBeltA", entity.StallResourceId);
        }

        [Test]
        public void ExtractFromADepletedNode_ReportsNodeEmptyAgainstTheSourceId()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();

            conveyor.System.Register(new BeltSegment("ScrapBeltA", 8));
            nodes.Registry.Register(new ResourceNode("AetherNode", ItemType.Aether, 0));

            GolemEntity entity = CreateExtractingGolem(conveyor, nodes, "AetherNode", "ScrapBeltA");
            entity.Tick(0);

            Assert.AreEqual(GolemState.Stalled, entity.Program.State);
            Assert.AreEqual(StallReason.NodeEmpty, entity.StallReason);
            Assert.AreEqual("AetherNode", entity.StallResourceId);
        }

        [Test]
        public void UnwiredGolem_ReportsUnconfiguredRatherThanAResourceFault()
        {
            var go = new GameObject("UnwiredGolem");
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();

            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.ExtractFromNode;
            entity.Program.appendages.Add(step);

            entity.Tick(0);

            Assert.AreEqual(StallReason.Unconfigured, entity.StallReason);
        }

        [Test]
        public void StallReason_IsNoneWhileTheGolemIsNotStalled()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            conveyor.System.Register(new BeltSegment("ScrapBeltA", 8));
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, ResourceNode.Infinite));

            GolemEntity entity = CreateExtractingGolem(conveyor, nodes, "ScrapNode", "ScrapBeltA");
            entity.Tick(0);

            Assert.AreNotEqual(GolemState.Stalled, entity.Program.State);
            Assert.AreEqual(StallReason.None, entity.StallReason);
            Assert.IsNull(entity.StallResourceId);
        }

        // --- Edge-triggered publishing ------------------------------------------------------
        // Republishing GolemStalledEvent every tick re-armed GolemVisual's stall shake at the
        // tick rate so the "single jolt" never decayed, and buried listeners wanting one
        // notification per incident.

        [Test]
        public void StayingStalled_PublishesOnceNotEveryTick()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            conveyor.System.Register(new BeltSegment("ScrapBeltA", 8));
            nodes.Registry.Register(new ResourceNode("AetherNode", ItemType.Aether, 0));

            GolemEntity entity = CreateExtractingGolem(conveyor, nodes, "AetherNode", "ScrapBeltA");

            int published = 0;
            System.Action<GolemStalledEvent> handler = delegate { published++; };
            EventBus.GolemStalled += handler;
            try
            {
                for (int tick = 0; tick < 10; tick++)
                {
                    entity.Tick(tick);
                }
            }
            finally
            {
                EventBus.GolemStalled -= handler;
            }

            Assert.AreEqual(1, published, "stall republished every tick instead of on the edge");
        }
    }
}
