using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GolemFactory.Contracts;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The City Office's notice (docs/contracts-design.md §2): [E] at the office. Who asks and
    /// their words, what is asked with what is delivered, the reward and the bonus with its par,
    /// and the rows to press: take the contract, collect the shelf, put a rung on a cart. Every
    /// rule is Core's <see cref="ContractPanel"/>; this node draws it and forwards clicks. Plain
    /// ink throughout: nothing here is ever red (invariant 2). Rows are kept in a list, never
    /// found by name, and rebuilt only when what they show changes.
    /// </summary>
    public partial class ContractScreen : CanvasLayer, IContractScreen, IClosableScreen
    {
        private const string Hud = ManagementScreen.Ui + "Hud/";
        private const float DimAlpha = 0.5f;
        private static readonly Color RowHover = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color RowPressed = new Color(0.85f, 0.66f, 0.32f, 0.25f);

        private SandboxWorld _world;
        private Control _root;
        private Control _panel;
        private Label _title;
        private Label _words;
        private Label _asks;
        private Label _reward;
        private Label _bonus;
        private Label _how;
        private Label _orderTitle;
        private Label _orderWords;
        private Label _orderAsks;
        private Label _orderReward;
        private Label _orderHow;
        private Label _status;
        private VBoxContainer _content;
        private string _signature;
        private readonly List<(ContractRow row, Button button)> _rows = new List<(ContractRow, Button)>();

        public bool IsOpen { get; private set; }

        // --- For scenarios ------------------------------------------------------------------
        public Control Panel => _panel;
        public string Title => _title.Text;
        public string Words => _words.Text;
        public string Asks => _asks.Text;
        public string Reward => _reward.Text;
        public string Bonus => _bonus.Text;
        public string Status => _status.Text;
        public string OrderTitle => _orderTitle.Text;
        public string OrderAsks => _orderAsks.Text;
        public string OrderReward => _orderReward.Text;
        public IReadOnlyList<(ContractRow row, Button button)> Rows => _rows;

        /// <summary>Every label the notice draws, for the scenario's id and glyph checks.</summary>
        public IEnumerable<string> DrawnText =>
            new[] { _title.Text, _words.Text, _asks.Text, _reward.Text, _bonus.Text, _how.Text, _status.Text,
                    _orderTitle.Text, _orderWords.Text, _orderAsks.Text, _orderReward.Text, _orderHow.Text }
                .Concat(_rows.SelectMany(r => new[] { r.row.Label, r.row.Detail, r.row.Price }));

        public Button Button(ContractAction action) => _rows.FirstOrDefault(r => r.row.Action == action).button;

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30;
            _world = WorldNode.Find(this).Sandbox;
            Build();
            _root.Visible = false;
            _world.Screens.Register(this);
            _world.ConfigureContractScreen(this);
        }

        public static ContractScreen Find(Node from) => from.GetTree().Root.FindChild("ContractScreen", true, false) as ContractScreen;

        public void Open()
        {
            _world.Screens.Opening(this);
            IsOpen = true;
            _status.Text = "";
            _signature = null;
            _root.Visible = true;
            Refresh();
            PadOpened();
        }

        public void Close()
        {
            IsOpen = false;
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
                PadProcess(delta);
            }
        }

        private void Refresh()
        {
            ContractPanel panel = _world.ContractPanel;
            _title.Text = panel.Title;
            _words.Text = panel.Words;
            _asks.Text = string.Join("\n", panel.AskLines);
            _reward.Text = panel.RewardLine;
            _bonus.Text = panel.BonusLine;
            _how.Text = panel.HowLine;
            _orderTitle.Text = panel.OrderTitle;
            _orderWords.Text = panel.OrderWords;
            _orderAsks.Text = panel.OrderAsks;
            _orderReward.Text = panel.OrderReward;
            _orderHow.Text = panel.OrderHow;
            IReadOnlyList<ContractRow> rows = panel.Rows();
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
            foreach (ContractRow row in rows)
            {
                Button button = RowButton(row);
                _content.AddChild(button);
                _rows.Add((row, button));
            }
        }

        private static string Signature(IReadOnlyList<ContractRow> rows)
        {
            var b = new StringBuilder();
            foreach (ContractRow r in rows)
            {
                b.Append((int)r.Action).Append('|').Append(r.ItemType).Append('|').Append(r.Label).Append('|')
                 .Append(r.Detail).Append('|').Append(r.Enabled).Append('|').Append(r.RungIndex).Append('|')
                 .Append(r.Spot?.Cell.x).Append('\n');
            }
            return b.ToString();
        }

        private void Pressed(ContractRow row)
        {
            _status.Text = _world.ContractPanel.Press(row);
            _signature = null;
            Refresh();
        }

        // --- Construction ---------------------------------------------------------------------

        private Button RowButton(ContractRow row)
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
                Texture = ItemIcons.Load(row.ItemType),
            };
            line.AddChild(icon);

            var words = new VBoxContainer { Name = "Words", MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            words.AddThemeConstantOverride("separation", 0);
            words.AddChild(Ugui.Text("Label", row.Label, 14, ManagementScreen.ItemNameInk, bold: true));
            Label detail = Ugui.Text("Detail", row.Detail ?? "", 12, ManagementScreen.DimInk);
            detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            words.AddChild(detail);
            line.AddChild(ManagementScreen.Flex(words));
            Label price = Ugui.Text("Price", row.Price ?? "", 13, row.Enabled ? ManagementScreen.QuantityInk : ManagementScreen.DimInk, align: 4, bold: row.Enabled);
            line.AddChild(ManagementScreen.Fixed(price, 110f));

            if (!row.Enabled)
            {
                icon.Modulate = new Color(1f, 1f, 1f, DimAlpha);
                words.Modulate = new Color(1f, 1f, 1f, DimAlpha);
            }
            return button;
        }

        private Label Wrapped(string name, int size, Color ink, float top, float height, bool bold = false)
        {
            Label label = Ugui.Place(Ugui.Text(name, "", size, ink, bold: bold), 0f, 1f, 1f, 1f, 0f, top, -32f, height, 0.5f, 1f);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.VerticalAlignment = VerticalAlignment.Top;
            _panel.AddChild(label);
            return label;
        }

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "ContractScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.80f))));

            // Left of centre, as the cart panel and the Crate screen are, so the guide's plate stays readable.
            _panel = new Control { Name = "ContractPanel", MouseFilter = Control.MouseFilterEnum.Stop };
            _root.AddChild(Ugui.Place(_panel, 0f, 0.5f, 0f, 0.5f, 140f, 0f, 520f, 640f, 0f, 0.5f));
            _panel.AddChild(Ugui.Fill(Ugui.Image("Plate", Ugui.NineSlice(Hud + "hud_panel_plain.png", 8), new Color(0.13f, 0.11f, 0.09f))));

            var office = Ugui.Place(new TextureRect
            {
                Name = "Office",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Texture = GD.Load<Texture2D>("res://art/city_office.png"),
            }, 0f, 1f, 0f, 1f, 16f, -10f, 64f, 64f, 0f, 1f);
            _panel.AddChild(office);

            Label kicker = Ugui.Place(Ugui.Text("Kicker", ContractText.OfficeName + " · a contract", 12, ManagementScreen.DimInk), 0f, 1f, 1f, 1f, 90f, -12f, -176f, 18f, 0f, 1f);
            _panel.AddChild(kicker);
            _title = Ugui.Place(Ugui.Text("Title", "", 18, ManagementScreen.HeaderInk, bold: true), 0f, 1f, 1f, 1f, 90f, -30f, -176f, 26f, 0f, 1f);
            _panel.AddChild(_title);

            Button closeButton = Ugui.Place(ManagementScreen.PlateButton("CloseButton", Hud + "hud_button_close.png", new Color(0.35f, 0.10f, 0.10f), "Close", 12, Colors.White, bold: false),
                1f, 1f, 1f, 1f, -10f, -8f, 60f, 24f, 1f, 1f);
            closeButton.Pressed += Close;
            _panel.AddChild(closeButton);

            // Top to bottom, each block as tall as its most lines: the words two, the asks two (a
            // contract asks one or two goods), the reward one, the bonus and the how-to two.
            _words = Wrapped("Words", 14, ManagementScreen.ItemNameInk, -82f, 42f);
            _asks = Wrapped("Asks", 16, ManagementScreen.QuantityInk, -128f, 48f, bold: true);
            _reward = Wrapped("Reward", 13, ManagementScreen.ItemNameInk, -180f, 32f);
            _bonus = Wrapped("Bonus", 13, ManagementScreen.ItemNameInk, -214f, 34f);
            _how = Wrapped("How", 12, ManagementScreen.DimInk, -250f, 34f);

            // The golem order beside the contract (docs/golem-sales-design.md §4): its heading,
            // the requester's words, what it wants with what is delivered, the reward, and how.
            _orderTitle = Wrapped("OrderTitle", 15, ManagementScreen.HeaderInk, -292f, 22f, bold: true);
            _orderWords = Wrapped("OrderWords", 13, ManagementScreen.ItemNameInk, -314f, 20f);
            _orderAsks = Wrapped("OrderAsks", 15, ManagementScreen.QuantityInk, -336f, 22f, bold: true);
            _orderReward = Wrapped("OrderReward", 12, ManagementScreen.ItemNameInk, -360f, 18f);
            _orderHow = Wrapped("OrderHow", 12, ManagementScreen.DimInk, -378f, 34f);

            var scroll = Ugui.Place(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled },
                // Under the notice's lines (to 412 from the top), 52 above the bottom edge for the status line.
                0f, 0f, 1f, 1f, 0f, -179.5f, -16f, -465f, 0.5f, 0.5f);
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
