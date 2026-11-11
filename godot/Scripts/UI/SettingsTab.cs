using Godot;
using System;
using System.Collections.Generic;
using GolemFactory.Settings;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Management screen's Settings tab: Sound (docs/audio-design.md §5), Display and Keys
    /// (docs/menus-design.md step 1). Sound is AudioNode's; the rest is <see cref="GameSettings"/>,
    /// applied at once and kept by <see cref="GameSettingsStore"/> in <c>user://settings.cfg</c>,
    /// never the save.
    ///
    /// <para>
    /// A key button starts a <see cref="KeyCapture"/>. The next key press reaches it through
    /// <see cref="ManagementScreen"/>'s <c>_Input</c> (<see cref="TryCapture"/>), which runs before
    /// the GUI and before the screen's own close keys, so E or Escape go to the capture, not to
    /// closing the screen. Leaving the tab or closing the screen cancels it.
    /// </para>
    /// </summary>
    public sealed class SettingsTab : IManagementTab
    {
        private readonly AudioNode _audio;
        private readonly Dictionary<string, Button> _keyButtons = new Dictionary<string, Button>();
        private KeyCapture _capture;

        public HSlider Master { get; private set; }
        public HSlider Music { get; private set; }
        public HSlider Effects { get; private set; }
        public CheckBox Mute { get; private set; }
        public CheckBox Fullscreen { get; private set; }
        public HSlider UiScale { get; private set; }
        public CheckBox ReducedMotion { get; private set; }
        public CheckBox ColorBlindMarks { get; private set; }
        public Button ResetKeys { get; private set; }

        /// <summary>Under the UI scale slider: the setting, and the scale the window allows if lower.</summary>
        public Label ScaleNote { get; private set; }

        /// <summary>One line under the keys: what the last press did, or what to do now.</summary>
        public Label KeyStatus { get; private set; }

        /// <summary>The key buttons by action (<see cref="KeyBindings.Actions"/>).</summary>
        public IReadOnlyDictionary<string, Button> KeyButtons => _keyButtons;

        public bool IsCapturing => _capture?.Listening != null;

        public SettingsTab(AudioNode audio) => _audio = audio;

        private static GameSettings Settings => GameSettingsStore.Current;

        private const string IdleText = "Click a key to change it.";

        /// <summary>The keys' line at rest: with the pad in use it says the keys are the keyboard's (docs/steam-deck-design.md D7).</summary>
        private static string Idle => GolemFactory.Settings.TextTokens.Fill(IdleText);

        public Control Build()
        {
            GameSettingsStore.Load();
            _capture = new KeyCapture(Settings.Keys);

            var root = Ugui.Fill(new Control { Name = "Settings", MouseFilter = Control.MouseFilterEnum.Pass });
            // Two columns, so every setting shows at once at the default scale: Sound and Display on
            // the left, Keys on the right. Each scrolls only when a large UI scale needs it.
            Control column = Column(root, "Left", 0.06f, 0.48f);
            Control keys = Column(root, "Right", 0.54f, 0.94f);

            column.AddChild(Ugui.Text("Heading", "Sound", 18, ManagementScreen.HeaderInk, bold: true));
            Master = Slider(column, "Master", _audio?.MasterVolume ?? 1f, 0, 1, 0.05, _ => ApplyAudio(), live: true);
            Music = Slider(column, "Music", _audio?.MusicVolume ?? 1f, 0, 1, 0.05, _ => ApplyAudio(), live: true);
            Effects = Slider(column, "Effects", _audio?.EffectsVolume ?? 1f, 0, 1, 0.05, _ => ApplyAudio(), live: true);
            Mute = Check(column, "Mute", "Mute all sound", _audio?.Muted ?? false, _ => ApplyAudio());

            column.AddChild(Ugui.Text("DisplayHeading", "Display", 18, ManagementScreen.HeaderInk, bold: true));
            Fullscreen = Check(column, "Fullscreen", "Fullscreen", Settings.Display == DisplayMode.Fullscreen, on =>
            {
                Settings.Display = on ? DisplayMode.Fullscreen : DisplayMode.Windowed;
                Commit();
            });
            // Not live: the screen rescaling under the pointer mid-drag would move the handle away
            // from it. Applied on release, or at once on a click or a key step.
            UiScale = Slider(column, "UI scale", Settings.UiScale, GameSettings.MinUiScale, GameSettings.MaxUiScale, 0.25, v =>
            {
                Settings.SetUiScale((float)v);
                Commit();
                ShowScale();
            }, live: false);
            ScaleNote = Ugui.Text("ScaleNote", "", 13, ManagementScreen.DimInk);
            ScaleNote.CustomMinimumSize = new Vector2(0f, 18f);
            column.AddChild(ScaleNote);
            ShowScale();
            ReducedMotion = Check(column, "ReducedMotion", "Reduced motion", Settings.ReducedMotion, on => { Settings.ReducedMotion = on; Commit(); });
            ColorBlindMarks = Check(column, "ColorBlindMarks", "Colour-blind marks", Settings.ColorBlindMarks, on => { Settings.ColorBlindMarks = on; Commit(); });

            keys.AddChild(Ugui.Text("KeysHeading", "Keys", 18, ManagementScreen.HeaderInk, bold: true));
            foreach (string action in KeyBindings.Actions())
            {
                KeyRow(keys, action);
            }
            KeyStatus = Ugui.Text("KeyStatus", Idle, 14, ManagementScreen.ItemNameInk);
            KeyStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            KeyStatus.CustomMinimumSize = new Vector2(0f, 40f);
            KeyStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            keys.AddChild(KeyStatus);
            ResetKeys = new Button { Name = "ResetKeys", Text = "Reset keys", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            ResetKeys.Pressed += () =>
            {
                _capture.Cancel();
                Settings.Keys = KeyBindings.Defaults();
                Commit();
                ShowAllKeys();
                KeyStatus.Text = "Keys reset to the defaults.";
            };
            keys.AddChild(ResetKeys);
            ShowAllKeys();
            return root;
        }

        private static Control Column(Control root, string name, float left, float right)
        {
            var scroll = Ugui.Place(new ScrollContainer { Name = name + "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, left, 0.2f, right, 0.92f);
            root.AddChild(scroll);
            var column = new VBoxContainer { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(column);
            return column;
        }

        public void Refresh(ManagementTab active)
        {
            if (active != ManagementTab.Settings)
            {
                CancelCapture();
                return;
            }
            ShowScale(); // the window may have been resized
            if (!IsCapturing && KeyStatus.Text != Idle && (KeyStatus.Text == IdleText || KeyStatus.Text == GolemFactory.Settings.PadPhrasing.Apply(IdleText)))
            {
                KeyStatus.Text = Idle; // the device changed
            }
        }

        private void ShowScale()
        {
            Window window = (Engine.GetMainLoop() as SceneTree)?.Root;
            float effective = GameSettingsStore.EffectiveScale(window);
            string text = Mathf.IsEqualApprox(effective, Settings.UiScale)
                ? $"{Settings.UiScale:0.##}×"
                : $"{Settings.UiScale:0.##}× set · {effective:0.##}× fits this window";
            if (ScaleNote.Text != text)
            {
                ScaleNote.Text = text;
            }
        }

        /// <summary>Stops listening for a key, if it was. The screen calls this when it closes.</summary>
        public void CancelCapture()
        {
            if (IsCapturing)
            {
                _capture.Cancel();
                ShowAllKeys();
                KeyStatus.Text = Idle;
            }
        }

        /// <summary>
        /// Gives a key press to the capture while one is listening. True when the press was taken,
        /// so the caller marks it handled and nothing else (closing on E or Escape) sees it.
        /// </summary>
        public bool TryCapture(InputEvent e)
        {
            if (!IsCapturing || e is not InputEventKey key)
            {
                return false;
            }
            if (!key.Pressed || key.Echo)
            {
                return true; // the release of the press that started it, or a held repeat
            }
            Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            string name = code.ToString();
            if (!GameSettingsStore.IsKey(name))
            {
                return true; // a key with no name to bind (some media keys): keep listening
            }
            string action = _capture.Listening;
            string heldBy = Settings.Keys.ActionWith(name);
            RebindResult result = _capture.Press(name);
            switch (result)
            {
                case RebindResult.Reserved:
                    KeyStatus.Text = $"{KeyNames.Display(name)} is kept for the game. Press another key, or Escape.";
                    return true;
                case RebindResult.Cancelled:
                    KeyStatus.Text = Idle;
                    break;
                case RebindResult.Swapped:
                    KeyStatus.Text = $"{Label(action)} is now {KeyNames.Display(name)}; {Label(heldBy)} took its old key.";
                    Commit();
                    break;
                case RebindResult.Bound:
                    KeyStatus.Text = $"{Label(action)} is now {KeyNames.Display(name)}.";
                    Commit();
                    break;
                default:
                    KeyStatus.Text = Idle;
                    break;
            }
            ShowAllKeys();
            return true;
        }

        public static string Label(string action) => action switch
        {
            "move_left" => "Move left",
            "move_right" => "Move right",
            "move_up" => "Move up",
            "move_down" => "Move down",
            "interact" => "Use",
            "golem_carry" => "Carry",
            "inventory" => "Pack",
            "build_rotate" => "Turn",
            _ => action,
        };

        private static void Commit()
        {
            GameSettingsStore.Save();
            GameSettingsStore.Apply((Engine.GetMainLoop() as SceneTree)?.Root);
        }

        private void KeyRow(Control parent, string action)
        {
            var row = new HBoxContainer { Name = action + "Row" };
            row.AddThemeConstantOverride("separation", 12);
            parent.AddChild(row);
            Label name = Ugui.Text("Label", Label(action), 15, ManagementScreen.ItemNameInk);
            name.CustomMinimumSize = new Vector2(120f, 24f);
            row.AddChild(name);
            var button = new Button { Name = action, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180f, 26f) };
            button.Pressed += () =>
            {
                _capture = new KeyCapture(Settings.Keys); // the live bindings, even after a reset replaced them
                _capture.Begin(action);
                ShowAllKeys();
                KeyStatus.Text = $"Press a key for {Label(action)}. Escape cancels.";
            };
            row.AddChild(button);
            _keyButtons[action] = button;
        }

        private void ShowAllKeys()
        {
            foreach (KeyValuePair<string, Button> entry in _keyButtons)
            {
                bool listening = _capture.Listening == entry.Key;
                entry.Value.Text = listening ? "press a key..." : string.Join(" / ", Names(Settings.Keys.Get(entry.Key)));
                // The listening key wears the header's amber, so which row is waiting is plain.
                if (listening)
                {
                    entry.Value.AddThemeColorOverride("font_color", ManagementScreen.HeaderInk);
                    entry.Value.AddThemeColorOverride("font_hover_color", ManagementScreen.HeaderInk);
                }
                else
                {
                    entry.Value.RemoveThemeColorOverride("font_color");
                    entry.Value.RemoveThemeColorOverride("font_hover_color");
                }
            }
        }

        private static IEnumerable<string> Names(IReadOnlyList<string> keys)
        {
            foreach (string k in keys)
            {
                yield return KeyNames.Display(k);
            }
        }

        private static CheckBox Check(Control parent, string name, string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Name = name, Text = text, ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
            box.AddThemeColorOverride("font_color", ManagementScreen.ItemNameInk);
            box.AddThemeFontSizeOverride("font_size", 15);
            box.Toggled += on => set(on);
            parent.AddChild(box);
            return box;
        }

        /// <summary>
        /// A labelled slider. <paramref name="live"/> applies every change; otherwise a drag applies
        /// on release, and a click or a step (no drag) applies at once.
        /// </summary>
        private static HSlider Slider(Control parent, string name, float value, double min, double max, double step,
            Action<double> set, bool live)
        {
            var row = new HBoxContainer { Name = name.Replace(" ", "") + "Row" };
            row.AddThemeConstantOverride("separation", 12);
            parent.AddChild(row);
            Label label = Ugui.Text("Label", name, 15, ManagementScreen.ItemNameInk);
            label.CustomMinimumSize = new Vector2(90f, 24f);
            row.AddChild(label);
            var slider = new HSlider
            {
                Name = name.Replace(" ", ""), MinValue = min, MaxValue = max, Step = step, Value = value,
                CustomMinimumSize = new Vector2(260f, 24f), FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            bool dragging = false;
            slider.DragStarted += () => dragging = true;
            slider.DragEnded += changed =>
            {
                dragging = false;
                if (changed && !live)
                {
                    set(slider.Value);
                }
            };
            slider.ValueChanged += v =>
            {
                if (live || !dragging)
                {
                    set(v);
                }
            };
            row.AddChild(slider);
            return slider;
        }

        private void ApplyAudio() =>
            _audio?.SetVolumes((float)Master.Value, (float)Music.Value, (float)Effects.Value, Mute.ButtonPressed);
    }
}
