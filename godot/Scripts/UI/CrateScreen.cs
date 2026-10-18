using System.Collections.Generic;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Crate screen ([I] at a crate, docs/inventory-screens-design.md): the crate and the pack
    /// side by side, a click moving a good across (all that fits; Shift, one), Take all, and the
    /// crate's label picker. Left of centre, so the guide's plate stays readable over it. Every
    /// rule is Core's <see cref="CrateScreenReadout"/>; this node draws and forwards clicks.
    /// </summary>
    public partial class CrateScreen : CanvasLayer, ICrateScreen, IClosableScreen
    {
        private const string Steampunk = ManagementScreen.Ui + "Steampunk/";
        public const string Hint = "Click a good to move what fits · Shift-click moves one";

        private SandboxWorld _world;
        private Control _root;
        private InventoryPanel _cratePanel;
        private InventoryPanel _packPanel;
        private Button _labelButton;
        private Label _labelText;
        private ScrollContainer _picker;
        private GridContainer _pickerGrid;
        private Label _status;
        private Label _summary;
        private readonly List<(string good, Button button)> _options = new List<(string, Button)>();

        public bool IsOpen { get; private set; }
        public ICrate Crate { get; private set; }
        public bool PickerOpen => _picker.Visible;

        // --- For scenarios ------------------------------------------------------------------
        public Control CratePanel => _cratePanel.Root;
        public Control PackPanel => _packPanel.Root;
        public IReadOnlyDictionary<string, Button> CrateRows => _cratePanel.Rows;
        public IReadOnlyDictionary<string, Button> PackRows => _packPanel.Rows;
        public Button LabelButton => _labelButton;
        public string LabelButtonText => _labelText.Text;
        public Button TakeAllButton { get; private set; }
        public IReadOnlyList<(string good, Button button)> PickerOptions => _options;
        public string Status => _status.Text;
        public string CrateTitle => _cratePanel.Title.Text;

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30;
            _world = WorldNode.Find(this).Sandbox;
            Build();
            _root.Visible = false;
            _world.Screens.Register(this);
            _world.ConfigureInventoryScreens(null, this);
        }

        public void Open(ICrate crate)
        {
            if (crate?.Buffer == null)
            {
                return;
            }
            _world.Screens.Opening(this);
            Crate = crate;
            IsOpen = true;
            _status.Text = "";
            ShowPicker(false);
            _cratePanel.Invalidate();
            _packPanel.Invalidate();
            _root.Visible = true;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            Crate = null;
            _root.Visible = false;
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (!IsOpen)
            {
                return;
            }
            if (e.IsActionPressed(BuildCursorNode.CancelAction) && PickerOpen)
            {
                ShowPicker(false);
                GetViewport().SetInputAsHandled();
            }
            else if (e.IsActionPressed(BuildCursorNode.CancelAction) || e.IsActionPressed(PlayerNode.Inventory) || e.IsActionPressed(PlayerNode.Interact))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            if (!IsOpen)
            {
                return;
            }
            // Demolished under the screen: its buffer went with it.
            if (Crate.Buffer == null)
            {
                Close();
                return;
            }
            Refresh();
        }

        private void Refresh()
        {
            CrateScreenReadout readout = _world.Inventory;
            _cratePanel.Title.Text = CrateScreenReadout.Title(Crate);
            _cratePanel.Cap.Text = CrateScreenReadout.CapLine(Crate);
            _labelText.Text = CrateScreenReadout.LabelText(Crate, PickerOpen);
            _labelButton.Disabled = !Crate.CanLabel;
            _labelButton.SelfModulate = Crate.CanLabel ? ManagementScreen.SelectedTab : new Color(0.34f, 0.30f, 0.26f, 1f);
            _labelText.AddThemeColorOverride("font_color", Crate.CanLabel ? ManagementScreen.SelectedTabLabel : ManagementScreen.DimInk);
            _cratePanel.Render(readout.CrateRows(Crate));
            _packPanel.Render(readout.PackRows(Crate));
            _summary.Text = readout.PackSummary;
        }

        // --- Clicks ---------------------------------------------------------------------------

        private void TakeClicked(string good, bool shift) => _status.Text = _world.Inventory.Take(Crate, good, shift);

        private void PutClicked(string good, bool shift) => _status.Text = _world.Inventory.Put(Crate, good, shift);

        private void ShowPicker(bool show)
        {
            _picker.Visible = show;
            _cratePanel.Scroll.Visible = !show;
            if (show)
            {
                BuildOptions();
            }
            if (Crate != null)
            {
                _labelText.Text = CrateScreenReadout.LabelText(Crate, show);
            }
        }

        private void BuildOptions()
        {
            _options.Clear();
            foreach (Node child in _pickerGrid.GetChildren())
            {
                _pickerGrid.RemoveChild(child);
                child.QueueFree();
            }
            foreach (string good in _world.Inventory.LabelOptions(Crate))
            {
                bool current = GolemFactory.Economy.DepotFilterOptions.Same(good, Crate.FilterItemType);
                var option = new Button { Name = "Option", CustomMinimumSize = new Vector2(40f, 40f), FocusMode = Control.FocusModeEnum.None };
                var plate = new StyleBoxFlat { BgColor = current ? ManagementScreen.SelectedTab : ManagementScreen.RowTint };
                foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
                {
                    option.AddThemeStyleboxOverride(state, state == "hover" && !current ? new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.09f) } : plate);
                }
                if (good == null)
                {
                    option.AddChild(Ugui.Fill(Ugui.Text("Any", "Any", 11, current ? ManagementScreen.SelectedTabLabel : ManagementScreen.ItemNameInk, align: 2)));
                }
                else
                {
                    var icon = Ugui.Place(new TextureRect
                    {
                        Name = "Icon",
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    }, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 28f, 28f);
                    icon.Texture = ItemIcons.Load(good);
                    option.AddChild(icon);
                }
                string captured = good;
                option.TooltipText = CrateScreenReadout.OptionName(good);
                option.MouseEntered += () => _status.Text = CrateScreenReadout.OptionName(captured);
                option.Pressed += () =>
                {
                    _status.Text = _world.Inventory.SetLabel(Crate, captured);
                    ShowPicker(false);
                };
                _pickerGrid.AddChild(option);
                _options.Add((good, option));
            }
        }

        // --- Construction ---------------------------------------------------------------------

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "CrateScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.80f))));

            _cratePanel = new InventoryPanel("CratePanel", 96f, Close, TakeClicked);
            _root.AddChild(Ugui.Place(_cratePanel.Root, 0f, 0.5f, 0f, 0.5f, 140f, 0f, 380f, 520f, 0f, 0.5f));

            _labelButton = Ugui.Place(ManagementScreen.PlateButton("LabelButton", Steampunk + "steampunk_button_blank.png", ManagementScreen.SelectedTab, "", 13, ManagementScreen.SelectedTabLabel, bold: false),
                0f, 1f, 1f, 1f, 0f, -60f, -24f, 28f, 0.5f, 1f);
            _labelText = _labelButton.GetNode<Label>("Label");
            _labelButton.Pressed += () => ShowPicker(!PickerOpen);
            _cratePanel.Root.AddChild(_labelButton);

            _picker = Ugui.Place(new ScrollContainer { Name = "LabelPicker", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false },
                0f, 0f, 1f, 1f, 0f, -22f, -16f, -148f, 0.5f, 0.5f);
            // 8 x 40 + 7 x 4 = 348 of the rect's 364: centred, and level with the rows' margin.
            var pickerMargin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            pickerMargin.AddThemeConstantOverride("margin_left", 8);
            pickerMargin.AddThemeConstantOverride("margin_top", 4);
            _picker.AddChild(pickerMargin);
            _pickerGrid = new GridContainer { Name = "Options", Columns = 8 };
            _pickerGrid.AddThemeConstantOverride("h_separation", 4);
            _pickerGrid.AddThemeConstantOverride("v_separation", 4);
            pickerMargin.AddChild(_pickerGrid);
            _cratePanel.Root.AddChild(_picker);

            // The blank plate, not the spec's accept sprite: 9-sliced, the accept's tick stretched
            // into a stroke through "Take all" (seen in the frames).
            TakeAllButton = Ugui.Place(ManagementScreen.PlateButton("TakeAllButton", Steampunk + "steampunk_button_blank.png", new Color(0.80f, 0.62f, 0.32f), "Take all", 13, ManagementScreen.SelectedTabLabel, bold: true),
                0f, 0f, 0f, 0f, 12f, 12f, 110f, 30f, 0f, 0f);
            TakeAllButton.Pressed += () => _status.Text = _world.Inventory.TakeAll(Crate);
            _cratePanel.Root.AddChild(TakeAllButton);

            _status = Ugui.Place(Ugui.Text("Status", "", 12, ManagementScreen.HeaderInk), 0f, 0f, 1f, 0f, 61f, 12f, -146f, 30f, 0.5f, 0f);
            _cratePanel.Root.AddChild(_status);

            _packPanel = new InventoryPanel("PackPanel", 64f, Close, PutClicked);
            _root.AddChild(Ugui.Place(_packPanel.Root, 0f, 0.5f, 0f, 0.5f, 536f, 0f, 380f, 520f, 0f, 0.5f));
            _packPanel.Title.Text = CrateScreenReadout.PackTitle;
            _packPanel.Cap.Text = _world.Inventory.PackCapLine;
            _summary = Ugui.Place(Ugui.Text("Summary", "", 12, ManagementScreen.DimInk), 0f, 0f, 1f, 0f, 0f, 12f, -24f, 30f, 0.5f, 0f);
            _packPanel.Root.AddChild(_summary);

            _root.AddChild(Ugui.Place(Ugui.Text("Hint", Hint, 12, ManagementScreen.DimInk, align: 2), 0f, 0.5f, 0f, 0.5f, 528f, -272f, 776f, 18f, 0.5f, 1f));
        }
    }
}
