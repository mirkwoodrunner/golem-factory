using NUnit.Framework;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Ported from Unity's EditMode suite. Setup only: the marker and the node registry are plain
    // objects, where Unity built a GameObject with a SpriteRenderer and a registry holder.
    public class ResourceNodeMarkerTests
    {
        private static ResourceNodeMarker Build() => new ResourceNodeMarker();

        [Test]
        public void TryHarvest_RegisteredNode_ExtractsRealItemType()
        {
            ResourceNodeMarker marker = Build();
            var registry = new ResourceNodeRegistry();
            registry.Register(new ResourceNode("ScrapNode", "Scrap"));
            marker.Configure(registry, "ScrapNode");

            bool result = marker.TryHarvest(out ItemStack item);

            Assert.IsTrue(result);
            Assert.AreEqual("Scrap", item.ItemType);
        }

        [Test]
        public void TryHarvest_FiniteNode_Depletes()
        {
            ResourceNodeMarker marker = Build();
            var registry = new ResourceNodeRegistry();
            registry.Register(new ResourceNode("AetherNode", "Aether", remainingQuantity: 1));
            marker.Configure(registry, "AetherNode");

            Assert.IsTrue(marker.TryHarvest(out _));
            Assert.IsFalse(marker.TryHarvest(out _));
        }

        [Test]
        public void TryHarvest_UnknownNodeId_Fails()
        {
            ResourceNodeMarker marker = Build();
            var registry = new ResourceNodeRegistry();
            marker.Configure(registry, "NoSuchNode");

            Assert.IsFalse(marker.TryHarvest(out _));
        }

        [Test]
        public void TryHarvest_UnconfiguredRegistry_FailsWithoutThrowing()
        {
            ResourceNodeMarker marker = Build();

            Assert.DoesNotThrow(() => marker.TryHarvest(out _));
            Assert.IsFalse(marker.TryHarvest(out _));
        }
    }
}
