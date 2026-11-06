using System.IO;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Events;
using GolemFactory.Menus;
using GolemFactory.Save;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The save slots and the autosave (docs/menus-design.md §7), with real keys and clicks,
    /// across the reloads that loading from the title and Quit to title make:
    /// <list type="number">
    ///   <item>A save from before slots moves into Slot 1, and the title offers it: Continue and Load.</item>
    ///   <item>The title's Load lists the slots; the keys walk the ones that load; Enter loads one.</item>
    ///   <item>In the game, the Save / Load tab saves into an empty slot at once, with its summary
    ///   and (windowed) its picture; saving over, loading and deleting each wait for a second press,
    ///   which any other press or closing the screen lets go.</item>
    ///   <item>A tower stage autosaves, and isn't the player's Save. Quit to title, Quit to desktop
    ///   and closing the window each autosave first; the title's showcase never does.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class SlotsScenario : StepScenario
    {
        private static readonly CoreVector2Int DepotA = new CoreVector2Int(-6, -2);
        private static readonly CoreVector2Int DepotB = new CoreVector2Int(-4, -2);

        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private TitleScreen Title => TitleScreen.Find(Runner);
        private GameSaves Saves => GameSaves.Find(Runner);
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private static bool Windowed => DisplayServer.GetName() != "headless";

        private static string Folder => SavePaths.Folder;
        private static string PathOf(string key) => SaveSlots.PathIn(Folder, key);
        private static MetaEntry MetaOf(string key) => SaveFileIO.ReadFromFile(PathOf(key))?.meta;

        private bool _quit;
        private string _slot2Before;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            PlanMigration();
            PlanTitleLoad();
            PlanSaving();
            PlanSecondPresses();
            PlanTowerAutosave();
            PlanQuitToTitle();
            PlanQuitGame();
            PlanShowcase();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        // --- 1. The save from before slots -------------------------------------------------------

        private void PlanMigration()
        {
            Do("a save from before slots", () =>
            {
                ScenarioSaves.Clear();
                PlaceDepot(DepotA);
                Sandbox.SaveTo(SavePaths.Legacy);
                return File.Exists(SavePaths.Legacy) && !Directory.Exists(Folder) ? null : "the old save wasn't written alone";
            });
            Reload("to the title", LaunchKind.Title);
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("it is Slot 1 now, and offered", () =>
            {
                if (File.Exists(SavePaths.Legacy) || !File.Exists(PathOf("slot-1")))
                {
                    return $"old file there {File.Exists(SavePaths.Legacy)}, Slot 1 there {File.Exists(PathOf("slot-1"))}";
                }
                Button cont = Title.Entries[TitleChoice.Continue], load = Title.Entries[TitleChoice.Load];
                if (cont.Disabled || load.Disabled || DetailOf(load) != "1 save" || !DetailOf(cont).StartsWith("Saved today at"))
                {
                    return $"Continue '{DetailOf(cont)}' disabled {cont.Disabled}, Load '{DetailOf(load)}' disabled {load.Disabled}";
                }
                Log("the old save moved into Slot 1");
                return null;
            });
        }

        // --- 2. Load on the title --------------------------------------------------------------

        private void PlanTitleLoad()
        {
            Do("click Load", () => { Click(Title.Entries[TitleChoice.Load]); return null; });
            Do("the slots are listed, Slot 1 lit", () =>
            {
                if (!Title.LoadOpen)
                {
                    return "no Load view";
                }
                SlotRow one = Title.SlotBoard.Row("slot-1");
                if (Title.SlotBoard.Selected != "slot-1" || one.Size != "0 golems · 1 building")
                {
                    return $"selected {Title.SlotBoard.Selected}, Slot 1 '{one.Where}' / '{one.Size}'";
                }
                foreach (string key in SaveSlots.Keys.Where(k => k != "slot-1"))
                {
                    Button b = Title.LoadSlots.Button(key, SlotAction.Load);
                    if (!b.Disabled || RowText(Title.LoadSlots, key, "Where") != SaveSlotBoard.EmptyText)
                    {
                        return $"{key}: Load enabled {!b.Disabled}, '{RowText(Title.LoadSlots, key, "Where")}'";
                    }
                }
                if (Title.LoadSlots.Button("slot-1", SlotAction.Save) != null)
                {
                    return "the title offers Save";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "title-load");
                return null;
            });
            Do("Escape", () => { Tap(Key.Escape); return null; });
            Do("back on the menu", () => !Title.LoadOpen && Title.IsOpen ? null : "Escape left Load up");
            Do("Up", () => Title.Menu.Selected == TitleChoice.Load ? TapAnd(null, Key.Up) : "Escape left the keyboard on " + Title.Menu.Selected);
            Do("Down to Load", () => Title.Menu.Selected == TitleChoice.Continue ? TapAnd(null, Key.Down) : "Up went to " + Title.Menu.Selected);
            Do("Enter", () => Title.Menu.Selected == TitleChoice.Load ? TapAnd(null, Key.Enter) : "selected " + Title.Menu.Selected);
            Do("Down wraps to the one slot that loads", () =>
            {
                if (!Title.LoadOpen)
                {
                    return "Enter didn't open Load";
                }
                Tap(Key.Down);
                return null;
            });
            Reloading("Enter on Slot 1", 1, () =>
            {
                if (Title.SlotBoard.Selected != "slot-1")
                {
                    throw new System.InvalidOperationException("selected " + Title.SlotBoard.Selected);
                }
                Tap(Key.Enter);
            });
            Until("loaded", 2, () => Title.LoadStatus != null ? null : "nothing loaded yet");
            Do("the game from Slot 1", () =>
            {
                if (World.Launch != LaunchKind.Continue || !Sandbox.Build.Buildings.Any(b => b.Cell == DepotA && !b.IsRemoved))
                {
                    return $"launch {World.Launch}, depot {Sandbox.Build.Buildings.Any(b => b.Cell == DepotA && !b.IsRemoved)}: {Title.LoadStatus}";
                }
                if (Tree.AutoAcceptQuit || !Saves.InGame)
                {
                    return "closing the window in a game would quit without the autosave";
                }
                Log("title Load: Slot 1 by the keys");
                return null;
            });
        }

        /// <summary>Taps <paramref name="key"/> and passes the step.</summary>
        private string TapAnd(string result, Key key)
        {
            Tap(key);
            return result;
        }

        // --- 3. Saving into a slot -------------------------------------------------------------

        private void PlanSaving()
        {
            Do("Tab", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "slots-world"); // what Slot 2's picture should show
                Tap(Key.Tab);
                return null;
            });
            Do("click Save / Load", () =>
            {
                if (!Management.IsOpen)
                {
                    return "Tab didn't open Management";
                }
                Click(Management.TabButtons[ManagementTab.SaveLoad]);
                return null;
            });
            Do("the slots", () =>
            {
                if (!Management.Slots.Button("slot-2", SlotAction.Save).IsVisibleInTree())
                {
                    return "the slot list isn't showing";
                }
                if (Management.Slots.Button(SaveSlots.AutosaveKey, SlotAction.Save).Visible)
                {
                    return "the autosave offers Save";
                }
                if (RowText(Management.Slots, "slot-1", "When").StartsWith("Saved today at") is false)
                {
                    return "Slot 1 reads " + RowText(Management.Slots, "slot-1", "When");
                }
                return Management.Slots.Button("slot-2", SlotAction.Load).Disabled && Management.Slots.Button("slot-2", SlotAction.Delete).Disabled
                    ? null : "an empty slot offers Load or Delete";
            });
            Do("click Save on Slot 2", () => { Click(Management.Slots.Button("slot-2", SlotAction.Save)); return null; });
            Do("saved at once, and described", () =>
            {
                if (Management.SaveStatus != "Slot 2: Saved 0 golems and 1 building." || Management.SlotBoard.Armed != null)
                {
                    return $"status '{Management.SaveStatus}', armed {Management.SlotBoard.Armed}";
                }
                string where = $"Chapter {Sandbox.Tutorial.ChapterOf(Sandbox.Tutorial.Index)} · {Sandbox.Tutorial.ChapterTitle(Sandbox.Tutorial.ChapterOf(Sandbox.Tutorial.Index))}";
                if (RowText(Management.Slots, "slot-2", "Where") != where || RowText(Management.Slots, "slot-2", "Size") != "0 golems · 1 building")
                {
                    return $"Slot 2 reads '{RowText(Management.Slots, "slot-2", "Where")}' / '{RowText(Management.Slots, "slot-2", "Size")}'";
                }
                return Sandbox.SavesMade == 1 ? null : $"{Sandbox.SavesMade} saves counted";
            });
            Until("its picture", 2, () =>
            {
                string png = SaveSlots.ThumbnailIn(Folder, "slot-2");
                bool shown = Management.Slots.RowControls["slot-2"].GetNode<TextureRect>("PictureFrame/Picture").Texture != null;
                if (!Windowed)
                {
                    return !File.Exists(png) && !shown && RowText(Management.Slots, "slot-2", "PictureFrame/NoPicture") == "No picture"
                        ? null : "a headless run drew a picture";
                }
                return File.Exists(png) && shown ? null : $"picture on disk {File.Exists(png)}, shown {shown}";
            });
            Do("capture", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "slots");
                if (ScenarioCapture.Directory != null && Windowed)
                {
                    File.Copy(SaveSlots.ThumbnailIn(Folder, "slot-2"), Path.Combine(ScenarioCapture.Directory, "slot-picture.png"), true);
                }
                Log(Windowed ? "Slot 2 saved with its picture" : "Slot 2 saved (headless: no picture)");
                return null;
            });
        }

        // --- Second presses --------------------------------------------------------------------

        private void PlanSecondPresses()
        {
            Do("another depot, then Save on Slot 2", () =>
            {
                PlaceDepot(DepotB);
                _slot2Before = File.ReadAllText(PathOf("slot-2"));
                Click(Management.Slots.Button("slot-2", SlotAction.Save));
                return null;
            });
            Do("it asks again, and saved nothing", () =>
            {
                if (!Management.SlotBoard.IsArmed("slot-2", SlotAction.Save) || LabelOf(Management.Slots.Button("slot-2", SlotAction.Save)) != "Save over")
                {
                    return $"armed {Management.SlotBoard.Armed}, the button reads '{LabelOf(Management.Slots.Button("slot-2", SlotAction.Save))}'";
                }
                if (!Management.SlotPrompt.StartsWith("Slot 2 holds a save from today at"))
                {
                    return "the prompt reads " + Management.SlotPrompt;
                }
                return File.ReadAllText(PathOf("slot-2")) == _slot2Before ? null : "the first press saved over it";
            });
            Do("click Delete on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Delete)); return null; });
            Do("that lets the Save go, and asks for itself", () =>
            {
                if (!Management.SlotBoard.IsArmed("slot-1", SlotAction.Delete) || LabelOf(Management.Slots.Button("slot-2", SlotAction.Save)) != "Save")
                {
                    return $"armed {Management.SlotBoard.Armed}, Slot 2's Save reads '{LabelOf(Management.Slots.Button("slot-2", SlotAction.Save))}'";
                }
                return LabelOf(Management.Slots.Button("slot-1", SlotAction.Delete)) == "Delete now" ? null : "no Delete now";
            });
            Do("click Delete now", () => { Click(Management.Slots.Button("slot-1", SlotAction.Delete)); return null; });
            Do("Slot 1 is empty", () =>
                !File.Exists(PathOf("slot-1")) && RowText(Management.Slots, "slot-1", "Where") == SaveSlotBoard.EmptyText && Management.SaveStatus == "Slot 1 is empty."
                    ? null : $"file there {File.Exists(PathOf("slot-1"))}, status '{Management.SaveStatus}'");

            Do("Save on Slot 2, then close", () => { Click(Management.Slots.Button("slot-2", SlotAction.Save)); return null; });
            Do("Escape", () =>
            {
                if (!Management.SlotBoard.IsArmed("slot-2", SlotAction.Save))
                {
                    return "the press didn't arm";
                }
                Tap(Key.Escape);
                return null;
            });
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("closing let it go", () =>
            {
                if (!Management.IsOpen || Management.SlotBoard.Armed != null || LabelOf(Management.Slots.Button("slot-2", SlotAction.Save)) != "Save")
                {
                    return $"open {Management.IsOpen}, armed {Management.SlotBoard.Armed}";
                }
                return Management.SlotPrompt == "" ? null : "the prompt stayed: " + Management.SlotPrompt;
            });
            Do("Save, Save over", () => { Click(Management.Slots.Button("slot-2", SlotAction.Save)); return null; });
            Do("Save over", () => { Click(Management.Slots.Button("slot-2", SlotAction.Save)); return null; });
            Do("saved over", () => MetaOf("slot-2")?.buildings == 2 ? null : $"Slot 2 holds {MetaOf("slot-2")?.buildings} buildings");

            Do("wreck a depot, then Load", () =>
            {
                Sandbox.Build.EnterDemolishMode();
                Sandbox.Build.PlaceOrRemove(DepotB);
                Sandbox.Build.CancelPlacement();
                Click(Management.Slots.Button("slot-2", SlotAction.Load));
                return null;
            });
            Do("Load asks again", () =>
            {
                if (!Management.SlotBoard.IsArmed("slot-2", SlotAction.Load) || !Management.SlotPrompt.Contains("haven't saved will be lost"))
                {
                    return $"armed {Management.SlotBoard.Armed}, prompt '{Management.SlotPrompt}'";
                }
                if (Sandbox.Build.Buildings.Any(b => b.Cell == DepotB && !b.IsRemoved))
                {
                    return "the first press loaded";
                }
                Click(Management.Slots.Button("slot-2", SlotAction.Load));
                return null;
            });
            Do("Load now loaded it", () =>
                Sandbox.Build.Buildings.Count(b => !b.IsRemoved) == 2 && Management.SaveStatus.StartsWith("Slot 2: Loaded")
                    ? null : $"{Sandbox.Build.Buildings.Count(b => !b.IsRemoved)} buildings, status '{Management.SaveStatus}'");
            Do("close", () =>
            {
                Tap(Key.Escape);
                Log("saving over, deleting and loading each took a second press; another press and closing let it go");
                return null;
            });
        }

        // --- 4. Autosaves ----------------------------------------------------------------------

        private void PlanTowerAutosave()
        {
            Do("a tower stage completes", () =>
            {
                if (Management.IsOpen || File.Exists(PathOf(SaveSlots.AutosaveKey)))
                {
                    return "Management still open, or an autosave already";
                }
                EventBus.Publish(new ClockTowerStageCompletedEvent(1, "Foundation", false, Sandbox.Clock.CurrentTick));
                return null;
            });
            Until("it autosaves", 1, () =>
            {
                MetaEntry meta = File.Exists(PathOf(SaveSlots.AutosaveKey)) ? MetaOf(SaveSlots.AutosaveKey) : null;
                if (meta == null || !meta.autosave || meta.reason != "Tower stage 1 complete")
                {
                    return "no autosave for the stage yet";
                }
                if (Sandbox.AutosaveDue != null || Sandbox.AutosavesMade != 1)
                {
                    return $"due '{Sandbox.AutosaveDue}', {Sandbox.AutosavesMade} made";
                }
                return Sandbox.SavesMade == 2 ? null : $"the autosave counted as the player's Save ({Sandbox.SavesMade})";
            });
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("the Autosave row says so", () =>
            {
                string when = RowText(Management.Slots, SaveSlots.AutosaveKey, "When");
                if (!when.StartsWith("Autosaved today at") || !when.EndsWith("· Tower stage 1 complete"))
                {
                    return "the Autosave reads " + when;
                }
                return Management.Slots.Button(SaveSlots.AutosaveKey, SlotAction.Load).Disabled ? "the Autosave can't be loaded" : null;
            });
        }

        private void PlanQuitToTitle()
        {
            Do("another depot", () => { PlaceDepot(DepotB + new CoreVector2Int(0, -2)); return null; });
            Reloading("click Quit to title", 1, () => Click(Management.QuitToTitleButton));
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("it autosaved first, and the title offers that", () =>
            {
                MetaEntry meta = MetaOf(SaveSlots.AutosaveKey);
                if (meta?.reason != GameSaves.QuitReason || meta.buildings != 3)
                {
                    return $"the autosave: '{meta?.reason}', {meta?.buildings} buildings";
                }
                Button cont = Title.Entries[TitleChoice.Continue];
                if (!DetailOf(cont).StartsWith("Autosaved today at") || DetailOf(Title.Entries[TitleChoice.Load]) != "2 saves")
                {
                    return $"Continue '{DetailOf(cont)}', Load '{DetailOf(Title.Entries[TitleChoice.Load])}'";
                }
                if (!Tree.AutoAcceptQuit)
                {
                    return "the title would ignore the window's close";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "title-autosaved");
                return null;
            });
            Reloading("Continue", 1, () => Click(Title.Entries[TitleChoice.Continue]));
            Until("loaded", 2, () => Title.LoadStatus != null ? null : "nothing loaded yet");
            Do("the autosave's game", () =>
            {
                int built = Sandbox.Build.Buildings.Count(b => !b.IsRemoved);
                if (built != 3)
                {
                    return $"{built} buildings: {Title.LoadStatus}";
                }
                Log("Quit to title autosaved, and Continue resumed it");
                return null;
            });
        }

        private void PlanQuitGame()
        {
            Do("Tab", () =>
            {
                Saves.QuitAction = () => _quit = true;
                Tap(Key.Tab);
                return null;
            });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Quit to desktop", () =>
            {
                File.Delete(PathOf(SaveSlots.AutosaveKey));
                Click(Management.QuitGameButton);
                return null;
            });
            Until("it autosaved, then quit", 1, () =>
                _quit && MetaOf(SaveSlots.AutosaveKey)?.reason == GameSaves.QuitReason ? null : $"quit {_quit}");

            Reload("a new game", LaunchKind.NewGame);
            Do("close the window", () =>
            {
                _quit = false;
                PlaceDepot(DepotA);
                Saves.QuitAction = () => _quit = true;
                File.Delete(PathOf(SaveSlots.AutosaveKey));
                Tree.Root.PropagateNotification((int)Node.NotificationWMCloseRequest);
                return null;
            });
            Until("it autosaved, then quit", 1, () =>
            {
                MetaEntry meta = File.Exists(PathOf(SaveSlots.AutosaveKey)) ? MetaOf(SaveSlots.AutosaveKey) : null;
                return _quit && meta?.reason == GameSaves.QuitReason && meta.buildings == 1 ? null : $"quit {_quit}, autosave {meta?.buildings}";
            });
            Do("log", () =>
            {
                Log("Quit to desktop and the window's close each autosaved first");
                return null;
            });
        }

        private void PlanShowcase()
        {
            Reload("to the title", LaunchKind.Title);
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("a tower stage on the title", () =>
            {
                File.Delete(PathOf(SaveSlots.AutosaveKey));
                EventBus.Publish(new ClockTowerStageCompletedEvent(1, "Foundation", false, Sandbox.Clock.CurrentTick));
                return null;
            });
            Wait(0.5);
            Do("the showcase never saves", () =>
            {
                if (File.Exists(PathOf(SaveSlots.AutosaveKey)) || Sandbox.AutosavesMade != 0 || Sandbox.SaveFolder != null)
                {
                    return "the showcase autosaved";
                }
                Log("the showcase never autosaves");
                return null;
            });
        }

        // --- Helpers ---------------------------------------------------------------------------

        private void PlaceDepot(CoreVector2Int cell)
        {
            PlaceableBuilding prefab = Sandbox.Placeables.Single(p => p.Key == "DepotPrefab").Prefab;
            foreach (var c in prefab.Cost)
            {
                Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
            }
            Sandbox.Build.SetActivePrefab(prefab);
            Sandbox.Build.PlaceOrRemove(cell);
            Sandbox.Build.CancelPlacement();
            if (!Sandbox.Build.Buildings.Any(b => b.Cell == cell && !b.IsRemoved))
            {
                throw new System.InvalidOperationException($"no depot at {cell}: {Sandbox.Build.LastStatusMessage}");
            }
        }

        private static string RowText(SaveSlotList list, string key, string line) =>
            list.RowControls[key].GetNodeOrNull<Label>(line)?.Text ?? "";

        private static string LabelOf(Button button) => button.GetNode<Label>("Label").Text;

        private static string DetailOf(Button entry) => entry.GetNodeOrNull<Label>("Detail")?.Text ?? "";
    }
}
