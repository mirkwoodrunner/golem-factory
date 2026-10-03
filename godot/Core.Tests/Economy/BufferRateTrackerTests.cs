using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    // BufferRateTracker is plain C# (BufferThroughputMonitor is the only part that touches
    // UnityEngine), so this runs in EditMode with no scene and no clock -- the same reason
    // SimulationClock and ConveyorSystem are testable this way.
    public class BufferRateTrackerTests
    {
        [Test]
        public void Sample_FromRegistry_TracksEveryBufferAndItemTypeSeparately()
        {
            var registry = new StorageBufferRegistry();
            var tracker = new BufferRateTracker();

            for (int step = 0; step <= 8; step++)
            {
                // ScrapBuffer gains 1 Scrap per second; BrassBuffer loses 1 Brass per
                // second from a stock of 100; Aether never moves.
                // Clear + re-deposit is how a test sets an absolute quantity: Deposit is
                // additive by design (StorageBufferRegistry.Clear exists for exactly this
                // reason on the save/load path).
                registry.Clear();
                registry.Deposit("ScrapBuffer", ItemType.Scrap, step);
                registry.Deposit("BrassBuffer", ItemType.Brass, 100 - step);
                registry.Deposit("BrassBuffer", ItemType.Aether, 7);
                tracker.Sample(step, registry);
            }

            float scrapRate;
            Assert.IsTrue(tracker.TryGetRatePerMinute("ScrapBuffer", ItemType.Scrap, out scrapRate));
            Assert.AreEqual(60f, scrapRate, 0.01f);

            float brassRate;
            Assert.IsTrue(tracker.TryGetRatePerMinute("BrassBuffer", ItemType.Brass, out brassRate));
            Assert.AreEqual(-60f, brassRate, 0.01f);

            float aetherRate;
            Assert.IsTrue(tracker.TryGetRatePerMinute("BrassBuffer", ItemType.Aether, out aetherRate));
            Assert.AreEqual(0f, aetherRate, 0.0001f);
        }

        [Test]
        public void TryGetRatePerMinute_ExactLinearSeries_MatchesTheKnownRate()
        {
            var tracker = new BufferRateTracker();

            // Three items per second for four seconds == 180 per minute.
            for (int step = 0; step <= 4; step++)
            {
                tracker.Sample(step, "ScrapBuffer", ItemType.Scrap, step * 3);
            }

            float rate;
            Assert.IsTrue(tracker.TryGetRatePerMinute("ScrapBuffer", ItemType.Scrap, out rate));
            Assert.AreEqual(180f, rate, 0.01f);
            Assert.AreEqual(StockTrend.Rising, tracker.GetTrend("ScrapBuffer", ItemType.Scrap));
        }

        [Test]
        public void TryGetRatePerMinute_UnknownSeries_ReturnsFalse()
        {
            var tracker = new BufferRateTracker();
            tracker.Sample(0f, "ScrapBuffer", ItemType.Scrap, 1);

            float rate;
            Assert.IsFalse(tracker.TryGetRatePerMinute("NoSuchBuffer", ItemType.Scrap, out rate));
            Assert.IsFalse(tracker.TryGetRatePerMinute("ScrapBuffer", "NoSuchItem", out rate));
            // Unknown reads Steady rather than guessing a direction.
            Assert.AreEqual(StockTrend.Steady, tracker.GetTrend("NoSuchBuffer", ItemType.Scrap));
        }

        [Test]
        public void TryGetRatePerMinute_NullIds_ReturnFalseInsteadOfThrowing()
        {
            var tracker = new BufferRateTracker();
            tracker.Sample(0f, null, ItemType.Scrap, 5);
            tracker.Sample(0f, "ScrapBuffer", null, 5);

            float rate;
            Assert.IsFalse(tracker.TryGetRatePerMinute(null, ItemType.Scrap, out rate));
            Assert.IsFalse(tracker.TryGetRatePerMinute("ScrapBuffer", null, out rate));
            Assert.AreEqual(0, tracker.SampleCount(null, null));
        }

        [Test]
        public void Sample_DropsSamplesOlderThanTheWindow()
        {
            var tracker = new BufferRateTracker(windowSeconds: 4f);

            for (int step = 0; step <= 20; step++)
            {
                tracker.Sample(step, "ScrapBuffer", ItemType.Scrap, step);
            }

            // Window is 4s and samples are 1s apart, so at t=20 only t=16..20 survive.
            Assert.AreEqual(5, tracker.SampleCount("ScrapBuffer", ItemType.Scrap));
        }

        [Test]
        public void Sample_StaleWindowDoesNotAnchorTheRate_AfterATrendReverses()
        {
            var tracker = new BufferRateTracker(windowSeconds: 4f);

            // Ten seconds of filling at +1/s...
            for (int step = 0; step <= 10; step++)
            {
                tracker.Sample(step, "ScrapBuffer", ItemType.Scrap, step);
            }
            float risingRate;
            Assert.IsTrue(tracker.TryGetRatePerMinute("ScrapBuffer", ItemType.Scrap, out risingRate));
            Assert.AreEqual(60f, risingRate, 0.01f);

            // ...then ten seconds of draining at -2/s. Once the old samples age out, the
            // readout must report the CURRENT trend, not an average of both eras.
            int quantity = 10;
            for (int step = 11; step <= 21; step++)
            {
                quantity -= 2;
                tracker.Sample(step, "ScrapBuffer", ItemType.Scrap, quantity);
            }

            float fallingRate;
            Assert.IsTrue(tracker.TryGetRatePerMinute("ScrapBuffer", ItemType.Scrap, out fallingRate));
            Assert.AreEqual(-120f, fallingRate, 0.01f);
            Assert.AreEqual(StockTrend.Falling, tracker.GetTrend("ScrapBuffer", ItemType.Scrap));
        }

        [Test]
        public void TryGetRatePerMinute_BeforeTheWindowSpansTheMinimum_ReturnsFalse()
        {
            var tracker = new BufferRateTracker();
            tracker.Sample(0f, "ScrapBuffer", ItemType.Scrap, 0);
            tracker.Sample(0.25f, "ScrapBuffer", ItemType.Scrap, 5);

            // 0.25s of history is not a rate, however dramatic the delta looks.
            float rate;
            Assert.IsFalse(tracker.TryGetRatePerMinute("ScrapBuffer", ItemType.Scrap, out rate));
        }

        [Test]
        public void Sample_NullRegistry_IsANoOp()
        {
            var tracker = new BufferRateTracker();
            Assert.DoesNotThrow(() => tracker.Sample(0f, null));
        }

        [Test]
        public void Clear_ForgetsEveryTrackedSeries()
        {
            var tracker = new BufferRateTracker();
            for (int step = 0; step <= 4; step++)
            {
                tracker.Sample(step, "ScrapBuffer", ItemType.Scrap, step);
            }

            tracker.Clear();

            Assert.AreEqual(0, tracker.SampleCount("ScrapBuffer", ItemType.Scrap));
            float rate;
            Assert.IsFalse(tracker.TryGetRatePerMinute("ScrapBuffer", ItemType.Scrap, out rate));
        }
    }
}
