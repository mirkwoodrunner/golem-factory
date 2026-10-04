using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.ClockTower;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    // The Clock Tower's §8 legibility surface. §12's self-assessment is explicit that these are
    // not polish -- cut the legibility surfaces and "the economy becomes invisible" -- so the
    // strings are tested like any other behaviour, the same way SteamGaugeUtilityTests and
    // StallDiagnosticsTests pin theirs.
    public class ClockTowerReadoutTests
    {
        private static ClockTowerDemandReading Row(
            string itemType, int required, int delivered, int fresh, int multiplierMilli,
            bool starved = false) =>
            new ClockTowerDemandReading(
                itemType, required, delivered, fresh, multiplierMilli, starved ? 0 : 12, starved);

        private static ClockTowerReading Running(
            string starvedItem, int deficit, params ClockTowerDemandReading[] rows) =>
            ClockTowerReading.Running(
                1, "Foundation", 42, 660, ItemType.FrameSection, starvedItem, deficit,
                new List<ClockTowerDemandReading>(rows));

        [Test]
        public void FormatMultiplier_PrintsTwoDecimalsWithAnAsciiX()
        {
            // Plain "x", never the multiplication sign: TMP's default LiberationSans SDF atlas
            // is an ASCII range and anything outside it renders as a missing-glyph box.
            Assert.AreEqual("x1.00", ClockTowerReadout.FormatMultiplier(1000));
            Assert.AreEqual("x3.00", ClockTowerReadout.FormatMultiplier(3000));
            Assert.AreEqual("x0.66", ClockTowerReadout.FormatMultiplier(666));
            Assert.AreEqual("x0.00", ClockTowerReadout.FormatMultiplier(0));
        }

        [Test]
        public void FormatMultiplier_TruncatesRatherThanRounding()
        {
            // The printed figure must never claim more throughput than the tower is crediting.
            Assert.AreEqual("x0.99", ClockTowerReadout.FormatMultiplier(999));
        }

        [Test]
        public void FormatDemandRow_ShowsRequiredDeliveredFreshAndTheMultiplier()
        {
            // FOUR columns. §8: "a player whose multiplier is capped by fresh production rather
            // than delivery needs to see exactly that", and Delivered 18 beside Fresh 6 is the
            // only way that state is visible at all.
            string row = ClockTowerReadout.FormatDemandRow(
                Row(ItemType.FrameSection, 6, 18, 6, 1000));

            Assert.AreEqual("FrameSection  6 / 18 / 6  x1.00", row);
        }

        [Test]
        public void FormatDemandTable_HeadsTheColumnsAndListsEveryDemandedItem()
        {
            ClockTowerReading reading = Running(
                null, 0,
                Row(ItemType.GreatCog, 3, 3, 3, 1000),
                Row(ItemType.FrameSection, 3, 9, 9, 3000));

            string table = ClockTowerReadout.FormatDemandTable(reading);
            string[] lines = table.Split('\n');

            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(ClockTowerReadout.DemandHeader, lines[0]);
            StringAssert.StartsWith("GreatCog", lines[1]);
            StringAssert.StartsWith("FrameSection", lines[2]);
        }

        [Test]
        public void FormatHeadline_NamesTheStageItsPercentAndItsMultiplier()
        {
            ClockTowerReading reading = Running(null, 0, Row(ItemType.FrameSection, 6, 4, 4, 660));
            Assert.AreEqual("Stage 1 Foundation - 42% - x0.66", ClockTowerReadout.FormatHeadline(reading));
        }

        [Test]
        public void FormatHeadline_OnCompletion_ReadsAsAWinNotAnEnding()
        {
            // §7: "the Clock Tower is a win, not a game-over" -- the save is still running behind
            // this line, and the wording must not suggest otherwise.
            string headline = ClockTowerReadout.FormatHeadline(
                ClockTowerReading.Completed("The Chronometer", 12345L));

            StringAssert.Contains("complete", headline);
            StringAssert.DoesNotContain("over", headline);
        }

        [Test]
        public void FormatHeadline_WithNoStages_SaysSoRatherThanPrintingStageZero()
        {
            Assert.AreEqual("Clock Tower dormant",
                ClockTowerReadout.FormatHeadline(ClockTowerReading.Dormant()));
        }

        [Test]
        public void FormatStarvedAlert_NamesTheItemAndTheDeficitInItemsPerMinute()
        {
            // §7: "An alert names the starved item; the HUD shows the deficit in items/min."
            // Naming the ITEM rather than the tower is the whole value of the row -- "the Clock
            // Tower is starved" is not actionable on a three-line stage.
            string alert = ClockTowerReadout.FormatStarvedAlert(ItemType.GreatCog, 3);

            StringAssert.StartsWith("[!] ", alert);
            StringAssert.Contains(ItemType.GreatCog, alert);
            StringAssert.Contains("3/min", alert);
            StringAssert.Contains("frozen", alert);
        }

        [Test]
        public void FormatStarvedAlert_WithNoDeficitToQuote_OmitsTheNumberRatherThanSayingZero()
        {
            string alert = ClockTowerReadout.FormatStarvedAlert(ItemType.Lens, 0);

            StringAssert.Contains(ItemType.Lens, alert);
            StringAssert.DoesNotContain("0/min", alert);
        }

        [Test]
        public void FormatStarvedAlert_WhenNothingIsStarved_IsEmpty()
        {
            ClockTowerReading reading = Running(null, 0, Row(ItemType.FrameSection, 6, 6, 6, 1000));
            Assert.AreEqual(string.Empty, ClockTowerReadout.FormatStarvedAlert(reading));
        }

        [Test]
        public void EveryStringIsPlainAscii()
        {
            // The constraint that keeps SteamGaugeUtility on " - ", BufferTrendUtility on
            // "^"/"v" and StallDiagnostics on "[!]": TMP's default atlas has no glyph for the
            // multiplication sign or the middle dot, so either would render as an empty box in
            // the middle of the most important readout in Phase 6.
            ClockTowerReading reading = Running(
                ItemType.GreatCog, 3,
                Row(ItemType.GreatCog, 3, 0, 0, 0, true),
                Row(ItemType.FrameSection, 3, 9, 9, 3000));

            string all = ClockTowerReadout.FormatHeadline(reading) +
                         ClockTowerReadout.FormatDemandTable(reading) +
                         ClockTowerReadout.FormatStarvedAlert(reading) +
                         ClockTowerReadout.FormatHeadline(ClockTowerReading.Completed("x", 1L));

            foreach (char c in all)
            {
                Assert.Less((int)c, 128, "non-ASCII character '" + c + "' in a HUD string");
            }
        }

        [Test]
        public void DeficitPerMinute_IsMeasuredAgainstDeliveryNotAgainstFreshProduction()
        {
            // The meter is fed by deliveries and by nothing else, so "get this many more per
            // minute into the tower" is the sentence that actually clears the alert. A
            // production shortfall shows in the Fresh column instead.
            ClockTowerDemandReading row = Row(ItemType.FrameSection, 6, 2, 0, 0, true);
            Assert.AreEqual(4, row.DeficitPerMinute);
        }
    }
}
