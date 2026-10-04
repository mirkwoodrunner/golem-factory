using NUnit.Framework;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class ResourceNodeTests
    {
        [Test]
        public void TryExtract_InfiniteNode_AlwaysSucceedsAndNeverDepletes()
        {
            var node = new ResourceNode("ScrapNode", "Scrap");

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(node.TryExtract(out ItemStack item));
                Assert.AreEqual("Scrap", item.ItemType);
            }
            Assert.IsFalse(node.IsDepleted);
        }

        [Test]
        public void TryExtract_FiniteNode_DecrementsRemainingQuantity()
        {
            var node = new ResourceNode("AetherNode", "Aether", remainingQuantity: 2);

            Assert.IsTrue(node.TryExtract(out _));
            Assert.AreEqual(1, node.RemainingQuantity);
        }

        [Test]
        public void TryExtract_FiniteNodeExhausted_FailsAndStaysDepleted()
        {
            var node = new ResourceNode("AetherNode", "Aether", remainingQuantity: 1);
            node.TryExtract(out _);

            Assert.IsTrue(node.IsDepleted);
            Assert.IsFalse(node.TryExtract(out ItemStack item));
            Assert.AreEqual(default(ItemStack).ItemType, item.ItemType);
        }
    }

    public class ResourceNodeRegistryTests
    {
        [Test]
        public void TryExtract_RegisteredNode_ReturnsItemFromThatNode()
        {
            var registry = new ResourceNodeRegistry();
            registry.Register(new ResourceNode("ScrapNode", "Scrap"));

            Assert.IsTrue(registry.TryExtract("ScrapNode", out ItemStack item));
            Assert.AreEqual("Scrap", item.ItemType);
        }

        [Test]
        public void TryExtract_UnknownNodeId_Fails()
        {
            var registry = new ResourceNodeRegistry();

            Assert.IsFalse(registry.TryExtract("NoSuchNode", out _));
        }

        [Test]
        public void TryExtract_NullNodeId_FailsWithoutThrowing()
        {
            var registry = new ResourceNodeRegistry();

            Assert.IsFalse(registry.TryExtract(null, out _));
        }
    }
}
