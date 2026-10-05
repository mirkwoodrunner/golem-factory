using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
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

        private static readonly Color SelectedTab = new Color(0.86f, 0.66f, 0.34f, 1f);
        private static readonly Color UnselectedTab = new Color(0.34f, 0.30f, 0.26f, 1f);
        private static readonly Color SelectedTabLabel = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedTabLabel = new Color(0.82f, 0.78f, 0.71f, 1f);

        // InventoryPanel's palette.
        private static readonly Color HeaderInk = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color ItemNameInk = new Color(0.88f, 0.84f, 0.76f, 1f);
        private static readonly Color QuantityInk = new Color(1f, 0.97f, 0.90f, 1f);
        private static readonly Color DimInk = new Color(0.55f, 0.52f, 0.47f, 1f);
        private static readonly Color RisingInk = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color FallingInk = new Color(0.45f, 0.80f, 0.82f, 1f);
        private static readonly Color RowTint = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color HeaderPlateTint = new Color(0.75f, 0.55f, 0.26f, 0.22f);
        private static readonly Color BarTrack = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color BarFill = new Color(0.85f, 0.66f, 0.32f, 0.55f);
        private static readonly Color CapacityFill = new Color(0.80f, 0.72f, 0.42f, 0.70f);
        private static readonly Color NearFullFill = new Color(1f, 0.70f, 0.28f, 0.80f);
        private static readonly Color FullFill = new Color(1f, 0.42f, 0.30f, 0.85f);

        private const string Ui = "res://art/UI/";

        private WorldNode _world;
        private readonly ManagementTabs _tabs = new ManagementTabs();
        private Control _root;
        private readonly Dictionary<ManagementTab, Button> _tabButtons = new Dictionary<ManagementTab, Button>();
        private readonly Dictionary<ManagementTab, Control> _tabContent = new Dictionary<ManagementTab, Control>();
        private VBoxContainer _inventory;
        private VBoxContainer _patents;
        private Label _saveStatus;
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
        public LedgerTab Ledger { get; private set; }

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30;
            _world = WorldNode.Find(this);
            if (!InputMap.HasAction(ToggleAction))
            {
                InputMap.AddAction(ToggleAction);
                InputMap.ActionAddEvent(ToggleAction, new InputEventKey { PhysicalKeycode = Key.Tab });
            }
            Build();
            if (_world.Sandbox.AssemblyLineBoard != null)
            {
                AssemblyLine = new AssemblyLineTab(_world.Sandbox.AssemblyLineBoard);
                AddTab(ManagementTab.AssemblyLine, AssemblyLine);
            }
            Ledger = new LedgerTab(_world.Sandbox.TechTree, new TechTreeReadout(
                System.Linq.Enumerable.ToList(_world.Definitions.Recipes.Values), _world.Sandbox.Throughput,
                _world.Sandbox.StockpileBufferId, _world.Clock.TicksPerSecond));
            AddTab(ManagementTab.TechTree, Ledger);
            _root.Visible = false;
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
        }

        public void Close()
        {
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
            _tabs.SelectTab(tab);
            ApplyTabs();
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
        }

        // --- Inventory ----------------------------------------------------------------------

        private void RefreshInventory()
        {
            List<InventoryRow> rows = InventoryReadout.Compose(_world.Buffers, _world.Sandbox.Throughput);
            string signature = Signature(rows);
            if (signature == _renderedInventory)
            {
                return; // only rebuild when something visible changed
            }
            _renderedInventory = signature;
            foreach (Node child in _inventory.GetChildren())
            {
                child.QueueFree();
            }
            foreach (InventoryRow row in rows)
            {
                _inventory.AddChild(InventoryRowControl(row));
            }
        }

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
                    line.AddChild(Fixed(Ugui.Text("Count", row.CountText, 11, DimInk, align: 4), 74f));
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
                    string path = ItemIcons.PathFor(row.ItemType);
                    if (ResourceLoader.Exists(path))
                    {
                        icon.Texture = GD.Load<Texture2D>(path);
                    }
                    line.AddChild(icon);
                    line.AddChild(Flex(Ugui.Text("Name", row.Text, 13, ItemNameInk)));
                    line.AddChild(Bar(row.Bar));
                    line.AddChild(Fixed(Ugui.Text("Quantity", row.QuantityText, row.Bar.Mode == StockBarMode.AgainstCapacity ? 13 : 15,
                        row.Bar.IsFull ? FullFill : QuantityInk, align: 4, bold: true), 52f));
                    Color rateInk = row.RateKind == StockFlowKind.Net ? (row.Trend == StockTrend.Rising ? RisingInk : FallingInk)
                        : row.RateKind == StockFlowKind.Throughput ? ItemNameInk : DimInk;
                    line.AddChild(Fixed(Ugui.Text("Rate", row.RateText, 12, rateInk, align: 4, bold: row.RateKind == StockFlowKind.Net), 74f));
                    return plate;
                }
                default:
                {
                    Panel plate = Row(26f, new StyleBoxEmpty());
                    plate.Name = "Message";
                    HBoxContainer line = RowLine(plate);
                    line.AddChild(Flex(Ugui.Text("Label", row.Text, 12, DimInk)));
                    return plate;
                }
            }
        }

        private static Control Bar(StockBar bar)
        {
            var track = new Control { Name = "Bar", CustomMinimumSize = new Vector2(56f, 8f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            track.AddChild(Ugui.Fill(Ugui.Rect("Track", BarTrack)));
            Color fill = bar.Mode == StockBarMode.RelativeToLargest ? BarFill : bar.IsFull ? FullFill : bar.IsNearFull ? NearFullFill : CapacityFill;
            track.AddChild(Ugui.Place(Ugui.Rect("Fill", fill), 0f, 0f, Mathf.Clamp(bar.Fraction, 0f, 1f), 1f));
            return track;
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
                Button load = PlateButton("Load", Ui + "Steampunk/steampunk_button_blank.png", Colors.White, "Load", 13, Colors.Black, bold: false);
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
            _root.AddChild(Ugui.Fill(Ugui.Image("Background", Ugui.NineSlice(Ui + "Steampunk/iron_panel_nobolt.png", 8), new Color(0.13f, 0.11f, 0.09f))));

            var bar = Ugui.Place(new MarginContainer { Name = "TabBar", MouseFilter = Control.MouseFilterEnum.Pass }, 0f, 0.845f, 0.82f, 0.95f);
            bar.AddThemeConstantOverride("margin_left", 12);
            bar.AddThemeConstantOverride("margin_right", 12);
            bar.AddThemeConstantOverride("margin_top", 14);
            bar.AddThemeConstantOverride("margin_bottom", 14);
            _root.AddChild(bar);
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
            row.AddThemeConstantOverride("separation", 8);
            bar.AddChild(row);
            foreach (ManagementTab tab in (ManagementTab[])Enum.GetValues(typeof(ManagementTab)))
            {
                Button button = PlateButton(tab + "Button", Ui + "Steampunk/steampunk_button_blank.png", UnselectedTab, ManagementTabs.Label(tab), 15, SelectedTabLabel, bold: true);
                button.CustomMinimumSize = new Vector2(168f, 46f);
                ManagementTab captured = tab;
                button.Pressed += () => SelectTab(captured);
                _tabButtons[tab] = button;
                row.AddChild(button);
            }

            Button close = Ugui.Place(PlateButton("CloseButton", Ui + "Steampunk/steampunk_button_exit.png", new Color(0.35f, 0.10f, 0.10f), "Close", 13, Colors.White, bold: false),
                1f, 1f, 1f, 1f, -6f, -46f, 70f, 28f, 1f, 1f);
            close.Pressed += Close;
            _root.AddChild(close);

            var content = Ugui.Place(new Control { Name = "TabContent", MouseFilter = Control.MouseFilterEnum.Pass }, 0f, 0f, 1f, 0.845f);
            _root.AddChild(content);

            _inventory = ScrollList(content, ManagementTab.Inventory, "InventoryTab");
            _patents = ScrollList(content, ManagementTab.Patents, "PatentsTab");
            _tabContent[ManagementTab.AssemblyLine] = TabHolder(content, "AssemblyLineTab");
            _tabContent[ManagementTab.TechTree] = TabHolder(content, "TechTreeTab");
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
        /// The SaveLoad tab's layout, as the prefab has it. Saving to disk is G9; until then the
        /// buttons are drawn but disabled, and the status line says why rather than leaving a
        /// tab that does nothing silently.
        /// </summary>
        private void BuildSaveLoad(Control parent)
        {
            Control holder = TabHolder(parent, "SaveLoadTab");
            _tabContent[ManagementTab.SaveLoad] = holder;
            var buttons = Ugui.Place(new HBoxContainer { Name = "ButtonRow", Alignment = BoxContainer.AlignmentMode.Begin }, 0.1f, 0.78f, 0.9f, 0.9f);
            buttons.AddThemeConstantOverride("separation", 16);
            holder.AddChild(buttons);
            foreach ((string name, string sprite) in new[] { ("Save", "steampunk_button_accept.png"), ("Load", "steampunk_button_blank.png") })
            {
                Button button = PlateButton(name + "Button", Ui + "Steampunk/" + sprite, new Color(0.80f, 0.62f, 0.32f), name, 15, SelectedTabLabel, bold: true);
                button.CustomMinimumSize = new Vector2(140f, 42f);
                button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                button.Disabled = true;
                button.Modulate = new Color(1f, 1f, 1f, 0.5f);
                buttons.AddChild(button);
            }
            _saveStatus = Ugui.Place(Ugui.Text("StatusText", "Saving and loading arrive with the save milestone (G9).", 15, HeaderInk), 0.1f, 0.62f, 0.9f, 0.74f);
            holder.AddChild(_saveStatus);
        }

        // --- Small builders -----------------------------------------------------------------

        private static Panel Row(float height, StyleBox style)
        {
            var plate = new Panel { CustomMinimumSize = new Vector2(0f, height), MouseFilter = Control.MouseFilterEnum.Ignore };
            plate.AddThemeStyleboxOverride("panel", style);
            return plate;
        }

        private static HBoxContainer RowLine(Panel plate)
        {
            var line = Ugui.Fill(new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass });
            line.OffsetLeft = 6f;
            line.OffsetRight = -6f;
            line.AddThemeConstantOverride("separation", 6);
            plate.AddChild(line);
            return line;
        }

        private static Control Flex(Control control)
        {
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            control.SizeFlagsVertical = Control.SizeFlags.Fill;
            return control;
        }

        private static Control Fixed(Control control, float width)
        {
            control.CustomMinimumSize = new Vector2(width, 0f);
            control.SizeFlagsVertical = Control.SizeFlags.Fill;
            return control;
        }

        private static Button PlateButton(string name, string sprite, Color tint, string label, int size, Color ink, bool bold)
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
    }
}
