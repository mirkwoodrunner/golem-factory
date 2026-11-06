using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Menus;
using GolemFactory.Save;
using GolemFactory.Tests.EditMode;
using GolemFactory.World;

namespace GolemFactory.Tests.Menus
{
    /// <summary>docs/menus-design.md §7: the slots on disk, and what a save says about itself.</summary>
    public class SaveSlotFileTests
    {
        private string _folder;

        [SetUp]
        public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "golem-factory-slots-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private string Write(string key, string text)
        {
            Directory.CreateDirectory(_folder);
            string path = SaveSlots.PathIn(_folder, key);
            File.WriteAllText(path, text);
            return path;
        }

        [Test]
        public void TheAutosaveComesFirst_ThenFiveSlots()
        {
            CollectionAssert.AreEqual(new[] { "autosave", "slot-1", "slot-2", "slot-3", "slot-4", "slot-5" }, SaveSlots.Keys);
            Assert.AreEqual("Autosave", SaveSlots.Title("autosave"));
            Assert.AreEqual("Slot 3", SaveSlots.Title("slot-3"));
            Assert.IsEmpty(SaveSlots.Read(_folder), "no folder yet: no saves, no error");
        }

        [Test]
        public void ReadingFindsEverySlotWithAFile_ReadableOrNot_AndNothingElse()
        {
            CrateWorld.Compose().SaveTo(SaveSlots.PathIn(_folder, "slot-2"));
            Write("slot-4", "{ this is not a save");
            File.WriteAllText(Path.Combine(_folder, SaveSlots.KitFileName), "{}");
            File.WriteAllText(Path.Combine(_folder, "notes.json"), "{}");

            var found = SaveSlots.Read(_folder);
            CollectionAssert.AreEqual(new[] { "slot-2", "slot-4" }, found.Select(s => s.Key), "the kit's save and stray files are no slot");
            Assert.IsTrue(found[0].Readable);
            Assert.IsNotNull(found[0].Meta);
            Assert.IsFalse(found[1].Readable);
            Assert.IsNull(found[1].Meta);
        }

        [Test]
        public void ASlotsTime_IsTheSavesOwn_NotTheFiles()
        {
            SandboxWorld world = CrateWorld.Compose();
            world.UtcNow = () => new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc);
            string path = SaveSlots.PathIn(_folder, "slot-1");
            world.SaveTo(path);
            File.SetLastWriteTime(path, new DateTime(2026, 11, 5, 12, 0, 0)); // copied: the file's time moved

            SaveSummary summary = SaveSlots.Summarize(_folder, "slot-1");
            Assert.AreEqual(new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc).ToLocalTime(), summary.SavedAt);
        }

        [Test]
        public void ASaveFromBeforeSlots_UsesTheFilesTime()
        {
            string path = Write("slot-1", "{ \"version\": 1 }");
            var written = new DateTime(2026, 10, 30, 18, 0, 0);
            File.SetLastWriteTime(path, written);

            SaveSummary summary = SaveSlots.Summarize(_folder, "slot-1");
            Assert.IsTrue(summary.Readable);
            Assert.IsNull(summary.Meta);
            Assert.AreEqual(written, summary.SavedAt);
        }

        [Test]
        public void APicture_IsFoundOnlyWhenItIsThere_AndDeleteTakesBoth()
        {
            Write("slot-1", "{ \"version\": 1 }");
            Assert.IsNull(SaveSlots.Summarize(_folder, "slot-1").Thumbnail);
            File.WriteAllBytes(SaveSlots.ThumbnailIn(_folder, "slot-1"), new byte[] { 1 });
            Assert.AreEqual(SaveSlots.ThumbnailIn(_folder, "slot-1"), SaveSlots.Summarize(_folder, "slot-1").Thumbnail);

            SaveSlots.Delete(_folder, "slot-1");
            Assert.IsFalse(File.Exists(SaveSlots.PathIn(_folder, "slot-1")));
            Assert.IsFalse(File.Exists(SaveSlots.ThumbnailIn(_folder, "slot-1")));
            Assert.DoesNotThrow(() => SaveSlots.Delete(_folder, "slot-1"), "deleting an empty slot is nothing");
        }

        [Test]
        public void TheSaveFromBeforeSlots_MovesIntoTheFirstEmptySlot()
        {
            string legacy = Path.Combine(Path.GetTempPath(), "golem-factory-legacy-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.IsNull(SaveSlots.MigrateLegacy(legacy, _folder), "nothing to move");

                File.WriteAllText(legacy, "{ \"version\": 1 }");
                Write("slot-1", "{ \"version\": 1 }");
                File.WriteAllBytes(SaveSlots.ThumbnailIn(_folder, "slot-2"), new byte[] { 1 }); // a picture with no save
                Assert.AreEqual("slot-2", SaveSlots.MigrateLegacy(legacy, _folder));
                Assert.IsFalse(File.Exists(legacy));
                Assert.AreEqual("{ \"version\": 1 }", File.ReadAllText(SaveSlots.PathIn(_folder, "slot-2")));
                Assert.IsFalse(File.Exists(SaveSlots.ThumbnailIn(_folder, "slot-2")), "the stray picture would show beside it");

                for (int i = 3; i <= SaveSlots.ManualCount; i++)
                {
                    Write(SaveSlots.ManualKey(i), "{}");
                }
                File.WriteAllText(legacy, "old");
                Assert.IsNull(SaveSlots.MigrateLegacy(legacy, _folder), "every slot full");
                Assert.AreEqual("old", File.ReadAllText(legacy), "left where it was, untouched");
                Assert.IsFalse(File.Exists(SaveSlots.PathIn(_folder, "autosave")), "never into the autosave");
            }
            finally
            {
                File.Delete(legacy);
            }
        }
    }

    /// <summary>docs/menus-design.md §7: what a save records about itself, and the autosave.</summary>
    public class SaveMetaAndAutosaveTests
    {
        private string _folder;

        [SetUp]
        public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "golem-factory-autosave-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private SandboxWorld World()
        {
            SandboxWorld world = CrateWorld.Compose();
            world.SaveFolder = _folder;
            world.UtcNow = () => new DateTime(2026, 11, 5, 14, 32, 0, DateTimeKind.Utc);
            return world;
        }

        [Test]
        public void ASave_RecordsWhenWhereAndHowBig()
        {
            SandboxWorld world = World();
            CrateWorld.PlaceDepot(world, new Vector2Int(6, 4));
            CrateWorld.PlaceDepot(world, new Vector2Int(8, 4));
            string path = SaveSlots.PathIn(_folder, "slot-1");
            world.SaveTo(path);

            MetaEntry meta = SaveFileIO.ReadFromFile(path).meta;
            Assert.AreEqual("2026-11-05T14:32:00.0000000Z", meta.savedAtUtc);
            Assert.IsFalse(meta.autosave);
            Assert.AreEqual("", meta.reason);
            Assert.AreEqual(1, meta.chapter);
            Assert.AreEqual(world.Tutorial.ChapterTitle(1), meta.chapterTitle);
            Assert.AreEqual(0, meta.golems);
            Assert.AreEqual(2, meta.buildings);
            Assert.AreEqual(0, meta.towerStagesDone);
        }

        [Test]
        public void WithTheGuidePutAway_NoChapterIsNamed()
        {
            SandboxWorld world = World();
            world.Tutorial.Dismiss();
            world.SaveTo(SaveSlots.PathIn(_folder, "slot-1"));
            MetaEntry meta = SaveFileIO.ReadFromFile(SaveSlots.PathIn(_folder, "slot-1")).meta;
            Assert.AreEqual(0, meta.chapter);
            Assert.AreEqual("", meta.chapterTitle);
        }

        [Test]
        public void ALoad_ReadsNothingBackFromTheSummary()
        {
            SandboxWorld world = World();
            string path = SaveSlots.PathIn(_folder, "slot-1");
            world.SaveTo(path);
            SaveData data = SaveFileIO.ReadFromFile(path);
            data.meta.golems = 99;
            data.meta.chapter = 9;
            SaveFileIO.WriteToFile(data, path);

            SandboxWorld fresh = World();
            fresh.LoadFrom(path);
            Assert.AreEqual(0, fresh.Golems.Count(g => !g.IsRemoved));
            Assert.AreEqual(world.Tutorial.Index, fresh.Tutorial.Index);
        }

        [Test]
        public void ATowerStage_AsksForAnAutosave_WhichWritesTheAutosaveSlot()
        {
            SandboxWorld world = World();
            try
            {
                Assert.IsNull(world.AutosaveDue);
                EventBus.Publish(new ClockTowerStageCompletedEvent(2, "The Movement", false, 0));
                Assert.AreEqual("Tower stage 2 complete", world.AutosaveDue);

                string status = world.Autosave(world.AutosaveDue);
                StringAssert.StartsWith("Saved", status);
                Assert.IsNull(world.AutosaveDue, "taken");
                MetaEntry meta = SaveFileIO.ReadFromFile(SaveSlots.PathIn(_folder, SaveSlots.AutosaveKey)).meta;
                Assert.IsTrue(meta.autosave);
                Assert.AreEqual("Tower stage 2 complete", meta.reason);
                Assert.AreEqual(1, world.AutosavesMade);
                Assert.AreEqual(0, world.SavesMade, "an autosave isn't the player saving: the guide's Save step waits");
                CollectionAssert.AreEqual(new[] { "autosave" }, SaveSlots.Read(_folder).Select(s => s.Key));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void NoAutosave_WhileLoading_OrInAWorldThatNeverSaves()
        {
            SandboxWorld world = World();
            SandboxWorld showcase = CrateWorld.Compose(); // the title's: no save folder
            try
            {
                world.Quietly(() =>
                {
                    EventBus.Publish(new ClockTowerStageCompletedEvent(1, "The Frame", false, 0));
                    return 0;
                });
                Assert.IsNull(world.AutosaveDue, "a load replaying a stage is no new stage");

                EventBus.Publish(new ClockTowerStageCompletedEvent(1, "The Frame", false, 0));
                Assert.IsNull(showcase.AutosaveDue);
                Assert.IsNull(showcase.Autosave("On quit"), "nowhere to write");
                Assert.AreEqual(0, showcase.AutosavesMade);
            }
            finally
            {
                world.Dispose();
                showcase.Dispose();
            }
        }

        [Test]
        public void TheKitsSaveStep_WritesBesideTheSlots_NeverIntoOne()
        {
            SandboxWorld world = World();
            try
            {
                world.Clock.Play();
                TitleShowcase.Stage(world, 7); // chapter 7 saves and loads
                Assert.Greater(world.SavesMade, 0, "the kit reached its save step");
                Assert.IsTrue(File.Exists(Path.Combine(_folder, SaveSlots.KitFileName)));
                Assert.IsEmpty(SaveSlots.Read(_folder), "no slot was written");
            }
            finally
            {
                world.Dispose();
            }
        }
    }

    /// <summary>docs/menus-design.md §7: the slot list's rows, buttons and second presses.</summary>
    public class SaveSlotBoardTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 5, 16, 0, 0);

        private static SaveSummary Slot(string key, DateTime at, MetaEntry meta = null, bool readable = true) =>
            new SaveSummary(key + ".json", at, readable, key, meta);

        private static readonly MetaEntry Chapter3 = new MetaEntry
        {
            chapter = 3,
            chapterTitle = "Steam",
            golems = 1,
            buildings = 14,
        };

        [Test]
        public void EverySlotHasARow_InOrder_EmptyOnesSaySo()
        {
            var board = new SaveSlotBoard(SlotBoardMode.InGame, null, Now);
            CollectionAssert.AreEqual(SaveSlots.Keys, board.Rows.Select(r => r.Key));
            Assert.IsTrue(board.Rows.All(r => r.Empty && r.Where == SaveSlotBoard.EmptyText && r.Size == "" && r.When == ""));
            Assert.AreEqual("Autosave", board.Rows[0].Title);
        }

        [Test]
        public void ARowSaysWhereWhenAndHowBig()
        {
            var auto = new MetaEntry { autosave = true, reason = "On quit", golems = 6, buildings = 1, towerStagesDone = 2 };
            var done = new MetaEntry { golems = 0, buildings = 2, towerComplete = true, towerStagesDone = 4 };
            var board = new SaveSlotBoard(SlotBoardMode.InGame, new[]
            {
                Slot("slot-1", new DateTime(2026, 11, 5, 14, 32, 0), Chapter3),
                Slot("autosave", new DateTime(2026, 11, 4, 9, 5, 0), auto),
                Slot("slot-2", new DateTime(2026, 11, 3, 9, 5, 0), done),
                Slot("slot-3", new DateTime(2026, 10, 30, 18, 0, 0)),
                Slot("slot-4", new DateTime(2026, 11, 5, 15, 0, 0), readable: false),
            }, Now);

            SlotRow one = board.Row("slot-1");
            Assert.AreEqual("Chapter 3 · Steam", one.Where);
            Assert.AreEqual("1 golem · 14 buildings", one.Size);
            Assert.AreEqual("Saved today at 14:32", one.When);

            SlotRow autosave = board.Row("autosave");
            Assert.AreEqual(SaveSlotBoard.FreePlay, autosave.Where);
            Assert.AreEqual("6 golems · 1 building · 2 tower stages done", autosave.Size);
            Assert.AreEqual("Autosaved yesterday at 9:05 · On quit", autosave.When);

            Assert.AreEqual("0 golems · 2 buildings · Clock Tower complete", board.Row("slot-2").Size);

            SlotRow legacy = board.Row("slot-3");
            Assert.AreEqual("", legacy.Where, "a save from before slots says only when");
            Assert.AreEqual("Saved 30 Oct at 18:00", legacy.When);

            SlotRow bad = board.Row("slot-4");
            Assert.AreEqual(SaveSlotBoard.UnreadableText, bad.Where);
            Assert.AreEqual("Written today at 15:00", bad.When);
        }

        [Test]
        public void WhatEachRowOffers_InAGame_AndOnTheTitle()
        {
            var found = new[] { Slot("autosave", Now, Chapter3), Slot("slot-1", Now, Chapter3), Slot("slot-2", Now, readable: false) };
            var game = new SaveSlotBoard(SlotBoardMode.InGame, found, Now);
            Assert.IsFalse(game.Can("autosave", SlotAction.Save), "only the game writes the autosave");
            Assert.IsTrue(game.Can("autosave", SlotAction.Load));
            Assert.IsTrue(game.Can("autosave", SlotAction.Delete));
            Assert.IsTrue(game.Can("slot-1", SlotAction.Save));
            Assert.IsFalse(game.Can("slot-2", SlotAction.Load), "a save that can't be read can't be loaded");
            Assert.IsTrue(game.Can("slot-2", SlotAction.Save) && game.Can("slot-2", SlotAction.Delete), "but it can be replaced or removed");
            Assert.IsTrue(game.Can("slot-3", SlotAction.Save));
            Assert.IsFalse(game.Can("slot-3", SlotAction.Load) || game.Can("slot-3", SlotAction.Delete), "an empty slot");

            var title = new SaveSlotBoard(SlotBoardMode.Title, found, Now);
            Assert.IsTrue(title.Can("slot-1", SlotAction.Load));
            Assert.IsFalse(title.Can("slot-1", SlotAction.Save) || title.Can("slot-1", SlotAction.Delete), "the title only loads");
        }

        [Test]
        public void SavingIntoAnEmptySlotIsAtOnce_SavingOverAskTwice()
        {
            var board = new SaveSlotBoard(SlotBoardMode.InGame, new[] { Slot("slot-1", new DateTime(2026, 11, 5, 14, 32, 0), Chapter3) }, Now);
            Assert.AreEqual(SlotPress.Go, board.Press("slot-2", SlotAction.Save));

            Assert.AreEqual(SlotPress.Armed, board.Press("slot-1", SlotAction.Save));
            Assert.AreEqual("Save over", board.Label("slot-1", SlotAction.Save));
            Assert.AreEqual("Save", board.Label("slot-2", SlotAction.Save), "only the pressed button changes");
            Assert.AreEqual("Slot 1 holds a save from today at 14:32. Click Save over to replace it.", board.Prompt);
            Assert.AreEqual(SlotPress.Go, board.Press("slot-1", SlotAction.Save));
            Assert.IsNull(board.Armed);
            Assert.AreEqual("", board.Prompt);
        }

        [Test]
        public void AnyOtherPress_OrRedrawing_LetsTheFirstPressGo()
        {
            var found = new[] { Slot("slot-1", Now, Chapter3), Slot("slot-2", Now, Chapter3) };
            var board = new SaveSlotBoard(SlotBoardMode.InGame, found, Now);
            board.Press("slot-1", SlotAction.Save);
            Assert.AreEqual(SlotPress.Armed, board.Press("slot-2", SlotAction.Save), "another slot's Save asks for itself");
            Assert.AreEqual(SlotPress.Armed, board.Press("slot-2", SlotAction.Delete));
            Assert.AreEqual("Click Delete now to remove Slot 2 for good.", board.Prompt);
            Assert.AreEqual(SlotPress.Refused, board.Press("slot-4", SlotAction.Load), "an empty slot");
            Assert.IsNull(board.Armed, "even a refused press lets go");

            board.Press("slot-1", SlotAction.Save);
            board.Show(found, Now);
            Assert.IsNull(board.Armed);
            board.Press("slot-1", SlotAction.Save);
            board.Disarm();
            Assert.AreEqual(SlotPress.Armed, board.Press("slot-1", SlotAction.Save), "a fresh first press");
        }

        [Test]
        public void LoadingInAGameAsksTwice_OnTheTitleItDoesNot()
        {
            var found = new[] { Slot("slot-1", Now, Chapter3) };
            var game = new SaveSlotBoard(SlotBoardMode.InGame, found, Now);
            Assert.AreEqual(SlotPress.Armed, game.Press("slot-1", SlotAction.Load));
            Assert.AreEqual("Load now", game.Label("slot-1", SlotAction.Load));
            StringAssert.Contains("haven't saved will be lost", game.Prompt);
            Assert.AreEqual(SlotPress.Go, game.Press("slot-1", SlotAction.Load));

            var title = new SaveSlotBoard(SlotBoardMode.Title, found, Now);
            Assert.AreEqual(SlotPress.Go, title.Press("slot-1", SlotAction.Load), "nothing to lose on the title");
        }

        [Test]
        public void OnTheTitle_TheKeysWalkTheRowsThatLoad_StartingOnTheNewest()
        {
            var board = new SaveSlotBoard(SlotBoardMode.Title, new[]
            {
                Slot("autosave", new DateTime(2026, 11, 5, 9, 0, 0), Chapter3),
                Slot("slot-2", new DateTime(2026, 11, 5, 15, 0, 0), Chapter3),
                Slot("slot-3", Now, readable: false),
                Slot("slot-5", new DateTime(2026, 11, 1, 9, 0, 0), Chapter3),
            }, Now);
            Assert.AreEqual("slot-2", board.Selected);
            board.Move(1);
            Assert.AreEqual("slot-5", board.Selected, "past the unreadable and the empty");
            board.Move(1);
            Assert.AreEqual("autosave", board.Selected, "wraps");
            board.Move(-1);
            Assert.AreEqual("slot-5", board.Selected);
            Assert.IsFalse(board.Select("slot-4"));
            Assert.IsFalse(board.Select("slot-3"));
            Assert.IsTrue(board.Select("slot-2"));
            Assert.AreEqual("slot-2", board.Selected);

            var none = new SaveSlotBoard(SlotBoardMode.Title, null, Now);
            Assert.IsNull(none.Selected);
            none.Move(1);
            Assert.IsNull(none.Selected);
        }
    }
}
