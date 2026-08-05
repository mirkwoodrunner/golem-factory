using NUnit.Framework;
using GolemFactory.Simulation;

namespace GolemFactory.Tests.EditMode
{
    public class ClockReadoutTests
    {
        [Test]
        public void FormatTick_GroupsLargeCountsSoTheyStayReadable()
        {
            Assert.AreEqual("1,234,567", ClockReadout.FormatTick(1234567));
        }

        [Test]
        public void FormatTick_Zero_IsZero()
        {
            Assert.AreEqual("0", ClockReadout.FormatTick(0));
        }

        [Test]
        public void FormatTick_NegativeTick_ClampsRatherThanShowingAMinus()
        {
            Assert.AreEqual("0", ClockReadout.FormatTick(-5));
        }

        [Test]
        public void FormatSpeed_WholeMultiplier_HasNoTrailingDecimal()
        {
            Assert.AreEqual("1x", ClockReadout.FormatSpeed(1f));
            Assert.AreEqual("4x", ClockReadout.FormatSpeed(4f));
        }

        [Test]
        public void FormatSpeed_FractionalMultiplier_KeepsItsDecimal()
        {
            Assert.AreEqual("0.5x", ClockReadout.FormatSpeed(0.5f));
        }

        [Test]
        public void Describe_Paused_SaysSoPlainlyRegardlessOfSpeed()
        {
            Assert.AreEqual("PAUSED", ClockReadout.Describe(ClockState.Paused, 10f, 4f));
        }

        [Test]
        public void Describe_Running_ReportsTheEffectiveRateNotTheBareMultiplier()
        {
            // Speed alone is meaningless without TicksPerSecond -- 2x of 10/s is 20/s.
            Assert.AreEqual("20 ticks/s", ClockReadout.Describe(ClockState.Running, 10f, 2f));
        }

        [Test]
        public void Describe_RunningAtAFractionalRate_KeepsOneDecimal()
        {
            Assert.AreEqual("5 ticks/s", ClockReadout.Describe(ClockState.Running, 10f, 0.5f));
            Assert.AreEqual("2.5 ticks/s", ClockReadout.Describe(ClockState.Running, 5f, 0.5f));
        }

        [Test]
        public void IndexOfSpeed_EachPreset_ResolvesToItself()
        {
            for (int i = 0; i < ClockReadout.SpeedPresets.Length; i++)
            {
                Assert.AreEqual(i, ClockReadout.IndexOfSpeed(ClockReadout.SpeedPresets[i]));
            }
        }

        [Test]
        public void IndexOfSpeed_ASpeedNoButtonOffers_IsUnmatchedRatherThanNearest()
        {
            // A bootstrap or test may set any speed; highlighting the nearest button would
            // claim the clock is on a preset it isn't.
            Assert.AreEqual(-1, ClockReadout.IndexOfSpeed(3f));
        }

        [Test]
        public void SpeedPresets_AreAscendingAndIncludeRealtime()
        {
            for (int i = 1; i < ClockReadout.SpeedPresets.Length; i++)
            {
                Assert.Greater(ClockReadout.SpeedPresets[i], ClockReadout.SpeedPresets[i - 1]);
            }

            Assert.AreNotEqual(-1, ClockReadout.IndexOfSpeed(1f), "no 1x preset to return to");
        }
    }
}
