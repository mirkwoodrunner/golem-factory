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

        /// <summary>The alert strip: a click on it looks at the next stopped golem.</summary>
        public Control AlertsStrip { get; private set; }

        private Control _banner;
        private Label _bannerText;

        /// <summary>The tower's stage banner's text while it shows; empty otherwise.</summary>
        public string BannerText => _banner.Visible ? _bannerText.Text : "";

        /// <summary>The line under the last stage's banner, while it shows; empty otherwise.</summary>
        public string BannerNext => _banner.Visible && _bannerSub.Visible ? _bannerSub.Text : "";

        private Label _bannerSub;
        public string GaugeText => _gauge.Text;
        public string TickText => _tick.Text;
        public Button PlayPauseButton => _playPause;
        public Button SpeedButton(int i) => _speeds[i];
        public bool Showing => _root.Visible;

        public const string CreativeHeadline = "Creative world";
        public const string CreativeNote = "Every card and building is yours, and nothing costs a thing. Your pack refills itself. No guide, no Clock Tower.";

        private Control _contract;
        private Label _contractText;
        private Control _region;
        private Label _regionText;

        /// <summary>The HUD's region plate as drawn, empty outside the Docks; for scenarios.</summary>
        public string RegionLine => _region != null && _region.Visible ? _regionText.Text : "";

        /// <summary>The HUD's contract line as drawn, empty while none is up; for scenarios.</summary>
        public string ContractLine => _contract != null && _contract.Visible ? _contractText.Text : "";

        /// <summary>The tower panel's lines under the headline, as drawn, for scenarios.</summary>
        public string TowerDetail => _demand?.Text ?? "";

        /// <summary>The tower panel's headline as drawn, for scenarios.</summary>
        public string TowerHeadline => _headline?.Text ?? "";

        public override void _Ready()
        {
            Layer = 2;
            _world = WorldNode.Find(this);
            _root = Ugui.Fill(new Control { Name = "Hud", MouseFilter = Control.MouseFilterEnum.Ignore });
            AddChild(_root);

            // Alerts strip.
            Panel strip = Ugui.Place(Ugui.Image("AlertsStrip", Ugui.NineSlice("res://art/UI/Hud/hud_panel_bolted.png", 12, 10, 12, 10), new Color(0f, 0f, 0f, 0.55f)),
                0.5f, 1f, 0.5f, 1f, 0f, -6f, 340f, 26f, 0.5f, 1f);
            _root.AddChild(strip);
            _alerts = Ugui.Fill(Ugui.Text("Text", "All golems running.", 13, Colors.White, align: 2));
            strip.AddChild(_alerts);
            // A click glides the camera to the next stopped golem (docs/game-feel-design.md §2).
            strip.MouseFilter = Control.MouseFilterEnum.Stop;
            strip.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            strip.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                {
                    _world.Sandbox.FocusNextAlert();
                }
            };
            AlertsStrip = strip;

            // The Clock Tower's stage banner, while its event plays (§3).
            _banner = Ugui.Place(Ugui.Image("TowerBanner", Ugui.NineSlice("res://art/UI/Hud/hud_panel_bolted.png", 12, 10, 12, 10), new Color(0.16f, 0.11f, 0.07f, 0.92f)),
                0.5f, 0.5f, 0.5f, 0.5f, 0f, 200f, 520f, 64f, 0.5f, 0.5f);
            _banner.MouseFilter = Control.MouseFilterEnum.Ignore;
            _bannerText = Ugui.Fill(Ugui.Text("Text", "", 22, Amber, align: 2, bold: true));
            _banner.AddChild(_bannerText);
            // The last stage's banner is the game's end screen: under it, where the game goes on
            // (docs/endless-design.md §3).
            _bannerSub = Ugui.Fill(Ugui.Text("Next", GolemFactory.Contracts.ContractText.EndlessInvitation, 13, ManagementScreen.ItemNameInk, align: 2));
            _bannerSub.OffsetTop = 38f;
            _bannerSub.OffsetBottom = -4f;
            _bannerSub.Visible = false;
            _banner.AddChild(_bannerSub);
            _banner.Visible = false;
            _root.AddChild(_banner);

            // Steam gauge.
            ColorRect gauge = Ugui.Place(Ugui.Rect("SteamGauge", Plate), 0f, 1f, 0f, 1f, 12f, -10f, 400f, 34f, 0f, 1f);
            _root.AddChild(gauge);
            _gauge = Ugui.Place(Ugui.Text("GaugeText", "", 16, Amber), 0f, 0f, 1f, 1f, 0f, 0f, -16f, -8f);
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

            // The contract under it (docs/contracts-design.md §5), shown while one is up. Plain
            // ink: a contract is never an alarm.
            _contract = Ugui.Place(Ugui.Rect("ContractLine", Plate), 1f, 1f, 1f, 1f, -12f, -164f, 320f, 26f, 1f, 1f);
            _root.AddChild(_contract);
            _contractText = Ugui.Place(Ugui.Text("ContractText", "", 13, ManagementScreen.ItemNameInk), 0f, 0f, 1f, 1f, 0f, 0f, -16f, -4f);
            _contractText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _contract.AddChild(_contractText);

            // The region's name while the player stands in the Docks (docs/region-design.md §5),
            // top centre under the clock bar, clear of the alerts strip and the right-hand panels.
            _region = Ugui.Place(Ugui.Rect("RegionPlate", Plate), 0.5f, 1f, 0.5f, 1f, 0f, -80f, 160f, 28f, 0.5f, 1f);
            _root.AddChild(_region);
            _regionText = Ugui.Place(Ugui.Text("RegionText", "", 15, Amber), 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            _regionText.HorizontalAlignment = HorizontalAlignment.Center;
            _region.AddChild(_regionText);
            _region.Visible = false;

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
                // Top centre, under the alerts strip (G10): the bottom edge belongs to the build
                // bar since it went horizontal.
                0.5f, 1f, 0.5f, 1f, 0f, -38f, 560f * BarScale, 52f * BarScale, 0.5f, 1f);
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
            using var perf = NodePerf.Measure("node.hud");
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
            Player.CameraDirector camera = _world.Sandbox.Camera;
            _banner.Visible = camera.TowerPhase != Player.TowerEventPhase.None && camera.TowerRevealed;
            if (_banner.Visible)
            {
                _bannerText.Text = Player.TowerBanner.Text(camera.TowerStageNumber, camera.TowerStageName);
                bool last = camera.TowerStageNumber >= _world.Sandbox.ClockTower.Site.StageCount;
                _bannerSub.Visible = last;
                _bannerText.OffsetBottom = last ? -24f : 0f;
            }

            // The two readouts refresh on Unity's 0.25 s interval rather than every frame.
            _untilRefresh -= (float)delta;
            if (_untilRefresh > 0f)
            {
                return;
            }
            _untilRefresh = 0.25f;

            SteamNetwork steam = _world.Sandbox.Steam;
            SteamGaugeReading reading = SteamGaugeUtility.Compute(steam.TotalCokeStock, steam.LastEvaluatedWorkingCount, steam.TotalPeakCokeStock);
            // Labelled: "0 Coke - 0/min - idle" alone did not say it was the boilers' fuel (G10).
            _gauge.Text = "Boiler fuel  " + SteamGaugeUtility.Format(reading);
            _gauge.AddThemeColorOverride("font_color", reading.IsLow ? Coral : reading.HasCountdown ? Amber : Dim);

            string contract = GolemFactory.Contracts.ContractText.HudLine(_world.Sandbox.Contracts);
            _contractText.Text = contract;
            _contract.Visible = contract.Length > 0;

            string region = GolemFactory.World.Docks.RegionName(_world.Sandbox.Interactor.PlayerCell, _world.Sandbox.Bounds.DocksNorth);
            _regionText.Text = region;
            _region.Visible = region.Length > 0;

            if (_world.Sandbox.Creative)
            {
                // A Creative world has no tower to build toward (docs/menus-design.md §9): the
                // panel says what kind of world this is instead.
                _headline.Text = CreativeHeadline;
                _demand.Text = CreativeNote;
                _starved.Text = "";
                return;
            }
            ClockTowerReading tower = _world.Sandbox.ClockTower.Site.BuildReading();
            // Once the tower stands, the panel follows the civic work under way, as it followed the
            // tower's stage (docs/endless-design.md §3).
            Civic.CivicWorks civic = _world.Sandbox.Civic;
            if (tower.IsComplete && civic != null && civic.IsOpen)
            {
                ClockTowerReading work = civic.Site.BuildReading();
                _headline.Text = Civic.CivicText.Headline(civic, work);
                _headline.AddThemeColorOverride("font_color", Amber);
                _demand.Text = ClockTowerReadout.FormatDemandTable(work);
                _starved.Text = Civic.CivicText.Starved(civic, work);
                return;
            }
            _headline.Text = ClockTowerReadout.FormatHeadline(tower);
            _headline.AddThemeColorOverride("font_color", tower.IsComplete ? Win : tower.HasActiveStage ? Amber : Dim);
            _demand.Text = tower.IsComplete
                ? GolemFactory.Contracts.ContractText.EndlessInvitation
                : ClockTowerReadout.FormatDemandTable(tower);
            _starved.Text = ClockTowerReadout.FormatStarvedAlert(tower);
        }
    }
}
