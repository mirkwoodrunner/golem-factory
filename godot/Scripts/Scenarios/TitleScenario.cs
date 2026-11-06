using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.Settings;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The title screen (docs/menus-design.md §6), with real keys and clicks, across the scene
    /// reloads that New game and Continue make (<see cref="IReloadingScenario"/>):
    /// <list type="number">
    ///   <item>With no save, the title opens over the staged workshop: golems at work, the camera
    ///   drifting at Near with the workshop clear of the menu, Continue greyed. The HUD, build
    ///   menu and guide are hidden, and no key or click reaches the world.</item>
    ///   <item>The keys walk the entries (wrapping past the greyed Continue), the pointer lights
    ///   one, Settings opens the tab (a rebind, Reduced motion stilling the drift), Back and
    ///   Escape return, Quit is chosen.</item>
    ///   <item>New game reloads onto a fresh game, with no listener left on the bus by the title's
    ///   world. The game is saved, and the title offers it: Enter on Continue loads it.</item>
    ///   <item>A save that can't be read brings Continue back to the title, saying so.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class TitleScenario : StepScenario
    {
        // Found again after every reload.
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private TitleScreen Title => TitleScreen.Find(Runner);
        private PlayerNode Player => Tree.Root.FindChild("Player", true, false) as PlayerNode;
        private CameraRigNode Camera => Runner.GetViewport().GetCamera2D() as CameraRigNode;
        private HudOverlay Hud => Tree.Root.FindChild("Hud", true, false) as HudOverlay;
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private PackScreen Pack => Tree.Root.FindChild("PackScreen", true, false) as PackScreen;
        private BuildMenuNode BuildMenu => Tree.Root.FindChild("BuildMenu", true, false) as BuildMenuNode;
        private TutorialPanel Guide => Tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;

        private static string SaveFile => SaveSlots.PathIn(SavePaths.Folder, SaveSlots.ManualKey(1));

        private int _gameListeners = -1;
        private Vector2 _playerAt;
        private Vector2 _cameraAt;
        private bool _quitChosen;
        private int _savedScrap;
        private CoreVector2Int _savedDepot = new CoreVector2Int(-6, -2);

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no save", () =>
            {
                ScenarioSaves.Clear();
                return System.IO.File.Exists(SaveFile) ? "could not clear the scratch save" : null;
            });
            Do("a game's listeners", () =>
            {
                _gameListeners = EventBus.ListenerCount;
                return _gameListeners > 0 ? null : "a game with nothing on the bus";
            });
            Reload("to the title", LaunchKind.Title);
            PlanTitle();
            PlanIsolation();
            PlanKeys();
            PlanSettings();
            PlanNewGame();
            PlanContinue();
            PlanBadSave();
        }

        // --- 1. The title over the workshop -----------------------------------------------------

        private void PlanTitle()
        {
            Until("the workshop is staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("the title is up", () =>
            {
                if (World.Launch != LaunchKind.Title || !Title.IsOpen || !ModalScreens.AnyOpen(Tree))
                {
                    return $"launch {World.Launch}, title open {Title.IsOpen}";
                }
                if (Hud.Showing || BuildMenu.IsBodyVisible || Guide.Plate.IsVisibleInTree())
                {
                    return $"behind the title: HUD {Hud.Showing}, build menu {BuildMenu.IsBodyVisible}, guide {Guide.Plate.IsVisibleInTree()}";
                }
                Button cont = Title.Entries[TitleChoice.Continue];
                if (!cont.Disabled || !DetailOf(cont).Contains(TitleMenu.NoSave))
                {
                    return $"Continue without a save: disabled {cont.Disabled}, '{DetailOf(cont)}'";
                }
                if (Title.Menu.Selected != TitleChoice.NewGame)
                {
                    return "the keyboard starts on " + Title.Menu.Selected;
                }
                return Title.Showcase.ChapterReached == TitleShowcase.Chapters + 1 ? null : "staged to chapter " + Title.Showcase.ChapterReached;
            });
            Do("the camera frames the workshop right of the menu", () =>
            {
                CameraDirector director = Sandbox.Camera;
                if (director.Focus != CameraFocus.Showcase || CameraDirector.ZoomNames[director.ZoomLevel] != "Near")
                {
                    return $"focus {director.Focus} at {CameraDirector.ZoomNames[director.ZoomLevel]}";
                }
                if (Math.Abs(Camera.OrthographicSize - CameraDirector.ZoomLevels[CameraDirector.ShowcaseZoomLevel]) > 0.01f)
                {
                    return "the camera is at size " + Camera.OrthographicSize;
                }
                Vector2 middle = ScreenOf(GridConversions.PointToWorld(Title.Showcase.Centre));
                Rect2 menu = Title.Entries[TitleChoice.Quit].GetGlobalRect().Merge(Title.Entries[TitleChoice.Continue].GetGlobalRect());
                Rect2 screen = Runner.GetViewport().GetVisibleRect();
                if (middle.X < menu.End.X + 40f || middle.X > screen.End.X - 120f || middle.Y < 120f || middle.Y > screen.End.Y - 120f)
                {
                    return $"the workshop's middle is drawn at {middle}, the menu ends at x {menu.End.X}";
                }
                _cameraAt = Camera.GlobalPosition;
                return null;
            });
            Until("the golems work and the camera drifts", 4, () =>
            {
                List<GolemEntity> golems = Sandbox.Golems.Where(g => g != null && !g.IsRemoved).ToList();
                int working = golems.Count(g => g.Mood == GolemMood.Working);
                GolemEntity stopped = golems.FirstOrDefault(g => g.Mood == GolemMood.Stalled || g.Mood == GolemMood.Starved);
                if (stopped != null)
                {
                    return $"{UI.GolemNames.Short(stopped)} stopped ({stopped.StallReason})";
                }
                int drawn = Tree.GetNodesInGroup(GolemNodeGroup.Name).Count;
                if (golems.Count < 6 || drawn != golems.Count || working < 3)
                {
                    return $"{golems.Count} golems, {drawn} drawn, {working} at work";
                }
                float drift = Camera.GlobalPosition.DistanceTo(_cameraAt);
                return drift > 8f ? null : $"the camera moved {drift:F1} px";
            });
            Do("capture", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "title");
                Log($"title over {Sandbox.Golems.Count(g => !g.IsRemoved)} golems");
                return null;
            });
        }

        // --- Nothing reaches the world ----------------------------------------------------------

        private void PlanIsolation()
        {
            Do("note where things are", () =>
            {
                _playerAt = Player.GlobalPosition;
                return null;
            });
            Do("hold D", () => { Press(Key.D, true); return null; });
            Do("Tab, 1, I, F1, Escape and the wheel", () =>
            {
                Press(Key.D, false);
                Tap(Key.Tab);
                return null;
            });
            Do("1", () => { Tap(Key.Key1); return null; });
            Do("I", () => { Tap(Key.I); return null; });
            Do("F1", () => { Tap(Key.F1); return null; });
            Do("Escape", () => { Tap(Key.Escape); return null; });
            Do("wheel", () =>
            {
                Vector2 at = Runner.GetViewport().GetVisibleRect().GetCenter() + new Vector2(200f, 0f);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Factor = 1f, Position = at, GlobalPosition = at });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = false, Factor = 1f, Position = at, GlobalPosition = at });
                return null;
            });
            Do("the world never heard them", () =>
            {
                if (Player.GlobalPosition.DistanceTo(_playerAt) > 0.5f)
                {
                    return $"the player walked from {_playerAt} to {Player.GlobalPosition}";
                }
                if (Management.IsOpen || Pack.IsOpen || Sandbox.Build.IsBuildToolActive)
                {
                    return $"Management {Management.IsOpen}, Pack {Pack.IsOpen}, build tool {Sandbox.Build.IsBuildToolActive}";
                }
                if (CameraDirector.ZoomNames[Sandbox.Camera.ZoomLevel] != "Near")
                {
                    return "the wheel zoomed to " + CameraDirector.ZoomNames[Sandbox.Camera.ZoomLevel];
                }
                if (Sandbox.Tutorial.Dismissed)
                {
                    return "F1 put the guide away";
                }
                return Title.IsOpen && !Title.SettingsOpen ? null : "Escape changed the title";
            });
        }

        // --- The keys and the pointer -----------------------------------------------------------

        private void PlanKeys()
        {
            Expect(Key.Down, TitleChoice.Creative);
            Expect(Key.Up, TitleChoice.NewGame);
            Expect(Key.Up, TitleChoice.Quit);    // past the greyed Load and Continue
            Expect(Key.S, TitleChoice.NewGame);  // the move keys walk it too
            Do("point at Settings", () => { PointAt(Title.Entries[TitleChoice.Settings]); return null; });
            Do("the pointer lights it", () => Title.Menu.Selected == TitleChoice.Settings ? null : "selected " + Title.Menu.Selected);
            Do("point at the greyed Continue", () => { PointAt(Title.Entries[TitleChoice.Continue]); return null; });
            Do("it stays dark", () => Title.Menu.Selected == TitleChoice.Settings ? null : "selected " + Title.Menu.Selected);
            Do("click Quit", () =>
            {
                Title.QuitAction = () => _quitChosen = true;
                Click(Title.Entries[TitleChoice.Quit]);
                return null;
            });
            Do("Quit is chosen", () => _quitChosen ? null : "Quit did nothing");
        }

        private void Expect(Key key, TitleChoice selected)
        {
            Do(key.ToString(), () => { Tap(key); return null; });
            Do(key + " selects " + selected, () => Title.Menu.Selected == selected ? null : "selected " + Title.Menu.Selected);
        }

        // --- Settings ---------------------------------------------------------------------------

        private void PlanSettings()
        {
            Do("click Settings", () => { Click(Title.Entries[TitleChoice.Settings]); return null; });
            Do("the tab is up", () =>
            {
                if (!Title.SettingsOpen || !Title.Settings.KeyButtons[PlayerNode.Interact].IsVisibleInTree())
                {
                    return "no Settings tab";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "title-settings");
                return null;
            });
            Do("click Use's key", () => { Click(Title.Settings.KeyButtons[PlayerNode.Interact]); return null; });
            Do("Escape cancels the listening, not the tab", () =>
            {
                if (!Title.Settings.IsCapturing)
                {
                    return "the key button isn't listening";
                }
                Tap(Key.Escape);
                return null;
            });
            Do("still in Settings", () => Title.SettingsOpen && !Title.Settings.IsCapturing ? null : $"open {Title.SettingsOpen}, listening {Title.Settings.IsCapturing}");
            Do("click Use's key again", () => { Click(Title.Settings.KeyButtons[PlayerNode.Interact]); return null; });
            Do("F", () => { Tap(Key.F); return null; });
            Do("Use is F", () => KeyNames.Interact == "F" ? null : "Use reads " + KeyNames.Interact);
            Do("click Reset keys", () => { Click(Title.Settings.ResetKeys); return null; });
            Do("Use is E again", () => KeyNames.Interact == "E" ? null : "Use reads " + KeyNames.Interact);

            Do("click Reduced motion", () => { Click(Title.Settings.ReducedMotion); return null; });
            Do("note the camera", () =>
            {
                if (!Sandbox.Motion.Reduced)
                {
                    return "Reduced motion didn't take";
                }
                _cameraAt = Camera.GlobalPosition;
                return null;
            });
            Wait(1.5);
            Do("the drift holds still", () =>
            {
                float moved = Camera.GlobalPosition.DistanceTo(_cameraAt);
                return moved < 0.01f ? null : $"the camera moved {moved:F2} px on Reduced motion";
            });
            Do("click Reduced motion off", () => { Click(Title.Settings.ReducedMotion); return null; });
            Do("Back", () =>
            {
                if (Sandbox.Motion.Reduced)
                {
                    return "Reduced motion stayed on";
                }
                Click(Title.BackButton);
                return null;
            });
            Do("back on the title", () => !Title.SettingsOpen && Title.Entries[TitleChoice.NewGame].IsVisibleInTree() ? null : "Back left Settings up");
            Do("Enter on Settings", () => { Tap(Key.Enter); return null; });
            Do("Settings again", () => Title.SettingsOpen ? null : "Enter on Settings didn't open it");
            Do("Escape", () => { Tap(Key.Escape); return null; });
            Do("Escape returns", () => !Title.SettingsOpen && Title.IsOpen ? null : "Escape didn't return to the title");
        }

        // --- New game, then Continue --------------------------------------------------------------

        private void PlanNewGame()
        {
            Do("Down to New game", () =>
            {
                Tap(Key.Down); // Settings -> Quit
                return null;
            });
            Do("Down again", () => { Tap(Key.Down); return null; }); // Quit -> New game (Continue and Load greyed)
            Do("on New game", () => Title.Menu.Selected == TitleChoice.NewGame ? null : "selected " + Title.Menu.Selected);
            Reloading("click New game", 1, () => Click(Title.Entries[TitleChoice.NewGame]));
            Do("a fresh game", () =>
            {
                if (World.Launch != LaunchKind.NewGame || Title.IsOpen || ModalScreens.AnyOpen(Tree))
                {
                    return $"launch {World.Launch}, title open {Title.IsOpen}";
                }
                if (Sandbox.Golems.Any(g => !g.IsRemoved) || Sandbox.Build.Buildings.Any(b => !b.IsRemoved))
                {
                    return "the showcase's golems or buildings came along";
                }
                if (Sandbox.Tutorial.Index != 0 || Sandbox.Camera.Focus != CameraFocus.Player)
                {
                    return $"guide at step {Sandbox.Tutorial.Index}, camera on {Sandbox.Camera.Focus}";
                }
                return Hud.Showing ? null : "no HUD";
            });
            Do("nothing left on the bus", () =>
            {
                int now = EventBus.ListenerCount;
                return now == _gameListeners ? null : $"{now} listeners, a fresh game has {_gameListeners}: the title's world is still listening";
            });
            Do("hold D", () =>
            {
                _playerAt = Player.GlobalPosition;
                Press(Key.D, true);
                return null;
            });
            Wait(0.4);
            Do("the player walks", () =>
            {
                Press(Key.D, false);
                return Player.GlobalPosition.X > _playerAt.X + 8f ? null : $"the player stayed at {Player.GlobalPosition}";
            });
            Do("make something and save it", () =>
            {
                PlaceableBuilding prefab = Sandbox.Placeables.Single(p => p.Key == "DepotPrefab").Prefab;
                foreach (var c in prefab.Cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
                }
                Sandbox.Build.SetActivePrefab(prefab);
                Sandbox.Build.PlaceOrRemove(_savedDepot);
                Sandbox.Build.CancelPlacement();
                Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, ItemType.Scrap, 17);
                _savedScrap = Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, ItemType.Scrap);
                if (!Sandbox.Build.Buildings.Any(b => b.Cell == _savedDepot && !b.IsRemoved))
                {
                    return "the depot wasn't placed: " + Sandbox.Build.LastStatusMessage;
                }
                Sandbox.SaveTo(SaveFile);
                return System.IO.File.Exists(SaveFile) ? null : "no save written";
            });
        }

        private void PlanContinue()
        {
            Reload("back to the title", LaunchKind.Title);
            Until("staged again", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("Continue offers the save", () =>
            {
                Button cont = Title.Entries[TitleChoice.Continue];
                if (cont.Disabled || !DetailOf(cont).StartsWith("Saved today at"))
                {
                    return $"Continue: disabled {cont.Disabled}, '{DetailOf(cont)}'";
                }
                if (Title.Menu.Selected != TitleChoice.Continue)
                {
                    return "the keyboard starts on " + Title.Menu.Selected;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "title-continue");
                return null;
            });
            Reloading("Enter on Continue", 1, () => Tap(Key.Enter));
            Until("the save is loaded", 2, () => Title.LoadStatus != null ? null : "nothing loaded yet");
            Do("the saved game is back", () =>
            {
                if (World.Launch != LaunchKind.Continue || Title.IsOpen)
                {
                    return $"launch {World.Launch}, title open {Title.IsOpen}";
                }
                if (!Sandbox.Build.Buildings.Any(b => b.Cell == _savedDepot && !b.IsRemoved))
                {
                    return "the depot didn't come back: " + Title.LoadStatus;
                }
                int scrap = Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, ItemType.Scrap);
                if (scrap != _savedScrap)
                {
                    return $"{scrap} Scrap in the pack, {_savedScrap} saved";
                }
                if (Sandbox.Golems.Any(g => !g.IsRemoved))
                {
                    return "the showcase's golems came along";
                }
                Log("continued: " + Title.LoadStatus);
                return null;
            });
            Do("nothing left on the bus", () =>
            {
                int now = EventBus.ListenerCount;
                return now == _gameListeners ? null : $"{now} listeners, a fresh game has {_gameListeners}";
            });
        }

        // --- A save that can't be read --------------------------------------------------------------

        private void PlanBadSave()
        {
            // The title reads the save when it opens; the file goes bad after that, as a save
            // deleted or damaged behind the game's back would.
            Reload("to the title", LaunchKind.Title);
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("Continue is offered, then the save goes bad", () =>
            {
                if (Title.Entries[TitleChoice.Continue].Disabled)
                {
                    return "Continue is greyed with a good save there";
                }
                System.IO.File.WriteAllText(SaveFile, "{ this is not a save");
                return null;
            });
            // Two reloads: onto the load, which fails and goes back to the title.
            Reloading("click Continue", 2, () => Click(Title.Entries[TitleChoice.Continue]));
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("the title says why, and offers it no more", () =>
            {
                if (World.Launch != LaunchKind.Title || !Title.IsOpen)
                {
                    return $"launch {World.Launch}";
                }
                if (!Title.NoticeText.Contains("couldn't be read"))
                {
                    return $"the notice reads '{Title.NoticeText}'";
                }
                Button cont = Title.Entries[TitleChoice.Continue];
                if (!cont.Disabled || DetailOf(cont) != TitleMenu.Unreadable)
                {
                    return $"Continue: disabled {cont.Disabled}, '{DetailOf(cont)}'";
                }
                if (Title.Menu.Selected != TitleChoice.NewGame)
                {
                    return "the keyboard starts on " + Title.Menu.Selected;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "title-notice");
                return System.IO.File.ReadAllText(SaveFile) == "{ this is not a save" ? null : "the bad save was changed";
            });
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        private Vector2 ScreenOf(Vector2 world) => Runner.GetViewport().GetCanvasTransform() * world;

        private static string DetailOf(Button entry) => entry.GetNodeOrNull<Label>("Detail")?.Text ?? "";
    }
}
