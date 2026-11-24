using System;
using System.Linq;
using GolemFactory.Data;
using GolemFactory.Menus;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Menus
{
    /// <summary>
    /// A new game at a later chapter, for testing (<see cref="ChapterStart"/>): the kit performs
    /// every earlier chapter, so the game opens on that chapter's first step, on real ground.
    /// </summary>
    public class ChapterStartTests
    {
        [SetUp, TearDown]
        public void Clean() => GameLaunch.Reset();

        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            world.Clock.Play();
            return world;
        }

        [TestCase(2, "gears")]
        [TestCase(5, "r4-card")]
        [TestCase(8, "scrap-rate")]
        public void AStart_OpensOnThatChaptersFirstStep(int chapter, string firstStep)
        {
            SandboxWorld world = Compose();
            Assert.AreEqual(chapter, ChapterStart.Stage(world, chapter));
            Assert.AreEqual(firstStep, world.Tutorial.Current.Id);
            Assert.IsFalse(world.Tutorial.KitRunning);
            Assert.AreEqual(chapter - 1, world.Tutorial.Playtest.KitUses.Count, "the report lists each chapter the kit did");
            Assert.IsTrue(world.Golems.Any(g => !g.IsRemoved), "the earlier chapters' golems stand");
            GolemFactory.Compat.Vector2Int at = world.Interactor.PlayerCell;
            Assert.IsTrue(world.Build.Buildings.Any(b => !b.IsRemoved && Math.Abs(b.Cell.x - at.x) + Math.Abs(b.Cell.y - at.y) <= 6),
                $"the player at {at} stands by the factory");
            Assert.IsFalse(world.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == at), "on free ground");
        }

        [Test]
        public void PastTheLastChapter_TheGuideIsFinished()
        {
            SandboxWorld world = Compose();
            Assert.AreEqual(ChapterStart.Finished, ChapterStart.Stage(world, ChapterStart.Finished));
            Assert.AreEqual("done", world.Tutorial.Current.Id);
            Assert.AreEqual(1, world.ClockTower.Site.StageIndex, "chapter 10 laid the Foundation");
        }

        [Test]
        public void AStartIsClampedToTheChapters()
        {
            Assert.AreEqual(ChapterStart.First, ChapterStart.Clamp(-3));
            Assert.AreEqual(ChapterStart.Finished, ChapterStart.Clamp(99));
            Assert.AreEqual(TutorialGuide.LastChapter + 1, ChapterStart.Finished);
        }

        [Test]
        public void TheLaunch_CarriesTheChapter_ToOneCampaignNewGameOnly()
        {
            GameLaunch.Request(LaunchKind.NewGame, startChapter: 6);
            Assert.AreEqual(LaunchKind.NewGame, GameLaunch.Take(underScenario: false));
            Assert.AreEqual(6, GameLaunch.StartChapter);
            GameLaunch.Take(underScenario: true);
            Assert.AreEqual(1, GameLaunch.StartChapter, "used up");

            GameLaunch.Request(LaunchKind.NewGame, creative: true, startChapter: 6);
            GameLaunch.Take(underScenario: false);
            Assert.AreEqual(1, GameLaunch.StartChapter, "Creative has no guide to fast-forward");

            GameLaunch.Request(LaunchKind.Continue, "C:/save.json", startChapter: 6);
            GameLaunch.Take(underScenario: false);
            Assert.AreEqual(1, GameLaunch.StartChapter, "a Continue is its save's chapter");

            GameLaunch.Request(LaunchKind.NewGame, startChapter: 40);
            GameLaunch.Take(underScenario: false);
            Assert.AreEqual(ChapterStart.Finished, GameLaunch.StartChapter);
        }

        [Test]
        public void TheMenu_OffersAStart_InPlaytestModeOnly_AfterCreative()
        {
            var plain = new TitleMenu(null, DateTime.Now);
            CollectionAssert.DoesNotContain(plain.Entries, TitleChoice.Chapter);
            Assert.IsFalse(plain.IsEnabled(TitleChoice.Chapter));

            var menu = new TitleMenu(null, DateTime.Now, chapterStarts: true);
            CollectionAssert.AreEqual(new[] { TitleChoice.Continue, TitleChoice.Load, TitleChoice.NewGame, TitleChoice.Creative,
                TitleChoice.Chapter, TitleChoice.Settings, TitleChoice.Quit }, menu.Entries);
            menu.Move(1);
            menu.Move(1);
            Assert.AreEqual(TitleChoice.Chapter, menu.Selected, "New game, Creative, then the start");
            Assert.AreEqual("Chapter 2", menu.EntryLabel(TitleChoice.Chapter));
            Assert.AreEqual(TutorialGuide.TitleOfChapter(2), menu.Detail(TitleChoice.Chapter));
        }

        [Test]
        public void LeftAndRight_WalkTheChapters_AndWrap()
        {
            var menu = new TitleMenu(null, DateTime.Now, chapterStarts: true);
            menu.StepChapter(-1);
            Assert.AreEqual(ChapterStart.Finished, menu.StartChapter, "left from the first wraps to the end");
            Assert.AreEqual("Guide finished", menu.EntryLabel(TitleChoice.Chapter));
            Assert.AreEqual("Start after chapter " + TutorialGuide.LastChapter, menu.Detail(TitleChoice.Chapter));
            menu.StepChapter(1);
            Assert.AreEqual(ChapterStart.First, menu.StartChapter);
            menu.StepChapter(1);
            Assert.AreEqual(3, menu.StartChapter);
            Assert.AreEqual(TutorialGuide.TitleOfChapter(3), menu.Detail(TitleChoice.Chapter));
        }
    }
}
