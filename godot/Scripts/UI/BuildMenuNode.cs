using System.Collections.Generic;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Player;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The build menu: an iron panel bottom-left, one brass row per placeable and the wrecking
    /// bar's Demolish row last.
    ///
    /// <para>
    /// <b>Compact since G10</b> (from playtest: "the side bar for building is big. Could you make
    /// it smaller? Maybe have cost appear when hovering over? Also, add numbers as quick select?").
    /// A row is its hotkey and a readable name (<see cref="BuildMenuLabels"/>); the cost, with any
    /// shortfall, and a line on what the building is for are in a hover card beside the menu. The
    /// keys are 1-9, 0, - and = in row order, and X for Demolish. A key does exactly what clicking
    /// its row does, so it TOGGLES too.
    /// </para>
    ///
    /// <para>
    /// A row toggles: clicking the one you hold puts it down. The highlight is POLLED from the
    /// controller every frame rather than set on click, because the tool can be put down several
    /// ways (a row, a key, Escape, right-click) -- the bug Unity's poll was added for was a row
    /// left lit after Escape.
    /// </para>
    /// </summary>
    public partial class BuildMenuNode : CanvasLayer
    {
        private const float RowHeight = 24f;
        private const float RowSpacing = 3f;
        private const float PanelWidth = 176f;
        private const float RowsTop = 34f;   // the row container's inset under the title
        private const float RowsBottom = 9f;
        private const float RowsSide = 9f;
        private const float CardWidth = 270f;

        private static readonly Color SelectedRowColor = new Color(0.88f, 0.68f, 0.35f, 1f);
        private static readonly Color UnselectedRowColor = new Color(0.36f, 0.32f, 0.27f, 1f);
        private static readonly Color SelectedLabelColor = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedLabelColor = new Color(0.82f, 0.78f, 0.71f, 1f);
        private static readonly Color KeyColor = new Color(0.98f, 0.80f, 0.42f, 1f);
        private static readonly Color TitleColor = new Color(0.96f, 0.86f, 0.65f, 1f);
        private static readonly Color DimColor = new Color(0.66f, 0.62f, 0.55f, 1f);
        private static readonly Color AffordColor = new Color(0.98f, 0.80f, 0.42f, 1f);
        private static readonly Color ShortColor = new Color(1f, 0.52f, 0.40f, 1f);

        private readonly List<(PlaceableBuilding prefab, Button row)> _rows = new List<(PlaceableBuilding, Button)>();
        private readonly Dictionary<Button, string> _rowKeys = new Dictionary<Button, string>();
        private Button _demolishRow;
        private BuildModeController _build;
        private World.SandboxWorld _world;
        private StyleBoxTexture _rowStyle;
        private StyleBoxTexture _rowActiveStyle;
        private FontVariation _bold;

        private Panel _card;
        private Label _cardName;
        private Label _cardText;
        private Label _cardCost;
        private Button _hovered;
        private VBoxContainer _cardColumn;

        /// <summary>The panel, for a scenario to find a row's screen position.</summary>
        public Control Panel { get; private set; }

        public IReadOnlyList<(PlaceableBuilding prefab, Button row)> Rows => _rows;
        public Button DemolishRow => _demolishRow;

        /// <summary>The hover card, and what it says, for scenarios.</summary>
        public Control HoverCard => _card;
        public string HoverCardName => _cardName?.Text ?? "";
        public string HoverCardCost => _cardCost?.Text ?? "";

        public override void _Ready()
        {
            WorldNode world = WorldNode.Find(this);
            _world = world.Sandbox;
            _build = world.Sandbox.Build;

            _rowStyle = NineSlice("res://art/UI/Steampunk/steampunk_button_blank.png", 8, 8, 8, 8);
            _rowActiveStyle = NineSlice("res://art/UI/Steampunk/steampunk_button_blank_iron.png", 8, 8, 8, 8);

            int rowCount = world.Sandbox.Placeables.Count + 1;
            float height = rowCount * RowHeight + (rowCount - 1) * RowSpacing + RowsTop + RowsBottom;

            // Unity's spriteBorder is (left, bottom, right, top).
            var panel = new Panel { Name = "BuildMenu", Size = new Vector2(PanelWidth, height) };
            panel.AddThemeStyleboxOverride("panel", NineSlice("res://art/UI/Steampunk/steampunk_panel_iron_bolt.png", 12, 10, 12, 10));
            panel.SelfModulate = new Color(1f, 1f, 1f, 0.96f);
            AddChild(panel);
            Panel = panel;
            // The project font (gui/theme/custom_font), emboldened: Unity's FontStyles.Bold.
            _bold = new FontVariation { BaseFont = panel.GetThemeFont("font", "Label"), VariationEmbolden = 0.8f };

            var title = new Label { Text = "Build", Position = new Vector2(RowsSide, 6f), Size = new Vector2(PanelWidth - 2 * RowsSide, 24f) };
            title.VerticalAlignment = VerticalAlignment.Center;
            title.AddThemeFontOverride("font", _bold);
            title.AddThemeFontSizeOverride("font_size", 15);
            title.AddThemeColorOverride("font_color", TitleColor);
            panel.AddChild(title);
            var hint = new Label
            {
                Text = "hover for cost",
                Position = new Vector2(RowsSide, 6f),
                Size = new Vector2(PanelWidth - 2 * RowsSide, 24f),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            hint.AddThemeFontSizeOverride("font_size", 10);
            hint.AddThemeColorOverride("font_color", DimColor);
            panel.AddChild(hint);

            var list = new VBoxContainer
            {
                Position = new Vector2(RowsSide, RowsTop),
                Size = new Vector2(PanelWidth - 2 * RowsSide, height - RowsTop - RowsBottom),
            };
            list.AddThemeConstantOverride("separation", (int)RowSpacing);
            panel.AddChild(list);

            for (int i = 0; i < world.Sandbox.Placeables.Count; i++)
            {
                PlaceableEntry entry = world.Sandbox.Placeables[i];
                PlaceableBuilding prefab = entry.Prefab;
                Button row = MakeRow(BuildMenuLabels.KeyFor(i), BuildMenuLabels.NameFor(entry.Key));
                row.Pressed += () => Toggle(prefab);
                list.AddChild(row);
                _rows.Add((prefab, row));
            }

            // Last, under the things it undoes: the one tool here that costs nothing.
            _demolishRow = MakeRow(BuildMenuLabels.DemolishKey, BuildMenuLabels.DemolishName);
            _demolishRow.Pressed += ToggleDemolish;
            list.AddChild(_demolishRow);

            BuildHoverCard();
            PlaceBottomLeft();
            GetViewport().SizeChanged += PlaceBottomLeft;
        }

        /// <summary>A row click or its key: pick the placeable up, or put it down if held.</summary>
        private void Toggle(PlaceableBuilding prefab)
        {
            if (_build.ActivePrefab == prefab)
            {
                _build.CancelPlacement();
            }
            else
            {
                _build.SetActivePrefab(prefab);
            }
        }

        private void ToggleDemolish()
        {
            if (_build.IsDemolishActive)
            {
                _build.CancelPlacement();
            }
            else
            {
                _build.EnterDemolishMode();
            }
        }

        /// <summary>Whether the menu is drawn: hidden while any full screen is up, as Unity's was.</summary>
        public bool IsBodyVisible => Panel.Visible;

        public override void _UnhandledInput(InputEvent e)
        {
            // The keys belong to the world, not to a full screen (the Workbench and Management
            // have their own uses for a keyboard), so they do nothing while one is open.
            if (e is not InputEventKey { Pressed: true, Echo: false } key || ModalScreens.AnyOpen(GetTree()))
            {
                return;
            }

            string pressed = KeyText(key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode);
            if (pressed == null)
            {
                return;
            }
            if (pressed == BuildMenuLabels.DemolishKey)
            {
                ToggleDemolish();
                GetViewport().SetInputAsHandled();
                return;
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                if (BuildMenuLabels.KeyFor(i) == pressed)
                {
                    Toggle(_rows[i].prefab);
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }

        private static string KeyText(Key key)
        {
            if (key >= Key.Key0 && key <= Key.Key9)
            {
                return ((int)(key - Key.Key0)).ToString();
            }
            switch (key)
            {
                case Key.Minus: return "-";
                case Key.Equal: return "=";
                case Key.X: return BuildMenuLabels.DemolishKey;
                default: return null;
            }
        }

        public override void _Process(double delta)
        {
            // Unity's BuildMenuPanel asked HudScreenPolicy every frame: no world HUD over a
            // full screen. HudScreenExclusivityTests' last case is the `management` scenario's.
            Panel.Visible = !ModalScreens.AnyOpen(GetTree());
            // The row under the mouse, asked of the viewport each frame rather than tracked
            // through enter/exit signals, which a row can miss (headless runs never send them).
            Control under = Panel.Visible ? GetViewport().GuiGetHoveredControl() : null;
            _hovered = under is Button button && _rowKeys.ContainsKey(button) ? button : null;
            foreach ((PlaceableBuilding prefab, Button row) in _rows)
            {
                Highlight(row, _build.ActivePrefab == prefab);
            }
            Highlight(_demolishRow, _build.IsDemolishActive);
            RefreshHoverCard();
        }

        private void PlaceBottomLeft()
        {
            Panel.Position = new Vector2(16f, GetViewport().GetVisibleRect().Size.Y - 16f - Panel.Size.Y);
        }

        private Button MakeRow(string hotkey, string name)
        {
            var row = new Button
            {
                Name = name.Replace(" ", "").Replace("-", "") + "Row",
                CustomMinimumSize = new Vector2(0f, RowHeight),
                FocusMode = Control.FocusModeEnum.None,
            };
            // The captions are CHILDREN, as in Unity (an Image row with a TMP child): the row's
            // brightness tint is SelfModulate, which would darken the button's own text too and
            // leave dark-brown words on a dark-brown plate.
            var keyLabel = new Label
            {
                Name = "Key",
                Text = hotkey,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = new Vector2(4f, 0f),
                Size = new Vector2(18f, RowHeight),
            };
            keyLabel.AddThemeFontOverride("font", _bold);
            keyLabel.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(keyLabel);

            var label = new Label
            {
                Name = "Label",
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            label.OffsetLeft = 26f;
            label.OffsetRight = -6f;
            label.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(label);

            _rowKeys[row] = hotkey;
            Highlight(row, false);
            return row;
        }

        private void Highlight(Button row, bool active)
        {
            StyleBoxTexture style = active ? _rowActiveStyle : _rowStyle;
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            {
                row.AddThemeStyleboxOverride(state, style);
            }
            // The two plates are near-identical brass, so brightness carries "which tool".
            // A hovered row lifts a little, so the card beside it is visibly ITS card.
            row.SelfModulate = active ? SelectedRowColor : row == _hovered ? UnselectedRowColor.Lightened(0.25f) : UnselectedRowColor;
            Label label = row.GetNode<Label>("Label");
            label.AddThemeColorOverride("font_color", active ? SelectedLabelColor : UnselectedLabelColor);
            row.GetNode<Label>("Key").AddThemeColorOverride("font_color", active ? SelectedLabelColor : KeyColor);
            if (active)
            {
                label.AddThemeFontOverride("font", _bold);
            }
            else
            {
                label.RemoveThemeFontOverride("font");
            }
        }

        // --- The hover card ---------------------------------------------------------------------

        private void BuildHoverCard()
        {
            _card = new Panel { Name = "HoverCard", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _card.AddThemeStyleboxOverride("panel", NineSlice("res://art/UI/Steampunk/steampunk_panel_iron_bolt.png", 12, 10, 12, 10));
            _card.SelfModulate = new Color(1f, 1f, 1f, 0.97f);
            AddChild(_card);

            var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _cardColumn = column;
            column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            column.OffsetLeft = 12f;
            column.OffsetRight = -12f;
            column.OffsetTop = 8f;
            column.OffsetBottom = -8f;
            column.AddThemeConstantOverride("separation", 2);
            _card.AddChild(column);

            _cardName = new Label { Name = "Name", MouseFilter = Control.MouseFilterEnum.Ignore };
            _cardName.AddThemeFontOverride("font", _bold);
            _cardName.AddThemeFontSizeOverride("font_size", 14);
            _cardName.AddThemeColorOverride("font_color", TitleColor);
            _cardText = new Label { Name = "Text", MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _cardText.AddThemeFontSizeOverride("font_size", 12);
            _cardText.AddThemeColorOverride("font_color", UnselectedLabelColor);
            _cardCost = new Label { Name = "Cost", MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _cardCost.AddThemeFontOverride("font", _bold);
            _cardCost.AddThemeFontSizeOverride("font_size", 12);
            // Wrapped labels measure against a width they do not yet have when the card is first
            // laid out; with none they wrap every word and report a card taller than the screen.
            _cardText.CustomMinimumSize = new Vector2(CardWidth - 24f, 0f);
            _cardCost.CustomMinimumSize = new Vector2(CardWidth - 24f, 0f);
            column.AddChild(_cardName);
            column.AddChild(_cardText);
            column.AddChild(_cardCost);
        }

        /// <summary>
        /// Refreshed every frame while a row is hovered, so the cost turns from red to amber the
        /// moment the stockpile covers it -- not only when the mouse moves.
        /// </summary>
        private void RefreshHoverCard()
        {
            if (_hovered == null || !Panel.Visible || !GodotObject.IsInstanceValid(_hovered))
            {
                _card.Visible = false;
                return;
            }

            string key = _rowKeys.TryGetValue(_hovered, out string k) ? k : "";
            if (_hovered == _demolishRow)
            {
                _cardName.Text = $"{BuildMenuLabels.DemolishName}   [{key}]";
                _cardText.Text = BuildMenuLabels.DemolishDescription;
                _cardCost.Text = "Full refund";
                _cardCost.AddThemeColorOverride("font_color", AffordColor);
            }
            else
            {
                PlaceableEntry entry = null;
                for (int i = 0; i < _rows.Count; i++)
                {
                    if (_rows[i].row == _hovered)
                    {
                        entry = _world.Placeables[i];
                        break;
                    }
                }
                if (entry == null)
                {
                    _card.Visible = false;
                    return;
                }
                _cardName.Text = $"{BuildMenuLabels.NameFor(entry.Key)}   [{key}]";
                _cardText.Text = BuildMenuLabels.DescriptionFor(entry.Key);
                _cardCost.Text = BuildMenuLabels.CostLine(
                    entry.Prefab.Cost, item => _world.Buffers.GetQuantity(_world.StockpileBufferId, item), out bool affordable);
                _cardCost.AddThemeColorOverride("font_color", affordable ? AffordColor : ShortColor);
            }

            // Beside the menu, level with the hovered row, kept on screen.
            // A Panel does not grow to its children: size it from the text column (whose wrapped
            // lines settle a frame after the text changes -- a one-frame lag nobody sees).
            _card.Size = new Vector2(CardWidth, Mathf.Max(_cardColumn.GetCombinedMinimumSize().Y + 16f, 56f));
            Rect2 row = _hovered.GetGlobalRect();
            float bottom = Mathf.Max(8f, GetViewport().GetVisibleRect().Size.Y - 8f - _card.Size.Y);
            float y = Mathf.Clamp(row.Position.Y + row.Size.Y / 2f - _card.Size.Y / 2f, 8f, bottom);
            _card.Position = new Vector2(Panel.Position.X + Panel.Size.X + 6f, y);
            _card.Visible = true;
        }

        private static StyleBoxTexture NineSlice(string path, int left, int bottom, int right, int top) =>
            new StyleBoxTexture
            {
                Texture = GD.Load<Texture2D>(path),
                TextureMarginLeft = left,
                TextureMarginBottom = bottom,
                TextureMarginRight = right,
                TextureMarginTop = top,
            };
    }
}
