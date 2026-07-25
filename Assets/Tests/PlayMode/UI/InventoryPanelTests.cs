using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Economy;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode: Refresh() instantiates real GameObjects under a RectTransform,
    // same UGUI-dynamic-content pattern WorkbenchControllerTests already exercises.
    public class InventoryPanelTests
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

        private (InventoryPanel panel, StorageBufferRegistryHolder holder, RectTransform content) Build()
        {
            _root = new GameObject("Root");
            var holder = _root.AddComponent<StorageBufferRegistryHolder>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_root.transform);
            var panel = _root.AddComponent<InventoryPanel>();
            panel.Configure(holder);
            panel.ConfigureUI(content);
            return (panel, holder, content);
        }

        [UnityTest]
        public IEnumerator Refresh_PopulatesOneRowPerBufferAndItemEntry()
        {
            (InventoryPanel panel, StorageBufferRegistryHolder holder, RectTransform content) = Build();
            holder.Registry.Deposit("FactoryStockpile", ItemType.Scrap);
            holder.Registry.Deposit("FactoryStockpile", ItemType.Brass);
            yield return null;

            panel.Refresh();

            // One header row for the buffer + one row per distinct item type it holds.
            Assert.AreEqual(3, content.childCount);
        }

        [UnityTest]
        public IEnumerator Refresh_ClearsStaleRowsBeforeRebuilding()
        {
            (InventoryPanel panel, StorageBufferRegistryHolder holder, RectTransform content) = Build();
            holder.Registry.Deposit("FactoryStockpile", ItemType.Scrap);
            yield return null;
            panel.Refresh();
            int firstCount = content.childCount;

            // ClearChildren uses Destroy(), which defers actual removal to end-of-frame --
            // the stale rows stay counted in childCount until then, so yield once after
            // Refresh() rebuilds (queuing their destruction) before reading childCount.
            panel.Refresh();
            yield return null;

            Assert.AreEqual(firstCount, content.childCount);
        }

        [UnityTest]
        public IEnumerator Refresh_NoBufferRegistryHolder_IsNoOp()
        {
            _root = new GameObject("Root");
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_root.transform);
            var panel = _root.AddComponent<InventoryPanel>();
            panel.ConfigureUI(content);
            yield return null;

            panel.Refresh();

            Assert.AreEqual(0, content.childCount);
        }
    }
}
