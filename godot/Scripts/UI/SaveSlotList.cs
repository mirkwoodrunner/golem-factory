using System;
using System.Collections.Generic;
using Godot;
using GolemFactory.Menus;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The save slots drawn as rows (docs/menus-design.md §7), for Management's Save / Load tab and
    /// the title's Load: each row a picture, the slot's name and three lines, and its buttons.
    /// What the rows say and which buttons work are Core's <see cref="SaveSlotBoard"/>; this only
    /// draws them and reports presses.
    /// </summary>
    public sealed class SaveSlotList
    {
        public const float RowHeight = 84f;
        public const float RowGap = 6f;
        private const float PictureWidth = 128f;
        private const float PictureHeight = 72f;
        private const float ButtonWidth = 92f;

        private static readonly Color ButtonTint = new Color(0.80f, 0.62f, 0.32f);
        private static readonly Color ArmedTint = new Color(1f, 0.70f, 0.28f);
        private static readonly Color DeleteTint = new Color(0.55f, 0.22f, 0.18f);
        private static readonly Color DisabledTint = new Color(0.20f, 0.18f, 0.16f, 1f);
        private static readonly Color PictureFrame = new Color(0.03f, 0.025f, 0.02f, 1f);
        private static readonly Color EmptyFrame = new Color(1f, 1f, 1f, 0.025f);
        private static readonly Color LitRow = new Color(0.86f, 0.66f, 0.34f, 0.20f);

        private readonly SaveSlotBoard _board;
        private readonly Action<string, SlotAction> _pressed;
        private readonly Dictionary<(string, SlotAction), Button> _buttons = new Dictionary<(string, SlotAction), Button>();
        private readonly Dictionary<string, Panel> _rows = new Dictionary<string, Panel>();
        private readonly Dictionary<string, string> _shownPicture = new Dictionary<string, string>();

        public SaveSlotList(SaveSlotBoard board, Action<string, SlotAction> pressed)
        {
            _board = board;
            _pressed = pressed;
        }

        /// <summary>The pointer moved onto a row (the title lights it, as its menu does).</summary>
        public Action<string> Hovered { get; set; }

        public IReadOnlyDictionary<(string, SlotAction), Button> Buttons => _buttons;
        public IReadOnlyDictionary<string, Panel> RowControls => _rows;

        public Button Button(string key, SlotAction action) => _buttons.TryGetValue((key, action), out Button b) ? b : null;

        /// <summary>The rows, top to bottom, <paramref name="width"/> wide.</summary>
        public Control Build(float width, IReadOnlyList<SlotAction> actions)
        {
            var column = new VBoxContainer { Name = "Slots", MouseFilter = Control.MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(width, 0f) };
            column.AddThemeConstantOverride("separation", (int)RowGap);
            foreach (string key in SaveSlots.Keys)
            {
                column.AddChild(Row(key, actions));
            }
            Refresh();
            return column;
        }

        private Panel Row(string key, IReadOnlyList<SlotAction> actions)
        {
            var row = new Panel { Name = key, CustomMinimumSize = new Vector2(0f, RowHeight), MouseFilter = Control.MouseFilterEnum.Pass };
            row.MouseEntered += () => Hovered?.Invoke(key);
            _rows[key] = row;

            var frame = Ugui.Rect("PictureFrame", PictureFrame);
            Place(frame, 8f, (RowHeight - PictureHeight) / 2f, PictureWidth, PictureHeight);
            row.AddChild(frame);
            Label none = Ugui.Text("NoPicture", "", 11, ManagementScreen.DimInk, align: 2);
            Ugui.Fill(none);
            frame.AddChild(none);
            var picture = Ugui.Fill(new TextureRect
            {
                Name = "Picture",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            frame.AddChild(picture);

            float textLeft = 8f + PictureWidth + 14f;
            AddLine(row, "Title", 16, ManagementScreen.HeaderInk, true, textLeft, 7f);
            AddLine(row, "Where", 13, ManagementScreen.ItemNameInk, false, textLeft, 29f);
            AddLine(row, "Size", 12, ManagementScreen.DimInk, false, textLeft, 47f);
            AddLine(row, "When", 12, ManagementScreen.DimInk, false, textLeft, 63f);

            var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Pass };
            buttons.AddThemeConstantOverride("separation", 8);
            buttons.AnchorLeft = buttons.AnchorRight = 1f;
            buttons.AnchorTop = buttons.AnchorBottom = 0.5f;
            buttons.OffsetLeft = -(actions.Count * (ButtonWidth + 8f)) - 6f;
            buttons.OffsetRight = -14f;
            buttons.OffsetTop = -15f;
            buttons.OffsetBottom = 15f;
            row.AddChild(buttons);
            foreach (SlotAction action in actions)
            {
                string sprite = action == SlotAction.Delete ? "hud_button_close.png" : action == SlotAction.Save ? "hud_button_accept.png" : "hud_button.png";
                Button button = ManagementScreen.PlateButton(action + "Button", ManagementScreen.Ui + "Hud/" + sprite, ButtonTint, action.ToString(), 13, ManagementScreen.SelectedTabLabel, bold: true);
                button.CustomMinimumSize = new Vector2(ButtonWidth, 30f);
                button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
                SlotAction captured = action;
                button.Pressed += () => _pressed(key, captured);
                button.MouseEntered += () => Hovered?.Invoke(key);
                _buttons[(key, action)] = button;
                buttons.AddChild(button);
            }
            return row;
        }

        private static void AddLine(Control row, string name, int size, Color ink, bool bold, float left, float top)
        {
            Label label = Ugui.Text(name, "", size, ink, bold: bold);
            label.AnchorLeft = 0f;
            label.AnchorRight = 1f;
            label.OffsetLeft = left;
            label.OffsetRight = -320f;
            label.OffsetTop = top;
            label.OffsetBottom = top + size + 6f;
            row.AddChild(label);
        }

        private static void Place(Control control, float x, float y, float width, float height)
        {
            control.AnchorLeft = control.AnchorRight = control.AnchorTop = control.AnchorBottom = 0f;
            control.OffsetLeft = x;
            control.OffsetTop = y;
            control.OffsetRight = x + width;
            control.OffsetBottom = y + height;
        }

        /// <summary>Draws every row as the board has it now: texts, pictures, buttons, the lit row.</summary>
        public void Refresh()
        {
            foreach (SlotRow slot in _board.Rows)
            {
                if (!_rows.TryGetValue(slot.Key, out Panel row))
                {
                    continue;
                }
                bool lit = _board.Mode == SlotBoardMode.Title && slot.Key == _board.Selected;
                row.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = lit ? LitRow : ManagementScreen.RowTint });
                row.GetNode<Label>("Title").Text = slot.Title;
                row.GetNode<Label>("Where").Text = slot.Where;
                row.GetNode<Label>("Where").AddThemeColorOverride("font_color", slot.Empty || !slot.Readable ? ManagementScreen.DimInk : ManagementScreen.ItemNameInk);
                row.GetNode<Label>("Size").Text = slot.Size;
                row.GetNode<Label>("When").Text = slot.When;
                row.GetNode<ColorRect>("PictureFrame").Color = slot.Empty ? EmptyFrame : PictureFrame; // an empty slot has no frame to fill
                ShowPicture(row, slot);
                foreach (SlotAction action in new[] { SlotAction.Save, SlotAction.Load, SlotAction.Delete })
                {
                    if (Button(slot.Key, action) is not Button button)
                    {
                        continue;
                    }
                    bool can = _board.Can(slot.Key, action);
                    bool armed = _board.IsArmed(slot.Key, action);
                    button.Disabled = !can;
                    button.Visible = !(action == SlotAction.Save && SaveSlots.IsAutosave(slot.Key));
                    button.SelfModulate = !can ? DisabledTint : armed ? ArmedTint : action == SlotAction.Delete ? DeleteTint : ButtonTint;
                    Label label = button.GetNode<Label>("Label");
                    label.Text = _board.Label(slot.Key, action);
                    label.AddThemeColorOverride("font_color", !can ? ManagementScreen.DimInk : action == SlotAction.Delete && !armed ? Colors.White : ManagementScreen.SelectedTabLabel);
                }
            }
        }

        private void ShowPicture(Control row, SlotRow slot)
        {
            var picture = row.GetNode<TextureRect>("PictureFrame/Picture");
            var none = row.GetNode<Label>("PictureFrame/NoPicture");
            string path = slot.Thumbnail;
            string stamp = path == null ? "" : path + "@" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            if (!_shownPicture.TryGetValue(slot.Key, out string shown) || shown != stamp)
            {
                _shownPicture[slot.Key] = stamp;
                picture.Texture = SavePaths.Thumbnail(path);
            }
            none.Text = picture.Texture != null ? "" : slot.Empty ? "" : "No picture";
        }
    }
}
