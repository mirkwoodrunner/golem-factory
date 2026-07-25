using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class ResourceNodeMarkerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private ResourceNodeMarker Build()
        {
            _root = new GameObject("Marker", typeof(SpriteRenderer));
            return _root.AddComponent<ResourceNodeMarker>();
        }

        [Test]
        public void TryHarvest_RegisteredNode_ExtractsRealItemType()
        {
            ResourceNodeMarker marker = Build();
            var holder = new GameObject("Nodes").AddComponent<ResourceNodeRegistryHolder>();
            holder.transform.SetParent(_root.transform);
            holder.Registry.Register(new ResourceNode("ScrapNode", "Scrap"));
            marker.Configure(holder, "ScrapNode");

            bool result = marker.TryHarvest(out ItemStack item);

            Assert.IsTrue(result);
            Assert.AreEqual("Scrap", item.ItemType);
        }

        [Test]
        public void TryHarvest_FiniteNode_Depletes()
        {
            ResourceNodeMarker marker = Build();
            var holder = new GameObject("Nodes").AddComponent<ResourceNodeRegistryHolder>();
            holder.transform.SetParent(_root.transform);
            holder.Registry.Register(new ResourceNode("AetherNode", "Aether", remainingQuantity: 1));
            marker.Configure(holder, "AetherNode");

            Assert.IsTrue(marker.TryHarvest(out _));
            Assert.IsFalse(marker.TryHarvest(out _));
        }

        [Test]
        public void TryHarvest_UnknownNodeId_Fails()
        {
            ResourceNodeMarker marker = Build();
            var holder = new GameObject("Nodes").AddComponent<ResourceNodeRegistryHolder>();
            holder.transform.SetParent(_root.transform);
            marker.Configure(holder, "NoSuchNode");

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
