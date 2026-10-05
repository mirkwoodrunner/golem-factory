using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode InventoryPanelTests, ported onto Core's <see cref="InventoryReadout"/>
    /// (G8): the rows the Inventory tab draws, decided without a screen. Unity counted the
    /// children of a Content RectTransform; here the composed rows are counted directly. The
    /// icon slot is the screen's job and is drawn for every Item row, which the `management`
    /// scenario checks against the Godot screen.
    /// </summary>
    public class InventoryPanelTests
    {
        private static List<InventoryRow> Rows(StorageBufferRegistry registry, BufferRateTracker tracker = null) =>
            InventoryReadout.Compose(registry, tracker);

        [Test]
        public void Refresh_PopulatesOneRowPerBufferAndItemEntry()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("FactoryStockpile", ItemType.Scrap);
            registry.Deposit("FactoryStockpile", ItemType.Brass);

            // One header row for the buffer + one row per distinct item type it holds.
            Assert.AreEqual(3, Rows(registry).Count);
        }

        [Test]
        public void Refresh_ClearsStaleRowsBeforeRebuilding()
        {
            // Unity's panel destroyed and rebuilt its rows; composing twice must give the same
            // rows, not the first set again on top.
            var registry = new StorageBufferRegistry();
            registry.Deposit("FactoryStockpile", ItemType.Scrap);
            int first = Rows(registry).Count;
            Assert.AreEqual(first, Rows(registry).Count);
        }

        [Test]
        public void Refresh_NoBufferRegistryHolder_ExplainsItselfInsteadOfRenderingNothing()
        {
            List<InventoryRow> rows = Rows(null);
            Assert.AreEqual(1, rows.Count);
            StringAssert.Contains("unavailable", rows[0].Text);
        }

        [Test]
        public void Refresh_NoBuffersRegisteredYet_ShowsAnEmptyStateRow()
        {
            List<InventoryRow> rows = Rows(new StorageBufferRegistry());
            Assert.AreEqual(1, rows.Count);
            StringAssert.Contains("No stockpiles", rows[0].Text);
        }

        [Test]
        public void Refresh_ItemRow_ShowsQuantityAndAnIconSlot()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("FactoryStockpile", ItemType.Scrap, 17);

            InventoryRow item = Rows(registry)[1];

            Assert.AreEqual(InventoryRowKind.Item, item.Kind, "every item row is an Item row, which the screen gives an icon slot");
            Assert.AreEqual(ItemType.Scrap, item.ItemType, "the icon is looked up by the row's item type");
            Assert.AreEqual("17", item.QuantityText);
        }

        [Test]
        public void Refresh_WithNoRateHistoryYet_ShowsNoReadingRatherThanClaimingZero()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("FactoryStockpile", ItemType.Scrap, 3);

            Assert.AreEqual("--", Rows(registry, new BufferRateTracker())[1].RateText);
        }

        [Test]
        public void Refresh_WithSampledHistory_ShowsASignedRateAndTrendGlyph()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("FactoryStockpile", ItemType.Scrap, 5);
            var tracker = new BufferRateTracker();

            // A known series: +1 Scrap/second is exactly +60/min.
            for (int step = 0; step <= 5; step++)
            {
                tracker.Sample(step, "FactoryStockpile", ItemType.Scrap, step);
            }

            string rateText = Rows(registry, tracker)[1].RateText;
            StringAssert.Contains("+60/min", rateText);
            StringAssert.Contains(BufferTrendUtility.TrendGlyph(StockTrend.Rising), rateText);
        }
    }
}
