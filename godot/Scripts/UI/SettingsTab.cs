using Godot;
using System;
using System.Collections.Generic;
using GolemFactory.Settings;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Management screen's Settings tab (docs/audio-design.md §5, decision 4): Master, Music
    /// and Effects sliders and a mute, applied to the buses at once and kept in
    /// <see cref="AudioNode.SettingsPath"/>, not the save.
    /// </summary>
    public sealed class SettingsTab : IManagementTab
    {
        private readonly AudioNode _audio;

        public HSlider Master { get; private set; }
        public HSlider Music { get; private set; }
        public HSlider Effects { get; private set; }
        public CheckBox Mute { get; private set; }

        public SettingsTab(AudioNode audio) => _audio = audio;

        public Control Build()
        {
            var root = Ugui.Fill(new Control { Name = "Settings", MouseFilter = Control.MouseFilterEnum.Pass });
            var scroll = Ugui.Place(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }, 0.1f, 0.2f, 0.7f, 0.92f);
            root.AddChild(scroll);
            var column = new VBoxContainer { Name = "Column", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(column);

            column.AddChild(Ugui.Text("Heading", "Sound", 18, ManagementScreen.HeaderInk, bold: true));
            Master = Slider(column, "Master", _audio?.MasterVolume ?? 1f);
            Music = Slider(column, "Music", _audio?.MusicVolume ?? 1f);
            Effects = Slider(column, "Effects", _audio?.EffectsVolume ?? 1f);

            Mute = new CheckBox { Name = "Mute", Text = "Mute all sound", ButtonPressed = _audio?.Muted ?? false, FocusMode = Control.FocusModeEnum.None };
            Mute.AddThemeColorOverride("font_color", ManagementScreen.ItemNameInk);
            Mute.AddThemeFontSizeOverride("font_size", 15);
            Mute.Toggled += _ => Apply();
            column.AddChild(Mute);

            GameSettings g = GameSettingsStore.Load();
            column.AddChild(Ugui.Text("DisplayHeading", "Display", 18, ManagementScreen.HeaderInk, bold: true));
            Check(column, "Fullscreen", g.Display == DisplayMode.Fullscreen, on =>
            {
                g.Display = on ? DisplayMode.Fullscreen : DisplayMode.Windowed;
                Commit();
            });
            Slider(column, "UI scale", g.UiScale, GameSettings.MinUiScale, GameSettings.MaxUiScale, 0.25, v =>
            {
                g.SetUiScale((float)v);
                Commit();
            });
            Check(column, "Reduced motion", g.ReducedMotion, on => { g.ReducedMotion = on; Commit(); });
            Check(column, "Colour-blind marks on cards", g.ColorBlindMarks, on => { g.ColorBlindMarks = on; Commit(); });

            column.AddChild(Ugui.Text("KeysHeading", "Keys (click, then press a key)", 18, ManagementScreen.HeaderInk, bold: true));
            foreach (string action in KeyBindings.Actions())
            {
                KeyRow(column, action);
            }
            var reset = new Button { Name = "ResetKeys", Text = "Reset keys", FocusMode = Control.FocusModeEnum.None };
            reset.Pressed += () =>
            {
                g.Keys = KeyBindings.Defaults();
                Commit();
                foreach (string a in _keyButtons.Keys)
                {
                    ShowKeys(a);
                }
            };
            column.AddChild(reset);
            return root;
        }

        private readonly Dictionary<string, Button> _keyButtons = new Dictionary<string, Button>();
        private string _listening;

        private static void Commit()
        {
            GameSettingsStore.Save();
            GameSettingsStore.Apply((Engine.GetMainLoop() as SceneTree)?.Root);
        }

        private static string Label(string action) => action switch
        {
            "move_left" => "Move left",
            "move_right" => "Move right",
            "move_up" => "Move up",
            "move_down" => "Move down",
            "interact" => "Use / close",
            "golem_carry" => "Carry",
            "inventory" => "Pack",
            _ => action,
        };

        private void KeyRow(Control parent, string action)
        {
            var row = new HBoxContainer { Name = action + "Row" };
            row.AddThemeConstantOverride("separation", 12);
            parent.AddChild(row);
            Label name = Ugui.Text("Label", Label(action), 15, ManagementScreen.ItemNameInk);
            name.CustomMinimumSize = new Vector2(120f, 24f);
            row.AddChild(name);
            var button = new Button { Name = action, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(160f, 24f) };
            button.Pressed += () =>
            {
                _listening = action;
                button.Text = "press a key…";
                button.SetProcessUnhandledKeyInput(true);
            };
            button.GuiInput += ev =>
            {
                if (_listening == action && ev is InputEventKey { Pressed: true } key)
                {
                    Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                    _listening = null;
                    button.AcceptEvent();
                    if (code != Key.Escape)
                    {
                        GameSettingsStore.Current.Keys.Rebind(action, code.ToString());
                        Commit();
                    }
                    foreach (string a in _keyButtons.Keys)
                    {
                        ShowKeys(a);
                    }
                }
            };
            row.AddChild(button);
            _keyButtons[action] = button;
            ShowKeys(action);
        }

        private void ShowKeys(string action) =>
            _keyButtons[action].Text = string.Join(" / ", GameSettingsStore.Current.Keys.Get(action));

        private static void Check(Control parent, string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Name = text, Text = text, ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
            box.AddThemeColorOverride("font_color", ManagementScreen.ItemNameInk);
            box.AddThemeFontSizeOverride("font_size", 15);
            box.Toggled += on => set(on);
            parent.AddChild(box);
        }

        private static void Slider(Control parent, string name, float value, double min, double max, double step, Action<double> set)
        {
            var row = new HBoxContainer { Name = name + "Row" };
            row.AddThemeConstantOverride("separation", 12);
            parent.AddChild(row);
            Label label = Ugui.Text("Label", name, 15, ManagementScreen.ItemNameInk);
            label.CustomMinimumSize = new Vector2(90f, 24f);
            row.AddChild(label);
            var slider = new HSlider
            {
                Name = name, MinValue = min, MaxValue = max, Step = step, Value = value,
                CustomMinimumSize = new Vector2(260f, 24f), FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            slider.DragEnded += changed => { if (changed) { set(slider.Value); } };
            row.AddChild(slider);
        }

        public void Refresh(ManagementTab active)
        {
        }

        private HSlider Slider(Control parent, string name, float value)
        {
            var row = new HBoxContainer { Name = name + "Row" };
            row.AddThemeConstantOverride("separation", 12);
            parent.AddChild(row);
            Label label = Ugui.Text("Label", name, 15, ManagementScreen.ItemNameInk);
            label.CustomMinimumSize = new Vector2(90f, 24f);
            row.AddChild(label);
            var slider = new HSlider
            {
                Name = name,
                MinValue = 0,
                MaxValue = 1,
                Step = 0.05,
                Value = value,
                CustomMinimumSize = new Vector2(260f, 24f),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                FocusMode = Control.FocusModeEnum.None,
            };
            slider.ValueChanged += _ => Apply();
            row.AddChild(slider);
            return slider;
        }

        private void Apply() =>
            _audio?.SetVolumes((float)Master.Value, (float)Music.Value, (float)Effects.Value, Mute.ButtonPressed);
    }
}
