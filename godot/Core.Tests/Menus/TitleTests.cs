using System;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.Tests.EditMode;
using GolemFactory.World;

namespace GolemFactory.Tests.Menus
{
    /// <summary>docs/menus-design.md §6: the launch, the menu's entries.</summary>
    public class GameLaunchTests
    {
        [SetUp, TearDown]
        public void Clean() => GameLaunch.Reset();

        [Test]
        public void TheGameOpensOnTheTitle_AScenarioOnANewGame()
        {
            Assert.AreEqual(LaunchKind.Title, GameLaunch.Take(underScenario: false));
            GameLaunch.Reset();
            Assert.AreEqual(LaunchKind.NewGame, GameLaunch.Take(underScenario: true));
        }

        [Test]
        public void ARequest_IsUsedUpByTheNextLoad()
        {
            GameLaunch.Request(LaunchKind.Continue, "C:/saves/save.json");
            Assert.AreEqual(LaunchKind.Continue, GameLaunch.Take(underScenario: true));
            Assert.AreEqual("C:/saves/save.json", GameLaunch.SavePath);

            Assert.AreEqual(LaunchKind.NewGame, GameLaunch.Take(underScenario: true), "nothing asked: the default");
            Assert.IsNull(GameLaunch.SavePath);
        }

        [Test]
        public void AScenarioMayAskForTheTitle()
        {
            GameLaunch.Request(LaunchKind.Title);
            Assert.AreEqual(LaunchKind.Title, GameLaunch.Take(underScenario: true));
        }

        [Test]
        public void ANotice_ReachesOneTitleOnly_AndAPathOnlyAContinue()
        {
            GameLaunch.Request(LaunchKind.Title, "C:/ignored.json", "That save couldn't be read.");
            GameLaunch.Take(underScenario: false);
            Assert.AreEqual("That save couldn't be read.", GameLaunch.Notice);
            Assert.IsNull(GameLaunch.SavePath, "only a Continue carries a path");
            GameLaunch.Take(underScenario: false);
            Assert.IsNull(GameLaunch.Notice);
        }
    }

    public class TitleMenuTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 5, 16, 0, 0);

        [Test]
        public void WithoutASave_ContinueAndLoadAreGreyed_AndTheKeyboardStartsOnNewGame()
        {
            var menu = new TitleMenu(Array.Empty<SaveSummary>(), Now);
            Assert.IsFalse(menu.IsEnabled(TitleChoice.Continue));
            Assert.IsFalse(menu.IsEnabled(TitleChoice.Load));
            Assert.AreEqual(TitleMenu.NoSave, menu.Detail(TitleChoice.Continue));
            Assert.AreEqual("", menu.Detail(TitleChoice.Load));
            Assert.AreEqual(TitleChoice.NewGame, menu.DefaultFocus);
            Assert.IsTrue(TitleMenu.Order.Where(c => c != TitleChoice.Continue && c != TitleChoice.Load).All(menu.IsEnabled));
        }

        [Test]
        public void ContinueOffersTheNewestSave()
        {
            var older = new SaveSummary("a.json", new DateTime(2026, 11, 3, 9, 5, 0));
            var newer = new SaveSummary("b.json", new DateTime(2026, 11, 5, 14, 32, 0));
            var menu = new TitleMenu(new[] { newer, older }, Now);
            Assert.IsTrue(menu.IsEnabled(TitleChoice.Continue));
            Assert.AreEqual("b.json", menu.Newest?.Path);
            Assert.AreEqual(TitleChoice.Continue, menu.DefaultFocus);
            Assert.AreEqual("Saved today at 14:32", menu.Detail(TitleChoice.Continue));
            Assert.AreEqual("", menu.Detail(TitleChoice.NewGame));
            Assert.IsTrue(menu.IsEnabled(TitleChoice.Load));
            Assert.AreEqual("2 saves", menu.Detail(TitleChoice.Load));
        }

        [Test]
        public void ContinueSaysWhenItsSaveIsTheAutosave()
        {
            var auto = new SaveSummary("autosave.json", new DateTime(2026, 11, 5, 15, 10, 0), key: "autosave",
                meta: new Save.MetaEntry { autosave = true, reason = "On quit" });
            var older = new SaveSummary("slot-1.json", new DateTime(2026, 11, 5, 9, 0, 0), key: "slot-1");
            var menu = new TitleMenu(new[] { older, auto }, Now);
            Assert.AreEqual("autosave.json", menu.Newest?.Path);
            Assert.AreEqual("Autosaved today at 15:10", menu.Detail(TitleChoice.Continue));
        }

        [Test]
        public void AnUnreadableSave_IsGreyedAndSaysSo_AndAnOlderGoodOneIsOffered()
        {
            var bad = new SaveSummary("bad.json", new DateTime(2026, 11, 5, 15, 0, 0), readable: false);
            var menu = new TitleMenu(new[] { bad }, Now);
            Assert.IsFalse(menu.IsEnabled(TitleChoice.Continue));
            Assert.AreEqual(TitleMenu.Unreadable, menu.Detail(TitleChoice.Continue));
            Assert.AreEqual(TitleChoice.NewGame, menu.DefaultFocus);

            var good = new SaveSummary("good.json", new DateTime(2026, 11, 4, 9, 0, 0));
            var both = new TitleMenu(new[] { bad, good }, Now);
            Assert.AreEqual("good.json", both.Newest?.Path, "the newest that reads, not the newest");
            Assert.AreEqual("1 save", both.Detail(TitleChoice.Load), "Load counts only what it can load");

            var twoBad = new TitleMenu(new[] { bad, new SaveSummary("worse.json", Now, readable: false) }, Now);
            Assert.AreEqual(TitleMenu.NoneReadable, twoBad.Detail(TitleChoice.Continue));
            Assert.IsFalse(twoBad.IsEnabled(TitleChoice.Load));
        }

        [Test]
        public void When_ReadsLikeAPerson()
        {
            Assert.AreEqual("today at 9:05", TitleMenu.When(new DateTime(2026, 11, 5, 9, 5, 0), Now));
            Assert.AreEqual("yesterday at 23:59", TitleMenu.When(new DateTime(2026, 11, 4, 23, 59, 0), Now));
            Assert.AreEqual("3 Nov at 9:05", TitleMenu.When(new DateTime(2026, 11, 3, 9, 5, 0), Now));
            Assert.AreEqual("3 Nov 2025", TitleMenu.When(new DateTime(2025, 11, 3, 9, 5, 0), Now));
        }

        [Test]
        public void TheKeysWalkTheEntries_SkippingAGreyedContinue_AndWrap()
        {
            var menu = new TitleMenu(null, Now);
            Assert.AreEqual(TitleChoice.NewGame, menu.Selected);
            menu.Move(-1);
            Assert.AreEqual(TitleChoice.Quit, menu.Selected, "up from New game wraps past the greyed Load and Continue");
            menu.Move(1);
            Assert.AreEqual(TitleChoice.NewGame, menu.Selected);
            menu.Move(1);
            menu.Move(1);
            Assert.AreEqual(TitleChoice.Quit, menu.Selected);
            Assert.IsFalse(menu.Select(TitleChoice.Continue), "the pointer can't light a greyed entry");
            Assert.AreEqual(TitleChoice.Quit, menu.Selected);

            var withSave = new TitleMenu(new[] { new SaveSummary("s.json", Now) }, Now);
            withSave.Move(-1);
            Assert.AreEqual(TitleChoice.Quit, withSave.Selected);
            withSave.Move(1);
            Assert.AreEqual(TitleChoice.Continue, withSave.Selected);
            withSave.Move(1);
            Assert.AreEqual(TitleChoice.Load, withSave.Selected);
        }

        [Test]
        public void EveryEntryHasALabel()
        {
            CollectionAssert.AreEqual(new[] { "Continue", "Load", "New game", "Settings", "Quit" }, TitleMenu.Order.Select(TitleMenu.Label));
        }
    }

    /// <summary>A world leaves the static bus as it found it, so a scene reload starts clean.</summary>
    public class WorldDisposeTests
    {
        [Test]
        public void Dispose_TakesEveryListenerOffTheBus()
        {
            int before = EventBus.ListenerCount;
            SandboxWorld world = CrateWorld.Compose();
            world.Clock.Play();
            TitleShowcase.Stage(world); // golems, the guide, the tower: everything that listens
            Assert.Greater(EventBus.ListenerCount, before, "the world listens while it lives");

            world.Dispose();
            Assert.AreEqual(before, EventBus.ListenerCount);
            Assert.IsTrue(world.Disposed);
            world.Dispose(); // twice is harmless
            Assert.AreEqual(before, EventBus.ListenerCount);
        }

        [Test]
        public void ADisposedWorld_NoLongerHearsTheNextOne()
        {
            SandboxWorld old = CrateWorld.Compose();
            old.Dispose();
            EventBus.Publish(new ClockTowerStageCompletedEvent(2, "The Movement", false, 0));
            Assert.AreEqual(TowerEventPhase.None, old.Camera.TowerPhase, "the old camera did not start the new world's tower event");
        }
    }

    /// <summary>The workshop behind the title: the guide's first six chapters, kept running.</summary>
    public class TitleShowcaseTests
    {
        private static (SandboxWorld world, TitleShowcase showcase) Staged()
        {
            SandboxWorld world = CrateWorld.Compose();
            world.Clock.Play();
            return (world, TitleShowcase.Stage(world));
        }

        [Test]
        public void ItIsTheWorkshopOfSixChapters()
        {
            (SandboxWorld world, TitleShowcase showcase) = Staged();
            Assert.AreEqual(TitleShowcase.Chapters + 1, showcase.ChapterReached, $"stuck on '{world.Tutorial.Current?.Title}'");
            var golems = world.Golems.Where(g => !g.IsRemoved).ToList();
            Assert.GreaterOrEqual(golems.Count, 6);
            Assert.GreaterOrEqual(golems.Select(g => g.Program.chassis?.name).Distinct().Count(), 3, "several chassis");
            Assert.IsTrue(world.Build.Buildings.Any(b => !b.IsRemoved && b.PrefabKey == "BeltPrefab"), "a belt runs");
            Assert.That(showcase.Extent.x, Is.InRange(3f, 16f), "fits the title's view at Near");
            Assert.That(showcase.Extent.y, Is.InRange(3f, 10f));
            Assert.AreEqual(0, world.SavesMade, "staging never saves");
        }

        [Test]
        public void Kept_NoGolemStopsInTwentyMinutes()
        {
            (SandboxWorld world, TitleShowcase showcase) = Staged();
            world.Camera.BeginShowcase(showcase.Centre);
            int working = 0, samples = 0;
            for (int second = 0; second < 20 * 60; second++)
            {
                for (int frame = 0; frame < 30; frame++)
                {
                    world.Advance(1f / 30f);
                    showcase.Keep(1f / 30f);
                }
                if (second % 5 != 0)
                {
                    continue;
                }
                foreach (GolemEntity golem in world.Golems.Where(g => !g.IsRemoved))
                {
                    Assert.AreNotEqual(GolemMood.Stalled, golem.Mood, $"{second} s: {Describe(world, golem)}");
                    Assert.AreNotEqual(GolemMood.Starved, golem.Mood, $"{second} s: {golem.Program.chassis?.name} out of steam");
                    working += golem.Mood == GolemMood.Working ? 1 : 0;
                    samples++;
                }
            }
            Assert.Greater(working, samples / 2, "most golems are at work most of the time");
            Assert.AreEqual(CameraFocus.Showcase, world.Camera.Focus, "the camera never left the workshop");
        }

        private static string Describe(SandboxWorld world, GolemEntity golem)
        {
            var crate = world.Build.Buildings.FirstOrDefault(b => !b.IsRemoved && b.Cell == golem.SourceCell)?.GetPart<GolemFactory.Buildings.ICrate>();
            string held = crate?.Buffer == null ? "no crate" : string.Join(",", crate.Buffer.Quantities.Select(q => q.Key + "=" + q.Value));
            return $"{golem.Program.chassis?.name} at {golem.Cell} on {golem.Program.CurrentStep?.name} " +
                   $"({golem.Program.GetItemTypeAt(golem.Program.CurrentStepIndex)}) stopped on {golem.StallReason}; behind it: {held}";
        }

        [Test]
        public void Unkept_TheSameWorkshopStops()
        {
            // What Keep is for: left alone, the guide's workshop needs a player within minutes.
            (SandboxWorld world, TitleShowcase _) = Staged();
            for (int frame = 0; frame < 30 * 60 * 5; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.IsTrue(world.Golems.Any(g => !g.IsRemoved && (g.Mood == GolemMood.Stalled || g.Mood == GolemMood.Starved)));
        }
    }

    public class ShowcaseCameraTests
    {
        [Test]
        public void TheTitleDriftsSlowly_OverTheWorkshopsMiddle_AtNear()
        {
            var camera = new CameraDirector();
            var centre = new Vector2(-4f, -13f);
            camera.BeginShowcase(centre);
            Assert.AreEqual(CameraFocus.Showcase, camera.Focus);
            Assert.AreEqual("Near", CameraDirector.ZoomNames[camera.ZoomLevel]);
            Assert.IsTrue(float.IsPositiveInfinity(camera.GlideSpeed), "the drift is the path; the rig follows it exactly");
            Assert.IsNull(camera.NextAlert(new[] { "g1" }), "no alert look over the title");

            Vector2 last = camera.ShowcasePoint;
            float travelled = 0f, fastest = 0f;
            for (int frame = 0; frame < 60 * 120; frame++)
            {
                camera.UpdateShowcase(1f / 60f);
                Vector2 now = camera.ShowcasePoint;
                float step = (now - last).magnitude;
                travelled += step;
                fastest = Math.Max(fastest, step * 60f);
                Assert.LessOrEqual(Math.Abs(now.x - centre.x), CameraDirector.ShowcaseDriftX + 1e-4f);
                Assert.LessOrEqual(Math.Abs(now.y - centre.y), CameraDirector.ShowcaseDriftY + 1e-4f);
                last = now;
            }
            Assert.Greater(travelled, 4f, "it moves");
            Assert.Less(fastest, 0.4f, "never faster than 0.4 cells a second");
        }

        [Test]
        public void ATowerStage_DoesNotTakeTheTitlesCamera()
        {
            var camera = new CameraDirector();
            camera.BeginShowcase(new Vector2(-4f, -13f));
            camera.BeginTowerEvent(1, "The Frame");
            Assert.AreEqual(CameraFocus.Showcase, camera.Focus);
            Assert.AreEqual(TowerEventPhase.None, camera.TowerPhase);
        }

        [Test]
        public void Reduced_TheTitleHoldsStill()
        {
            var camera = new CameraDirector();
            camera.Motion.Reduced = true;
            var centre = new Vector2(-4f, -13f);
            camera.BeginShowcase(centre);
            camera.UpdateShowcase(10f);
            Assert.AreEqual(centre, camera.ShowcasePoint);
        }
    }
}
