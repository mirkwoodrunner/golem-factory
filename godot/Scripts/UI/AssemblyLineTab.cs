using System.Collections.Generic;
using System.Text;
using Godot;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Management screen's Assembly Line tab: draws Core's <see cref="AssemblyLineBoard"/>
    /// rows (wallet, bays, floor expansion, draft slots, waiting cards) with Unity's
    /// AssemblyLinePanel layout -- 28px rows, a 260px right-aligned cost column, 60-74px brass
    /// buttons disabled when unaffordable -- and the status line under them.
    /// </summary>
    public sealed class AssemblyLineTab : IManagementTab
    {
        private static readonly Color RowInk = new Color(0.88f, 0.84f, 0.76f, 1f);
        private static readonly Color DimInk = new Color(0.55f, 0.52f, 0.47f, 1f);
        private static readonly Color CostInk = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color StatusInk = new Color(0.86f, 0.66f, 0.34f, 1f);
        private static readonly Color RowTint = new Color(1f, 1f, 1f, 0.035f);

        private readonly AssemblyLineBoard _board;
        private VBoxContainer _rows;
        private Label _status;
        private string _rendered;

        public AssemblyLineTab(AssemblyLineBoard board) => _board = board;

        /// <summary>The row controls, for a scenario.</summary>
        public Control RowList => _rows;

        public Control Build()
        {
            var holder = Ugui.Fill(new Control { Name = "AssemblyLine", MouseFilter = Control.MouseFilterEnum.Pass });
            var margin = Ugui.Place(new MarginContainer { Name = "Content", MouseFilter = Control.MouseFilterEnum.Pass }, 0f, 0.15f, 1f, 1f);
            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            {
                margin.AddThemeConstantOverride(side, 8);
            }
            holder.AddChild(margin);
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            margin.AddChild(scroll);
            _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            _rows.AddThemeConstantOverride("separation", 2);
            scroll.AddChild(_rows);
            _status = Ugui.Place(Ugui.Text("StatusText", "", 14, StatusInk), 0f, 0f, 1f, 0.15f);
            _status.VerticalAlignment = VerticalAlignment.Top;
            _status.OffsetLeft = 8f;
            holder.AddChild(_status);
            return holder;
        }

        public void Refresh(ManagementTab active)
        {
            if (active != ManagementTab.AssemblyLine || _rows == null)
            {
                return;
            }
            List<AssemblyLineRow> rows = _board.Rows();
            string signature = Signature(rows) + "|" + _board.Status;
            if (signature == _rendered)
            {
                return;
            }
            _rendered = signature;
            foreach (Node child in _rows.GetChildren())
            {
                child.QueueFree();
            }
            foreach (AssemblyLineRow row in rows)
            {
                _rows.AddChild(RowControl(row));
            }
            _status.Text = _board.Status;
        }

        private static string Signature(List<AssemblyLineRow> rows)
        {
            var b = new StringBuilder();
            foreach (AssemblyLineRow r in rows)
            {
                b.Append(r.Kind).Append(r.Text).Append(r.CostText).Append(r.Affordable).Append('\n');
            }
            return b.ToString();
        }

        private Control RowControl(AssemblyLineRow row)
        {
            var plate = new Panel { Name = row.Kind + "Row", CustomMinimumSize = new Vector2(0f, 28f), MouseFilter = Control.MouseFilterEnum.Pass };
            plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = RowTint });
            var line = Ugui.Fill(new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass });
            line.OffsetLeft = 6f;
            line.OffsetRight = -6f;
            line.AddThemeConstantOverride("separation", row.Kind == AssemblyLineRowKind.Slot ? 12 : 6);
            plate.AddChild(line);

            Color ink = row.Kind == AssemblyLineRowKind.Wallet ? CostInk
                : row.Kind == AssemblyLineRowKind.Bay ? (row.Bright ? CostInk : RowInk)
                : row.Bright ? RowInk : DimInk;
            Label label = Ugui.Text("Label", row.Text, 13, ink);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(label);

            if (!string.IsNullOrEmpty(row.CostText))
            {
                Label cost = Ugui.Text("Cost", row.CostText, 13, row.Affordable && row.ButtonLabel != null ? CostInk : DimInk, align: 4, bold: true);
                cost.CustomMinimumSize = new Vector2(260f, 0f);
                line.AddChild(cost);
            }

            if (row.ButtonLabel != null)
            {
                var button = new Button
                {
                    Name = row.ButtonLabel,
                    CustomMinimumSize = new Vector2(row.ButtonLabel == "Claim" ? 60f : 74f, 24f),
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    FocusMode = Control.FocusModeEnum.None,
                    Disabled = !row.Affordable,
                };
                StyleBoxTexture face = Ugui.NineSlice("res://art/UI/Steampunk/steampunk_button_blank.png", 8);
                foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
                {
                    button.AddThemeStyleboxOverride(state, face);
                }
                button.Modulate = row.Affordable ? Colors.White : new Color(1f, 1f, 1f, 0.5f);
                button.AddChild(Ugui.Fill(Ugui.Text("Label", row.ButtonLabel, 13, Colors.Black, align: 2)));
                int slot = row.SlotIndex;
                switch (row.ButtonLabel)
                {
                    case "Claim":
                        button.Pressed += () => _board.Claim(slot);
                        break;
                    case "Upgrade":
                        button.Pressed += () => _board.UpgradeBays();
                        break;
                    case "Extend":
                        button.Pressed += () => _board.ExtendFloor();
                        break;
                }
                line.AddChild(button);
            }
            return plate;
        }
    }
}
