using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TMPro;
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
        public IEnumerator Refresh_NoBufferRegistryHolder_ExplainsItselfInsteadOfRenderingNothing()
        {
            _root = new GameObject("Root");
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_root.transform);
            var panel = _root.AddComponent<InventoryPanel>();
            panel.ConfigureUI(content);
            yield return null;

            panel.Refresh();

            // An unwired tab used to render as a silently blank panel, which is
            // indistinguishable from "you own nothing yet". It now says which it is.
            Assert.AreEqual(1, content.childCount);
            TextMeshProUGUI message = content.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
            StringAssert.Contains("unavailable", message.text);
        }

        [UnityTest]
        public IEnumerator Refresh_NoBuffersRegisteredYet_ShowsAnEmptyStateRow()
        {
            (InventoryPanel panel, StorageBufferRegistryHolder _, RectTransform content) = Build();
            yield return null;

            panel.Refresh();

            Assert.AreEqual(1, content.childCount);
            TextMeshProUGUI message = content.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
            StringAssert.Contains("No stockpiles", message.text);
        }

        [UnityTest]
        public IEnumerator Refresh_ItemRow_ShowsQuantityAndAnIconSlot()
        {
            (InventoryPanel panel, StorageBufferRegistryHolder holder, RectTransform content) = Build();
            holder.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 17);
            yield return null;

            panel.Refresh();

            // Row 0 is the buffer header; row 1 is the Scrap entry.
            Transform itemRow = content.GetChild(1);
            Assert.IsNotNull(itemRow.Find("Icon"), "Every item row keeps an icon slot so text stays left-aligned");
            Assert.IsNotNull(itemRow.Find("Bar"));
            Assert.AreEqual("17", itemRow.Find("Quantity").GetComponent<TextMeshProUGUI>().text);
        }

        [UnityTest]
        public IEnumerator Refresh_WithNoRateHistoryYet_ShowsNoReadingRatherThanClaimingZero()
        {
            (InventoryPanel panel, StorageBufferRegistryHolder holder, RectTransform content) = Build();
            holder.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 3);
            yield return null;

            panel.Refresh();

            // "no reading yet" and "genuinely flat" are different claims; the panel must
            // not print 0/min for the first.
            Assert.AreEqual("--", content.GetChild(1).Find("Rate").GetComponent<TextMeshProUGUI>().text);
        }

        [UnityTest]
        public IEnumerator Refresh_WithSampledHistory_ShowsASignedRateAndTrendGlyph()
        {
            (InventoryPanel panel, StorageBufferRegistryHolder holder, RectTransform content) = Build();
            var monitor = _root.AddComponent<BufferThroughputMonitor>();
            monitor.Configure(holder);
            panel.ConfigureThroughput(monitor);
            // Disabled so its Update() can't interleave a real Time.time sample with the
            // synthetic series below -- the test-runner clock is already many seconds in,
            // which would age every synthetic sample straight out of the 8s window.
            monitor.enabled = false;

            holder.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 5);
            yield return null;

            // Drive the tracker directly with a known series: +1 Scrap/second is exactly
            // +60/min.
            for (int step = 0; step <= 5; step++)
            {
                monitor.Tracker.Sample(step, "FactoryStockpile", ItemType.Scrap, step);
            }

            panel.Refresh();

            string rateText = content.GetChild(1).Find("Rate").GetComponent<TextMeshProUGUI>().text;
            StringAssert.Contains("+60/min", rateText);
            StringAssert.Contains(BufferTrendUtility.TrendGlyph(StockTrend.Rising), rateText);
        }
    }
}
