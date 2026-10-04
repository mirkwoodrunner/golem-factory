using NUnit.Framework;
using GolemFactory.ClockTower;

namespace GolemFactory.Tests.EditMode
{
    // The Clock Tower's pure arithmetic (docs/progression-design.md §7): the rate-scaled
    // multiplier, the 3x cap, the supply-pressure meter's decay and its [0, 60] clamp, and the
    // rolling 60 s window that feeds both.
    //
    // Everything here runs without a scene, a clock or a golem -- the point of keeping the math
    // in ClockTowerProgress/SupplyPressureMeter/RollingSupplyWindow rather than inside the
    // MonoBehaviour. ClockTowerSiteTests exercises the same rules end to end on a running site.
    public class ClockTowerProgressTests
    {
        // --- The multiplier -------------------------------------------------------------------

        [Test]
        public void ScaledRatio_AtExactlyDemand_IsExactlyOneTimes()
        {
            // The single most important assertion in the file: one tick at 1x supply must add
            // exactly one tick of nominal progress, with no remainder. Everything about "a stage
            // takes its nominal duration at 1x" rests on this being exact rather than close.
            Assert.AreEqual(ClockTowerProgress.ProgressScale, ClockTowerProgress.ScaledRatio(6, 6));
            Assert.AreEqual(ClockTowerProgress.ProgressScale, ClockTowerProgress.ScaledRatio(24, 24));
            Assert.AreEqual(ClockTowerProgress.ProgressScale, ClockTowerProgress.ScaledRatio(1, 1));
        }

        [Test]
        public void ScaledRatio_AtTripleDemand_IsExactlyThreeTimes()
        {
            Assert.AreEqual(3L * ClockTowerProgress.ProgressScale, ClockTowerProgress.ScaledRatio(18, 6));
        }

        [Test]
        public void ScaledRatio_AboveTripleDemand_ClampsAtThree()
        {
            // §7 caps the multiplier at 3x. A factory running ten times the demand finishes a
            // stage in a third, not a tenth -- surplus above triple is free.
            long atTriple = ClockTowerProgress.ScaledRatio(18, 6);
            Assert.AreEqual(atTriple, ClockTowerProgress.ScaledRatio(30, 6));
            Assert.AreEqual(atTriple, ClockTowerProgress.ScaledRatio(600, 6));
        }

        [Test]
        public void ScaledRatio_WithNoSupply_IsZeroAndNeverNegative()
        {
            Assert.AreEqual(0L, ClockTowerProgress.ScaledRatio(0, 6));
            Assert.AreEqual(0L, ClockTowerProgress.ScaledRatio(-5, 6));
        }

        [Test]
        public void ScaledRatio_AtHalfDemand_IsHalfSpeed()
        {
            Assert.AreEqual(ClockTowerProgress.ProgressScale / 2L, ClockTowerProgress.ScaledRatio(3, 6));
        }

        [Test]
        public void EffectiveRate_TakesTheSmallerOfDeliveryAndFreshProduction()
        {
            // §7's anti-hoard rule in one line. A warehouse can smooth a production dip
            // (delivery 9 covering fresh 6 still only counts 6) but can never raise the number.
            Assert.AreEqual(6, ClockTowerProgress.EffectiveRate(9, 6));
            Assert.AreEqual(6, ClockTowerProgress.EffectiveRate(6, 9));
            Assert.AreEqual(0, ClockTowerProgress.EffectiveRate(60, 0));
        }

        // --- Nominal durations -----------------------------------------------------------------

        [TestCase(360, 3600)]
        [TestCase(480, 4800)]
        [TestCase(600, 6000)]
        public void RequiredProgressUnits_IsExactlyNominalTicksAtOneTimes(int nominalSeconds, int expectedTicks)
        {
            // "Nominal duration is at exactly 1x supply." Since one tick at 1x contributes
            // exactly ProgressScale, the required total must be that many ticks' worth -- the
            // cheapest possible check that the formula is wired the right way up.
            Assert.AreEqual(
                (long)expectedTicks * ClockTowerProgress.ProgressScale,
                ClockTowerProgress.RequiredProgressUnits(nominalSeconds));
        }

        [Test]
        public void RequiredProgressUnits_DividedByTheThreeTimesIncrement_IsAThirdOfNominal()
        {
            long required = ClockTowerProgress.RequiredProgressUnits(360);
            long perTickAtTriple = ClockTowerProgress.ScaledRatio(18, 6);
            Assert.AreEqual(1200L, required / perTickAtTriple, "3600 ticks at 1x is 1200 at 3x");
        }

        // --- Display -------------------------------------------------------------------------

        [Test]
        public void MultiplierMilli_ConvertsScaledToThousandths()
        {
            Assert.AreEqual(1000, ClockTowerProgress.MultiplierMilli(ClockTowerProgress.ProgressScale));
            Assert.AreEqual(3000, ClockTowerProgress.MultiplierMilli(3L * ClockTowerProgress.ProgressScale));
            Assert.AreEqual(0, ClockTowerProgress.MultiplierMilli(0L));
        }

        [Test]
        public void ProgressPercent_ClampsToAHundredAndFloorsAtZero()
        {
            Assert.AreEqual(0, ClockTowerProgress.ProgressPercent(0L, 1000L));
            Assert.AreEqual(50, ClockTowerProgress.ProgressPercent(500L, 1000L));
            Assert.AreEqual(100, ClockTowerProgress.ProgressPercent(5000L, 1000L));
        }

        // --- The supply-pressure meter ---------------------------------------------------------

        [Test]
        public void Meter_ClampsAtSixtyUnitsNoMatterHowMuchIsDumpedIn()
        {
            // §7's "[0, 60]" clamp -- the anti-hoard cap on STOCK credit. Dumping a thousand
            // units banks a minute's worth of pressure and not one unit more.
            var meter = new SupplyPressureMeter();
            meter.Credit(1000);

            Assert.AreEqual(SupplyPressureMeter.CapUnits, meter.Units);
            Assert.AreEqual(SupplyPressureMeter.CapScaled, meter.Scaled);
        }

        [Test]
        public void Meter_DecaysExactlyDemandRateOverSixtySeconds()
        {
            // "-demandRate/60 per second". At 6/min that is one whole unit per 100 ticks, and it
            // has to be exact in integers or two identical towers drift apart across a stage.
            var meter = new SupplyPressureMeter();
            meter.Credit(10);

            for (int i = 0; i < 100; i++)
            {
                meter.Decay(6);
            }

            Assert.AreEqual(9, meter.Units);

            for (int i = 0; i < 500; i++)
            {
                meter.Decay(6);
            }

            Assert.AreEqual(4, meter.Units, "600 ticks at 6/min drains six whole units");
        }

        [Test]
        public void Meter_NeverGoesNegative()
        {
            // §7: frozen, never negative. A meter in debt would have to be repaid before
            // progress resumed, which is a stage getting WORSE -- the one thing the design's
            // rubric-5 guarantee forbids.
            var meter = new SupplyPressureMeter();
            meter.Credit(1);

            for (int i = 0; i < 10_000; i++)
            {
                meter.Decay(24);
            }

            Assert.AreEqual(0, meter.Scaled);
            Assert.IsTrue(meter.IsEmpty);
        }

        // --- The rolling window ------------------------------------------------------------

        [Test]
        public void Window_CountsExactlyTheRateOfASteadySupply()
        {
            // 6/min is one unit every 100 ticks. Once the window is full it must read exactly 6
            // at EVERY tick, not oscillate between 5 and 7 -- an off-by-one on the boundary
            // convention would make a perfectly supplied line look intermittently short.
            var window = new RollingSupplyWindow();

            for (long tick = 0; tick <= 1200; tick++)
            {
                if (tick % 100 == 0)
                {
                    window.Record(tick, 1);
                }

                window.Trim(tick);

                if (tick >= ClockTowerProgress.SupplyWindowTicks)
                {
                    Assert.AreEqual(6, window.UnitsInWindow, "at tick " + tick);
                }
            }
        }

        [Test]
        public void Window_DropsEverythingOlderThanSixtySeconds()
        {
            var window = new RollingSupplyWindow();
            window.Record(0, 40);
            window.Trim(0);
            Assert.AreEqual(40, window.UnitsInWindow);

            window.Trim(ClockTowerProgress.SupplyWindowTicks);
            Assert.AreEqual(0, window.UnitsInWindow);
            Assert.AreEqual(0, window.SampleCount);
        }

        [Test]
        public void Window_MergesSameTickArrivalsRatherThanLosingThem()
        {
            // Two golems pushing into the tower on one tick, which is the normal case once a
            // stage has several feeding lines.
            var window = new RollingSupplyWindow();
            window.Record(5, 3);
            window.Record(5, 4);
            window.Trim(5);

            Assert.AreEqual(7, window.UnitsInWindow);
            Assert.AreEqual(1, window.SampleCount);
        }
    }
}
