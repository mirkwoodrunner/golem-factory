using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The inventory's bars were relative-only: §1.2 added CapacityPerType/RoomFor and nothing
    /// read it, so buffer backpressure -- which §5.3(c)'s Slag economy runs on -- stayed
    /// invisible until a golem stalled OutputFull.
    /// </summary>
    public class StockBarPolicyTests
    {
        [Test]
        public void Unlimited_FallsBackToRelativeToLargest()
        {
            StockBar bar = StockBarPolicy.Evaluate(
                quantity: 25, capacityPerType: StorageBuffer.Unlimited, largestOnScreen: 100);

            Assert.AreEqual(StockBarMode.RelativeToLargest, bar.Mode);
            Assert.AreEqual(0.25f, bar.Fraction, 0.0001f);
            Assert.IsFalse(bar.IsNearFull);
            Assert.IsFalse(bar.IsFull);
        }

        [Test]
        public void Capped_MeasuresAgainstItsOwnCapacityNotTheScreen()
        {
            // The distinction that matters: 25 of a 50-cap buffer is HALF FULL, whatever the
            // biggest row on screen happens to hold.
            StockBar bar = StockBarPolicy.Evaluate(
                quantity: 25, capacityPerType: 50, largestOnScreen: 1000);

            Assert.AreEqual(StockBarMode.AgainstCapacity, bar.Mode);
            Assert.AreEqual(0.5f, bar.Fraction, 0.0001f);
        }

        [Test]
        public void Capped_AtCapacity_ReadsFull()
        {
            StockBar bar = StockBarPolicy.Evaluate(100, 100, 100);

            Assert.IsTrue(bar.IsFull);
            Assert.IsTrue(bar.IsNearFull);
            Assert.AreEqual(1f, bar.Fraction, 0.0001f);
        }

        [Test]
        public void Capped_OverCapacity_ClampsRatherThanOverdrawingTheBar()
        {
            // Can't happen through Deposit, which clamps -- but a capacity LOWERED under
            // existing stock can, and a bar longer than its track is a rendering bug.
            StockBar bar = StockBarPolicy.Evaluate(150, 100, 150);

            Assert.AreEqual(1f, bar.Fraction, 0.0001f);
            Assert.IsTrue(bar.IsFull);
        }

        [Test]
        public void NearFull_TripsAtFiveSixths_NotAtTheStall()
        {
            StockBar below = StockBarPolicy.Evaluate(83, 100, 100);
            StockBar at = StockBarPolicy.Evaluate(84, 100, 100);

            Assert.IsFalse(below.IsNearFull);
            Assert.IsTrue(at.IsNearFull);
            Assert.IsFalse(at.IsFull, "A warning has to arrive before the stall, not with it.");
        }

        [Test]
        public void NoStockAndNothingOnScreen_IsAnEmptyBarNotADivideByZero()
        {
            StockBar bar = StockBarPolicy.Evaluate(0, StorageBuffer.Unlimited, 0);

            Assert.AreEqual(0f, bar.Fraction);
            Assert.AreEqual(StockBarMode.RelativeToLargest, bar.Mode);
        }

        [Test]
        public void ZeroCapacity_ReadsAsNoCapacityRatherThanPermanentlyFull()
        {
            StockBar bar = StockBarPolicy.Evaluate(0, 0, 10);

            Assert.AreEqual(StockBarMode.RelativeToLargest, bar.Mode);
            Assert.IsFalse(bar.IsFull);
        }
    }
}
