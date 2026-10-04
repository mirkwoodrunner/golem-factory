using NUnit.Framework;
using GolemFactory.Steam;

namespace GolemFactory.Tests.EditMode
{
    // The boiler fuel gauge (docs/progression-design.md §8). Pure arithmetic and formatting, so
    // it is tested without a Canvas the way BufferTrendUtility and StallDiagnostics are.
    //
    // §12's self-assessment ties the whole scarcity design to this landing: "if the legibility
    // surfaces are cut the economy becomes invisible and this point fails."
    public class SteamGaugeUtilityTests
    {
        [Test]
        public void BurnRate_IsExactlyProportionalToPoweredGolems()
        {
            // The line the entire mechanic turns on. A flat per-boiler burn would make these
            // four numbers identical, and the marginal cost of a golem would be zero.
            Assert.AreEqual(0, SteamGaugeUtility.BurnPerMinute(0));
            Assert.AreEqual(6, SteamGaugeUtility.BurnPerMinute(1));
            Assert.AreEqual(24, SteamGaugeUtility.BurnPerMinute(4));
            Assert.AreEqual(48, SteamGaugeUtility.BurnPerMinute(8));
        }

        [Test]
        public void TheDesignsWorkedExample_ReadsBackExactly()
        {
            // §8: "240 Coke, 24/min, 10:00 left".
            SteamGaugeReading reading = SteamGaugeUtility.Compute(240, 4, 240);

            Assert.AreEqual(240, reading.CokeStock);
            Assert.AreEqual(24, reading.BurnPerMinute);
            Assert.IsTrue(reading.HasCountdown);
            Assert.AreEqual(600, reading.SecondsRemaining);
            Assert.AreEqual("240 Coke - 24/min - 10:00 left", SteamGaugeUtility.Format(reading));
            Assert.IsFalse(reading.IsLow, "a full boiler is not an alert");
        }

        [Test]
        public void TheCountdownRespondsToEveryGolemPlaced()
        {
            // §3.1: "with proportional burn the countdown responds live to every golem placed".
            Assert.AreEqual(2400, SteamGaugeUtility.Compute(240, 1, 240).SecondsRemaining);
            Assert.AreEqual(1200, SteamGaugeUtility.Compute(240, 2, 240).SecondsRemaining);
            Assert.AreEqual(300, SteamGaugeUtility.Compute(240, 8, 240).SecondsRemaining);
        }

        [Test]
        public void TheCountdownRoundsDown_SoItNeverPromisesTimeThePlayerDoesNotHave()
        {
            SteamGaugeReading reading = SteamGaugeUtility.Compute(239, 4, 240);

            Assert.AreEqual(597, reading.SecondsRemaining, "239 * 60 / 24 = 597.5 -> 597");
            Assert.AreEqual("239 Coke - 24/min - 9:57 left", SteamGaugeUtility.Format(reading));
        }

        [Test]
        public void AnIdleBoiler_HasNoCountdownAtAll()
        {
            // An idle boiler burns nothing (§3.1), so its countdown is genuinely infinite.
            // Printing "0:00 left" would read as an emergency; printing a huge number would be
            // a promise about a factory that is not running.
            SteamGaugeReading reading = SteamGaugeUtility.Compute(240, 0, 240);

            Assert.IsFalse(reading.HasCountdown);
            Assert.AreEqual(0, reading.BurnPerMinute);
            Assert.AreEqual("240 Coke - 0/min - idle", SteamGaugeUtility.Format(reading));
        }

        [Test]
        public void TheAlertFiresAtOrBelowTwentyFivePercent()
        {
            // §8's "with an alert at 25 %". Against the design's 240-Coke Phase-1 boiler that is
            // 60 Coke -- and the boundary itself must count as low, or an exactly-quarter-full
            // boiler is the one case the player is never warned about.
            Assert.IsFalse(SteamGaugeUtility.Compute(61, 4, 240).IsLow);
            Assert.IsTrue(SteamGaugeUtility.Compute(60, 4, 240).IsLow, "the boundary is an alert");
            Assert.IsTrue(SteamGaugeUtility.Compute(1, 4, 240).IsLow);
            Assert.IsTrue(SteamGaugeUtility.Compute(0, 4, 240).IsLow, "empty is certainly low");
        }

        [Test]
        public void ABoilerThatHasNeverHeldCoke_IsNotAnAlert()
        {
            // Reference 0 means there is no 100 % to take a quarter of. An unfuelled boiler
            // nobody has touched yet is not an emergency.
            Assert.IsFalse(SteamGaugeUtility.Compute(0, 0, 0).IsLow);
        }

        [Test]
        public void NegativeInputs_ClampRatherThanPropagate()
        {
            SteamGaugeReading reading = SteamGaugeUtility.Compute(-5, -3, 240);

            Assert.AreEqual(0, reading.CokeStock);
            Assert.AreEqual(0, reading.PoweredGolems);
            Assert.AreEqual(0, reading.BurnPerMinute);
        }

        [Test]
        public void Countdown_FormatsMinutesUncapped()
        {
            Assert.AreEqual("0:00", SteamGaugeUtility.FormatCountdown(0));
            Assert.AreEqual("0:09", SteamGaugeUtility.FormatCountdown(9));
            Assert.AreEqual("1:00", SteamGaugeUtility.FormatCountdown(60));
            Assert.AreEqual("102:30", SteamGaugeUtility.FormatCountdown(6150));
        }

        [Test]
        public void TheGaugeTextIsPlainAscii()
        {
            // Same LiberationSans SDF constraint as StallDiagnostics/BufferTrendUtility: TMP's
            // default atlas has no glyph for the design's middle-dot separator, so it would
            // render as a box.
            string[] texts =
            {
                SteamGaugeUtility.Format(SteamGaugeUtility.Compute(240, 4, 240)),
                SteamGaugeUtility.Format(SteamGaugeUtility.Compute(240, 0, 240))
            };

            foreach (string text in texts)
            {
                foreach (char c in text)
                {
                    Assert.Less((int)c, 128, "non-ASCII '" + c + "' has no glyph in LiberationSans SDF");
                }
            }
        }
    }
}
