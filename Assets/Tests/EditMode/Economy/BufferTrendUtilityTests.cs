using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    // Pure math, so EditMode with no scene -- same split as GridCoordinateConverterTests /
    // BeltFlowUtilityTests / StallDiagnosticsTests.
    //
    // The rate readout is the one number in the Management HUD a player would actually
    // plan around, so these assert the exact slope against hand-computed series rather
    // than only checking a sign.
    public class BufferTrendUtilityTests
    {
        [Test]
        public void TryComputeRatePerMinute_ExactlyLinearRise_ReturnsExactSlopeScaledToMinutes()
        {
            // 2 items per second over 4 seconds == 120 items per minute, exactly.
            var times = new List<float> { 0f, 1f, 2f, 3f, 4f };
            var quantities = new List<int> { 0, 2, 4, 6, 8 };

            float rate;
            Assert.IsTrue(BufferTrendUtility.TryComputeRatePerMinute(times, quantities, out rate));
            Assert.AreEqual(120f, rate, 0.001f);
        }

        [Test]
        public void TryComputeRatePerMinute_ExactlyLinearFall_ReturnsNegativeSlope()
        {
            // -1 item per second == -60 per minute.
            var times = new List<float> { 10f, 11f, 12f, 13f };
            var quantities = new List<int> { 30, 29, 28, 27 };

            float rate;
            Assert.IsTrue(BufferTrendUtility.TryComputeRatePerMinute(times, quantities, out rate));
            Assert.AreEqual(-60f, rate, 0.001f);
        }

        [Test]
        public void TryComputeRatePerMinute_FlatSeries_ReturnsZero()
        {
            var times = new List<float> { 0f, 1f, 2f, 3f };
            var quantities = new List<int> { 12, 12, 12, 12 };

            float rate;
            Assert.IsTrue(BufferTrendUtility.TryComputeRatePerMinute(times, quantities, out rate));
            Assert.AreEqual(0f, rate, 0.0001f);
        }

        [Test]
        public void TryComputeRatePerMinute_IsALeastSquaresFit_NotAnEndpointDelta()
        {
            // Quantities move in whole-item steps, so where the window edges land changes a
            // first-to-last delta materially. This staircase sits one step below its own
            // trend line at both ends, so an endpoint delta under-reads it.
            var times = new List<float> { 0f, 1f, 2f, 3f, 4f, 5f };
            var quantities = new List<int> { 0, 0, 1, 1, 2, 2 };

            float rate;
            Assert.IsTrue(BufferTrendUtility.TryComputeRatePerMinute(times, quantities, out rate));

            float endpointDeltaPerMinute = (quantities[5] - quantities[0]) / (times[5] - times[0]) * 60f;
            Assert.AreEqual(24f, endpointDeltaPerMinute, 0.001f);
            // Hand-computed least squares: mean t = 2.5, mean q = 1;
            // cov = 2.5 + 1.5 + 0 + 0 + 1.5 + 2.5 = 8; var = 17.5; slope = 8/17.5 = 0.457142/s.
            Assert.AreEqual(8f / 17.5f * 60f, rate, 0.01f);
            Assert.AreNotEqual(endpointDeltaPerMinute, rate);
        }

        [Test]
        public void TryComputeRatePerMinute_FewerThanTwoSamples_ReturnsFalse()
        {
            float rate;
            Assert.IsFalse(BufferTrendUtility.TryComputeRatePerMinute(
                new List<float> { 1f }, new List<int> { 5 }, out rate));
            Assert.AreEqual(0f, rate);
        }

        [Test]
        public void TryComputeRatePerMinute_SpanShorterThanMinimum_ReturnsFalse()
        {
            // Two samples 20ms apart straddling one deposit would extrapolate to 3000/min.
            var times = new List<float> { 0f, 0.02f };
            var quantities = new List<int> { 0, 1 };

            float rate;
            Assert.IsFalse(BufferTrendUtility.TryComputeRatePerMinute(times, quantities, out rate));
        }

        [Test]
        public void TryComputeRatePerMinute_NullInputs_ReturnFalse()
        {
            float rate;
            Assert.IsFalse(BufferTrendUtility.TryComputeRatePerMinute(null, new List<int> { 1, 2 }, out rate));
            Assert.IsFalse(BufferTrendUtility.TryComputeRatePerMinute(new List<float> { 0f, 2f }, null, out rate));
        }

        [Test]
        public void TryComputeRatePerMinute_AllSamplesAtOneTimestamp_ReturnsFalse()
        {
            // Zero variance in t: a vertical line has no slope to report.
            var times = new List<float> { 5f, 5f, 5f };
            var quantities = new List<int> { 1, 4, 9 };

            float rate;
            Assert.IsFalse(BufferTrendUtility.TryComputeRatePerMinute(times, quantities, out rate));
        }

        [Test]
        public void Classify_UsesDeadbandInBothDirections()
        {
            Assert.AreEqual(StockTrend.Rising, BufferTrendUtility.Classify(120f));
            Assert.AreEqual(StockTrend.Falling, BufferTrendUtility.Classify(-120f));
            Assert.AreEqual(StockTrend.Steady, BufferTrendUtility.Classify(0f));
            // Exactly on the deadband is Steady -- strictly greater/less than is what makes
            // the boundary unambiguous.
            Assert.AreEqual(StockTrend.Steady, BufferTrendUtility.Classify(BufferTrendUtility.DefaultDeadbandPerMinute));
            Assert.AreEqual(StockTrend.Steady, BufferTrendUtility.Classify(-BufferTrendUtility.DefaultDeadbandPerMinute));
            Assert.AreEqual(StockTrend.Rising, BufferTrendUtility.Classify(BufferTrendUtility.DefaultDeadbandPerMinute + 0.01f));
        }

        [Test]
        public void FormatRate_AlwaysSignsIncome_AndCollapsesSubDeadbandToZero()
        {
            Assert.AreEqual("+120/min", BufferTrendUtility.FormatRate(120f));
            Assert.AreEqual("-60/min", BufferTrendUtility.FormatRate(-60f));
            Assert.AreEqual("+2.5/min", BufferTrendUtility.FormatRate(2.5f));
            Assert.AreEqual("-2.5/min", BufferTrendUtility.FormatRate(-2.5f));
            Assert.AreEqual("0/min", BufferTrendUtility.FormatRate(0.1f));
            Assert.AreEqual("0/min", BufferTrendUtility.FormatRate(-0.1f));
        }

        [Test]
        public void TrendGlyph_IsDistinctPerDirection()
        {
            Assert.AreNotEqual(BufferTrendUtility.TrendGlyph(StockTrend.Rising), BufferTrendUtility.TrendGlyph(StockTrend.Falling));
            Assert.AreNotEqual(BufferTrendUtility.TrendGlyph(StockTrend.Rising), BufferTrendUtility.TrendGlyph(StockTrend.Steady));
            // ASCII only: TextMeshPro renders a missing-glyph box for anything outside its
            // default atlas, which would put a literal box next to every number.
            foreach (StockTrend trend in new[] { StockTrend.Rising, StockTrend.Falling, StockTrend.Steady })
            {
                foreach (char c in BufferTrendUtility.TrendGlyph(trend))
                {
                    Assert.Less((int)c, 128, "Trend glyph must stay ASCII");
                }
            }
        }

        [Test]
        public void SortItemTypes_PutsNamedResourcesInProductionOrderThenAlphabetical()
        {
            var itemTypes = new List<string> { "Widget", ItemType.Aether, ItemType.Scrap, "Anvil", ItemType.Brass };

            BufferTrendUtility.SortItemTypes(itemTypes);

            Assert.AreEqual(
                new List<string> { ItemType.Scrap, ItemType.Brass, ItemType.Aether, "Anvil", "Widget" },
                itemTypes);
        }

        [Test]
        public void SortItemTypes_NullList_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => BufferTrendUtility.SortItemTypes(null));
        }
    }
}
