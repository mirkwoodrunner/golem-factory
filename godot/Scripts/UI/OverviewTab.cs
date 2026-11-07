using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GolemFactory.Golems;
using GolemFactory.Overview;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes
{
    /// <summary>The overview's lower panel: the alerts, or the market (docs/supply-design.md §4).</summary>
    public enum OverviewPanel
    {
        Alerts,
        Market,
    }

    /// <summary>
    /// Management's Overview tab (docs/menus-design.md §8): the factory at a glance. The map of
    /// the workshop and the street, every golem by its mood; what the factory has made or spent
    /// over ten minutes or an hour; and, below, every time a golem stopped or the market's carts
    /// with their stock and flow (docs/supply-design.md §4). A golem picked on the map or in the
    /// alerts takes the camera to it, and the screen closes so the player can see it. The rules
    /// are Core's (<see cref="FactoryMap"/>, <see cref="ProductionChart"/>, <see cref="AlertLog"/>,
    /// <see cref="MarketReadout"/>); this draws them and feeds clicks.
    /// </summary>
    public sealed partial class OverviewTab : IManagementTab
    {
        public const float RefreshSeconds = 0.5f;

        /// <summary>The graph's line colours, validated as a set against the panel (§8).</summary>
        public static readonly Color[] LineColours =
        {
            new Color("#3987e5"), new Color("#d95926"), new Color("#199e70"), new Color("#c98500"),
        };

        private readonly SandboxWorld _world;
        private readonly Action _close;
        private Control _root;
        private Label _summary;
        private VBoxContainer _legend;
        private VBoxContainer _alerts;
        private VBoxContainer _market;
        private Control _marketHead;
        private Label _productionHeading;
        private Label _panelHeading;
        private string _renderedLegend;
        private string _renderedAlerts;
        private string _renderedMarket;
        private double _sinceRefresh = RefreshSeconds;
        private ulong _lastFrame;
        private readonly Dictionary<HistoryRange, Button> _rangeButtons = new Dictionary<HistoryRange, Button>();
        private readonly Dictionary<ChartMeasure, Button> _measureButtons = new Dictionary<ChartMeasure, Button>();
        private readonly Dictionary<OverviewPanel, Button> _panelButtons = new Dictionary<OverviewPanel, Button>();

        public OverviewTab(SandboxWorld world, Action close)
        {
            _world = world;
            _close = close;
            Chart = new ProductionChart(world.History);
        }

        // --- For scenarios ------------------------------------------------------------------
        public ProductionChart Chart { get; }
        public MapView Map { get; private set; }
        public GraphView Graph { get; private set; }
        public string Summary => _summary?.Text ?? "";

        /// <summary>The running totals under the map, as drawn (docs/endless-design.md §3).</summary>
        public string Totals => _totals?.Text ?? "";

        private Label _totals;
        public IReadOnlyDictionary<HistoryRange, Button> RangeButtons => _rangeButtons;
        public IReadOnlyDictionary<ChartMeasure, Button> MeasureButtons => _measureButtons;
        public IReadOnlyDictionary<OverviewPanel, Button> PanelButtons => _panelButtons;
        public OverviewPanel Panel { get; private set; } = OverviewPanel.Alerts;
        public string ProductionHeading => _productionHeading?.Text ?? "";
        public string PanelHeading => _panelHeading?.Text ?? "";

        /// <summary>The legend's rows by good, the alerts' rows newest first, and the market's rows west to east, as drawn.</summary>
        public Dictionary<string, Button> LegendRows { get; } = new Dictionary<string, Button>();
        public List<Button> AlertRows { get; } = new List<Button>();
        public List<Control> MarketRows { get; } = new List<Control>();

        public Control Build()
        {
            _root = Ugui.Fill(new Control { Name = "Overview", MouseFilter = Control.MouseFilterEnum.Pass });

            Header("MapHeading", "Workshop map", 16f, 10f);
            _summary = Ugui.Text("MapSummary", "", 12, ManagementScreen.DimInk);
            TopLeft(_summary, 150f, 12f, 480f, 20f);
            _root.AddChild(_summary);
            Map = new MapView(this) { Name = "Map" };
            // 24 px short of the tab's foot, for the running totals under it (endless-design §3).
            TopLeft(Map, 16f, 36f, 620f, 536f);
            _root.AddChild(Map);
            _totals = Ugui.Text("Totals", "", 12, ManagementScreen.ItemNameInk);
            TopLeft(_totals, 16f, 576f, 620f, 18f);
            _root.AddChild(_totals);

            _productionHeading = Header("ProductionHeading", Chart.Heading, 660f, 10f);
            float x = 1264f - 2f * 84f - 8f - 16f - 2f * 72f - 8f;
            foreach (ChartMeasure measure in new[] { ChartMeasure.Made, ChartMeasure.Spent })
            {
                ChartMeasure captured = measure;
                _measureButtons[measure] = Switch(measure + "Button", ProductionChart.MeasureName(measure), x, 8f, 72f, () => SetMeasure(captured));
                x += 80f;
            }
            x += 16f;
            foreach (HistoryRange range in new[] { HistoryRange.TenMinutes, HistoryRange.Hour })
            {
                HistoryRange captured = range;
                _rangeButtons[range] = Switch(range + "Button", ProductionChart.RangeName(range), x, 8f, 84f, () => SetRange(captured));
                x += 92f;
            }
            Graph = new GraphView(this) { Name = "Graph" };
            TopLeft(Graph, 660f, 38f, 390f, 262f);
            _root.AddChild(Graph);
            // The legend's two columns, named once above them: it is the graph's table view.
            ColumnHead("MadeHead", "made", 1062f + 202f - 6f - LegendColumn * 2f - 6f, 38f, LegendColumn);
            ColumnHead("SpentHead", "spent", 1062f + 202f - 6f - LegendColumn, 38f, LegendColumn);
            _legend = List("Legend", 1062f, 56f, 202f, 244f);

            _panelHeading = Header("PanelHeading", "", 660f, 316f);
            x = 1264f - 2f * 84f - 8f;
            foreach (OverviewPanel panel in new[] { OverviewPanel.Alerts, OverviewPanel.Market })
            {
                OverviewPanel captured = panel;
                _panelButtons[panel] = Switch(panel + "Button", panel.ToString(), x, 314f, 84f, () => ShowPanel(captured));
                x += 92f;
            }
            _alerts = List("Alerts", 660f, 342f, 604f, 254f);
            _marketHead = MarketLine(null);
            TopLeft(_marketHead, 660f, 342f, 604f, 20f);
            _root.AddChild(_marketHead);
            _market = List("Market", 660f, 364f, 604f, 232f);
            ShowSwitches();
            return _root;
        }

        private const float LegendColumn = 40f;

        public void Refresh(ManagementTab active)
        {
            ulong frame = Engine.GetProcessFrames();
            bool reopened = frame > _lastFrame + 1; // a frame went by without this: the screen was closed
            _lastFrame = frame;
            if (active != ManagementTab.Overview || reopened)
            {
                _sinceRefresh = RefreshSeconds; // fresh the moment it is shown
            }
            if (active != ManagementTab.Overview)
            {
                return;
            }
            _sinceRefresh += _root.GetProcessDeltaTime();
            if (_sinceRefresh < RefreshSeconds)
            {
                return;
            }
            _sinceRefresh = 0;
            RefreshNow();
        }

        /// <summary>Recomposes everything now: the map, the graph, the legend and the alerts.</summary>
        public void RefreshNow()
        {
            Map.Model = FactoryMap.Compose(_world);
            _summary.Text = Map.Model.Summary;
            _totals.Text = _world.RunTotalsLine;
            Map.QueueRedraw();
            Chart.Refresh();
            Graph.QueueRedraw();
            RefreshLegend();
            RefreshAlerts();
            RefreshMarket();
        }

        private void SetRange(HistoryRange range)
        {
            Chart.Range = range;
            ShowSwitches();
            RefreshNow();
        }

        private void SetMeasure(ChartMeasure measure)
        {
            Chart.Measure = measure;
            ShowSwitches();
            RefreshNow();
        }

        /// <summary>Shows the alerts or the market below the graph.</summary>
        public void ShowPanel(OverviewPanel panel)
        {
            Panel = panel;
            ShowSwitches();
            RefreshNow();
        }

        private void ShowSwitches()
        {
            foreach (KeyValuePair<HistoryRange, Button> pair in _rangeButtons)
            {
                Light(pair.Value, pair.Key == Chart.Range);
            }
            foreach (KeyValuePair<ChartMeasure, Button> pair in _measureButtons)
            {
                Light(pair.Value, pair.Key == Chart.Measure);
            }
            foreach (KeyValuePair<OverviewPanel, Button> pair in _panelButtons)
            {
                Light(pair.Value, pair.Key == Panel);
            }
            _productionHeading.Text = Chart.Heading;
            bool market = Panel == OverviewPanel.Market;
            _alerts.GetParent<Control>().Visible = !market;
            _market.GetParent<Control>().Visible = market;
            _marketHead.Visible = market;
        }

        private static void Light(Button button, bool lit)
        {
            button.SelfModulate = lit ? ManagementScreen.SelectedTab : new Color(0.34f, 0.30f, 0.26f, 1f);
            button.GetNode<Label>("Label").AddThemeColorOverride("font_color", lit ? ManagementScreen.SelectedTabLabel : ManagementScreen.ItemNameInk);
        }

        // --- The legend: every good made, its average, and its line -----------------------------

        private void RefreshLegend()
        {
            IReadOnlyList<ChartRow> rows = Chart.Rows();
            // Rebuilt only when the rows themselves change; a rate is written in place, so a
            // row is never swapped out from under a click.
            var signature = new StringBuilder(Chart.Range.ToString()).Append(Chart.Measure).Append('\n');
            foreach (ChartRow row in rows)
            {
                signature.Append(row.ItemType).Append('|').Append(row.Slot).Append('\n');
            }
            if (signature.ToString() == _renderedLegend)
            {
                foreach (ChartRow row in rows)
                {
                    if (LegendRows.TryGetValue(row.ItemType, out Button drawn))
                    {
                        drawn.GetNode<Label>("Line/Made").Text = row.MadeText;
                        drawn.GetNode<Label>("Line/Spent").Text = row.SpentText;
                    }
                }
                return;
            }
            _renderedLegend = signature.ToString();
            Clear(_legend);
            LegendRows.Clear();
            if (rows.Count == 0)
            {
                _legend.AddChild(Message("Nothing made or spent in the last " + ProductionChart.RangeName(Chart.Range) + ". A golem that Assembles shows here."));
                return;
            }
            foreach (ChartRow row in rows)
            {
                Button button = RowButton("Row", 26f);
                HBoxContainer line = ManagementScreen.RowLine(button);
                line.Name = "Line";
                var swatch = new ColorRect
                {
                    Name = "Swatch",
                    Color = row.Shown ? LineColours[row.Slot] : new Color(1f, 1f, 1f, 0.10f),
                    CustomMinimumSize = new Vector2(12f, 12f),
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                line.AddChild(swatch);
                line.AddChild(ManagementScreen.Flex(Ugui.Text("Name", row.Name, 12, row.Shown ? ManagementScreen.ItemNameInk : ManagementScreen.DimInk)));
                bool made = Chart.Measure == ChartMeasure.Made;
                line.AddChild(ManagementScreen.Fixed(Ugui.Text("Made", row.MadeText, 12, made ? ManagementScreen.QuantityInk : ManagementScreen.DimInk, align: 4, bold: made), LegendColumn));
                line.AddChild(ManagementScreen.Fixed(Ugui.Text("Spent", row.SpentText, 12, made ? ManagementScreen.DimInk : ManagementScreen.QuantityInk, align: 4, bold: !made), LegendColumn));
                string good = row.ItemType;
                button.TooltipText = row.Shown ? "Click to hide its line" : "Click to draw its line (four at most)";
                button.Pressed += () =>
                {
                    Chart.Toggle(good);
                    RefreshNow();
                };
                LegendRows[good] = button;
                _legend.AddChild(button);
            }
        }

        // --- The alerts ------------------------------------------------------------------------

        private void RefreshAlerts()
        {
            AlertLog log = _world.Alerts?.Log;
            float now = _world.Alerts?.Seconds ?? 0f;
            IReadOnlyList<AlertEntry> entries = log?.Entries ?? Array.Empty<AlertEntry>();
            // Rebuilt only when an entry comes, goes or ends; the times are written in place, so
            // a row is never swapped out from under a click.
            var signature = new StringBuilder();
            foreach (AlertEntry e in entries)
            {
                signature.Append(e.GolemId).Append('|').Append(e.StartedAt).Append('|').Append(e.Open).Append('\n');
            }
            if (signature.ToString() == _renderedAlerts)
            {
                for (int i = 0; i < entries.Count && i < AlertRows.Count; i++)
                {
                    AlertRows[i].GetNode<Label>("When").Text = WhenLine(entries[i], now);
                }
                return;
            }
            _renderedAlerts = signature.ToString();
            Clear(_alerts);
            AlertRows.Clear();
            if (entries.Count == 0)
            {
                _alerts.AddChild(Message("No golem has stopped yet. When one does, it is listed here, and a click takes you to it."));
                return;
            }
            foreach (AlertEntry entry in entries)
            {
                Button button = RowButton("Alert", 40f);
                var mark = new ColorRect
                {
                    Name = "Mark",
                    // A contract's note is plain ink, never a stop's red or blue (contracts-design §5).
                    Color = entry.IsNote
                        ? ManagementScreen.DimInk
                        : Ugui.ToGodot(GolemMoodPalette.Badge(entry.NoSteam ? GolemMood.Starved : GolemMood.Stalled)),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                TopLeft(mark, 8f, 8f, 6f, 24f);
                button.AddChild(mark);
                Label text = Ugui.Text("Text", entry.Text, 13, entry.Open ? ManagementScreen.ItemNameInk : ManagementScreen.DimInk);
                TopLeft(text, 22f, 3f, 560f, 18f);
                button.AddChild(text);
                Label when = Ugui.Text("When", WhenLine(entry, now), 11,
                    entry.Open ? ManagementScreen.NearFullFill : ManagementScreen.DimInk);
                TopLeft(when, 22f, 21f, 560f, 16f);
                button.AddChild(when);
                string id = entry.GolemId;
                if (!entry.IsNote)
                {
                    button.TooltipText = "Click to look at it";
                    button.Pressed += () => LookAt(id);
                }
                AlertRows.Add(button);
                _alerts.AddChild(button);
            }
        }

        // --- The market ------------------------------------------------------------------------

        private void RefreshMarket()
        {
            if (Panel != OverviewPanel.Market)
            {
                _panelHeading.Text = "Alerts · every time a golem stopped";
                return;
            }
            _panelHeading.Text = "Market · " + MarketReadout.Summary(_world.Supply);
            IReadOnlyList<MarketRow> rows = MarketReadout.Rows(_world.Supply);
            // Rebuilt only when a cart comes, goes or changes its good; the numbers are written in place.
            var signature = new StringBuilder();
            foreach (MarketRow row in rows)
            {
                signature.Append(row.Cell.x).Append(',').Append(row.Cell.y).Append('|').Append(row.ItemType).Append('\n');
            }
            if (signature.ToString() != _renderedMarket)
            {
                _renderedMarket = signature.ToString();
                Clear(_market);
                MarketRows.Clear();
                foreach (MarketRow row in rows)
                {
                    Control line = MarketLine(row);
                    MarketRows.Add(line);
                    _market.AddChild(line);
                }
            }
            for (int i = 0; i < rows.Count && i < MarketRows.Count; i++)
            {
                WriteMarket(MarketRows[i], rows[i]);
            }
        }

        /// <summary>A market row, or with no row the column heads above them.</summary>
        private static Control MarketLine(MarketRow? row)
        {
            var plate = new Panel
            {
                Name = row == null ? "MarketHead" : "Cart",
                CustomMinimumSize = new Vector2(0f, row == null ? 20f : 28f),
                MouseFilter = row == null ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop,
            };
            plate.AddThemeStyleboxOverride("panel", row == null ? new StyleBoxEmpty() : new StyleBoxFlat { BgColor = ManagementScreen.RowTint });
            HBoxContainer line = ManagementScreen.RowLine(plate);
            line.Name = "Line";
            Color head = ManagementScreen.DimInk;
            if (row is MarketRow r)
            {
                var icon = new TextureRect
                {
                    Name = "Icon",
                    Texture = ItemIcons.Load(r.ItemType),
                    CustomMinimumSize = new Vector2(22f, 22f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                line.AddChild(icon);
            }
            else
            {
                line.AddChild(ManagementScreen.Fixed(new Control { Name = "Icon", MouseFilter = Control.MouseFilterEnum.Ignore }, 22f));
            }
            line.AddChild(ManagementScreen.Flex(Ugui.Text("Name", row?.Name ?? "Cart", row == null ? 11 : 13, row == null ? head : ManagementScreen.ItemNameInk)));
            var track = new Control { Name = "Bar", CustomMinimumSize = new Vector2(90f, 8f), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            if (row != null)
            {
                track.AddChild(Ugui.Fill(Ugui.Rect("Track", ManagementScreen.BarTrack)));
                track.AddChild(Ugui.Place(Ugui.Rect("Fill", ManagementScreen.CapacityFill), 0f, 0f, 0f, 1f));
            }
            line.AddChild(track);
            line.AddChild(ManagementScreen.Fixed(Ugui.Text("Holds", row == null ? "holds" : "", row == null ? 11 : 12, row == null ? head : ManagementScreen.QuantityInk, align: 4), 92f));
            line.AddChild(ManagementScreen.Fixed(Ugui.Text("In", row == null ? "in a min" : "", row == null ? 11 : 12, row == null ? head : ManagementScreen.ItemNameInk, align: 4), 64f));
            line.AddChild(ManagementScreen.Fixed(Ugui.Text("Out", row == null ? "out a min" : "", row == null ? 11 : 12, row == null ? head : ManagementScreen.ItemNameInk, align: 4), 64f));
            line.AddChild(ManagementScreen.Fixed(Ugui.Text("State", "", 12, head), 118f));
            return plate;
        }

        private static void WriteMarket(Control plate, MarketRow row)
        {
            plate.GetNode<Label>("Line/Holds").Text = row.Holds;
            plate.GetNode<Label>("Line/In").Text = row.State == MarketReadout.Endless ? "" : ProductionChart.Number(row.In);
            plate.GetNode<Label>("Line/Out").Text = row.State == MarketReadout.Endless ? "" : ProductionChart.Number(row.Out);
            Label state = plate.GetNode<Label>("Line/State");
            state.Text = row.State == MarketReadout.Endless ? "" : row.State;
            // Short is a fact, not an alarm (§4): amber and in words, never red.
            state.AddThemeColorOverride("font_color", row.Short ? ManagementScreen.NearFullFill : ManagementScreen.DimInk);
            Control fill = plate.GetNode<Control>("Line/Bar/Fill");
            fill.AnchorRight = Mathf.Clamp(row.Fill, 0f, 1f);
            fill.OffsetRight = 0f;
            plate.TooltipText = MarketReadout.Tooltip(row);
        }

        /// <summary>"2 min ago · out of steam · stopped for 0:42", or a contract note's.</summary>
        private static string WhenLine(AlertEntry entry, float now) => AlertLog.Line(entry, now);

        /// <summary>Takes the camera to a golem and closes the screen, so the player sees it.</summary>
        public void LookAt(string golemId)
        {
            if (_world.FocusGolem(golemId))
            {
                _close();
            }
        }

        // --- Builders --------------------------------------------------------------------------

        private Label Header(string name, string text, float x, float y)
        {
            Label label = Ugui.Text(name, text, 15, ManagementScreen.HeaderInk, bold: true);
            TopLeft(label, x, y, 420f, 22f);
            _root.AddChild(label);
            return label;
        }

        private void ColumnHead(string name, string text, float x, float y, float width)
        {
            Label label = Ugui.Text(name, text, 11, ManagementScreen.DimInk, align: 4);
            TopLeft(label, x, y, width, 18f);
            _root.AddChild(label);
        }

        /// <summary>One of the tab's switches (made or spent, the range, alerts or market): lit while chosen.</summary>
        private Button Switch(string name, string text, float x, float y, float width, Action pressed)
        {
            Button button = ManagementScreen.PlateButton(name, ManagementScreen.Ui + "Hud/hud_button.png",
                Colors.White, text, 12, ManagementScreen.SelectedTabLabel, bold: true);
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            TopLeft(button, x, y, width, 24f);
            button.Pressed += pressed;
            _root.AddChild(button);
            return button;
        }

        private VBoxContainer List(string name, float x, float y, float width, float height)
        {
            var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            TopLeft(scroll, x, y, width, height);
            _root.AddChild(scroll);
            var list = new VBoxContainer { Name = "Rows", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 2);
            scroll.AddChild(list);
            return list;
        }

        private static Button RowButton(string name, float height)
        {
            var button = new Button
            {
                Name = name,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0f, height),
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = ManagementScreen.RowTint });
            button.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = ManagementScreen.HeaderPlateTint });
            button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = ManagementScreen.HeaderPlateTint });
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            return button;
        }

        private static Label Message(string text)
        {
            Label label = Ugui.Text("Message", text, 12, ManagementScreen.DimInk);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.ClipText = false;
            label.CustomMinimumSize = new Vector2(0f, 40f);
            label.VerticalAlignment = VerticalAlignment.Top;
            return label;
        }

        private static void Clear(Node list)
        {
            foreach (Node child in list.GetChildren())
            {
                list.RemoveChild(child);
                child.QueueFree();
            }
        }

        internal static void TopLeft(Control control, float x, float y, float width, float height)
        {
            control.AnchorLeft = control.AnchorRight = control.AnchorTop = control.AnchorBottom = 0f;
            control.OffsetLeft = x;
            control.OffsetTop = y;
            control.OffsetRight = x + width;
            control.OffsetBottom = y + height;
        }

        // =========================================================================================

        /// <summary>
        /// The map: the floor and the street, what stands on them, the golems by mood (a stopped
        /// one ringed, so it isn't colour alone), and the player. The pointer names what it is
        /// over; a click on a golem looks at it.
        /// </summary>
        public sealed partial class MapView : Control
        {
            private static readonly Color Floor = new Color(0.30f, 0.22f, 0.15f, 1f);
            private static readonly Color Street = new Color(0.20f, 0.20f, 0.21f, 1f);
            private static readonly Color Edge = new Color(0.55f, 0.45f, 0.33f, 1f);
            private static readonly Color Belt = new Color(0.52f, 0.52f, 0.55f, 1f);
            private static readonly Color Crate = new Color(0.80f, 0.62f, 0.32f, 1f);
            private static readonly Color Steam = new Color(0.72f, 0.42f, 0.24f, 1f);
            private static readonly Color Machine = new Color(0.46f, 0.42f, 0.38f, 1f);
            private static readonly Color Station = new Color(0.62f, 0.56f, 0.44f, 1f);
            private static readonly Color Stall = new Color(0.36f, 0.52f, 0.34f, 1f);
            private static readonly Color Tower = new Color(0.86f, 0.70f, 0.40f, 1f);
            private static readonly Color Working = new Color(0.98f, 0.86f, 0.55f, 1f);
            private static readonly Color Ring = new Color(1f, 0.97f, 0.90f, 1f);
            private const float KeyHeight = 26f;

            private readonly OverviewTab _tab;
            private CoreVector2Int? _hover;

            public MapView(OverviewTab tab)
            {
                _tab = tab;
                MouseFilter = MouseFilterEnum.Stop;
                ClipContents = true;
            }

            public FactoryMap Model { get; set; }

            /// <summary>What the pointer is over, as the tooltip says it; "" over nothing.</summary>
            public string HoverText { get; private set; } = "";

            private float CellScale => Model == null ? 1f
                : Mathf.Min(Size.X / Model.Extent.Width, (Size.Y - KeyHeight) / Model.Extent.Height);

            private Vector2 Origin => Model == null ? Vector2.Zero
                : new Vector2((Size.X - Model.Extent.Width * CellScale) / 2f, (Size.Y - KeyHeight - Model.Extent.Height * CellScale) / 2f);

            /// <summary>The top-left corner of a cell, in this control's pixels: +y is north in Core, down here.</summary>
            public Vector2 CellCorner(CoreVector2Int cell) =>
                Origin + new Vector2((cell.x - Model.Extent.X) * CellScale, (Model.Extent.Y + Model.Extent.Height - 1 - cell.y) * CellScale);

            /// <summary>A cell's centre in the viewport's pixels, for a scenario's pointer.</summary>
            public Vector2 GlobalCentreOf(CoreVector2Int cell) => GlobalPosition + CellCorner(cell) + new Vector2(CellScale, CellScale) / 2f;

            private CoreVector2Int? CellAt(Vector2 local)
            {
                if (Model == null || CellScale <= 0f)
                {
                    return null;
                }
                Vector2 p = (local - Origin) / CellScale;
                var cell = new CoreVector2Int(Model.Extent.X + Mathf.FloorToInt(p.X), Model.Extent.Y + Model.Extent.Height - 1 - Mathf.FloorToInt(p.Y));
                return Model.Extent.Contains(cell) ? cell : (CoreVector2Int?)null;
            }

            public override void _GuiInput(InputEvent e)
            {
                if (e is InputEventMouseMotion motion)
                {
                    _hover = CellAt(motion.Position);
                    MapMark? mark = _hover is CoreVector2Int c ? Model?.MarkAt(c) : null;
                    HoverText = mark is MapMark m ? Model.Describe(m) : "";
                    TooltipText = mark?.Kind == MapMarkKind.Golem ? HoverText + "\nClick to look at it" : HoverText;
                    QueueRedraw();
                }
                else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click
                    && CellAt(click.Position) is CoreVector2Int cell && Model?.MarkAt(cell) is MapMark picked
                    && picked.Kind == MapMarkKind.Golem)
                {
                    AcceptEvent();
                    _tab.LookAt(picked.GolemId);
                }
            }

            public override void _Notification(int what)
            {
                if (what == NotificationMouseExit)
                {
                    _hover = null;
                    HoverText = "";
                    QueueRedraw();
                }
            }

            public override void _Draw()
            {
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.08f, 0.065f, 0.05f, 1f));
                if (Model == null)
                {
                    return;
                }
                float s = CellScale;
                DrawBlock(Model.Street, Street, null);
                DrawBlock(Model.Workshop, Floor, Edge);
                foreach (MapMark mark in Model.Marks.Where(m => m.Kind != MapMarkKind.Golem && m.Kind != MapMarkKind.Player))
                {
                    DrawBuilding(mark, s);
                }
                foreach (MapMark mark in Model.Marks.Where(m => m.Kind == MapMarkKind.Golem))
                {
                    DrawGolem(mark, s);
                }
                foreach (MapMark mark in Model.Marks.Where(m => m.Kind == MapMarkKind.Player))
                {
                    Vector2 c = CellCorner(mark.Cell) + new Vector2(s, s) / 2f;
                    float r = s * 0.55f;
                    DrawColoredPolygon(new[] { c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0) }, Colors.White);
                }
                if (_hover is CoreVector2Int h)
                {
                    DrawRect(new Rect2(CellCorner(h), new Vector2(s, s)), new Color(1f, 1f, 1f, 0.8f), false, 1f);
                }
                DrawKey();
            }

            private void DrawBlock(MapRect r, Color fill, Color? edge)
            {
                var rect = new Rect2(CellCorner(new CoreVector2Int(r.X, r.Y + r.Height - 1)), new Vector2(r.Width, r.Height) * CellScale);
                DrawRect(rect, fill);
                if (edge is Color e)
                {
                    DrawRect(rect, e, false, 2f);
                }
            }

            private void DrawBuilding(MapMark mark, float s)
            {
                Vector2 corner = CellCorner(new CoreVector2Int(mark.Cell.x, mark.Cell.y + mark.Size - 1));
                var rect = new Rect2(corner, new Vector2(mark.Size, mark.Size) * s).Grow(-1f);
                switch (mark.Kind)
                {
                    case MapMarkKind.Belt:
                    {
                        DrawRect(rect.Grow(-s * 0.15f), Belt);
                        // A tick on the side the belt carries toward.
                        Vector2 centre = rect.GetCenter();
                        Vector2 dir = mark.Facing switch
                        {
                            Facing.East => Vector2.Right,
                            Facing.West => Vector2.Left,
                            Facing.South => Vector2.Down,
                            _ => Vector2.Up,
                        };
                        DrawLine(centre, centre + dir * s * 0.45f, new Color(0.15f, 0.15f, 0.16f, 1f), 2f);
                        break;
                    }
                    case MapMarkKind.Crate:
                        DrawRect(rect, Crate, false, 2f);
                        break;
                    case MapMarkKind.Steam:
                        DrawRect(rect.Grow(-s * 0.1f), Steam);
                        break;
                    case MapMarkKind.Station:
                        DrawRect(rect, Station);
                        break;
                    case MapMarkKind.Cart:
                        DrawRect(rect, Stall);
                        break;
                    case MapMarkKind.Spot:
                        DrawRect(rect.Grow(-s * 0.15f), Stall with { A = 0.55f }, false, 1f);
                        break;
                    case MapMarkKind.Tower:
                        DrawRect(rect, Tower);
                        DrawRect(rect, Edge, false, 2f);
                        break;
                    default:
                        DrawRect(rect, Machine);
                        break;
                }
            }

            private void DrawGolem(MapMark mark, float s)
            {
                Vector2 c = CellCorner(mark.Cell) + new Vector2(s, s) / 2f;
                Color fill = MoodColour(mark.Mood);
                if (mark.Stopped)
                {
                    DrawCircle(c, s * 0.62f, Ring);
                    DrawCircle(c, s * 0.48f, fill);
                }
                else
                {
                    DrawCircle(c, s * 0.40f, fill);
                }
            }

            public static Color MoodColour(GolemMood mood) => mood switch
            {
                GolemMood.Working or GolemMood.Straining => Working,
                _ => Ugui.ToGodot(GolemMoodPalette.Badge(mood)) with { A = 1f },
            };

            /// <summary>The key along the bottom: every golem colour and the ring, in words.</summary>
            private void DrawKey()
            {
                Font font = GetThemeDefaultFont();
                float y = Size.Y - KeyHeight / 2f;
                float x = 10f;
                foreach ((GolemMood mood, string words, bool ring) in new[]
                {
                    (GolemMood.Working, "working", false),
                    (GolemMood.Waiting, "idle or waiting", false),
                    (GolemMood.Stalled, "stuck", true),
                    (GolemMood.Starved, "out of steam", true),
                })
                {
                    if (ring)
                    {
                        DrawCircle(new Vector2(x + 6f, y), 6.5f, Ring);
                    }
                    DrawCircle(new Vector2(x + 6f, y), ring ? 5f : 5f, MoodColour(mood));
                    DrawString(font, new Vector2(x + 16f, y + 4f), words, HorizontalAlignment.Left, -1, 12, ManagementScreen.ItemNameInk);
                    x += 24f + font.GetStringSize(words, HorizontalAlignment.Left, -1, 12).X;
                }
                Vector2 p = new Vector2(x + 6f, y);
                DrawColoredPolygon(new[] { p + new Vector2(0, -6), p + new Vector2(6, 0), p + new Vector2(0, 6), p + new Vector2(-6, 0) }, Colors.White);
                DrawString(font, new Vector2(x + 16f, y + 4f), "you", HorizontalAlignment.Left, -1, 12, ManagementScreen.ItemNameInk);
            }
        }

        // =========================================================================================

        /// <summary>
        /// The production graph: one 2 px line a good, at most four, each named at its end; a
        /// recessive grid with three values; the time along the bottom. The pointer draws a
        /// crosshair and says each line's rate there.
        /// </summary>
        public sealed partial class GraphView : Control
        {
            private const float Left = 34f;
            private const float Right = 92f;
            private const float Top = 10f;
            private const float Bottom = 22f;
            private static readonly Color Grid = new Color(1f, 1f, 1f, 0.08f);

            private readonly OverviewTab _tab;
            private int _hoverPoint = -1;

            public GraphView(OverviewTab tab)
            {
                _tab = tab;
                MouseFilter = MouseFilterEnum.Stop;
            }

            /// <summary>The point the pointer is over, or -1; what the tooltip says there.</summary>
            public int HoverPoint => _hoverPoint;
            public IReadOnlyList<string> HoverLines => _hoverPoint < 0 ? Array.Empty<string>() : _tab.Chart.HoverLines(_hoverPoint);

            private Rect2 Plot => new Rect2(Left, Top, Size.X - Left - Right, Size.Y - Top - Bottom);

            private float XOf(int point) => Plot.Position.X + Plot.Size.X * point / (ProductionHistory.Points - 1);

            /// <summary>A point's x in the viewport's pixels, for a scenario's pointer.</summary>
            public Vector2 GlobalPointAt(int point) => GlobalPosition + new Vector2(XOf(point), Plot.GetCenter().Y);

            public override void _GuiInput(InputEvent e)
            {
                if (e is InputEventMouseMotion motion)
                {
                    float t = (motion.Position.X - Plot.Position.X) / Plot.Size.X;
                    _hoverPoint = t < -0.02f || t > 1.02f ? -1 : Mathf.Clamp(Mathf.RoundToInt(t * (ProductionHistory.Points - 1)), 0, ProductionHistory.Points - 1);
                    TooltipText = string.Join("\n", HoverLines);
                    QueueRedraw();
                }
            }

            public override void _Notification(int what)
            {
                if (what == NotificationMouseExit)
                {
                    _hoverPoint = -1;
                    QueueRedraw();
                }
            }

            public override void _Draw()
            {
                Font font = GetThemeDefaultFont();
                Rect2 plot = Plot;
                ProductionChart chart = _tab.Chart;
                float top = chart.AxisTop();
                foreach (float value in new[] { 0f, top / 2f, top })
                {
                    float y = plot.End.Y - plot.Size.Y * value / top;
                    DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Grid, 1f);
                    DrawString(font, new Vector2(0f, y + 4f), Number(value), HorizontalAlignment.Right, Left - 6f, 11, ManagementScreen.DimInk);
                }
                DrawString(font, new Vector2(plot.Position.X, Size.Y - 6f), chart.RangeStart, HorizontalAlignment.Left, -1, 11, ManagementScreen.DimInk);
                DrawString(font, new Vector2(plot.End.X - 40f, Size.Y - 6f), "now", HorizontalAlignment.Right, 40f, 11, ManagementScreen.DimInk);

                var ends = new List<(float y, string name, Color colour)>();
                foreach (string good in chart.Shown)
                {
                    Color colour = LineColours[chart.SlotOf(good)];
                    float[] rates = chart.Rates(good);
                    var points = new List<Vector2>();
                    for (int i = 0; i < rates.Length; i++)
                    {
                        if (!float.IsNaN(rates[i]))
                        {
                            points.Add(new Vector2(XOf(i), plot.End.Y - plot.Size.Y * Mathf.Min(rates[i], top) / top));
                        }
                    }
                    if (points.Count >= 2)
                    {
                        DrawPolyline(points.ToArray(), colour, 2f, true);
                    }
                    if (points.Count >= 1)
                    {
                        DrawCircle(points[points.Count - 1], 4f, colour);
                        ends.Add((points[points.Count - 1].Y, Economy.ItemTiers.DisplayName(good), colour));
                    }
                }
                // Each line named at its end, pushed apart so no two names overlap.
                ends.Sort((a, b) => a.y.CompareTo(b.y));
                float last = float.NegativeInfinity;
                foreach ((float y, string name, Color colour) in ends)
                {
                    float at = Mathf.Max(y, last + 14f);
                    last = at;
                    DrawString(font, new Vector2(plot.End.X + 8f, at + 4f), name, HorizontalAlignment.Left, Right - 10f, 11, ManagementScreen.ItemNameInk);
                }
                if (ends.Count == 0)
                {
                    DrawString(font, new Vector2(plot.Position.X, plot.GetCenter().Y), "Nothing to draw yet", HorizontalAlignment.Center, plot.Size.X, 13, ManagementScreen.DimInk);
                }
                if (_hoverPoint >= 0)
                {
                    float x = XOf(_hoverPoint);
                    DrawLine(new Vector2(x, plot.Position.Y), new Vector2(x, plot.End.Y), new Color(1f, 1f, 1f, 0.35f), 1f);
                }
            }

            private static string Number(float value) =>
                value.ToString(value < 10f && value % 1f != 0f ? "0.#" : "0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
