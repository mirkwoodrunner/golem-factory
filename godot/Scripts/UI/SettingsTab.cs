using Godot;
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
            var column = Ugui.Place(new VBoxContainer { Name = "Column" }, 0.1f, 0.25f, 0.6f, 0.9f);
            column.AddThemeConstantOverride("separation", 14);
            root.AddChild(column);

            column.AddChild(Ugui.Text("Heading", "Sound", 18, ManagementScreen.HeaderInk, bold: true));
            Master = Slider(column, "Master", _audio?.MasterVolume ?? 1f);
            Music = Slider(column, "Music", _audio?.MusicVolume ?? 1f);
            Effects = Slider(column, "Effects", _audio?.EffectsVolume ?? 1f);

            Mute = new CheckBox { Name = "Mute", Text = "Mute all sound", ButtonPressed = _audio?.Muted ?? false, FocusMode = Control.FocusModeEnum.None };
            Mute.AddThemeColorOverride("font_color", ManagementScreen.ItemNameInk);
            Mute.AddThemeFontSizeOverride("font_size", 15);
            Mute.Toggled += _ => Apply();
            column.AddChild(Mute);
            return root;
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
