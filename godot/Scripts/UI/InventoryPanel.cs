using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using GolemFactory.Economy;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// One 380x520 panel of the Pack or Crate screen (docs/inventory-screens-design.md §4.2-4.3):
    /// plate, title, Close, cap line and a scrolling list of rows from
    /// <see cref="CrateScreenReadout"/>. It only draws; a row's click goes to the screen with the
    /// good and whether Shift was down. Rows are kept by good, never found by node name.
    /// </summary>
    internal sealed class InventoryPanel
    {
        private const string Steampunk = ManagementScreen.Ui + "Steampunk/";

        private static readonly Color RowHover = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color RowPressed = new Color(0.85f, 0.66f, 0.32f, 0.25f);
        private const float DimAlpha = 0.55f;

        private readonly Dictionary<string, Button> _rows = new Dictionary<string, Button>();
        private readonly Action<string, bool> _click;
        private string _signature;

        public Control Root { get; }
        public Label Title { get; }
        public Label Cap { get; }
        public Button CloseButton { get; }
        public ScrollContainer Scroll { get; }
        public VBoxContainer Content { get; }

        /// <summary>The row drawn for each good, by item type.</summary>
        public IReadOnlyDictionary<string, Button> Rows => _rows;

        /// <param name="listTop">The list's top inset: 96 under a crate's label button, 64 in a pack panel.</param>
        /// <param name="click">A row's click: the good, and whether Shift was down. Null for a panel only shown.</param>
        public InventoryPanel(string name, float listTop, Action close, Action<string, bool> click)
        {
            _click = click;
            Root = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Stop };
            Root.AddChild(Ugui.Fill(Ugui.Image("Plate", Ugui.NineSlice(Steampunk + "iron_panel_nobolt.png", 8), new Color(0.13f, 0.11f, 0.09f))));

            Title = Ugui.Place(Ugui.Text("Title", "", 17, ManagementScreen.HeaderInk, bold: true), 0f, 1f, 1f, 1f, -32f, -10f, -88f, 26f, 0.5f, 1f);
            Root.AddChild(Title);

            CloseButton = Ugui.Place(ManagementScreen.PlateButton("CloseButton", Steampunk + "steampunk_button_exit.png", new Color(0.35f, 0.10f, 0.10f), "Close", 12, Colors.White, bold: false),
                1f, 1f, 1f, 1f, -10f, -8f, 60f, 24f, 1f, 1f);
            CloseButton.Pressed += close;
            Root.AddChild(CloseButton);

            Cap = Ugui.Place(Ugui.Text("Cap", "", 12, ManagementScreen.DimInk), 0f, 1f, 1f, 1f, 0f, -38f, -24f, 18f, 0.5f, 1f);
            Root.AddChild(Cap);

            // Insets 8 left and right, listTop above, 52 below: Place's centre-pivot form.
            float height = -(listTop + 52f);
            Scroll = Ugui.Place(new ScrollContainer { Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled },
                0f, 0f, 1f, 1f, 0f, (52f - listTop) / 2f, -16f, height, 0.5f, 0.5f);
            Root.AddChild(Scroll);
            var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            {
                margin.AddThemeConstantOverride(side, 4);
            }
            Scroll.AddChild(margin);
            Content = new VBoxContainer { Name = "Content", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Content.AddThemeConstantOverride("separation", 2);
            margin.AddChild(Content);
        }

        /// <summary>Forces the next <see cref="Render"/> to rebuild.</summary>
        public void Invalidate() => _signature = null;

        /// <summary>Redraws the list, only when what it shows has changed (golems keep filling a crate).</summary>
        public void Render(List<CrateScreenRow> rows)
        {
            string signature = Signature(rows);
            if (signature == _signature)
            {
                return;
            }
            _signature = signature;
            _rows.Clear();
            foreach (Node child in Content.GetChildren())
            {
                Content.RemoveChild(child);
                child.QueueFree();
            }
            foreach (CrateScreenRow row in rows)
            {
                Content.AddChild(row.IsMessage ? MessageRow(row.Name) : ItemRow(row));
            }
        }

        private static string Signature(List<CrateScreenRow> rows)
        {
            var b = new StringBuilder();
            foreach (CrateScreenRow r in rows)
            {
                b.Append(r.ItemType).Append('|').Append(r.Name).Append('|').Append(r.QuantityText).Append('|')
                 .Append(r.CanMove).Append('|').Append(r.Reason).Append('\n');
            }
            return b.ToString();
        }

        private Control ItemRow(CrateScreenRow row)
        {
            var button = new Button { Name = "Row", CustomMinimumSize = new Vector2(0f, 30f), FocusMode = Control.FocusModeEnum.None };
            button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = ManagementScreen.RowTint });
            button.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = _click != null ? RowHover : ManagementScreen.RowTint });
            button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = _click != null ? RowPressed : ManagementScreen.RowTint });
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            if (_click == null)
            {
                button.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            else
            {
                string good = row.ItemType;
                button.GuiInput += e =>
                {
                    if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
                    {
                        _click(good, press.ShiftPressed);
                    }
                };
            }

            HBoxContainer line = ManagementScreen.RowLine(button);
            line.MouseFilter = Control.MouseFilterEnum.Ignore;
            var icon = new TextureRect
            {
                Name = "Icon",
                CustomMinimumSize = new Vector2(24f, 24f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            string path = ItemIcons.PathFor(row.ItemType);
            if (ResourceLoader.Exists(path))
            {
                icon.Texture = GD.Load<Texture2D>(path);
            }
            line.AddChild(icon);
            Control name = ManagementScreen.Flex(Ugui.Text("Name", row.Name, 13, ManagementScreen.ItemNameInk));
            line.AddChild(name);
            line.AddChild(ManagementScreen.Bar(row.Bar));
            line.AddChild(ManagementScreen.Fixed(Ugui.Text("Quantity", row.QuantityText, 13,
                row.Bar.IsFull ? ManagementScreen.FullFill : ManagementScreen.QuantityInk, align: 4, bold: true), 60f));

            // A good that can't move still takes the click, which says why where the player looks.
            if (!row.CanMove)
            {
                icon.Modulate = new Color(1f, 1f, 1f, DimAlpha);
                name.Modulate = new Color(1f, 1f, 1f, DimAlpha);
                button.TooltipText = row.Reason ?? "";
            }
            _rows[row.ItemType] = button;
            return button;
        }

        private static Control MessageRow(string text)
        {
            var plate = new Panel { Name = "Message", CustomMinimumSize = new Vector2(0f, 30f), MouseFilter = Control.MouseFilterEnum.Ignore };
            plate.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            ManagementScreen.RowLine(plate).AddChild(ManagementScreen.Flex(Ugui.Text("Label", text, 12, ManagementScreen.DimInk)));
            return plate;
        }
    }
}
