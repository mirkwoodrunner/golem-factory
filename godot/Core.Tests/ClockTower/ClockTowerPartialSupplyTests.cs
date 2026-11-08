using System.Linq;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/tuning-data-spec.md §10.1 and §15.6: how long a stage takes when its line runs short
    /// of the demand. The guide's <c>stage1</c> text says "Short of 6 a minute it still builds,
    /// just slower", and §7 says progress "scales with delivered rate". Measured on the real
    /// stage 1 (6 Frame Sections a minute, 360 s), delivered at an even spacing, from windows
    /// already holding a minute at that rate.
    /// </summary>
    public class ClockTowerPartialSupplyTests
    {
        private const int FourHoursOfTicks = 4 * 60 * 60 * ClockTowerProgress.TicksPerSecond;

        private static ClockTowerStageDefinition StageOne() =>
            AuthoredData.Load().ClockTowerStages.Values.Single(s => s.stageNumber == 1);

        // The same exact-rate driver as ClockTowerSiteTests: any 600-tick window holds `rate` units.
        private static int UnitsDueOnTick(long tick, int ratePerMinute)
        {
            long window = ClockTowerProgress.SupplyWindowTicks;
            return (int)((tick + 1) * ratePerMinute / window - tick * ratePerMinute / window);
        }

        private static void Feed(ClockTowerSite site, string item, int ratePerMinute, long tick)
        {
            int units = UnitsDueOnTick(tick, ratePerMinute);
            if (units > 0)
            {
                site.RecordDelivery(item, units, tick);
            }
        }

        /// <summary>Ticks to finish stage 1 at <paramref name="ratePerMinute"/>, or -1; and the progress reached.</summary>
        private static (int ticks, int percent) Run(int ratePerMinute)
        {
            ClockTowerStageDefinition stage = StageOne();
            string item = stage.demands.Single().itemType;
            var site = new ClockTowerSite();
            site.SetStages(new[] { stage });

            long tick = 0;
            for (; tick < ClockTowerProgress.SupplyWindowTicks; tick++)
            {
                Feed(site, item, ratePerMinute, tick);
            }

            long last = site.ProgressUnits;
            for (int elapsed = 1; elapsed <= FourHoursOfTicks; elapsed++, tick++)
            {
                Feed(site, item, ratePerMinute, tick);
                site.Tick(tick);
                if (site.StageIndex != 0)
                {
                    return (elapsed, 100);
                }
                Assert.GreaterOrEqual(site.ProgressUnits, last, $"{ratePerMinute}/min: progress never goes back");
                last = site.ProgressUnits;
            }
            return (-1, site.ProgressPercent);
        }

        private static string Clock(int ticks)
        {
            int seconds = ticks / ClockTowerProgress.TicksPerSecond;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        [Test]
        public void UnderSupply_StageTime_IsMeasured()
        {
            ClockTowerStageDefinition stage = StageOne();
            int demand = stage.demands.Single().ratePerMinute;
            int nominalTicks = stage.nominalSeconds * ClockTowerProgress.TicksPerSecond;

            foreach (int rate in new[] { 6, 3, 2, 1 })
            {
                (int ticks, int percent) = Run(rate);
                string linear = Clock(nominalTicks * demand / rate);
                TestContext.Out.WriteLine(ticks >= 0
                    ? $"{rate}/min of {demand}: {Clock(ticks)} (nominal × d/s = {linear})"
                    : $"{rate}/min of {demand}: {percent}% after 4:00:00 (nominal × d/s = {linear})");
                if (rate == demand)
                {
                    Assert.AreEqual(nominalTicks, ticks, 10, "full supply builds in the nominal time");
                }
            }
        }

        /// <summary>
        /// The decision this spec recorded (§17 Q4): the meter drains at the line's delivered
        /// rate, so a stage short of its demand takes nominal × d/s, as §7 and the guide's
        /// <c>stage1</c> text say. Before it, the meter drained at the demand rate and emptied
        /// between deliveries, and 1 a minute of 6 took 36 × nominal.
        /// </summary>
        [Test]
        public void UnderSupply_AStageTakesNominalTimesDemandOverSupply()
        {
            ClockTowerStageDefinition stage = StageOne();
            int demand = stage.demands.Single().ratePerMinute;
            int nominalTicks = stage.nominalSeconds * ClockTowerProgress.TicksPerSecond;

            foreach (int rate in new[] { 3, 2, 1 })
            {
                (int ticks, int _) = Run(rate);
                Assert.AreEqual(nominalTicks * demand / rate, ticks, 10, $"{rate} a minute of {demand}");
            }
        }

        [Test]
        public void ALineThatStops_StillEmptiesItsMeter_AndNamesTheStarvedGood()
        {
            ClockTowerStageDefinition stage = StageOne();
            string item = stage.demands.Single().itemType;
            var site = new ClockTowerSite();
            site.SetStages(new[] { stage });

            long tick = 0;
            for (; tick < 3 * ClockTowerProgress.SupplyWindowTicks; tick++)
            {
                Feed(site, item, 3, tick);
                site.Tick(tick);
            }
            Assert.IsNull(site.StarvedItemType, "half the demand, steadily, is slow, not starved");

            // Then nothing: the window empties within a minute, and the meter after it.
            for (int i = 0; i < 3 * ClockTowerProgress.SupplyWindowTicks; i++, tick++)
            {
                site.Tick(tick);
            }
            Assert.AreEqual(item, site.StarvedItemType);
            Assert.AreEqual(0, site.MeterUnits(item));
        }
    }
}
