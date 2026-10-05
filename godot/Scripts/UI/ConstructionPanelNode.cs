using System.Collections.Generic;
using System.Text;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// "Construct Golem": Unity's GolemConstructionPanel, opened by [E] at a station.
    ///
    /// <para>
    /// A dimmed backdrop and a 560-wide iron window: title, subtitle, the stockpile line, one
    /// 52px row per chassis in the station's roster (portrait, name, tier and slots or the
    /// shortfall, cost on the right), a status line and Close. An unaffordable row is greyed and
    /// will not click; a refusal says why (the station's own reason first -- bay full, steam --
    /// then the shortfall). Rows re-render only when the costs-versus-stock signature changes,
    /// Unity's rule, so a hover is not torn down every frame.
    /// </para>
    ///
    /// <para>
    /// After a build Unity opened the Workbench on the new golem. The Workbench is G7; until then
    /// the panel closes and the golem stands at the station's door, idle, to be carried with [G].
    /// </para>
    /// </summary>
    public partial class ConstructionPanelNode : CanvasLayer, IConstructionScreen
    {
        private static readonly Color WindowColor = new Color(0.13f, 0.11f, 0.09f, 0.99f);
        private static readonly Color BackdropColor = new Color(0.04f, 0.03f, 0.02f, 0.78f);
        private static readonly Color TitleColor = new Color(0.90f, 0.72f, 0.36f, 1f);
        private static readonly Color SubtitleColor = new Color(0.62f, 0.58f, 0.52f, 1f);
        private static readonly Color StockColor = new Color(0.92f, 0.88f, 0.80f, 1f);
        private static readonly Color RowAffordableTint = new Color(0.86f, 0.66f, 0.34f, 0.20f);
        private static readonly Color RowUnaffordableTint = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color NameAffordableColor = new Color(1f, 0.96f, 0.88f, 1f);
        private static readonly Color NameUnaffordableColor = new Color(0.56f, 0.57f, 0.58f, 1f);
        private static readonly Color CostAffordableColor = new Color(0.94f, 0.78f, 0.44f, 1f);
        private static readonly Color CostUnaffordableColor = new Color(0.52f, 0.55f, 0.58f, 1f);
        private static readonly Color StatusColor = new Color(0.94f, 0.78f, 0.44f, 1f);
        private const float WindowWidth = 560f;
        private const float RowHeight = 52f;
        private const float PortraitSize = 40f;
        private const float CostWidth = 176f;

        private readonly List<(ChassisDefinition chassis, Button row)> _rows = new List<(ChassisDefinition, Button)>();
        private GolemConstructionStation _station;
        private string _statusMessage = "";
        private string _renderedSignature;
        private Control _root;
        private PanelContainer _window;
        private VBoxContainer _rowContainer;
        private Label _stockLabel;
        private Label _statusLabel;
        private FontVariation _bold;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<(ChassisDefinition chassis, Button row)> Rows => _rows;
        public string Status => _statusLabel.Text;

        /// <summary>The golem the last successful build made, for a scenario.</summary>
        public GolemEntity LastBuilt { get; private set; }

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 40; // Unity's sortingOrder 40: above the HUD and the build menu.
            Build();
            WorldNode.Find(this).Sandbox.ConfigureScreens(this, null, null);
            _root.Visible = false;
        }

        public void Open(GolemConstructionStation station)
        {
            _station = station;
            _statusMessage = "";
            _renderedSignature = null;
            IsOpen = true;
            _root.Visible = true;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            _statusMessage = "";
            _root.Visible = false;
        }

        public override void _Process(double delta)
        {
            if (IsOpen)
            {
                Refresh();
            }
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (IsOpen && (e.IsActionPressed(BuildCursorNode.CancelAction) || e.IsActionPressed(PlayerNode.Interact)))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>Builds <paramref name="chassis"/> at the open station. What a row's click does.</summary>
        public bool TryConstruct(ChassisDefinition chassis)
        {
            if (_station == null || chassis == null)
            {
                _statusMessage = "No construction station selected.";
                return false;
            }

            if (!_station.TryConstructGolem(chassis, out GolemEntity golem))
            {
                if (!string.IsNullOrEmpty(_station.LastRefusalReason))
                {
                    _statusMessage = _station.LastRefusalReason;
                    return false;
                }
                string shortfall = ConstructionCostPolicy.FormatShortfall(_station.StockOf, chassis.cost);
                _statusMessage = string.IsNullOrEmpty(shortfall)
                    ? "Could not build " + chassis.name + "."
                    : shortfall + " to build " + chassis.name + ".";
                return false;
            }

            LastBuilt = golem;
            Close(); // the Workbench would open here (G7)
            return true;
        }

        private void Refresh()
        {
            _statusLabel.Text = _statusMessage ?? "";
            if (_station == null)
            {
                _stockLabel.Text = "No construction station selected.";
                return;
            }

            bool hasStock = _station.TryGetStockpile(out int scrap, out int brass);
            _stockLabel.Text = hasStock
                ? "Stockpile:  " + scrap + " Scrap    " + brass + " Brass"
                : "Stockpile:  empty -- harvest a resource node to start one.";
            _stockLabel.AddThemeColorOverride("font_color", hasStock ? StockColor : SubtitleColor);

            string signature = Signature();
            if (signature == _renderedSignature)
            {
                return;
            }
            _renderedSignature = signature;

            foreach (Node child in _rowContainer.GetChildren())
            {
                child.QueueFree();
            }
            _rows.Clear();

            ChassisDefinition[] roster = _station.ChassisRoster;
            if (roster == null || roster.Length == 0)
            {
                _rowContainer.AddChild(MakeLabel("This station has no chassis roster assigned.", SubtitleColor, 13));
                return;
            }
            foreach (ChassisDefinition chassis in roster)
            {
                if (chassis != null)
                {
                    _rowContainer.AddChild(MakeRow(chassis));
                }
            }
        }

        private string Signature()
        {
            var b = new StringBuilder();
            ChassisDefinition[] roster = _station.ChassisRoster;
            b.Append(roster?.Length ?? 0);
            foreach (ChassisDefinition chassis in roster ?? new ChassisDefinition[0])
            {
                foreach (RecipeIngredient c in chassis?.cost ?? new List<RecipeIngredient>())
                {
                    b.Append('|').Append(c.itemType).Append(':').Append(_station.StockOf(c.itemType));
                }
            }
            return b.ToString();
        }

        private Button MakeRow(ChassisDefinition chassis)
        {
            bool affordable = ConstructionCostPolicy.CanAfford(_station.StockOf, chassis.cost);
            var row = new Button
            {
                Name = "Chassis_" + chassis.name,
                CustomMinimumSize = new Vector2(0f, RowHeight),
                Disabled = !affordable,
                FocusMode = Control.FocusModeEnum.None,
            };
            var tint = new StyleBoxFlat { BgColor = affordable ? RowAffordableTint : RowUnaffordableTint };
            var hover = new StyleBoxFlat { BgColor = affordable ? RowAffordableTint.Lightened(0.25f) with { A = 0.32f } : RowUnaffordableTint };
            foreach (string state in new[] { "normal", "disabled", "focus" })
            {
                row.AddThemeStyleboxOverride(state, tint);
            }
            row.AddThemeStyleboxOverride("hover", hover);
            row.AddThemeStyleboxOverride("pressed", hover);
            ChassisDefinition captured = chassis;
            row.Pressed += () => TryConstruct(captured);

            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            line.OffsetLeft = 10f;
            line.OffsetRight = -12f;
            line.AddThemeConstantOverride("separation", 10);
            row.AddChild(line);

            var portrait = new TextureRect
            {
                CustomMinimumSize = new Vector2(PortraitSize, PortraitSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = affordable ? Colors.White : new Color(0.45f, 0.47f, 0.50f, 0.85f),
            };
            // The converter wrote Unity's sprite reference as the PNG's file name.
            string sprite = chassis.chassisSprite ?? "";
            string path = "res://art/" + (sprite.EndsWith(".png") ? sprite : sprite + ".png");
            if (sprite.Length > 0 && ResourceLoader.Exists(path))
            {
                portrait.Texture = GD.Load<Texture2D>(path);
            }
            line.AddChild(portrait);

            var text = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            text.AddThemeConstantOverride("separation", 1);
            line.AddChild(text);
            Label name = MakeLabel(chassis.name, affordable ? NameAffordableColor : NameUnaffordableColor, 16);
            name.AddThemeFontOverride("font", _bold);
            text.AddChild(name);
            string detail = affordable
                ? "Tier " + chassis.tier + "   -   " + chassis.maxAppendageSlots + " appendage slots"
                : ConstructionCostPolicy.FormatShortfall(_station.StockOf, chassis.cost);
            text.AddChild(MakeLabel(detail, affordable ? SubtitleColor : CostUnaffordableColor, 12));

            Label cost = MakeLabel(ConstructionCostPolicy.FormatCost(chassis.cost), affordable ? CostAffordableColor : CostUnaffordableColor, 14);
            cost.CustomMinimumSize = new Vector2(CostWidth, 0f);
            cost.HorizontalAlignment = HorizontalAlignment.Right;
            cost.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (affordable)
            {
                cost.AddThemeFontOverride("font", _bold);
            }
            line.AddChild(cost);

            _rows.Add((chassis, row));
            return row;
        }

        private void Build()
        {
            _root = new Control { Name = "ConstructionScreen" };
            _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(_root);

            var backdrop = new ColorRect { Color = BackdropColor };
            backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(backdrop);

            var center = new CenterContainer();
            center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(center);

            _window = new PanelContainer { CustomMinimumSize = new Vector2(WindowWidth, 0f) };
            var panelStyle = NineSlice("res://art/UI/Steampunk/iron_panel_nobolt.png", 8);
            panelStyle.ContentMarginLeft = 16;
            panelStyle.ContentMarginRight = 16;
            panelStyle.ContentMarginTop = 14;
            panelStyle.ContentMarginBottom = 14;
            _window.AddThemeStyleboxOverride("panel", panelStyle);
            // Unity multiplied the iron plate by a near-black window colour; SelfModulate tints
            // the plate alone, not the rows inside it.
            _window.SelfModulate = WindowColor;
            center.AddChild(_window);
            _bold = new FontVariation { BaseFont = _window.GetThemeDefaultFont(), VariationEmbolden = 0.7f };

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 6);
            _window.AddChild(column);

            Label title = MakeLabel("Construct Golem", TitleColor, 22);
            title.AddThemeFontOverride("font", _bold);
            column.AddChild(title);
            column.AddChild(MakeLabel(
                "Pick a chassis. It arrives bare -- the Workbench opens next to fit its logic core.", SubtitleColor, 12));
            _stockLabel = MakeLabel("", StockColor, 14);
            _stockLabel.AddThemeFontOverride("font", _bold);
            column.AddChild(_stockLabel);

            _rowContainer = new VBoxContainer();
            _rowContainer.AddThemeConstantOverride("separation", 4);
            column.AddChild(_rowContainer);

            _statusLabel = MakeLabel("", StatusColor, 13);
            column.AddChild(_statusLabel);

            var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0f, 32f), FocusMode = Control.FocusModeEnum.None };
            StyleBoxTexture button = NineSlice("res://art/UI/Steampunk/steampunk_button_blank.png", 8);
            foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            {
                close.AddThemeStyleboxOverride(state, button);
            }
            close.AddThemeFontSizeOverride("font_size", 14);
            close.AddThemeFontOverride("font", _bold);
            close.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.80f, 1f));
            close.Pressed += Close;
            column.AddChild(close);
        }

        private static Label MakeLabel(string text, Color color, int size)
        {
            // Truncated at the END, with an ellipsis, as Unity's TextOverflowModes.Truncate was. A
            // plain ClipText on a right-aligned label cuts the START instead, which turned a long
            // cost into "...+ 20 Iron Plate + 10 Gear" with the first ingredient missing.
            var label = new Label
            {
                Text = text,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", color);
            return label;
        }

        private static StyleBoxTexture NineSlice(string path, int margin) =>
            new StyleBoxTexture
            {
                Texture = GD.Load<Texture2D>(path),
                TextureMarginLeft = margin,
                TextureMarginRight = margin,
                TextureMarginTop = margin,
                TextureMarginBottom = margin,
            };
    }
}
