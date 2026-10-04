using System.Collections.Generic;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Player;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The build menu: Unity's BuildMenuPanel, recreated with the same art and measurements.
    ///
    /// <para>
    /// An iron panel bottom-left (16,16 from the corner, 280 wide), the title "Build", one brass
    /// row per placeable labelled "Name   (cost)", and the wrecking bar's "Demolish   (full
    /// refund)" row last. A row TOGGLES: clicking the one you hold puts it down. The highlight is
    /// POLLED from the controller every frame rather than set on click, because the tool can be
    /// put down several ways (a row, Escape, right-click) -- the bug Unity's poll was added for
    /// was a row left lit after Escape.
    /// </para>
    /// </summary>
    public partial class BuildMenuNode : CanvasLayer
    {
        private const float RowHeight = 30f;
        private const float RowSpacing = 4f;
        private const float PanelWidth = 280f;
        private const float MinPanelHeight = 356f;
        private const float RowsTop = 38f;   // the row container's inset under the title
        private const float RowsBottom = 10f;
        private const float RowsSide = 10f;

        private static readonly Color SelectedRowColor = new Color(0.88f, 0.68f, 0.35f, 1f);
        private static readonly Color UnselectedRowColor = new Color(0.36f, 0.32f, 0.27f, 1f);
        private static readonly Color SelectedLabelColor = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedLabelColor = new Color(0.82f, 0.78f, 0.71f, 1f);
        private static readonly Color TitleColor = new Color(0.96f, 0.86f, 0.65f, 1f);

        private readonly List<(PlaceableBuilding prefab, Button row)> _rows = new List<(PlaceableBuilding, Button)>();
        private Button _demolishRow;
        private BuildModeController _build;
        private StyleBoxTexture _rowStyle;
        private StyleBoxTexture _rowActiveStyle;
        private FontVariation _bold;

        /// <summary>The panel, for a scenario to find a row's screen position.</summary>
        public Control Panel { get; private set; }

        public IReadOnlyList<(PlaceableBuilding prefab, Button row)> Rows => _rows;
        public Button DemolishRow => _demolishRow;

        public override void _Ready()
        {
            WorldNode world = WorldNode.Find(this);
            _build = world.Sandbox.Build;

            _rowStyle = NineSlice("res://art/UI/Steampunk/steampunk_button_blank.png", 8, 8, 8, 8);
            _rowActiveStyle = NineSlice("res://art/UI/Steampunk/steampunk_button_blank_iron.png", 8, 8, 8, 8);

            int rowCount = world.Sandbox.Placeables.Count + 1;
            float height = Mathf.Max(MinPanelHeight, rowCount * RowHeight + (rowCount - 1) * RowSpacing + RowsTop + RowsBottom);

            // Unity's spriteBorder is (left, bottom, right, top).
            var panel = new Panel { Name = "BuildMenu", Size = new Vector2(PanelWidth, height) };
            panel.AddThemeStyleboxOverride("panel", NineSlice("res://art/UI/Steampunk/steampunk_panel_iron_bolt.png", 12, 10, 12, 10));
            panel.SelfModulate = new Color(1f, 1f, 1f, 0.96f);
            AddChild(panel);
            Panel = panel;
            PlaceBottomLeft();
            GetViewport().SizeChanged += PlaceBottomLeft;
            // The project font (gui/theme/custom_font), emboldened: Unity's FontStyles.Bold.
            _bold = new FontVariation { BaseFont = panel.GetThemeFont("font", "Label"), VariationEmbolden = 0.8f };

            var title = new Label { Text = "Build", Position = new Vector2(RowsSide, 8f), Size = new Vector2(PanelWidth - 2 * RowsSide, 26f) };
            title.VerticalAlignment = VerticalAlignment.Center;
            title.AddThemeFontOverride("font", _bold);
            title.AddThemeFontSizeOverride("font_size", 16);
            title.AddThemeColorOverride("font_color", TitleColor);
            panel.AddChild(title);

            var list = new VBoxContainer
            {
                Position = new Vector2(RowsSide, RowsTop),
                Size = new Vector2(PanelWidth - 2 * RowsSide, height - RowsTop - RowsBottom),
            };
            list.AddThemeConstantOverride("separation", (int)RowSpacing);
            panel.AddChild(list);

            foreach (PlaceableEntry entry in world.Sandbox.Placeables)
            {
                PlaceableBuilding prefab = entry.Prefab;
                Button row = MakeRow(entry.DisplayName + "   (" + ConstructionCostPolicy.FormatCost(prefab.Cost) + ")");
                // TOGGLE, not just select: the row you hold puts the placeable down.
                row.Pressed += () =>
                {
                    if (_build.ActivePrefab == prefab)
                    {
                        _build.CancelPlacement();
                    }
                    else
                    {
                        _build.SetActivePrefab(prefab);
                    }
                };
                list.AddChild(row);
                _rows.Add((prefab, row));
            }

            // Last, under the things it undoes: the one tool here that costs nothing.
            _demolishRow = MakeRow("Demolish   (full refund)");
            _demolishRow.Pressed += () =>
            {
                if (_build.IsDemolishActive)
                {
                    _build.CancelPlacement();
                }
                else
                {
                    _build.EnterDemolishMode();
                }
            };
            list.AddChild(_demolishRow);
        }

        public override void _Process(double delta)
        {
            foreach ((PlaceableBuilding prefab, Button row) in _rows)
            {
                Highlight(row, _build.ActivePrefab == prefab);
            }
            Highlight(_demolishRow, _build.IsDemolishActive);
        }

        private void PlaceBottomLeft()
        {
            Panel.Position = new Vector2(16f, GetViewport().GetVisibleRect().Size.Y - 16f - Panel.Size.Y);
        }

        private Button MakeRow(string text)
        {
            var row = new Button
            {
                CustomMinimumSize = new Vector2(0f, RowHeight),
                FocusMode = Control.FocusModeEnum.None,
            };
            // The caption is a CHILD, as in Unity (an Image row with a TMP child): the row's
            // brightness tint is SelfModulate, which would darken the button's own text too and
            // leave dark-brown words on a dark-brown plate.
            var label = new Label
            {
                Name = "Label",
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            label.AddThemeFontSizeOverride("font_size", 11);
            row.AddChild(label);
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
            row.SelfModulate = active ? SelectedRowColor : UnselectedRowColor;
            Label label = row.GetNode<Label>("Label");
            label.AddThemeColorOverride("font_color", active ? SelectedLabelColor : UnselectedLabelColor);
            if (active)
            {
                label.AddThemeFontOverride("font", _bold);
            }
            else
            {
                label.RemoveThemeFontOverride("font");
            }
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
