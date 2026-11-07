using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GolemFactory.Supply;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The cart panel (docs/supply-design.md §2): [E] at an empty spot, [I] at a cart. An empty
    /// spot lists the carts it can take, each with its terms and price; a cart lists its Rate and
    /// Capacity rungs, the goods it can change to, and Remove with what comes back. Every rule is
    /// Core's <see cref="SupplyPanel"/>; this node draws its rows and forwards clicks. Rows are
    /// kept in a list, never found by name, and rebuilt only when what they show changes, so a
    /// click is never lost to a rebuild under the mouse.
    /// </summary>
    public partial class SupplyScreen : CanvasLayer, ISupplyScreen, IClosableScreen
    {
        private const string Hud = ManagementScreen.Ui + "Hud/";
        private const float DimAlpha = 0.5f;
        private static readonly Color RowHover = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color RowPressed = new Color(0.85f, 0.66f, 0.32f, 0.25f);

        private SandboxWorld _world;
        private Control _root;
        private Control _panel;
        private Label _title;
        private Label _holding;
        private Label _status;
        private VBoxContainer _content;
        private TextureRect _cart;
        private string _signature;
        private readonly List<(SupplyRow row, Button button)> _rows = new List<(SupplyRow, Button)>();

        public bool IsOpen { get; private set; }
        public CartSpot Spot { get; private set; }

        // --- For scenarios ------------------------------------------------------------------
        public Control Panel => _panel;
        public string Title => _title.Text;
        public string Holding => _holding.Text;
        public string Status => _status.Text;
        public IReadOnlyList<(SupplyRow row, Button button)> Rows => _rows;

        public Button Button(SupplyAction action, string itemType = null) =>
            _rows.FirstOrDefault(r => r.row.Action == action && (itemType == null || r.row.ItemType == itemType)).button;

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30;
            _world = WorldNode.Find(this).Sandbox;
            Build();
            _root.Visible = false;
            _world.Screens.Register(this);
            _world.ConfigureSupplyScreen(this);
        }

        public static SupplyScreen Find(Node from) => from.GetTree().Root.FindChild("SupplyScreen", true, false) as SupplyScreen;

        public void Open(CartSpot spot)
        {
            if (spot == null)
            {
                return;
            }
            _world.Screens.Opening(this);
            Spot = spot;
            IsOpen = true;
            _status.Text = "";
            _signature = null;
            _root.Visible = true;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            Spot = null;
            _root.Visible = false;
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (!IsOpen)
            {
                return;
            }
            if (e.IsActionPressed(BuildCursorNode.CancelAction) || e.IsActionPressed(PlayerNode.Inventory) || e.IsActionPressed(PlayerNode.Interact))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            if (IsOpen)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            SupplyPanel panel = _world.SupplyPanel;
            _title.Text = panel.Title(Spot);
            _holding.Text = panel.Status(Spot);
            _cart.Texture = GD.Load<Texture2D>("res://art/" + SupplyArt.SpriteFor(Spot) + ".png");
            IReadOnlyList<SupplyRow> rows = panel.Rows(Spot);
            string signature = Signature(rows);
            if (signature == _signature)
            {
                return;
            }
            _signature = signature;
            _rows.Clear();
            foreach (Node child in _content.GetChildren())
            {
                _content.RemoveChild(child);
                child.QueueFree();
            }
            foreach (SupplyRow row in rows)
            {
                if (row.Action == SupplyAction.Change && !_rows.Any(r => r.row.Action == SupplyAction.Change))
                {
                    _content.AddChild(Header("Change its good"));
                }
                if (row.Action == SupplyAction.Remove)
                {
                    _content.AddChild(Header("Take it away"));
                }
                Button button = RowButton(row);
                _content.AddChild(button);
                _rows.Add((row, button));
            }
        }

        private static string Signature(IReadOnlyList<SupplyRow> rows)
        {
            var b = new StringBuilder();
            foreach (SupplyRow r in rows)
            {
                b.Append((int)r.Action).Append('|').Append(r.ItemType).Append('|').Append(r.Label).Append('|')
                 .Append(r.Detail).Append('|').Append(r.Price).Append('|').Append(r.Enabled).Append('|').Append(r.Note).Append('\n');
            }
            return b.ToString();
        }

        private void Pressed(SupplyRow row)
        {
            if (!row.Enabled)
            {
                _status.Text = row.Note ?? "";
                return;
            }
            SupplyResult result = _world.SupplyPanel.Press(Spot, row);
            _status.Text = result.Message;
            _signature = null;
            Refresh();
        }

        // --- Construction ---------------------------------------------------------------------

        private Button RowButton(SupplyRow row)
        {
            var button = new Button { Name = "Row", CustomMinimumSize = new Vector2(0f, 44f), FocusMode = Control.FocusModeEnum.None };
            button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = ManagementScreen.RowTint });
            button.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = RowHover });
            button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = row.Enabled ? RowPressed : RowHover });
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            button.Pressed += () => Pressed(row);
            button.TooltipText = row.Enabled ? "" : row.Note ?? "";

            HBoxContainer line = ManagementScreen.RowLine(button);
            line.MouseFilter = Control.MouseFilterEnum.Ignore;
            var icon = new TextureRect
            {
                Name = "Icon",
                CustomMinimumSize = new Vector2(28f, 28f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Texture = ItemIcons.Load(row.ItemType ?? Spot?.Cart?.ItemType),
            };
            line.AddChild(icon);

            var words = new VBoxContainer { Name = "Words", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            words.AddThemeConstantOverride("separation", 0);
            words.AddChild(Ugui.Text("Label", row.Label, 14, ManagementScreen.ItemNameInk, bold: true));
            words.AddChild(Ugui.Text("Detail", row.Detail ?? "", 12, ManagementScreen.DimInk));
            line.AddChild(ManagementScreen.Flex(words));
            Label price = Ugui.Text("Price", row.Price ?? "", 13, row.Enabled ? ManagementScreen.QuantityInk : ManagementScreen.DimInk, align: 4, bold: row.Enabled);
            price.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.AddChild(ManagementScreen.Fixed(price, 230f));

            if (!row.Enabled)
            {
                icon.Modulate = new Color(1f, 1f, 1f, DimAlpha);
                words.Modulate = new Color(1f, 1f, 1f, DimAlpha);
            }
            return button;
        }

        private static Control Header(string text)
        {
            var plate = new Panel { Name = "Header", CustomMinimumSize = new Vector2(0f, 24f), MouseFilter = Control.MouseFilterEnum.Ignore };
            plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = ManagementScreen.HeaderPlateTint });
            ManagementScreen.RowLine(plate).AddChild(ManagementScreen.Flex(Ugui.Text("Name", text, 13, ManagementScreen.HeaderInk, bold: true)));
            return plate;
        }

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "SupplyScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.80f))));

            // Left of centre, as the Crate screen is, so the guide's plate stays readable.
            _panel = new Control { Name = "SupplyPanel", MouseFilter = Control.MouseFilterEnum.Stop };
            _root.AddChild(Ugui.Place(_panel, 0f, 0.5f, 0f, 0.5f, 140f, 0f, 520f, 520f, 0f, 0.5f));
            _panel.AddChild(Ugui.Fill(Ugui.Image("Plate", Ugui.NineSlice(Hud + "hud_panel_plain.png", 8), new Color(0.13f, 0.11f, 0.09f))));

            // The cart (or the rope) as it stands on the street, beside the title.
            _cart = Ugui.Place(new TextureRect
            {
                Name = "Cart",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            }, 0f, 1f, 0f, 1f, 16f, -10f, 64f, 80f, 0f, 1f);
            _panel.AddChild(_cart);

            _title = Ugui.Place(Ugui.Text("Title", "", 18, ManagementScreen.HeaderInk, bold: true), 0f, 1f, 1f, 1f, 90f, -14f, -176f, 26f, 0f, 1f);
            _panel.AddChild(_title);
            _holding = Ugui.Place(Ugui.Text("Holding", "", 13, ManagementScreen.ItemNameInk), 0f, 1f, 1f, 1f, 90f, -42f, -176f, 20f, 0f, 1f);
            _panel.AddChild(_holding);

            Button closeButton = Ugui.Place(ManagementScreen.PlateButton("CloseButton", Hud + "hud_button_close.png", new Color(0.35f, 0.10f, 0.10f), "Close", 12, Colors.White, bold: false),
                1f, 1f, 1f, 1f, -10f, -8f, 60f, 24f, 1f, 1f);
            closeButton.Pressed += Close;
            _panel.AddChild(closeButton);

            var scroll = Ugui.Place(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled },
                // Insets 8 left and right, 100 above (under the cart and its lines), 52 below.
                0f, 0f, 1f, 1f, 0f, -24f, -16f, -152f, 0.5f, 0.5f);
            _panel.AddChild(scroll);
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            {
                margin.AddThemeConstantOverride(side, 4);
            }
            scroll.AddChild(margin);
            _content = new VBoxContainer { Name = "Content", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _content.AddThemeConstantOverride("separation", 3);
            margin.AddChild(_content);

            _status = Ugui.Place(Ugui.Text("Status", "", 12, ManagementScreen.HeaderInk), 0f, 0f, 1f, 0f, 0f, 12f, -24f, 30f, 0.5f, 0f);
            _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _panel.AddChild(_status);
        }
    }
}
