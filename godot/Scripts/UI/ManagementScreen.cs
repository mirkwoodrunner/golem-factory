using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Menus;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Management screen (Tab): Unity's ManagementScreen, rebuilt from WorkbenchCanvas.prefab's
    /// numbers (G8). A tab bar -- Inventory, AssemblyLine, Patents, SaveLoad, Ledger -- over the
    /// active tab's content, on a near-black iron panel. The state is Core's
    /// <see cref="ManagementTabs"/>; each tab's rows come from Core (InventoryReadout,
    /// PatentBrowser, AssemblyLineReadout, the tech tree) and are re-rendered while it is open.
    /// Opening it closes the Workbench and the construction panel (ScreenCoordinator).
    /// </summary>
    public partial class ManagementScreen : CanvasLayer, IClosableScreen
    {
        public const string ToggleAction = "toggle_menu";

        internal static readonly Color SelectedTab = new Color(0.86f, 0.66f, 0.34f, 1f);
        private static readonly Color UnselectedTab = new Color(0.34f, 0.30f, 0.26f, 1f);
        internal static readonly Color SelectedTabLabel = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedTabLabel = new Color(0.82f, 0.78f, 0.71f, 1f);

        // InventoryPanel's palette.
        internal static readonly Color HeaderInk = new Color(0.85f, 0.66f, 0.32f, 1f);
        internal static readonly Color ItemNameInk = new Color(0.88f, 0.84f, 0.76f, 1f);
        internal static readonly Color QuantityInk = new Color(1f, 0.97f, 0.90f, 1f);
        internal static readonly Color DimInk = new Color(0.55f, 0.52f, 0.47f, 1f);
        private static readonly Color RisingInk = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color FallingInk = new Color(0.45f, 0.80f, 0.82f, 1f);
        internal static readonly Color RowTint = new Color(1f, 1f, 1f, 0.035f);
        internal static readonly Color HeaderPlateTint = new Color(0.75f, 0.55f, 0.26f, 0.22f);
        internal static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color BarFill = new Color(0.85f, 0.66f, 0.32f, 0.55f);
        internal static readonly Color CapacityFill = new Color(0.80f, 0.72f, 0.42f, 0.70f);
        internal static readonly Color NearFullFill = new Color(1f, 0.70f, 0.28f, 0.80f);
        internal static readonly Color FullFill = new Color(1f, 0.42f, 0.30f, 0.85f);

        internal const string Ui = "res://art/UI/";

        private WorldNode _world;
        private readonly ManagementTabs _tabs = new ManagementTabs();
        private Control _root;
        private readonly Dictionary<ManagementTab, Button> _tabButtons = new Dictionary<ManagementTab, Button>();
        private readonly Dictionary<ManagementTab, Control> _tabContent = new Dictionary<ManagementTab, Control>();
        private VBoxContainer _inventory;
        private VBoxContainer _patents;
        private Label _saveStatus;
        private Label _slotPrompt;
        private string _renderedInventory;
        private int _renderedPatents = -1;
        private readonly List<IManagementTab> _extraTabs = new List<IManagementTab>();

        public ManagementTabs Tabs => _tabs;
        public bool IsOpen => _tabs.IsOpen;

        // --- For scenarios ------------------------------------------------------------------
        public IReadOnlyDictionary<ManagementTab, Button> TabButtons => _tabButtons;
        public IReadOnlyDictionary<ManagementTab, Control> TabContent => _tabContent;
        public Control InventoryList => _inventory;
        public Control PatentList => _patents;
        public Control RootControl => _root;
        public AssemblyLineTab AssemblyLine { get; private set; }

        /// <summary>The Save / Load tab's slots (docs/menus-design.md §7): what they say, and their buttons.</summary>
        public SaveSlotBoard SlotBoard { get; private set; }
        public SaveSlotList Slots { get; private set; }
        public Button QuitToTitleButton { get; private set; }
        public Button QuitGameButton { get; private set; }
        public string SaveStatus => _saveStatus?.Text ?? "";

        /// <summary>Why the armed button asks to be pressed again, or "".</summary>
        public string SlotPrompt => _slotPrompt?.Text ?? "";
        public LedgerTab Ledger { get; private set; }

        /// <summary>The Overview tab: the map, the production graph and the alerts (docs/menus-design.md §8).</summary>
        public OverviewTab Overview { get; private set; }

        /// <summary>The Settings tab: sound, display and keys.</summary>
        public SettingsTab Settings { get; private set; }

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30;
            _world = WorldNode.Find(this);
            if (!InputMap.HasAction(ToggleAction))
            {
                InputMap.AddAction(ToggleAction);
                InputMap.ActionAddEvent(ToggleAction, new InputEventKey { PhysicalKeycode = Key.Tab });
                PadBindings.AddTo(ToggleAction); // Start
            }
            Build();
            if (_world.Sandbox.AssemblyLineBoard != null)
            {
                AssemblyLine = new AssemblyLineTab(_world.Sandbox.AssemblyLineBoard);
                AddTab(ManagementTab.AssemblyLine, AssemblyLine);
            }
            Ledger = new LedgerTab(_world.Sandbox.TechTree, _world.Sandbox.LedgerReadout);
            AddTab(ManagementTab.TechTree, Ledger);
            Settings = new SettingsTab(AudioNode.Find(this));
            AddTab(ManagementTab.Settings, Settings);
            Overview = new OverviewTab(_world.Sandbox, Close);
            AddTab(ManagementTab.Overview, Overview);
            _root.Visible = false;
            // The Inventory tab's rows built now, while the scene loads: its first open then only
            // updates them in place (D8).
            RefreshInventory();
            if (GetParent()?.GetNodeOrNull<GameSaves>("Saves") is GameSaves saves)
            {
                saves.PictureTaken += OnPictureTaken;
            }
            _world.Sandbox.Screens.Register(this);
            _world.Sandbox.ConfigureScreens(null, null, this);
        }

        /// <summary>A tab whose content another node builds (the Assembly Line, the Ledger).</summary>
        public void AddTab(ManagementTab tab, IManagementTab content)
        {
            _extraTabs.Add(content);
            Control holder = _tabContent[tab];
            foreach (Node child in holder.GetChildren())
            {
                child.QueueFree();
            }
            holder.AddChild(content.Build());
        }

        // --- Open / close / tabs ------------------------------------------------------------

        public void Open()
        {
            _world.Sandbox.Screens.Opening(this);
            _tabs.Open();
            _root.Visible = true;
            ApplyTabs();
            PadOpened();
        }

        public void Close()
        {
            Settings?.CancelCapture();
            DisarmSlots();
            _tabs.Close();
            _root.Visible = false;
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void SelectTab(ManagementTab tab)
        {
            DisarmSlots();
            _tabs.SelectTab(tab);
            ApplyTabs();
        }

        /// <summary>
        /// A key press while the Settings tab waits for a key goes to the capture, before the GUI
        /// and before <see cref="_UnhandledInput"/>'s Tab and Escape, so any key can be chosen.
        /// </summary>
        public override void _Input(InputEvent e)
        {
            if (IsOpen && Settings != null && Settings.TryCapture(e))
            {
                GetViewport().SetInputAsHandled();
            }
            else if (IsOpen && (e is InputEventJoypadButton || e is InputEventJoypadMotion))
            {
                GetViewport().SetInputAsHandled(); // the screen's, read by polling (PadProcess)
            }
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (e.IsActionPressed(ToggleAction))
            {
                // Tab is the menu key wherever the player is, unless another screen is up.
                if (IsOpen || !ModalScreens.AnyOpen(GetTree()))
                {
                    Toggle();
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (IsOpen && e.IsActionPressed(BuildCursorNode.CancelAction))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        private void ApplyTabs()
        {
            foreach (KeyValuePair<ManagementTab, Control> entry in _tabContent)
            {
                entry.Value.Visible = _tabs.IsContentShown(entry.Key);
            }
            foreach (KeyValuePair<ManagementTab, Button> entry in _tabButtons)
            {
                bool lit = _tabs.IsHighlighted(entry.Key);
                entry.Value.SelfModulate = lit ? SelectedTab : UnselectedTab;
                entry.Value.GetNode<Label>("Label").AddThemeColorOverride("font_color", lit ? SelectedTabLabel : UnselectedTabLabel);
            }
            _renderedInventory = null;
            _renderedPatents = -1;
            if (IsOpen && _tabs.IsContentShown(ManagementTab.SaveLoad))
            {
                ReadSlots(); // what is on disk now, every time the tab is shown
            }
        }

        public override void _Process(double delta)
        {
            if (!IsOpen)
            {
                return;
            }
            switch (_tabs.ActiveTab)
            {
                case ManagementTab.Inventory:
                    RefreshInventory();
                    break;
                case ManagementTab.Patents:
                    RefreshPatents();
                    break;
            }
            foreach (IManagementTab tab in _extraTabs)
            {
                tab.Refresh(_tabs.ActiveTab);
            }
            PadProcess(delta);
        }

        // --- Inventory ----------------------------------------------------------------------

        private void RefreshInventory()
        {
            List<InventoryRow> rows = InventoryReadout.Compose(_world.Buffers, _world.Sandbox.Throughput, _world.Sandbox.StockpileBufferId, SandboxWorld.PostBufferId);
            string signature = Signature(rows);
            if (signature == _renderedInventory)
            {
                return; // only rebuild when something visible changed
            }
            _renderedInventory = signature;
            // The same rows as drawn: a count, a bar or a rate moved, so they update in place
            // (docs/steam-deck-design.md D8). Rates move often, and rebuilding every row cost
            // about a millisecond a row each time, while the tab was open.
            string structure = InventoryStructure(rows);
            if (structure == _inventoryStructure && _inventoryUpdates.Count == rows.Count)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    _inventoryUpdates[i]?.Invoke(rows[i]);
                }
                return;
            }
            _inventoryStructure = structure;
            _inventoryUpdates.Clear();
            foreach (Node child in _inventory.GetChildren())
            {
                child.QueueFree();
            }
            foreach (InventoryRow row in rows)
            {
                _inventory.AddChild(InventoryRowControl(row));
            }
        }

        private string _inventoryStructure;

        /// <summary>Each drawn row's update in place, in order (a message row has none).</summary>
        private readonly List<Action<InventoryRow>> _inventoryUpdates = new List<Action<InventoryRow>>();

        // What decides the rows' controls: their kinds, goods and headings, and how a bar is read.
        private static string InventoryStructure(List<InventoryRow> rows)
        {
            var b = new StringBuilder();
            foreach (InventoryRow r in rows)
            {
                b.Append(r.Kind).Append('|').Append(r.Kind == InventoryRowKind.Item ? r.ItemType : r.Text).Append('|')
                 .Append(r.Bar.Mode).Append('\n');
            }
            return b.ToString();
        }

        private static Color RateInk(InventoryRow row) =>
            row.RateKind == StockFlowKind.Net ? (row.Trend == StockTrend.Rising ? RisingInk : FallingInk)
            : row.RateKind == StockFlowKind.Throughput ? ItemNameInk : DimInk;

        private static string Signature(List<InventoryRow> rows)
        {
            var b = new StringBuilder();
            foreach (InventoryRow r in rows)
            {
                b.Append(r.Kind).Append('|').Append(r.Text).Append('|').Append(r.QuantityText).Append('|')
                 .Append(r.RateText).Append('|').Append(r.Bar.Fraction.ToString("F3")).Append('\n');
            }
            return b.ToString();
        }

        private Control InventoryRowControl(InventoryRow row)
        {
            switch (row.Kind)
            {
                case InventoryRowKind.Header:
                {
                    Panel plate = Row(24f, new StyleBoxFlat { BgColor = HeaderPlateTint });
                    HBoxContainer line = RowLine(plate);
                    line.AddChild(Flex(Ugui.Text("Name", row.Text, 14, HeaderInk, bold: true)));
                    var count = (Label)Fixed(Ugui.Text("Count", row.CountText, 11, DimInk, align: 4), 74f);
                    line.AddChild(count);
                    _inventoryUpdates.Add(r => count.Text = r.CountText);
                    return plate;
                }
                case InventoryRowKind.Item:
                {
                    Panel plate = Row(26f, new StyleBoxFlat { BgColor = RowTint });
                    plate.Name = "Item";
                    HBoxContainer line = RowLine(plate);
                    var icon = new TextureRect
                    {
                        Name = "Icon",
                        CustomMinimumSize = new Vector2(22f, 22f),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                    // The slot is drawn whether or not a sprite resolves, so every row's text
                    // starts at the same x.
                    icon.Texture = ItemIcons.Load(row.ItemType);
                    line.AddChild(icon);
                    var name = (Label)Flex(Ugui.Text("Name", row.Text, 13, ItemNameInk));
                    line.AddChild(name);
                    Control bar = Bar(row.Bar);
                    line.AddChild(bar);
                    var quantity = (Label)Fixed(Ugui.Text("Quantity", row.QuantityText, row.Bar.Mode == StockBarMode.AgainstCapacity ? 13 : 15,
                        row.Bar.IsFull ? FullFill : QuantityInk, align: 4, bold: true), 52f);
                    line.AddChild(quantity);
                    // The rate's weight (bold for a net flow) is set here; a change of kind redraws it.
                    var rate = (Label)Fixed(Ugui.Text("Rate", row.RateText, 12, RateInk(row), align: 4, bold: row.RateKind == StockFlowKind.Net), 74f);
                    line.AddChild(rate);
                    StockFlowKind kind = row.RateKind;
                    _inventoryUpdates.Add(r =>
                    {
                        if (r.RateKind != kind)
                        {
                            // The rate's weight changes: rebuilt next frame (both, or the frame
                            // would find these rows already drawn and skip it).
                            _inventoryStructure = null;
                            _renderedInventory = null;
                        }
                        name.Text = r.Text;
                        SetBar(bar, r.Bar);
                        quantity.Text = r.QuantityText;
                        quantity.AddThemeColorOverride("font_color", r.Bar.IsFull ? FullFill : QuantityInk);
                        rate.Text = r.RateText;
                        rate.AddThemeColorOverride("font_color", RateInk(r));
                    });
                    return plate;
                }
                default:
                {
                    Panel plate = Row(26f, new StyleBoxEmpty());
                    plate.Name = "Message";
                    HBoxContainer line = RowLine(plate);
                    line.AddChild(Flex(Ugui.Text("Label", row.Text, 12, DimInk)));
                    _inventoryUpdates.Add(null); // its text is its structure
                    return plate;
                }
            }
        }

        internal static Control Bar(StockBar bar)
        {
            var track = new Control { Name = "Bar", CustomMinimumSize = new Vector2(56f, 8f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            track.AddChild(Ugui.Fill(Ugui.Rect("Track", BarTrack)));
            track.AddChild(Ugui.Place(Ugui.Rect("Fill", BarColor(bar)), 0f, 0f, Mathf.Clamp(bar.Fraction, 0f, 1f), 1f));
            return track;
        }

        /// <summary>A bar's fill: amber against the largest, else by how full against the cap.</summary>
        internal static Color BarColor(StockBar bar) =>
            bar.Mode == StockBarMode.RelativeToLargest ? BarFill : bar.IsFull ? FullFill : bar.IsNearFull ? NearFullFill : CapacityFill;

        /// <summary>
        /// Shows <paramref name="bar"/> on a track <see cref="Bar"/> made, in place: a screen's rows
        /// change as goods move, and rebuilding them cost about a millisecond a row (D8).
        /// </summary>
        internal static void SetBar(Control track, StockBar bar)
        {
            if (track.GetNodeOrNull<ColorRect>("Fill") is ColorRect fill)
            {
                fill.Color = BarColor(bar);
                fill.AnchorRight = Mathf.Clamp(bar.Fraction, 0f, 1f);
                fill.OffsetRight = 0f;
            }
        }

        // --- Patents ------------------------------------------------------------------------

        private void RefreshPatents()
        {
            PatentRegistry registry = _world.Sandbox.Patents;
            if (registry.Blueprints.Count == _renderedPatents)
            {
                return;
            }
            _renderedPatents = registry.Blueprints.Count;
            foreach (Node child in _patents.GetChildren())
            {
                child.QueueFree();
            }

            string empty = PatentBrowser.EmptyMessage(registry);
            if (empty != null)
            {
                Label message = Ugui.Text("Message", empty, 12, DimInk);
                message.CustomMinimumSize = new Vector2(0f, 40f);
                message.VerticalAlignment = VerticalAlignment.Top;
                _patents.AddChild(message);
                return;
            }
            foreach (Blueprint blueprint in PatentBrowser.Rows(registry))
            {
                Panel plate = Row(28f, new StyleBoxFlat { BgColor = RowTint });
                plate.Name = "Blueprint";
                plate.MouseFilter = Control.MouseFilterEnum.Pass;
                HBoxContainer line = RowLine(plate);
                line.AddThemeConstantOverride("separation", 12);
                line.AddChild(Flex(Ugui.Text("Label", blueprint.BlueprintId, 13, ItemNameInk)));
                Button load = PlateButton("Load", Ui + "Hud/hud_button.png", Colors.White, "Load", 13, Colors.Black, bold: false);
                load.CustomMinimumSize = new Vector2(60f, 24f);
                load.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                Blueprint captured = blueprint;
                load.Pressed += () =>
                {
                    Close();
                    PatentBrowser.Load(captured, _world.Sandbox.WorkbenchScreen, _world.Sandbox.Workbench);
                };
                line.AddChild(load);
                _patents.AddChild(plate);
            }
        }

        // --- Construction -------------------------------------------------------------------

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "ManagementScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.985f))));
            _root.AddChild(Ugui.Fill(Ugui.Image("Background", Ugui.NineSlice(Ui + "Hud/hud_panel_plain.png", 8), new Color(0.13f, 0.11f, 0.09f))));

            var bar = Ugui.Place(new MarginContainer { Name = "TabBar", MouseFilter = Control.MouseFilterEnum.Pass }, 0f, 0.845f, 0.82f, 0.95f);
            bar.AddThemeConstantOverride("margin_left", 12);
            bar.AddThemeConstantOverride("margin_right", 12);
            bar.AddThemeConstantOverride("margin_top", 14);
            bar.AddThemeConstantOverride("margin_bottom", 14);
            _root.AddChild(bar);
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
            row.AddThemeConstantOverride("separation", 8);
            bar.AddChild(row);
            foreach (ManagementTab tab in ManagementTabs.BarOrder)
            {
                Button button = PlateButton(tab + "Button", Ui + "Hud/hud_button.png", UnselectedTab, ManagementTabs.Label(tab), 15, SelectedTabLabel, bold: true);
                // Seven fit the bar at 138 (six had 150): "Assembly Line" still clears its plate.
                button.CustomMinimumSize = new Vector2(138f, 46f);
                ManagementTab captured = tab;
                button.Pressed += () => SelectTab(captured);
                _tabButtons[tab] = button;
                row.AddChild(button);
            }

            Button close = Ugui.Place(PlateButton("CloseButton", Ui + "Hud/hud_button_close.png", new Color(0.35f, 0.10f, 0.10f), "Close", 13, Colors.White, bold: false),
                1f, 1f, 1f, 1f, -6f, -46f, 70f, 28f, 1f, 1f);
            close.Pressed += Close;
            _root.AddChild(close);
            _closeButton = close;

            var content = Ugui.Place(new Control { Name = "TabContent", MouseFilter = Control.MouseFilterEnum.Pass }, 0f, 0f, 1f, 0.845f);
            _root.AddChild(content);

            _inventory = ScrollList(content, ManagementTab.Inventory, "InventoryTab");
            _patents = ScrollList(content, ManagementTab.Patents, "PatentsTab");
            _tabContent[ManagementTab.AssemblyLine] = TabHolder(content, "AssemblyLineTab");
            _tabContent[ManagementTab.TechTree] = TabHolder(content, "TechTreeTab");
            _tabContent[ManagementTab.Settings] = TabHolder(content, "SettingsTab");
            _tabContent[ManagementTab.Overview] = TabHolder(content, "OverviewTab");
            BuildSaveLoad(content);
        }

        private Control TabHolder(Control parent, string name)
        {
            var holder = Ugui.Fill(new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Pass });
            parent.AddChild(holder);
            return holder;
        }

        private VBoxContainer ScrollList(Control parent, ManagementTab tab, string name)
        {
            Control holder = TabHolder(parent, name);
            _tabContent[tab] = holder;
            var scroll = Ugui.Fill(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
            holder.AddChild(scroll);
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            {
                margin.AddThemeConstantOverride(side, 8);
            }
            scroll.AddChild(margin);
            var list = new VBoxContainer { Name = "Content", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 2);
            margin.AddChild(list);
            return list;
        }

        /// <summary>
        /// The Save / Load tab (docs/menus-design.md §7): the six slots down the left, each with
        /// Save, Load and Delete; on the right the last thing done, why a button waits for its
        /// second press, and the two ways out of the game, each autosaving first.
        /// </summary>
        private void BuildSaveLoad(Control parent)
        {
            Control holder = TabHolder(parent, "SaveLoadTab");
            _tabContent[ManagementTab.SaveLoad] = holder;

            SlotBoard = new SaveSlotBoard(SlotBoardMode.InGame, null, DateTime.Now);
            Slots = new SaveSlotList(SlotBoard, PressSlot);
            Control list = Slots.Build(ListWidth, new[] { SlotAction.Save, SlotAction.Load, SlotAction.Delete });
            TopLeft(list, 24f, 18f, ListWidth, 6f * SaveSlotList.RowHeight + 5f * SaveSlotList.RowGap);
            holder.AddChild(list);

            float side = 24f + ListWidth + 28f;
            bool creative = _world.Sandbox.Creative;
            Label heading = Ugui.Text("SideHeading", creative ? "Saving a Creative world" : "Saving", 15, HeaderInk, bold: true);
            TopLeft(heading, side, 18f, SideWidth, 22f);
            holder.AddChild(heading);
            Label about = Wrapped("About", creative
                ? "Five slots are yours, apart from the campaign's. The game writes the Autosave itself when you quit."
                : "Five slots are yours. The game writes the Autosave itself: when you quit, and when a Clock Tower stage completes.", 12, DimInk);
            TopLeft(about, side, 44f, SideWidth, 64f);
            holder.AddChild(about);

            _saveStatus = Wrapped("StatusText", "", 14, ItemNameInk);
            TopLeft(_saveStatus, side, 124f, SideWidth, 66f);
            holder.AddChild(_saveStatus);
            _slotPrompt = Wrapped("Prompt", "", 14, NearFullFill);
            TopLeft(_slotPrompt, side, 196f, SideWidth, 66f);
            holder.AddChild(_slotPrompt);

            QuitToTitleButton = SideButton("QuitToTitleButton", "Quit to title", side, 440f);
            QuitToTitleButton.Pressed += () => GameSaves.Find(this)?.QuitToTitle();
            holder.AddChild(QuitToTitleButton);
            QuitGameButton = SideButton("QuitGameButton", "Quit to desktop", side, 486f);
            QuitGameButton.Pressed += () => GameSaves.Find(this)?.QuitGame();
            holder.AddChild(QuitGameButton);
            Label note = Ugui.Text("QuitNote", "Both save to the Autosave first.", 12, DimInk);
            TopLeft(note, side, 530f, SideWidth, 20f);
            holder.AddChild(note);
        }

        private const float ListWidth = 860f;
        private const float SideWidth = 320f;

        private static Button SideButton(string name, string label, float x, float y)
        {
            Button button = PlateButton(name, Ui + "Hud/hud_button.png", new Color(0.80f, 0.62f, 0.32f), label, 15, SelectedTabLabel, bold: true);
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            TopLeft(button, x, y, 220f, 38f);
            return button;
        }

        private static Label Wrapped(string name, string text, int size, Color ink)
        {
            Label label = Ugui.Text(name, text, size, ink);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.ClipText = false;
            label.VerticalAlignment = VerticalAlignment.Top;
            return label;
        }

        /// <summary>Positions <paramref name="control"/> from the top left, in canvas pixels.</summary>
        private static void TopLeft(Control control, float x, float y, float width, float height)
        {
            control.AnchorLeft = control.AnchorRight = control.AnchorTop = control.AnchorBottom = 0f;
            control.OffsetLeft = x;
            control.OffsetTop = y;
            control.OffsetRight = x + width;
            control.OffsetBottom = y + height;
        }

        /// <summary>The slots as they are on disk now; anything armed is let go.</summary>
        private void ReadSlots()
        {
            SlotBoard.Show(SavePaths.Saves(_world.Sandbox.Creative), DateTime.Now);
            ShowSlots();
        }

        private void ShowSlots()
        {
            Slots.Refresh();
            _slotPrompt.Text = SlotBoard.Prompt;
        }

        private void DisarmSlots()
        {
            if (SlotBoard?.Armed != null)
            {
                SlotBoard.Disarm();
                ShowSlots();
            }
        }

        /// <summary>A slot's button: the first press of one that loses something only arms it.</summary>
        private void PressSlot(string key, SlotAction action)
        {
            GameSaves saves = GameSaves.Find(this);
            if (saves == null || saves.Leaving || SlotBoard.Press(key, action) != SlotPress.Go)
            {
                ShowSlots();
                return;
            }
            switch (action)
            {
                case SlotAction.Save:
                    _saveStatus.Text = saves.SaveSlot(key);
                    break;
                case SlotAction.Load:
                    _saveStatus.Text = saves.LoadSlot(key);
                    break;
                default:
                    _saveStatus.Text = saves.DeleteSlot(key);
                    break;
            }
            ReadSlots();
        }

        /// <summary>A picture lands a frame after its save: the row shows it then, unless a press is waiting.</summary>
        private void OnPictureTaken(string key)
        {
            if (IsOpen && _tabs.IsContentShown(ManagementTab.SaveLoad) && SlotBoard.Armed == null)
            {
                ReadSlots();
            }
        }

        // --- Small builders -----------------------------------------------------------------

        private static Panel Row(float height, StyleBox style)
        {
            var plate = new Panel { CustomMinimumSize = new Vector2(0f, height), MouseFilter = Control.MouseFilterEnum.Ignore };
            plate.AddThemeStyleboxOverride("panel", style);
            return plate;
        }

        internal static HBoxContainer RowLine(Control plate)
        {
            var line = Ugui.Fill(new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass });
            line.OffsetLeft = 6f;
            line.OffsetRight = -6f;
            line.AddThemeConstantOverride("separation", 6);
            plate.AddChild(line);
            return line;
        }

        internal static Control Flex(Control control)
        {
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            control.SizeFlagsVertical = Control.SizeFlags.Fill;
            return control;
        }

        internal static Control Fixed(Control control, float width)
        {
            control.CustomMinimumSize = new Vector2(width, 0f);
            control.SizeFlagsVertical = Control.SizeFlags.Fill;
            return control;
        }

        internal static Button PlateButton(string name, string sprite, Color tint, string label, int size, Color ink, bool bold)
        {
            var button = new Button { Name = name, SelfModulate = tint, FocusMode = Control.FocusModeEnum.None };
            StyleBoxTexture box = Ugui.NineSlice(sprite, 8);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            {
                button.AddThemeStyleboxOverride(state, box);
            }
            button.AddChild(Ugui.Fill(Ugui.Text("Label", label, size, ink, align: 2, bold: bold)));
            return button;
        }
    }

    /// <summary>A Management tab whose content lives in its own class.</summary>
    public interface IManagementTab
    {
        Control Build();

        /// <summary>Per frame while the screen is open; <paramref name="active"/> is the shown tab.</summary>
        void Refresh(ManagementTab active);
    }

    /// <summary>Item icons by item-type id: <c>art/item_&lt;snake_case&gt;.png</c>.</summary>
    public static class ItemIcons
    {
        public static string PathFor(string itemType)
        {
            if (string.IsNullOrEmpty(itemType))
            {
                return "";
            }
            var b = new StringBuilder("res://art/item_");
            for (int i = 0; i < itemType.Length; i++)
            {
                char c = itemType[i];
                if (char.IsUpper(c) && i > 0)
                {
                    b.Append('_');
                }
                b.Append(char.ToLowerInvariant(c));
            }
            return b.Append(".png").ToString();
        }

        // Kept once loaded (docs/steam-deck-design.md D8): a screen's rows are rebuilt on every
        // open, and a texture nothing else held was freed with its row, so each open checked the
        // disk and loaded and uploaded every icon again. A good's icon never changes.
        private static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>();

        /// <summary>The good's icon, or null where it has none.</summary>
        public static Texture2D Load(string itemType)
        {
            string path = PathFor(itemType);
            if (!Loaded.TryGetValue(path, out Texture2D icon))
            {
                icon = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
                Loaded[path] = icon;
            }
            return icon;
        }
    }
}
