using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The rate column used to read NET stock change, so a line running 60/min in and 60/min
    /// out -- fully loaded -- printed "0/min, Steady", identical to a line that had been dead
    /// for ten minutes.
    /// </summary>
    public class BufferFlowTests
    {
        private const string Buffer = "IronLine";
        private const string Item = ItemType.Scrap;

        [Test]
        public void Throughput_IsTheLesserOfTheTwoSidesNotTheirSum()
        {
            // 60 in and 40 out is 40 passing through plus 20 accumulating -- and the 20 is
            // already reported as net movement, so counting it twice would read as 100.
            Assert.AreEqual(40f, BufferFlowUtility.ThroughputPerMinute(60f, 40f), 0.0001f);
            Assert.AreEqual(0f, BufferFlowUtility.ThroughputPerMinute(60f, 0f), 0.0001f);
        }

        [Test]
        public void FlatLevelWithTrafficBothWays_ReadsAsThroughputNotIdle()
        {
            StockFlowKind kind = BufferFlowUtility.Classify(
                hasRate: true, netPerMinute: 0f, hasFlow: true, inPerMinute: 60f, outPerMinute: 60f);

            Assert.AreEqual(StockFlowKind.Throughput, kind);
            Assert.AreEqual("60/min", BufferFlowUtility.FormatFlow(kind, 0f, 60f, 60f));
            Assert.AreEqual(">>", BufferFlowUtility.Glyph(kind, StockTrend.Steady));
        }

        [Test]
        public void FlatLevelWithNoTraffic_ReadsAsIdle()
        {
            StockFlowKind kind = BufferFlowUtility.Classify(true, 0f, true, 0f, 0f);

            Assert.AreEqual(StockFlowKind.Idle, kind);
            Assert.AreEqual("0/min", BufferFlowUtility.FormatFlow(kind, 0f, 0f, 0f));
        }

        [Test]
        public void MovingLevel_StillReportsTheNetRate()
        {
            StockFlowKind kind = BufferFlowUtility.Classify(true, 30f, true, 60f, 30f);

            Assert.AreEqual(StockFlowKind.Net, kind);
            Assert.AreEqual("+30/min", BufferFlowUtility.FormatFlow(kind, 30f, 60f, 30f));
        }

        [Test]
        public void NoHistory_StaysUnknownRatherThanClaimingZero()
        {
            StockFlowKind kind = BufferFlowUtility.Classify(false, 0f, false, 0f, 0f);

            Assert.AreEqual(StockFlowKind.Unknown, kind);
            Assert.AreEqual("--", BufferFlowUtility.FormatFlow(kind, 0f, 0f, 0f));
        }

        [Test]
        public void StorageBuffer_CountsAcceptedUnitsOnlyNotRefusedOnes()
        {
            var buffer = new StorageBuffer(Buffer, capacityPerType: 10);

            buffer.Deposit(Item, 8);
            buffer.Deposit(Item, 8); // only 2 fit

            Assert.AreEqual(10, buffer.GetQuantity(Item));
            Assert.AreEqual(10, buffer.TotalDeposited(Item),
                "A refused deposit moved nothing -- counting it would make a jammed buffer " +
                "read as the busiest thing on screen.");
        }

        [Test]
        public void StorageBuffer_CountsWithdrawalsAndForgetsThemOnClear()
        {
            var buffer = new StorageBuffer(Buffer);
            buffer.Deposit(Item, 10);
            buffer.TryWithdraw(Item, 4);
            buffer.TryWithdraw(Item, 99); // refused

            Assert.AreEqual(4, buffer.TotalWithdrawn(Item));

            buffer.ClearContents();

            // Clear() is the LOAD path; carrying totals across it would fit a slope over a
            // discontinuity, the same reason the Clock Tower's rate windows are not restored.
            Assert.AreEqual(0, buffer.TotalDeposited(Item));
            Assert.AreEqual(0, buffer.TotalWithdrawn(Item));
        }

        [Test]
        public void Tracker_SeesThroughputOnALevelThatNeverMoves()
        {
            var tracker = new BufferRateTracker();
            var registry = new StorageBufferRegistry();
            StorageBuffer buffer = registry.GetOrCreate(Buffer);
            buffer.Deposit(Item, 30);

            // One unit in and one unit straight back out, every tenth of a second: the level
            // never changes, and the line is moving 600/min.
            for (int i = 0; i < 40; i++)
            {
                tracker.Sample(i * 0.1f, registry);
                buffer.Deposit(Item, 1);
                buffer.TryWithdraw(Item, 1);
            }

            Assert.IsTrue(tracker.TryGetRatePerMinute(Buffer, Item, out float net));
            Assert.AreEqual(0f, net, 0.5f);

            Assert.IsTrue(tracker.TryGetFlowPerMinute(Buffer, Item, out float inRate, out float outRate));
            Assert.AreEqual(600f, inRate, 30f);
            Assert.AreEqual(600f, outRate, 30f);

            StockFlowKind kind = BufferFlowUtility.Classify(true, net, true, inRate, outRate);
            Assert.AreEqual(StockFlowKind.Throughput, kind);
        }

        [Test]
        public void Tracker_LevelOnlySamples_ReportNoGrossMovement()
        {
            // The synthetic overload every pre-existing test uses. It knows nothing about
            // deposits, so it must not invent any -- and the net rate must be unaffected.
            var tracker = new BufferRateTracker();
            for (int i = 0; i < 20; i++)
            {
                tracker.Sample(i * 0.25f, Buffer, Item, i);
            }

            Assert.IsTrue(tracker.TryGetRatePerMinute(Buffer, Item, out float net));
            Assert.AreEqual(240f, net, 1f);

            tracker.TryGetFlowPerMinute(Buffer, Item, out float inRate, out float outRate);
            Assert.AreEqual(0f, inRate, 0.0001f);
            Assert.AreEqual(0f, outRate, 0.0001f);
        }
    }
}
