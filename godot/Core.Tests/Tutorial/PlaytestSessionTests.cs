using GolemFactory.Data;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>Playtest mode (G10, at the user's call: "integrate the playtest script into the tutorial").</summary>
    public class PlaytestSessionTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        /// <summary>testscript/phase-2-playtest.md: the first fifteen minutes, timed to chapter 5.</summary>
        [Test]
        public void TheReport_TimesChaptersOneToFour_AndSaysWhetherTheKitWasUsed()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            Assert.IsNull(playtest.ChaptersOneToFour(), "not there yet");

            playtest.StepEntered("r4-card", "Claim Iron Smelting", 754f);
            Assert.AreEqual("Chapters 1–4: 12:34 (target 15:00) · kit used: no", playtest.ChaptersOneToFour());
            StringAssert.Contains("Chapters 1–4: 12:34", playtest.Compose("", 800f));

            var kit = new PlaytestSession();
            kit.StepEntered("scrap", "Gather Scrap", 0f);
            kit.RecordKit("fast-forwarded chapter 1", 30f);
            kit.StepEntered("r4-card", "Claim Iron Smelting", 300f);
            StringAssert.EndsWith("kit used: yes", kit.ChaptersOneToFour(), "a kit use voids the timing");
        }

        /// <summary>
        /// Phase 2's test pass: F9 in chapter 5 or later (the Phase 1 script carries on from
        /// here) doesn't void the chapters 1-4 time, an overrun prints past 15:00 as it is, and
        /// the line sits at the top of the report, before the chapter table.
        /// </summary>
        [Test]
        public void TheChaptersOneToFourLine_IgnoresLaterKitUse_AndPrintsAnOverrun()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            playtest.StepEntered("patent", "Patent a program", 900f);
            playtest.StepEntered("r4-card", "Claim Iron Smelting", 1085.6f);
            playtest.RecordKit("fast-forwarded chapter 5", 1200f);
            playtest.StepEntered("copper", "Buy Copper Ore", 1210f);

            Assert.AreEqual("Chapters 1–4: 18:05 (target 15:00) · kit used: no", playtest.ChaptersOneToFour());
            string report = playtest.Compose("Build: test", 1300f);
            Assert.Less(report.IndexOf("Chapters 1–4: 18:05", System.StringComparison.Ordinal),
                report.IndexOf("## Time per chapter", System.StringComparison.Ordinal), "the line opens the report");
            StringAssert.Contains("fast-forwarded chapter 5", report, "the later kit use is still reported");
        }

        /// <summary>
        /// performance-report-template.md §5: "Time per chapter" (before the step timings, each chapter
        /// against its budget), the "Chapters 1–10" line once `done` is reached, and "Frame rate"
        /// (after the step timings) with each chapter's percentiles and its last counts.
        /// </summary>
        [Test]
        public void TheReport_TimesEachChapter_AndChaptersOneToTen()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f, 1);
            playtest.StepEntered("coal", "Buy Coal", 100f, 1);
            playtest.StepEntered("gears", "Gears", 290f, 2);
            Assert.IsNull(playtest.ChaptersOneToTen(), "not done yet");

            string report = playtest.Compose("", 400f);
            StringAssert.Contains("| 1 | 4:50 | 5:00 |", report);
            StringAssert.Contains("| 2 | 1:50 (so far) | 4:30 |", report);
            Assert.Less(report.IndexOf("## Time per chapter", System.StringComparison.Ordinal),
                report.IndexOf("## Time on each step", System.StringComparison.Ordinal));

            playtest.RecordKit("fast-forwarded chapter 8", 5000f);
            playtest.StepEntered("tower-visit", "Visit the tower", 7000f, 10);
            playtest.StepEntered("done", "Done", 9667f, 10);
            Assert.AreEqual("Chapters 1–10: 2:41:07 (target 3:00:00) · kit used: yes", playtest.ChaptersOneToTen());
            report = playtest.Compose("", 9700f);
            StringAssert.Contains("| 10 | 44:27 | 45:00 |", report, "the last chapter ends at done");
            StringAssert.Contains("Chapters 1–10: 2:41:07", report);
        }

        [Test]
        public void TheFrameRateSection_HasEachChaptersPercentiles()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f, 1);
            for (int i = 1; i <= 100; i++)
            {
                playtest.RecordFrame(1, i, 0, 0);
            }
            playtest.RecordFrame(5, 16.6, 12, 40);
            playtest.RecordFrame(5, 20.0, 14, 52);

            string report = playtest.Compose("", 10f);
            StringAssert.Contains("## Frame rate", report);
            StringAssert.Contains("| Chapter | Frames | p50 ms | p95 ms | worst ms | golems | belt items |", report);
            StringAssert.Contains("| 1 | 100 | 50.0 | 95.0 | 100.0 | 0 | 0 |", report);
            StringAssert.Contains("| 5 | 2 | 16.6 | 20.0 | 20.0 | 14 | 52 |", report, "golems and belt items at the chapter's last frame");
            Assert.Less(report.IndexOf("## Time on each step", System.StringComparison.Ordinal),
                report.IndexOf("## Frame rate", System.StringComparison.Ordinal));
        }

        [Test]
        public void TheGuide_ReportsEachStepsChapter()
        {
            SandboxWorld world = Compose();
            string report = world.Tutorial.Playtest.Compose("", 1f);
            StringAssert.Contains("| 1 | 0:01 (so far) | 5:00 |", report);
        }

        [Test]
        public void TheReport_HasTimings_KitUses_AndErrors_AndNoAnswers()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            playtest.StepEntered("coal", "Buy Coal", 95f);
            playtest.RecordKit("jumped to chapter 4", 100f);
            playtest.RecordError("NullReferenceException in Foo", 101f);

            string report = playtest.Compose("Build: test", 130f);

            StringAssert.Contains("Build: test", report);
            StringAssert.DoesNotContain("## Your answers", report, "the guide asks no questions (cleared 2026-11-21)");
            StringAssert.Contains("| Gather Scrap | 0:00 | 1:35 |", report);
            StringAssert.Contains("| Buy Coal | 1:35 | 0:35 |", report);
            StringAssert.Contains("1:40  jumped to chapter 4", report);
            StringAssert.Contains("1:41  NullReferenceException in Foo", report);
        }

        [Test]
        public void APreviousReport_IsKeptUnderADatedName_NeverOverwritten()
        {
            string report = System.IO.Path.Combine("user", "playtest-report.md");
            var when = new System.DateTime(2026, 10, 12, 19, 30, 5);

            Assert.AreEqual(System.IO.Path.Combine("user", "playtest-report-20261012-1930.md"),
                PlaytestReportArchive.NameFor(report, when, _ => false));

            var taken = new System.Collections.Generic.HashSet<string>
            {
                System.IO.Path.Combine("user", "playtest-report-20261012-1930.md"),
            };
            Assert.AreEqual(System.IO.Path.Combine("user", "playtest-report-20261012-1930-2.md"),
                PlaytestReportArchive.NameFor(report, when, taken.Contains), "two sessions ending in one minute");
        }
    }
}
