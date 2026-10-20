using System.Linq;
using GolemFactory.Data;
using GolemFactory.Economy;
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

        [Test]
        public void EveryQuestion_WaitsForAStepTheGuideHas()
        {
            SandboxWorld world = Compose();
            var stepIds = world.Tutorial.Steps.Select(s => s.Id).ToList();
            foreach (PlaytestQuestion question in PlaytestSession.Questions)
            {
                CollectionAssert.Contains(stepIds, question.AfterStepId, question.Id);
                Assert.GreaterOrEqual(question.Options.Count, 2, question.Id);
            }
            CollectionAssert.AllItemsAreUnique(PlaytestSession.Questions.Select(q => q.Id));
        }

        [Test]
        public void TheSandboxRunsPlaytestMode_AndAsksAboutTheLightFirst()
        {
            SandboxWorld world = Compose();
            PlaytestSession playtest = world.Tutorial.Playtest;

            Assert.IsNotNull(playtest, "sandbox.json turns playtest mode on");
            Assert.AreEqual("light", playtest.Current?.Id);
        }

        [Test]
        public void AQuestion_WaitsUntilItsStep_ThenAnAnswerIsRecorded()
        {
            SandboxWorld world = Compose();
            PlaytestSession playtest = world.Tutorial.Playtest;
            playtest.Skip(1f);
            Assert.IsNull(playtest.Current, "nothing else is due on step 1");

            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 20);
            world.Tutorial.Update();
            Assert.IsNull(playtest.Current, "the Workbench question waits for the Workbench");

            // Straight to the depot step: the first golem has been programmed.
            foreach (string item in new[] { ItemType.Coal, ItemType.Coke, ItemType.IronPlate })
            {
                world.Buffers.Deposit(world.StockpileBufferId, item, 50);
            }
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 200);
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "BoilerPrefab").Prefab);
            world.Build.PlaceOrRemove(world.Tutorial.BoilerSpot);
            world.Build.CancelPlacement();
            world.Tutorial.Update();
            var boiler = world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == world.Tutorial.BoilerSpot);
            world.Interactor.TryRefuelBoiler(boiler.GetPart<Buildings.PlaceableBoiler>());
            world.StarterStation.TryConstructGolem(world.Definitions.Chassis["ClockworkScavenger"], out var golem);
            golem.Program.logicCore = world.Definitions.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(world.Definitions.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(world.Definitions.Appendages["PushOutput"]);
            world.Tutorial.Update();
            Assert.AreEqual("depot", world.Tutorial.Current.Id);

            Assert.AreEqual("workbench", playtest.Current?.Id, "asked once the first program is written");
            playtest.Answer("A real decision", "  the dial is nice  ", 120f);

            Assert.AreEqual("hint", playtest.Current?.Id, "Phase 2's hint question waits behind it, at the same step");
            playtest.Skip(121f);
            Assert.IsNull(playtest.Current);
            PlaytestAnswer answer = playtest.Answers.Single(a => a.QuestionId == "workbench");
            Assert.AreEqual("A real decision", answer.Choice);
            Assert.AreEqual("the dial is nice", answer.Note, "trimmed");
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
        /// the line sits at the top of the report, before the answers.
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
                report.IndexOf("## Your answers", System.StringComparison.Ordinal), "the line opens the report");
            StringAssert.Contains("fast-forwarded chapter 5", report, "the later kit use is still reported");
        }

        /// <summary>
        /// performance-report-template.md §5: "Time per chapter" (before the answers, each chapter
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
                report.IndexOf("## Your answers", System.StringComparison.Ordinal));

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
            SandboxWorld world = SandboxWorld.Compose(AuthoredData.Load(), SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(AuthoredData.Load()));
            string report = world.Tutorial.Playtest.Compose("", 1f);
            StringAssert.Contains("| 1 | 0:01 (so far) | 5:00 |", report);
        }

        [Test]
        public void TheReport_HasAnswers_Timings_KitUses_AndErrors()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            playtest.Answer("Too dark", "", 5f);
            playtest.StepEntered("coal", "Buy Coal", 95f);
            playtest.RecordKit("jumped to chapter 4", 100f);
            playtest.RecordError("NullReferenceException in Foo", 101f);

            string report = playtest.Compose("Build: test", 130f);

            StringAssert.Contains("Build: test", report);
            StringAssert.Contains("**Look around the workshop. Is the room too dark?** Too dark", report);
            StringAssert.Contains("| Gather Scrap | 0:00 | 1:35 |", report);
            StringAssert.Contains("| Buy Coal | 1:35 | 0:35 |", report);
            StringAssert.Contains("1:40  jumped to chapter 4", report);
            StringAssert.Contains("1:41  NullReferenceException in Foo", report);
        }

        [Test]
        public void AQuestionIsAskedOnce_EvenIfItsStepComesRoundAgain()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            playtest.Skip(1f);
            playtest.StepEntered("coal", "Buy Coal", 2f);
            playtest.StepEntered("scrap", "Gather Scrap", 3f); // F1 after finishing restarts the guide

            Assert.IsNull(playtest.Current);
            Assert.AreEqual(1, playtest.Answers.Count);
            Assert.AreEqual("skipped", playtest.Answers[0].Choice);
        }

        [Test]
        public void ThePackUpgradeQuestions_AreAskedWhereTheDocSays()
        {
            // docs/pack-upgrades-design.md §6 and §8.5: the upgrade's question as `ledger` opens
            // (the step after `pack`), the dear-room one as `frame-section` opens.
            var after = PlaytestSession.Questions.ToDictionary(q => q.Id, q => q.AfterStepId);
            Assert.AreEqual("ledger", after["pack-upgrade"]);
            Assert.AreEqual("frame-section", after["dear-room"]);

            SandboxWorld world = Compose();
            var ids = world.Tutorial.Steps.Select(s => s.Id).ToList();
            Assert.AreEqual(ids.IndexOf("pack") + 1, ids.IndexOf("ledger"), "`ledger` comes straight after `pack`");
            Assert.AreEqual(ids.IndexOf("bays") + 1, ids.IndexOf("pack"), "`pack` comes straight after `bays`");
        }

        [Test]
        public void OnlyTheTowerAndTheGuideItself_WaitForTheLastStep()
        {
            // A chapter's question is asked as the next chapter opens. Nine once waited for
            // "done", after the Clock Tower's Foundation, so a playtest that stopped sooner never
            // asked them at all.
            CollectionAssert.AreEquivalent(
                new[] { "tower", "guide" },
                PlaytestSession.Questions.Where(q => q.AfterStepId == "done").Select(q => q.Id));
        }

        [Test]
        public void EachChaptersQuestion_IsAskedBeforeTheChapterAfterNextBegins()
        {
            SandboxWorld world = Compose();
            TutorialGuide guide = world.Tutorial;
            int StepIndex(string id) => guide.Steps.ToList().FindIndex(s => s.Id == id);
            var askedAt = PlaytestSession.Questions.ToDictionary(q => q.Id, q => guide.ChapterOf(StepIndex(q.AfterStepId)));

            // Where each experience happens, and so the latest chapter its question may wait for.
            Assert.LessOrEqual(askedAt["badge"], 5, "the stall on purpose is chapter 4");
            Assert.LessOrEqual(askedAt["slag"], 6, "Slag is chapter 5");
            Assert.LessOrEqual(askedAt["drag"], 6, "dragging is chapter 6");
            Assert.LessOrEqual(askedAt["save-load"], 8, "save and load are chapter 7");
            Assert.LessOrEqual(askedAt["goals"], 9, "the goal steps are chapter 8");
            Assert.LessOrEqual(askedAt["zeppelin"], 10, "the Zeppelin is chapter 9");
            Assert.LessOrEqual(askedAt["carry"], 2, "the first golem is carried in chapter 1");
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

        [Test]
        public void TheKitSavesBesideThePlayersSave_NeverOverIt()
        {
            string slot = System.IO.Path.Combine("appdata", "Golem Factory", "save.json");
            Assert.AreEqual(System.IO.Path.Combine("appdata", "Golem Factory", "kit-save.json"),
                TutorialGuide.KitSavePathBeside(slot));
            Assert.IsNull(TutorialGuide.KitSavePathBeside(null));
        }
    }
}
