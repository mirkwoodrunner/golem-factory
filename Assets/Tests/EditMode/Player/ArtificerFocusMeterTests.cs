using NUnit.Framework;
using GolemFactory.Player;

namespace GolemFactory.Tests.EditMode
{
    public class ArtificerFocusMeterTests
    {
        [Test]
        public void NewMeter_StartsAtMaxFocus()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f);

            Assert.AreEqual(100f, meter.CurrentFocus);
        }

        [Test]
        public void TryConsume_SufficientFocus_SucceedsAndDecrements()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f);

            bool result = meter.TryConsume(30f);

            Assert.IsTrue(result);
            Assert.AreEqual(70f, meter.CurrentFocus);
        }

        [Test]
        public void TryConsume_InsufficientFocus_FailsAndLeavesUnchanged()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f);
            meter.TryConsume(90f);

            bool result = meter.TryConsume(20f);

            Assert.IsFalse(result);
            Assert.AreEqual(10f, meter.CurrentFocus);
        }

        [Test]
        public void TryConsume_NegativeAmount_Fails()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f);

            Assert.IsFalse(meter.TryConsume(-5f));
        }

        [Test]
        public void Refund_IncreasesFocus_ClampedToMax()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f);
            meter.TryConsume(30f);

            meter.Refund(10f);

            Assert.AreEqual(80f, meter.CurrentFocus);

            meter.Refund(1000f);

            Assert.AreEqual(100f, meter.CurrentFocus);
        }

        [Test]
        public void Refund_NegativeAmount_IsNoOp()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f);
            meter.TryConsume(30f);

            meter.Refund(-10f);

            Assert.AreEqual(70f, meter.CurrentFocus);
        }

        [Test]
        public void Regen_IncreasesFocus_ClampedToMax()
        {
            var meter = new ArtificerFocusMeter("LocalPlayer", maxFocus: 100f, regenPerSecond: 10f);
            meter.TryConsume(50f);

            meter.Regen(2f);

            Assert.AreEqual(70f, meter.CurrentFocus);

            meter.Regen(10f);

            Assert.AreEqual(100f, meter.CurrentFocus);
        }
    }
}
