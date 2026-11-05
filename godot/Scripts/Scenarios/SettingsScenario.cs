using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Settings;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The Settings tab (docs/menus-design.md step 1), driven with real keys and clicks:
    /// <list type="bullet">
    ///   <item>Tab opens Management and a click opens Settings.</item>
    ///   <item>Rebinding: a click on Use's key listens; reserved keys (Tab, a build-menu digit, Shift) are
    ///   refused and keep it listening, with the screen still open; E (its own key) ends it
    ///   unchanged, the screen still open; F binds it. Carry onto F swaps (Use takes G). Escape
    ///   while listening cancels and leaves the screen open. Switching tabs cancels.</item>
    ///   <item>The rebind is live: the InputMap holds the new keys, the guide panel's step 1 and the
    ///   Pack screen's hint name them, and at the Scrap stall the prompt reads "[G] Harvest" and G
    ///   harvests while E does nothing.</item>
    ///   <item>Reset keys restores the defaults, in the InputMap and the text.</item>
    ///   <item>UI scale by clicks on its track: 1.5 is kept but a 1280x720 window stays at 1 (the
    ///   screens' fixed layouts need that canvas) and the note says so; 1920x1080 lets it through;
    ///   a 1024x576 window is fitted down to 0.8; every tab button and Close stay on screen and
    ///   apart at each. Fullscreen, Reduced motion and Colour-blind marks toggle by click.</item>
    ///   <item>At 1.5 the camera still frames as many cells as at 1: only the size of things grows.</item>
    ///   <item>A fresh read of the saved file (as a restart sees it) equals what is shown, and a
    ///   hand-edited file with a key Godot doesn't know and a reserved key reads back safe.</item>
    /// </list>
    /// Writes only <see cref="GameSettingsStore.ScenarioPath"/>, never the player's settings.
    /// Captures <c>screen-settings</c>, <c>settings-listening</c>, <c>settings-scale</c>,
    /// <c>settings-scale-world</c> and <c>settings-small-window</c>.
    /// </summary>
    public sealed class SettingsScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private ManagementScreen _screen;
        private TutorialPanel _guide;
        private PackScreen _pack;
        private PlayerNode _player;
        private InteractionPromptNode _prompt;
        private SettingsTab _tab;
        private int _wait;
        private Key? _heldTap;
        private int _scrapBefore;
        private float _cellsTall;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _screen = _tree.Root.FindChild("Management", true, false) as ManagementScreen;
            _guide = _tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;
            _pack = _tree.Root.FindChild("PackScreen", true, false) as PackScreen;
            _player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            _prompt = _tree.Root.FindChild("InteractionPrompt", true, false) as InteractionPromptNode;
            if (_screen == null || _guide == null || _pack == null || _player == null || _prompt == null)
            {
                Do("scene", () => "Sandbox.tscn is missing Management, the guide, the Pack screen, the player or the prompt");
                return;
            }
            GameSettingsStore.ResetForScenario(_tree.Root);

            PlanOpen();
            PlanRebind();
            PlanLive();
            PlanReset();
            PlanDisplay();
            PlanReload();
        }

        public ScenarioResult? Step(double delta)
        {
            if (_wait-- > 0)
            {
                return null;
            }
            if (_heldTap is Key held)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
                _heldTap = null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Dequeue();
            string failure = step();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        private void PlanOpen()
        {
            Do("the guide names the default keys", () =>
                _guide.BodyText.Contains("Walk with WASD") && _guide.BodyText.Contains("Press E to take Scrap")
                    ? null : $"guide reads '{_guide.BodyText}'");
            Do("note how many cells tall the view is at scale 1", () =>
            {
                _cellsTall = CellsTall();
                return _cellsTall > 0f ? null : "no camera";
            });
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("Management is open", () => _screen.IsOpen ? null : "Tab did not open Management");
            Do("click the Settings tab", () => { Click(_screen.TabButtons[ManagementTab.Settings]); return null; });
            Do("Settings shows", () =>
            {
                _tab = _screen.Settings;
                if (!_screen.TabContent[ManagementTab.Settings].Visible)
                {
                    return "the Settings tab is not showing";
                }
                string[] expected = KeyBindings.Actions().ToArray();
                if (!expected.All(a => _tab.KeyButtons.ContainsKey(a)))
                {
                    return "a rebindable action has no button";
                }
                string use = _tab.KeyButtons["interact"].Text;
                return use == "E" ? null : $"Use's button reads '{use}'";
            });
            Do("capture the Settings tab", () => { ScenarioCapture.Shot(_tree.Root, "screen-settings"); return null; });
        }

        private void PlanRebind()
        {
            Do("click Use's key", () => { ClickKey("interact"); return null; });
            Do("it listens", () =>
                _tab.IsCapturing && _tab.KeyButtons["interact"].Text == "press a key..." && _tab.KeyStatus.Text.Contains("Press a key for Use")
                    ? null : $"capturing {_tab.IsCapturing}, button '{_tab.KeyButtons["interact"].Text}', status '{_tab.KeyStatus.Text}'");
            Do("capture the listening button", () => { ScenarioCapture.Shot(_tree.Root, "settings-listening"); return null; });

            Do("press Tab (reserved)", () => { Tap(Key.Tab); return null; });
            Do("Tab is refused, the screen stays open and it still listens", () =>
                _screen.IsOpen && _tab.IsCapturing && _tab.KeyStatus.Text.Contains("kept for the game") && Keys("interact") == "E"
                    ? null : $"open {_screen.IsOpen}, capturing {_tab.IsCapturing}, status '{_tab.KeyStatus.Text}', keys {Keys("interact")}");
            Do("press 3 (a build-menu row)", () => { Tap(Key.Key3); return null; });
            Do("3 is refused too", () =>
                _tab.IsCapturing && _tab.KeyStatus.Text.StartsWith("3 is kept") ? null : $"status '{_tab.KeyStatus.Text}'");
            Do("press Shift (Shift-click's)", () => { Tap(Key.Shift); return null; });
            Do("Shift is refused too", () =>
                _tab.IsCapturing && _tab.KeyStatus.Text.StartsWith("Shift is kept") ? null : $"status '{_tab.KeyStatus.Text}'");

            Do("press E (Use's own key)", () => { Tap(Key.E); return null; });
            Do("E ends it unchanged, and did not close the screen", () =>
                _screen.IsOpen && !_tab.IsCapturing && Keys("interact") == "E"
                    ? null : $"open {_screen.IsOpen}, capturing {_tab.IsCapturing}, keys {Keys("interact")}");

            Do("click Use's key again", () => { ClickKey("interact"); return null; });
            Do("press F", () => { Tap(Key.F); return null; });
            Do("Use is F, in the settings, the InputMap and the button", () =>
                Keys("interact") == "F" && MapKeys("interact") == "F" && _tab.KeyButtons["interact"].Text == "F"
                && _tab.KeyStatus.Text == "Use is now F."
                    ? null : $"keys {Keys("interact")}, map {MapKeys("interact")}, button '{_tab.KeyButtons["interact"].Text}', status '{_tab.KeyStatus.Text}'");

            Do("click Carry's key", () => { ClickKey("golem_carry"); return null; });
            Do("press F (Use's)", () => { Tap(Key.F); return null; });
            Do("Carry is F and Use took G", () =>
                Keys("golem_carry") == "F" && Keys("interact") == "G" && MapKeys("interact") == "G" && MapKeys("golem_carry") == "F"
                && _tab.KeyStatus.Text.Contains("Use took its old key")
                    ? null : $"carry {Keys("golem_carry")}, use {Keys("interact")}, status '{_tab.KeyStatus.Text}'");

            Do("click Pack's key", () => { ClickKey("inventory"); return null; });
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("Escape cancels, keeps I, and leaves the screen open", () =>
                _screen.IsOpen && !_tab.IsCapturing && Keys("inventory") == "I" && _tab.KeyButtons["inventory"].Text == "I"
                    ? null : $"open {_screen.IsOpen}, capturing {_tab.IsCapturing}, keys {Keys("inventory")}");

            Do("click Turn's key", () => { ClickKey("build_rotate"); return null; });
            Do("switch to the Ledger", () => { Click(_screen.TabButtons[ManagementTab.TechTree]); return null; });
            Do("leaving the tab cancels", () => !_tab.IsCapturing && Keys("build_rotate") == "R" ? null : "still listening after the tab changed");
            Do("back to Settings", () => { Click(_screen.TabButtons[ManagementTab.Settings]); return null; });
            Do("press Escape to close", () => { Tap(Key.Escape); return null; });
            Do("Management closed", () => !_screen.IsOpen ? null : "Escape did not close Management");
            Do("rebinds", () => { _log.Add("Tab and 3 refused, E unchanged, Use→F, Carry→F swapped Use to G, Escape and a tab change cancel, the screen never closed mid-capture"); return null; });
        }

        private void PlanLive()
        {
            Do("the guide's step 1 names the new key", () =>
                _guide.BodyText.Contains("Press G to take Scrap; hold G to keep") ? null : $"guide reads '{_guide.BodyText}'");

            SandboxSetup.NodeEntry scrap = _world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Do("walk up to the Scrap stall", () =>
            {
                _player.TeleportTo(new CoreVector3(scrap.x, scrap.y + 1f, 0f));
                return null;
            });
            Do("the prompt names G", () =>
            {
                _scrapBefore = Pack(ItemType.Scrap);
                return _prompt.Caption.StartsWith("[G]") && _prompt.Caption.Contains("Scrap") ? null : $"caption '{_prompt.Caption}'";
            });
            Do("press E (no longer Use)", () => { Tap(Key.E); return null; });
            Do("E harvested nothing", () => Pack(ItemType.Scrap) == _scrapBefore ? null : "E still harvests");
            Do("press G (now Use)", () => { Tap(Key.G); return null; });
            Do("G harvested one", () =>
                Pack(ItemType.Scrap) == _scrapBefore + 1 ? null : $"pack went {_scrapBefore} -> {Pack(ItemType.Scrap)}");
            Do("press I", () => { Tap(Key.I); return null; });
            Do("the Pack screen's hint names I", () =>
                _pack.IsOpen && _pack.HintText.Contains("press I to fill") ? null : $"open {_pack.IsOpen}, hint '{_pack.HintText}'");
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("live", () =>
            {
                _log.Add($"after the rebind: guide 'Press G to take Scrap', prompt '{_prompt.Caption}', G harvested and E didn't");
                return null;
            });
        }

        private void PlanReset()
        {
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("click Reset keys", () => { Click(_tab.ResetKeys); return null; });
            Do("the defaults are back", () =>
                Keys("interact") == "E" && Keys("golem_carry") == "G" && MapKeys("interact") == "E" && MapKeys("golem_carry") == "G"
                && _tab.KeyButtons["interact"].Text == "E" && _tab.KeyButtons["move_left"].Text == "A / Left"
                    ? null : $"use {Keys("interact")}, carry {Keys("golem_carry")}, button '{_tab.KeyButtons["interact"].Text}'");
            Do("press Escape (the guide hides behind full screens)", () => { Tap(Key.Escape); return null; });
            Do("the guide reads E again", () =>
                _guide.BodyText.Contains("Press E to take Scrap") ? null : $"guide reads '{_guide.BodyText}'");
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("Settings is still the tab", () => _screen.TabContent[ManagementTab.Settings].Visible ? null : "Settings is not showing");
        }

        private void PlanDisplay()
        {
            Do("click the UI scale track at 1.5", () =>
            {
                // Range 1..2 in 0.25 steps: 1.5 is halfway along the track.
                Rect2 r = _tab.UiScale.GetGlobalRect();
                ClickAt(new Vector2(r.Position.X + r.Size.X * 0.5f, r.GetCenter().Y));
                return null;
            });
            Do("1.5 is kept, but a 1280x720 window stays at 1 and says so", () =>
                Mathf.IsEqualApprox(GameSettingsStore.Current.UiScale, 1.5f) && Mathf.IsEqualApprox(_tree.Root.ContentScaleFactor, 1f)
                && _tab.ScaleNote.Text == "1.5× set · 1× fits this window"
                    ? null : $"setting {GameSettingsStore.Current.UiScale}, window {_tree.Root.ContentScaleFactor}, note '{_tab.ScaleNote.Text}'");
            Do("make the window 1920x1080", () => { _tree.Root.Size = new Vector2I(1920, 1080); return null; });
            Do("the resize lets 1.5 through", () =>
                Mathf.IsEqualApprox(_tree.Root.ContentScaleFactor, 1.5f) && _tab.ScaleNote.Text == "1.5×"
                    ? null : $"window {_tree.Root.Size} at {_tree.Root.ContentScaleFactor}, note '{_tab.ScaleNote.Text}'");
            Do("every tab and Close fit, apart, at 1.5", () => TabsFit());
            Do("capture at 1.5", () => { ScenarioCapture.Shot(_tree.Root, "settings-scale"); return null; });
            Do("press Escape to see the world", () => { Tap(Key.Escape); return null; });
            Do("the camera frames as many cells as at scale 1", () =>
                Mathf.Abs(CellsTall() - _cellsTall) < 0.01f ? null : $"{CellsTall():0.00} cells tall at 1.5, {_cellsTall:0.00} at 1");
            Do("capture the world at 1.5", () => { ScenarioCapture.Shot(_tree.Root, "settings-scale-world"); return null; });
            Do("press Tab", () => { Tap(Key.Tab); return null; });
            Do("Settings is showing again", () => _screen.TabContent[ManagementTab.Settings].Visible ? null : "Settings is not showing");
            Do("make the window 1024x576", () => { _tree.Root.Size = new Vector2I(1024, 576); return null; });
            Do("a window smaller than the layout is fitted down", () =>
                Mathf.IsEqualApprox(_tree.Root.ContentScaleFactor, 0.8f) ? null : $"window at {_tree.Root.ContentScaleFactor}");
            Do("every tab fits at 1024x576", () => TabsFit());
            Do("capture at 1024x576", () => { ScenarioCapture.Shot(_tree.Root, "settings-small-window"); return null; });
            Do("back to 1280x720", () => { _tree.Root.Size = new Vector2I(1280, 720); return null; });
            Do("the window is at 1 again", () =>
                Mathf.IsEqualApprox(_tree.Root.ContentScaleFactor, 1f) ? null : $"window at {_tree.Root.ContentScaleFactor}");
            Do("every tab fits at 1", () => TabsFit());
            ScrollIntoView("Reduced motion", () => _tab.ReducedMotion);
            Do("click Reduced motion", () => { Click(_tab.ReducedMotion); return null; });
            ScrollIntoView("Colour-blind marks", () => _tab.ColorBlindMarks);
            Do("click Colour-blind marks", () => { Click(_tab.ColorBlindMarks); return null; });
            Do("both are on", () =>
                GameSettingsStore.Current.ReducedMotion && GameSettingsStore.Current.ColorBlindMarks ? null : "a toggle did not take");
            ScrollIntoView("Fullscreen", () => _tab.Fullscreen);
            Do("click Fullscreen", () => { Click(_tab.Fullscreen); return null; });
            Do("Fullscreen is set", () => GameSettingsStore.Current.Display == DisplayMode.Fullscreen ? null : "Fullscreen did not take");
            Do("click Fullscreen again", () => { Click(_tab.Fullscreen); return null; });
            Do("windowed again", () =>
                GameSettingsStore.Current.Display == DisplayMode.Windowed
                && _tree.Root.Mode is not (Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen)
                    ? null : $"setting {GameSettingsStore.Current.Display}, window {_tree.Root.Mode}");
        }

        private void PlanReload()
        {
            Do("a fresh read of the file matches", () =>
            {
                GameSettings read = GameSettingsStore.ReadFile();
                if (read == null)
                {
                    return $"nothing saved to {GameSettingsStore.ScenarioPath}";
                }
                var shown = GameSettingsStore.Current.ToPairs();
                var saved = read.ToPairs();
                string diff = string.Join(", ", shown.Where(p => !saved.TryGetValue(p.Key, out string v) || v != p.Value).Select(p => p.Key));
                return diff.Length == 0 ? null : $"differs in {diff}";
            });
            ScrollIntoView("UI scale", () => _tab.UiScale);
            Do("a hand-edited file with a bogus and a reserved key reads back safe", () =>
            {
                var config = new ConfigFile();
                config.Load(GameSettingsStore.ScenarioPath);
                config.SetValue(GameSettingsStore.Section, "key.interact", "Bogus,F");
                config.SetValue(GameSettingsStore.Section, "key.golem_carry", "Tab");
                config.Save(GameSettingsStore.ScenarioPath);
                GameSettings read = GameSettingsStore.ReadFile();
                string use = string.Join(",", read.Keys.Get("interact"));
                string carry = string.Join(",", read.Keys.Get("golem_carry"));
                // Carry is left with no key, so every binding goes back to the defaults.
                return use == "E" && carry == "G" ? null : $"use {use}, carry {carry}";
            });
            Do("put the file back", () => { GameSettingsStore.Save(); return null; });
            Do("put the scale back", () =>
            {
                Rect2 r = _tab.UiScale.GetGlobalRect();
                ClickAt(new Vector2(r.Position.X + 2f, r.GetCenter().Y)); // the start: 1
                return null;
            });
            Do("scale 1 again", () =>
                Mathf.IsEqualApprox(GameSettingsStore.Current.UiScale, 1f) && Mathf.IsEqualApprox(_tree.Root.ContentScaleFactor, 1f)
                    ? null : $"setting {GameSettingsStore.Current.UiScale}, window {_tree.Root.ContentScaleFactor}");
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("display", () =>
            {
                _log.Add("UI scale by clicks on the track: 1.5 kept but capped to 1 at 1280x720, let through at 1920x1080, a 1024x576 window fitted to 0.8, tabs fit at each; Fullscreen, Reduced motion and Colour-blind toggled; Reset keys restored E; the saved file reads back the same");
                return null;
            });
        }

        // --- Helpers ------------------------------------------------------------------------

        private int Pack(string item) => _world.Buffers.GetQuantity(_world.StockpileBufferId, item);

        /// <summary>How many cells tall the camera's view is (the camera fits a size in cells).</summary>
        private float CellsTall()
        {
            Camera2D camera = _tree.Root.GetCamera2D();
            return camera == null ? 0f : _tree.Root.GetVisibleRect().Size.Y / (camera.Zoom.Y * GridConversions.CellPixels);
        }

        private static string Keys(string action) => GameSettingsStore.Current.Keys.Get(action).FirstOrDefault() ?? "";

        /// <summary>The InputMap's first key for an action: what the game actually responds to.</summary>
        private static string MapKeys(string action)
        {
            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                if (e is InputEventKey k)
                {
                    return k.PhysicalKeycode.ToString();
                }
            }
            return "";
        }

        private void ClickKey(string action) => Click(_tab.KeyButtons[action]);

        /// <summary>Every tab button and Close lie wholly on screen, none overlapping another.</summary>
        private string TabsFit()
        {
            Rect2 screen = _tree.Root.GetVisibleRect();
            var buttons = _screen.TabButtons.Values.Cast<Control>()
                .Append(_screen.FindChild("CloseButton", true, false) as Control).ToList();
            if (buttons.Contains(null))
            {
                return "no Close button";
            }
            foreach (Control b in buttons)
            {
                Rect2 r = b.GetGlobalRect();
                if (!screen.Encloses(r))
                {
                    return $"{b.Name} at {r} runs off the {screen.Size} screen";
                }
                foreach (Control other in buttons.Where(o => o != b))
                {
                    if (r.Intersects(other.GetGlobalRect()))
                    {
                        return $"{b.Name} overlaps {other.Name}";
                    }
                }
            }
            return null;
        }

        /// <summary>Wholly inside, give or take the pixel a fractional UI scale rounds away.</summary>
        private static bool InView(Rect2 r, Rect2 view) =>
            r.Position.Y >= view.Position.Y - 1.5f && r.End.Y <= view.End.Y + 1.5f;

        /// <summary>
        /// Turns the mouse wheel over the control's scroll column until the control is wholly in
        /// view, one notch a step, as a player would; fails if it never gets there.
        /// </summary>
        private void ScrollIntoView(string name, Func<Control> control)
        {
            for (int notch = 0; notch < 12; notch++)
            {
                Do($"scroll toward {name}", () =>
                {
                    Control c = control();
                    Rect2 view = ((Control)c.GetParent().GetParent()).GetGlobalRect();
                    Rect2 r = c.GetGlobalRect();
                    if (InView(r, view))
                    {
                        return null;
                    }
                    MouseButton wheel = r.End.Y > view.End.Y ? MouseButton.WheelDown : MouseButton.WheelUp;
                    Vector2 at = view.GetCenter() * _tree.Root.ContentScaleFactor;
                    _tree.Root.WarpMouse(at);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel, Pressed = true, Factor = 1f, Position = at, GlobalPosition = at });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel, Pressed = false, Factor = 1f, Position = at, GlobalPosition = at });
                    return null;
                });
            }
            Do($"{name} is in view", () =>
            {
                Control c = control();
                Rect2 view = ((Control)c.GetParent().GetParent()).GetGlobalRect();
                Rect2 r = c.GetGlobalRect();
                return InView(r, view) ? null : $"{name} at {r} outside {view}";
            });
        }

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Click(Control control) => ClickAt(control.GetGlobalRect().GetCenter());

        private void ClickAt(Vector2 at)
        {
            // Controls report canvas positions; the viewport's input arrives in window pixels.
            Vector2 window = at * _tree.Root.ContentScaleFactor;
            _tree.Root.WarpMouse(window);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = window, GlobalPosition = window });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window, GlobalPosition = window });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = window, GlobalPosition = window });
        }

        private void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }
    }
}
