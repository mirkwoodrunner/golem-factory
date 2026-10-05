using Godot;
using GolemFactory.ClockTower;
using GolemFactory.Simulation;
using GolemFactory.Steam;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Sandbox's world HUD, as WorkbenchCanvas.prefab and SimulationControlBar laid it out
    /// (G8), replacing the spike's debug readout:
    /// <list type="bullet">
    ///   <item>top-left, the steam gauge (Coke left and how long it lasts at the current draw);</item>
    ///   <item>top-centre, the alerts strip ("All golems running." or the worst stall);</item>
    ///   <item>top-right, the Clock Tower panel (stage, demand table, starved alert);</item>
    ///   <item>bottom-centre, the simulation control bar (tick, state, play/pause, speeds).</item>
    /// </list>
    /// All of it hides while a full screen is open, as Unity's hideWhileOpen chrome did. Every
    /// line of text is a Core readout's; this node only places and colours it.
    /// </summary>
    public partial class HudOverlay : CanvasLayer
    {
        private static readonly Color Amber = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color Coral = new Color(1f, 0.52f, 0.40f, 1f);
        private static readonly Color Dim = new Color(0.55f, 0.52f, 0.47f, 1f);
        private static readonly Color Win = new Color(0.62f, 0.86f, 0.55f, 1f);
        private static readonly Color Plate = new Color(0.09f, 0.08f, 0.07f, 0.94f);

        // SimulationControlBar was authored against a 1920x1080 reference; at the 1280x720
        // window everything in it is two thirds of its authored size.
        private const float BarScale = 1280f / 1920f;

        private WorldNode _world;
        private Control _root;
        private Label _alerts;
        private Label _gauge;
        private Label _headline;
        private Label _demand;
        private Label _starved;
        private Label _tick;
        private Label _state;
        private Button _playPause;
        private readonly Button[] _speeds = new Button[ClockReadout.SpeedPresets.Length];
        private float _untilRefresh;

        public string AlertsText => _alerts.Text;
        public string GaugeText => _gauge.Text;
        public string TickText => _tick.Text;
        public Button PlayPauseButton => _playPause;
        public Button SpeedButton(int i) => _speeds[i];
        public bool Showing => _root.Visible;

        public override void _Ready()
        {
            Layer = 2;
            _world = WorldNode.Find(this);
            _root = Ugui.Fill(new Control { Name = "Hud", MouseFilter = Control.MouseFilterEnum.Ignore });
            AddChild(_root);

            // Alerts strip.
            Panel strip = Ugui.Place(Ugui.Image("AlertsStrip", Ugui.NineSlice("res://art/UI/Steampunk/steampunk_panel_iron_bolt.png", 12, 10, 12, 10), new Color(0f, 0f, 0f, 0.55f)),
                0.5f, 1f, 0.5f, 1f, 0f, -6f, 340f, 26f, 0.5f, 1f);
            _root.AddChild(strip);
            _alerts = Ugui.Fill(Ugui.Text("Text", "All golems running.", 13, Colors.White, align: 2));
            strip.AddChild(_alerts);

            // Steam gauge.
            ColorRect gauge = Ugui.Place(Ugui.Rect("SteamGauge", Plate), 0f, 1f, 0f, 1f, 12f, -10f, 320f, 34f, 0f, 1f);
            _root.AddChild(gauge);
            _gauge = Ugui.Place(Ugui.Text("GaugeText", "", 18, Amber), 0f, 0f, 1f, 1f, 0f, 0f, -16f, -8f);
            gauge.AddChild(_gauge);

            // Clock Tower panel.
            ColorRect tower = Ugui.Place(Ugui.Rect("ClockTowerPanel", Plate), 1f, 1f, 1f, 1f, -12f, -10f, 320f, 148f, 1f, 1f);
            _root.AddChild(tower);
            _headline = TopLeft(Ugui.Place(Ugui.Text("Headline", "", 17, Amber), 0f, 0.8f, 1f, 0.98f, 0f, 0f, -16f, 0f));
            _demand = TopLeft(Ugui.Place(Ugui.Text("DemandTable", "", 14, Amber), 0f, 0.26f, 1f, 0.78f, 0f, 0f, -16f, 0f));
            _starved = TopLeft(Ugui.Place(Ugui.Text("Alert", "", 14, Coral), 0f, 0.02f, 1f, 0.24f, 0f, 0f, -16f, 0f));
            tower.AddChild(_headline);
            tower.AddChild(_demand);
            tower.AddChild(_starved);

            BuildControlBar();
        }

        private static Label TopLeft(Label label)
        {
            label.VerticalAlignment = VerticalAlignment.Top;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.ClipText = false;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return label;
        }

        private void BuildControlBar()
        {
            ColorRect bar = Ugui.Place(Ugui.Rect("SimControlBar", new Color(0.16f, 0.11f, 0.07f, 0.96f)),
                0.5f, 0f, 0.5f, 0f, 0f, 16f * BarScale, 560f * BarScale, 52f * BarScale, 0.5f, 0f);
            bar.MouseFilter = Control.MouseFilterEnum.Stop;
            _root.AddChild(bar);
            var row = Ugui.Fill(new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Pass });
            row.AddThemeConstantOverride("separation", (int)(8 * BarScale));
            bar.AddChild(row);

            _tick = BarLabel("TickLabel", 130f);
            _state = BarLabel("StateLabel", 130f);
            row.AddChild(_tick);
            row.AddChild(_state);
            _playPause = BarButton("PlayPause", 96f, "PAUSE");
            _playPause.Pressed += TogglePlayPause;
            row.AddChild(_playPause);
            for (int i = 0; i < _speeds.Length; i++)
            {
                Button speed = BarButton("Speed" + i, 48f, ClockReadout.FormatSpeed(ClockReadout.SpeedPresets[i]));
                int captured = i;
                speed.Pressed += () => _world.Clock.Speed = ClockReadout.SpeedPresets[captured];
                _speeds[i] = speed;
                row.AddChild(speed);
            }
        }

        private static Label BarLabel(string name, float width)
        {
            Label label = Ugui.Text(name, "", (int)(20 * BarScale), new Color(0.93f, 0.86f, 0.72f), align: 2);
            label.CustomMinimumSize = new Vector2(width * BarScale, 32f * BarScale);
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            return label;
        }

        private static Button BarButton(string name, float width, string text)
        {
            var button = new Button
            {
                Name = name,
                CustomMinimumSize = new Vector2(width * BarScale, 34f * BarScale),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                FocusMode = Control.FocusModeEnum.None,
            };
            var face = new StyleBoxFlat { BgColor = Colors.White };
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            {
                button.AddThemeStyleboxOverride(state, face);
            }
            button.SelfModulate = new Color(0.38f, 0.28f, 0.19f);
            button.AddChild(Ugui.Fill(Ugui.Text("Label", text, (int)(18 * BarScale), new Color(0.97f, 0.92f, 0.82f), align: 2, bold: true)));
            return button;
        }

        private void TogglePlayPause()
        {
            if (_world.Clock.State == ClockState.Paused)
            {
                _world.Clock.Play();
            }
            else
            {
                _world.Clock.Pause();
            }
        }

        public override void _Process(double delta)
        {
            _root.Visible = !ModalScreens.AnyOpen(GetTree());
            if (!_root.Visible)
            {
                return;
            }

            SimulationClock clock = _world.Clock;
            _tick.Text = "TICK " + ClockReadout.FormatTick(clock.CurrentTick);
            bool paused = clock.State == ClockState.Paused;
            _state.Text = ClockReadout.Describe(clock.State, clock.TicksPerSecond, clock.Speed);
            _state.AddThemeColorOverride("font_color", paused ? new Color(1f, 0.78f, 0.35f) : new Color(0.82f, 0.86f, 0.78f));
            _playPause.GetNode<Label>("Label").Text = paused ? "PLAY" : "PAUSE";
            int active = ClockReadout.IndexOfSpeed(clock.Speed);
            for (int i = 0; i < _speeds.Length; i++)
            {
                _speeds[i].SelfModulate = i == active ? new Color(0.95f, 0.62f, 0.20f) : new Color(0.38f, 0.28f, 0.19f);
            }

            _alerts.Text = _world.Sandbox.Alerts?.Text ?? "";

            // The two readouts refresh on Unity's 0.25 s interval rather than every frame.
            _untilRefresh -= (float)delta;
            if (_untilRefresh > 0f)
            {
                return;
            }
            _untilRefresh = 0.25f;

            SteamNetwork steam = _world.Sandbox.Steam;
            SteamGaugeReading reading = SteamGaugeUtility.Compute(steam.TotalCokeStock, steam.LastEvaluatedPoweredCount, steam.TotalPeakCokeStock);
            _gauge.Text = SteamGaugeUtility.Format(reading);
            _gauge.AddThemeColorOverride("font_color", reading.IsLow ? Coral : reading.HasCountdown ? Amber : Dim);

            ClockTowerReading tower = _world.Sandbox.ClockTower.Site.BuildReading();
            _headline.Text = ClockTowerReadout.FormatHeadline(tower);
            _headline.AddThemeColorOverride("font_color", tower.IsComplete ? Win : tower.HasActiveStage ? Amber : Dim);
            _demand.Text = ClockTowerReadout.FormatDemandTable(tower);
            _starved.Text = ClockTowerReadout.FormatStarvedAlert(tower);
        }
    }
}
