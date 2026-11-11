using System;
using System.Collections.Generic;
using Godot;
using GolemFactory.Menus;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The title screen (docs/menus-design.md §6): Continue, Load, New game, Creative, Settings
    /// and Quit over a live workshop the player watches but cannot touch.
    ///
    /// <para>
    /// The workshop is this scene's own world, built by <see cref="TitleShowcase"/> (the guide's
    /// first chapters, performed by the playtest kit) and kept running by it. The title is a full
    /// screen, so the player stays still and the HUD, build menu, guide and prompts hide, as they
    /// do under any screen; every key and click that reaches the world is swallowed here. Starting
    /// a game reloads the scene (<see cref="GameLaunch"/>), so the showcase never mixes with play:
    /// New game gets a fresh world, Continue a fresh world with the newest save loaded into it,
    /// Load the same with the slot the player picks (§7). A save that can't be read brings the
    /// player back here, told so.
    /// </para>
    ///
    /// <para>
    /// The menu's rules -- what is greyed, where the keyboard starts and walks, what Continue says
    /// -- are Core's <see cref="TitleMenu"/>. Settings is the same tab Management shows.
    /// </para>
    /// </summary>
    public partial class TitleScreen : CanvasLayer, IClosableScreen
    {
        /// <summary>
        /// Above everything, the build menu's card (45) included, on a title scene only: in a game
        /// the node draws nothing and keeps the default layer, so the card stays on top there.
        /// </summary>
        public const int LayerIndex = 50;

        private const float MenuLeft = 80f;
        private const float MenuWidth = 340f;
        private const float EntryHeight = 48f;
        private const float EntryGap = 8f;
        private const float MenuTop = 252f;

        private static readonly Color DisabledTint = new Color(0.20f, 0.18f, 0.16f, 1f);
        private static readonly Color Shade = new Color(0.045f, 0.036f, 0.030f, 1f);

        private WorldNode _world;
        private TitleMenu _menu;
        private Control _root;
        private Control _main;
        private Control _settingsView;
        private Control _loadView;
        private Label _notice;
        private readonly Dictionary<TitleChoice, Button> _entries = new Dictionary<TitleChoice, Button>();
        private bool _shown;
        private bool _leaving;

        // --- For scenarios ------------------------------------------------------------------
        public TitleMenu Menu => _menu;
        public IReadOnlyDictionary<TitleChoice, Button> Entries => _entries;
        public Control RootControl => _root;
        public TitleShowcase Showcase { get; private set; }
        public SettingsTab Settings { get; private set; }
        public Button BackButton { get; private set; }
        public bool SettingsOpen => _settingsView != null && _settingsView.Visible;

        /// <summary>The Load view: the slots, load only (docs/menus-design.md §7).</summary>
        public SaveSlotBoard SlotBoard { get; private set; }
        public SaveSlotList LoadSlots { get; private set; }
        public Button LoadBackButton { get; private set; }
        public bool LoadOpen => _loadView != null && _loadView.Visible;

        /// <summary>Whether the Load view lists Creative's slots rather than the campaign's (docs/menus-design.md §9).</summary>
        public bool LoadingCreative { get; private set; }

        /// <summary>The Load view's two kinds: Campaign and Creative, Left and Right between them.</summary>
        public Button CampaignTab { get; private set; }
        public Button CreativeTab { get; private set; }
        public string NoticeText => _notice != null && _notice.Visible ? _notice.Text : "";

        /// <summary>What a Continue's load reported, on the scene it started.</summary>
        public string LoadStatus { get; private set; }

        /// <summary>What Quit does. A scenario replaces it, to see it chosen without the engine quitting.</summary>
        public Action QuitAction { get; set; }

        /// <summary>The title is up for the whole of a title scene, until a choice reloads it.</summary>
        public bool IsOpen => _shown;

        /// <summary>The scene's title node, by its place (a label elsewhere is also called "Title").</summary>
        public static TitleScreen Find(Node from) => from.GetTree().CurrentScene?.GetNodeOrNull<TitleScreen>("Title");

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            QuitAction ??= () => GetTree().Quit();
            switch (_world?.Launch)
            {
                case LaunchKind.Title:
                    Layer = LayerIndex;
                    _menu = new TitleMenu(SavePaths.AllSaves(), DateTime.Now);
                    Build();
                    _shown = true;
                    _world.Sandbox.Screens.Register(this);
                    // Deferred: every node must be ready to draw what staging builds.
                    Callable.From(Stage).CallDeferred();
                    break;
                case LaunchKind.Continue:
                    Callable.From(LoadSave).CallDeferred();
                    SetProcess(false);
                    break;
                default:
                    SetProcess(false);
                    break;
            }
        }

        /// <summary>A screen opening elsewhere can't close the title: only a choice leaves it.</summary>
        public void Close()
        {
        }

        // --- The showcase ---------------------------------------------------------------------

        private void Stage()
        {
            // Under the floor, dark as the menu's shade: the room's edges fade into it rather
            // than into the engine's grey.
            GetTree().CurrentScene.AddChild(new ColorRect
            {
                Name = "TitleBackdrop",
                Color = Shade,
                Position = new Vector2(-200f, -200f) * GridConversions.CellPixels,
                Size = new Vector2(400f, 400f) * GridConversions.CellPixels,
                ZIndex = -20,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            Showcase = TitleShowcase.Stage(_world.Sandbox);
            _world.Sandbox.Camera.BeginShowcase(Showcase.Centre);
            FrameShowcase();
        }

        /// <summary>The workshop's middle sits in the middle of the room right of the menu.</summary>
        private void FrameShowcase()
        {
            if (GetViewport().GetCamera2D() is CameraRigNode rig)
            {
                rig.ShowcaseScreenOffset = (MenuLeft + MenuWidth) / 2f;
            }
        }

        public override void _Process(double delta)
        {
            Showcase?.Keep((float)delta);
            if (SettingsOpen)
            {
                Settings.Refresh(ManagementTab.Settings);
            }
            PadProcess(delta);
        }

        // --- Continue's load, on the scene it started -----------------------------------------

        private void LoadSave()
        {
            string path = GameLaunch.SavePath;
            string problem = null;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                problem = "That save is no longer there.";
            }
            else
            {
                try
                {
                    LoadStatus = _world.Sandbox.LoadFrom(path);
                }
                catch (Exception e)
                {
                    GD.PushWarning($"Continue: {path} could not be read: {e.Message}");
                    problem = "That save couldn't be read, so it was left as it is.";
                }
            }
            if (problem != null)
            {
                GameLaunch.Request(LaunchKind.Title, notice: problem);
                GetTree().ReloadCurrentScene();
            }
        }

        // --- Choices --------------------------------------------------------------------------

        /// <summary>Does what <paramref name="choice"/> says, as a click or Enter on it does.</summary>
        public void Pick(TitleChoice choice)
        {
            if (_leaving || !_menu.IsEnabled(choice))
            {
                return;
            }
            _menu.Select(choice);
            ShowSelection();
            switch (choice)
            {
                case TitleChoice.Continue:
                    Leave(LaunchKind.Continue, _menu.Newest?.Path, _menu.Newest?.Creative == true);
                    break;
                case TitleChoice.Load:
                    OpenLoad();
                    break;
                case TitleChoice.NewGame:
                    Leave(LaunchKind.NewGame, null);
                    break;
                case TitleChoice.Creative:
                    Leave(LaunchKind.NewGame, null, creative: true);
                    break;
                case TitleChoice.Settings:
                    OpenSettings();
                    break;
                case TitleChoice.Quit:
                    QuitAction();
                    break;
            }
        }

        private void Leave(LaunchKind kind, string path, bool creative = false)
        {
            _leaving = true;
            GameLaunch.Request(kind, path, creative: creative);
            GetTree().ReloadCurrentScene();
        }

        public void OpenSettings()
        {
            _main.Visible = false;
            _settingsView.Visible = true;
        }

        public void CloseSettings()
        {
            Settings.CancelCapture();
            _settingsView.Visible = false;
            _main.Visible = true;
        }

        /// <summary>Opens Load on the kind of the newest save that loads: the one the player most likely wants.</summary>
        public void OpenLoad()
        {
            ShowKind(_menu.Newest?.Creative == true);
            _main.Visible = false;
            _loadView.Visible = true;
        }

        /// <summary>Lists the campaign's slots or Creative's: the two are never mixed (decision 2).</summary>
        public void ShowKind(bool creative)
        {
            LoadingCreative = creative;
            SlotBoard.Show(SavePaths.Saves(creative), DateTime.Now);
            LoadSlots.Refresh();
            foreach ((Button tab, bool lit) in new[] { (CampaignTab, !creative), (CreativeTab, creative) })
            {
                tab.SelfModulate = lit ? ManagementScreen.SelectedTab : new Color(0.34f, 0.30f, 0.26f, 1f);
                tab.GetNode<Label>("Label").AddThemeColorOverride("font_color", lit ? ManagementScreen.SelectedTabLabel : ManagementScreen.ItemNameInk);
            }
        }

        public void CloseLoad()
        {
            _loadView.Visible = false;
            _main.Visible = true;
        }

        /// <summary>Loads a slot, as a click on its Load or Enter on its row does: a fresh game with it loaded.</summary>
        public void PickSlot(string key)
        {
            if (_leaving || key == null || SlotBoard.Press(key, SlotAction.Load) != SlotPress.Go)
            {
                return;
            }
            SlotBoard.Select(key);
            LoadSlots.Refresh();
            Leave(LaunchKind.Continue, SlotBoard.Row(key).Save?.Path, LoadingCreative);
        }

        // --- Input ----------------------------------------------------------------------------

        /// <summary>A key the Settings tab is listening for goes to it first, as in Management.</summary>
        public override void _Input(InputEvent e)
        {
            if (_shown && SettingsOpen && Settings.TryCapture(e))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (_shown && PadFocus.IsPadEvent(e))
            {
                // The pad's, read by polling (PadProcess): its stick would otherwise step the menu
                // once per motion event, and the d-pad would also zoom the showcase's camera.
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (!_shown)
            {
                return;
            }
            // Nothing reaches the world behind the title: no Tab, no build keys, no wheel. Marked
            // first, because a choice below reloads the scene and takes this node out of it.
            if (e is InputEventKey or InputEventMouseButton)
            {
                GetViewport().SetInputAsHandled();
            }
            if (SettingsOpen)
            {
                if (e.IsActionPressed(BuildCursorNode.CancelAction))
                {
                    CloseSettings();
                }
            }
            else if (LoadOpen)
            {
                if (e.IsActionPressed(BuildCursorNode.CancelAction))
                {
                    CloseLoad();
                }
                else if (e.IsActionPressed("ui_left") || e.IsActionPressed(PlayerNode.MoveLeft) ||
                         e.IsActionPressed("ui_right") || e.IsActionPressed(PlayerNode.MoveRight))
                {
                    ShowKind(!LoadingCreative);
                }
                else if (e.IsActionPressed("ui_up") || e.IsActionPressed(PlayerNode.MoveUp))
                {
                    SlotBoard.Move(-1);
                    LoadSlots.Refresh();
                }
                else if (e.IsActionPressed("ui_down") || e.IsActionPressed(PlayerNode.MoveDown))
                {
                    SlotBoard.Move(1);
                    LoadSlots.Refresh();
                }
                else if (e.IsActionPressed("ui_accept") || e.IsActionPressed(PlayerNode.Interact))
                {
                    PickSlot(SlotBoard.Selected);
                }
            }
            else if (e.IsActionPressed("ui_up") || e.IsActionPressed(PlayerNode.MoveUp))
            {
                _menu.Move(-1);
                ShowSelection();
            }
            else if (e.IsActionPressed("ui_down") || e.IsActionPressed(PlayerNode.MoveDown))
            {
                _menu.Move(1);
                ShowSelection();
            }
            else if (e.IsActionPressed("ui_accept") || e.IsActionPressed(PlayerNode.Interact))
            {
                Pick(_menu.Selected);
            }
        }

        // --- Construction ---------------------------------------------------------------------

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "TitleScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);

            _main = Ugui.Fill(new Control { Name = "Main", MouseFilter = Control.MouseFilterEnum.Pass });
            _root.AddChild(_main);
            _main.AddChild(Ugui.Fill(new TextureRect
            {
                Name = "Shade",
                Texture = ShadeTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }));

            Label name = Ugui.Text("GameName", TitleMenu.GameName, 52, ManagementScreen.HeaderInk, bold: true);
            Place(name, MenuLeft, 132f, 640f, 64f);
            _main.AddChild(name);
            Label subtitle = Ugui.Text("Subtitle", TitleMenu.Subtitle, 20, ManagementScreen.ItemNameInk);
            Place(subtitle, MenuLeft + 2f, 196f, 640f, 28f);
            _main.AddChild(subtitle);
            ColorRect rule = Ugui.Rect("Rule", new Color(ManagementScreen.HeaderInk, 0.7f));
            Place(rule, MenuLeft + 2f, 236f, 240f, 2f);
            _main.AddChild(rule);

            var column = new VBoxContainer { Name = "Entries", MouseFilter = Control.MouseFilterEnum.Pass };
            column.AddThemeConstantOverride("separation", (int)EntryGap);
            Place(column, MenuLeft, MenuTop, MenuWidth, TitleMenu.Order.Count * (EntryHeight + EntryGap));
            _main.AddChild(column);
            foreach (TitleChoice choice in TitleMenu.Order)
            {
                column.AddChild(Entry(choice));
            }

            _notice = Ugui.Text("Notice", GameLaunch.Notice ?? "", 15, ManagementScreen.HeaderInk);
            _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _notice.ClipText = false;
            _notice.VerticalAlignment = VerticalAlignment.Top;
            _notice.Visible = !string.IsNullOrEmpty(GameLaunch.Notice);
            Place(_notice, MenuLeft, MenuTop + TitleMenu.Order.Count * (EntryHeight + EntryGap) + 8f, MenuWidth + 80f, 48f);
            _main.AddChild(_notice);

            Label hint = _mainHint = Ugui.Text("Hint", MainHint, 13, ManagementScreen.DimInk);
            hint.AnchorTop = 1f;
            hint.AnchorBottom = 1f;
            hint.OffsetLeft = MenuLeft;
            hint.OffsetRight = MenuLeft + 480f;
            hint.OffsetTop = -56f;
            hint.OffsetBottom = -34f;
            _main.AddChild(hint);

            BuildSettings();
            BuildLoad();
            ShowSelection();
        }

        /// <summary>Positions <paramref name="control"/> from the top left, in canvas pixels.</summary>
        private static void Place(Control control, float x, float y, float width, float height)
        {
            control.AnchorLeft = control.AnchorRight = control.AnchorTop = control.AnchorBottom = 0f;
            control.OffsetLeft = x;
            control.OffsetTop = y;
            control.OffsetRight = x + width;
            control.OffsetBottom = y + height;
        }

        private Button Entry(TitleChoice choice)
        {
            var button = new Button
            {
                Name = choice + "Entry",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(MenuWidth, EntryHeight),
                Disabled = !_menu.IsEnabled(choice),
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            StyleBoxTexture plate = Ugui.NineSlice(ManagementScreen.Ui + "Hud/hud_button.png", 8);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            {
                button.AddThemeStyleboxOverride(state, plate);
            }
            Label label = Ugui.Text("Label", TitleMenu.Label(choice), 20, ManagementScreen.SelectedTabLabel, bold: true);
            Ugui.Fill(label);
            label.OffsetLeft = 22f;
            button.AddChild(label);
            string detail = _menu.Detail(choice);
            if (detail.Length > 0)
            {
                Label line = Ugui.Text("Detail", detail, 13, ManagementScreen.DimInk, align: 4);
                Ugui.Fill(line);
                line.OffsetRight = -18f;
                button.AddChild(line);
            }
            button.MouseEntered += () =>
            {
                if (!_leaving && _menu.Select(choice))
                {
                    ShowSelection();
                }
            };
            button.Pressed += () => Pick(choice);
            _entries[choice] = button;
            return button;
        }

        /// <summary>Lights the selected entry amber, as Management lights its tab; a greyed one stays dark.</summary>
        private void ShowSelection()
        {
            foreach (KeyValuePair<TitleChoice, Button> entry in _entries)
            {
                bool enabled = _menu.IsEnabled(entry.Key);
                bool lit = enabled && entry.Key == _menu.Selected;
                entry.Value.SelfModulate = !enabled ? DisabledTint : lit ? ManagementScreen.SelectedTab : new Color(0.34f, 0.30f, 0.26f, 1f);
                entry.Value.GetNode<Label>("Label").AddThemeColorOverride("font_color",
                    !enabled ? ManagementScreen.DimInk : lit ? ManagementScreen.SelectedTabLabel : ManagementScreen.ItemNameInk);
                if (entry.Value.GetNodeOrNull<Label>("Detail") is Label detail)
                {
                    detail.AddThemeColorOverride("font_color", lit ? new Color(0.22f, 0.16f, 0.10f, 1f) : ManagementScreen.DimInk);
                }
            }
        }

        /// <summary>The Settings tab on Management's plate, with Back where Management has Close.</summary>
        private void BuildSettings()
        {
            _settingsView = Ugui.Fill(new Control { Name = "SettingsView", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false });
            _root.AddChild(_settingsView);
            _settingsView.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.985f))));
            _settingsView.AddChild(Ugui.Fill(Ugui.Image("Background", Ugui.NineSlice(ManagementScreen.Ui + "Hud/hud_panel_plain.png", 8), new Color(0.13f, 0.11f, 0.09f))));
            Label heading = Ugui.Place(Ugui.Text("Heading", "Settings", 22, ManagementScreen.HeaderInk, bold: true), 0f, 0.845f, 0.6f, 0.95f);
            heading.OffsetLeft = 24f;
            _settingsView.AddChild(heading);

            var holder = Ugui.Place(new Control { Name = "SettingsTab", MouseFilter = Control.MouseFilterEnum.Pass }, 0f, 0f, 1f, 0.845f);
            _settingsView.AddChild(holder);
            Settings = new SettingsTab(AudioNode.Find(this));
            holder.AddChild(Settings.Build());

            BackButton = Ugui.Place(ManagementScreen.PlateButton("BackButton", ManagementScreen.Ui + "Hud/hud_button.png",
                    new Color(0.34f, 0.30f, 0.26f, 1f), "Back", 14, ManagementScreen.ItemNameInk, bold: true),
                1f, 1f, 1f, 1f, -6f, -46f, 90f, 30f, 1f, 1f);
            BackButton.Pressed += CloseSettings;
            _settingsView.AddChild(BackButton);
        }

        /// <summary>
        /// The Load view on Management's plate: the six slots, each with Load, and Back. The
        /// keyboard walks the slots that load, starting on the newest; the pointer lights one.
        /// </summary>
        private void BuildLoad()
        {
            _loadView = Ugui.Fill(new Control { Name = "LoadView", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false });
            _root.AddChild(_loadView);
            _loadView.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.985f))));
            _loadView.AddChild(Ugui.Fill(Ugui.Image("Background", Ugui.NineSlice(ManagementScreen.Ui + "Hud/hud_panel_plain.png", 8), new Color(0.13f, 0.11f, 0.09f))));
            Label heading = Ugui.Text("Heading", "Load", 22, ManagementScreen.HeaderInk, bold: true);
            Place(heading, LoadLeft, 30f, 400f, 32f);
            _loadView.AddChild(heading);
            CampaignTab = KindTab("CampaignTab", "Campaign", LoadLeft + LoadWidth - 2f * 120f - 8f, false);
            CreativeTab = KindTab("CreativeTab", "Creative", LoadLeft + LoadWidth - 120f, true);

            SlotBoard = new SaveSlotBoard(SlotBoardMode.Title, null, DateTime.Now);
            LoadSlots = new SaveSlotList(SlotBoard, (key, _) => PickSlot(key))
            {
                Hovered = key =>
                {
                    if (!_leaving && SlotBoard.Select(key))
                    {
                        LoadSlots.Refresh();
                    }
                },
            };
            Control list = LoadSlots.Build(LoadWidth, new[] { SlotAction.Load });
            Place(list, LoadLeft, 78f, LoadWidth, 6f * SaveSlotList.RowHeight + 5f * SaveSlotList.RowGap);
            _loadView.AddChild(list);

            Label hint = _loadHint = Ugui.Text("Hint", LoadHint, 13, ManagementScreen.DimInk);
            Place(hint, LoadLeft, 78f + 6f * SaveSlotList.RowHeight + 5f * SaveSlotList.RowGap + 18f, LoadWidth, 22f);
            _loadView.AddChild(hint);

            LoadBackButton = Ugui.Place(ManagementScreen.PlateButton("BackButton", ManagementScreen.Ui + "Hud/hud_button.png",
                    new Color(0.34f, 0.30f, 0.26f, 1f), "Back", 14, ManagementScreen.ItemNameInk, bold: true),
                1f, 1f, 1f, 1f, -6f, -46f, 90f, 30f, 1f, 1f);
            LoadBackButton.Pressed += CloseLoad;
            _loadView.AddChild(LoadBackButton);
        }

        private Button KindTab(string name, string label, float x, bool creative)
        {
            Button tab = ManagementScreen.PlateButton(name, ManagementScreen.Ui + "Hud/hud_button.png",
                new Color(0.34f, 0.30f, 0.26f, 1f), label, 14, ManagementScreen.ItemNameInk, bold: true);
            tab.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            Place(tab, x, 30f, 120f, 32f);
            tab.Pressed += () => ShowKind(creative);
            _loadView.AddChild(tab);
            return tab;
        }

        private const float LoadWidth = 760f;
        private const float LoadLeft = (1280f - LoadWidth) / 2f;

        /// <summary>Dark behind the menu, clear over the workshop: the words read, the factory shows.</summary>
        private static GradientTexture2D ShadeTexture()
        {
            var gradient = new Gradient();
            gradient.SetColor(0, new Color(Shade, 0.90f));
            gradient.SetColor(1, new Color(Shade, 0f));
            gradient.AddPoint(0.30f, new Color(Shade, 0.78f));
            gradient.AddPoint(0.42f, new Color(Shade, 0.35f));
            return new GradientTexture2D { Gradient = gradient, Width = 256, Height = 4, FillFrom = Vector2.Zero, FillTo = new Vector2(0.62f, 0f) };
        }
    }
}
