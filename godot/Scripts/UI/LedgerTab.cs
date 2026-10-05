using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Progression;
using GolemFactory.UI;
using CoreRect = GolemFactory.Compat.Rect;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Artificer's Ledger (the Management screen's Ledger tab): Unity's TechTreePanel, the
    /// chart drawn from Core's <see cref="TechTreeCatalog"/>, <see cref="TechTreeChartLayout"/>
    /// and <see cref="TechTreeStatusRules"/> over the generated tt_* chrome (G8).
    ///
    /// <para>
    /// A tiled drafting-paper field; the routes, each coloured by the state of the node it ARRIVES
    /// at (long hauls faded); a brass plate per phase; one clickable plaque per node with its kind
    /// badge, name and authored detail; the legend; and the headline -- researched count and the
    /// next objective. Clicking a plaque opens Core's <see cref="TechTreeReadout"/> in a pane that
    /// lives OUTSIDE the chart, so redrawing the chart never destroys it. States are re-resolved
    /// only when the ledger's version moves. The chart is wider than the screen; drag it to pan.
    /// It is a readout, never a gate.
    /// </para>
    /// </summary>
    public sealed class LedgerTab : IManagementTab
    {
        private const string Art = "res://art/UI/TechTree/";

        private static readonly Color ResearchedName = new Color(0.13f, 0.09f, 0.04f, 1f);
        private static readonly Color ResearchedDetail = new Color(0.32f, 0.23f, 0.11f, 1f);
        private static readonly Color AvailableName = new Color(0.16f, 0.11f, 0.05f, 1f);
        private static readonly Color AvailableDetail = new Color(0.36f, 0.26f, 0.13f, 1f);
        private static readonly Color LockedName = new Color(0.62f, 0.60f, 0.56f, 1f);
        private static readonly Color LockedDetail = new Color(0.45f, 0.44f, 0.41f, 1f);
        private static readonly Color PlannedName = new Color(0.62f, 0.74f, 0.84f, 1f);
        private static readonly Color PlannedDetail = new Color(0.44f, 0.54f, 0.62f, 1f);
        private static readonly Color LineResearched = new Color(0.85f, 0.66f, 0.32f, 0.85f);
        private static readonly Color LineAvailable = new Color(1f, 0.80f, 0.34f, 0.95f);
        private static readonly Color LineLocked = new Color(0.42f, 0.40f, 0.38f, 0.55f);
        private const float LongHaulFade = 0.45f;
        private static readonly Color PhaseTitle = new Color(0.14f, 0.10f, 0.06f, 1f);
        private static readonly Color PhaseGoal = new Color(0.24f, 0.17f, 0.09f, 1f);
        private static readonly Color BadgeInk = new Color(0.22f, 0.15f, 0.07f, 1f);
        private static readonly Color ResearchedPlaqueTint = new Color(0.78f, 0.74f, 0.68f, 1f);
        private static readonly Color LegendCaption = new Color(0.85f, 0.80f, 0.72f, 1f);
        private static readonly Color HeaderInk = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color DetailPaneColor = new Color(0.90f, 0.85f, 0.75f, 0.97f);
        private const float BadgeSize = 16f;

        private sealed class NodeView
        {
            public Button Plaque;
            public TextureRect Badge;
            public Label Name;
            public Label Detail;
        }

        private readonly TechTreeProgressTracker _tracker;
        private readonly TechTreeReadout _readout;
        private readonly List<TechTreeEdge> _edges = new List<TechTreeEdge>();
        private readonly List<ColorRect[]> _edgeViews = new List<ColorRect[]>();
        private NodeView[] _nodes;
        private TechTreeNodeState[] _states;
        private int _appliedVersion = -1;
        private Label _header;
        private ScrollContainer _scroll;
        private Control _chart;
        private Panel _pane;
        private Label _paneTitle;
        private Label _paneBody;
        private bool _panning;

        public LedgerTab(TechTreeProgressTracker tracker, TechTreeReadout readout)
        {
            _tracker = tracker;
            _readout = readout;
        }

        // --- For scenarios ------------------------------------------------------------------
        public Control Chart => _chart;
        public IEnumerable<Button> Plaques => _nodes?.Select(n => n.Plaque) ?? Enumerable.Empty<Button>();
        public string HeaderText => _header?.Text ?? "";
        public string PaneBody => _pane != null && _pane.Visible ? _paneBody.Text : "";
        public TechTreeReadout Readout => _readout;

        /// <summary>
        /// A node's plaque by node id. Not by Godot name: '.' is not allowed in a node name, so
        /// "Node_r4.ironsmelting" is stored as "Node_r4_ironsmelting".
        /// </summary>
        public Button PlaqueFor(string nodeId)
        {
            for (int i = 0; i < TechTreeCatalog.Nodes.Count && _nodes != null; i++)
            {
                if (TechTreeCatalog.Nodes[i].Id == nodeId)
                {
                    return _nodes[i].Plaque;
                }
            }
            return null;
        }

        /// <summary>Redraws the chart from the catalog, keeping the selection (by id) and the pane.</summary>
        public void RebuildChart()
        {
            foreach (Node child in _chart.GetChildren())
            {
                child.QueueFree();
            }
            BuildChart();
            // States straight away: left to the next Refresh, the fresh plaques drew for one
            // frame in Godot's default button style (caught in the rendered frames).
            _appliedVersion = _tracker?.Ledger?.Version ?? 0;
            ApplyStates(_tracker?.Ledger);
        }

        public Control Build()
        {
            var holder = Ugui.Fill(new Control { Name = "Ledger", MouseFilter = Control.MouseFilterEnum.Pass });
            _header = Ugui.Place(Ugui.Text("Header", "THE ARTIFICER'S LEDGER", 14, HeaderInk, bold: true), 0f, 1f, 1f, 1f, 0f, -4f, -16f, 22f, 0.5f, 1f);
            holder.AddChild(_header);

            var legend = Ugui.Place(new Control { Name = "Legend", MouseFilter = Control.MouseFilterEnum.Ignore }, 0f, 1f, 1f, 1f, 0f, -28f, -16f, 20f, 0.5f, 1f);
            holder.AddChild(legend);
            LegendChip(legend, 0, "tt_node_researched.png", "Researched");
            LegendChip(legend, 1, "tt_node_available.png", "Available now");
            LegendChip(legend, 2, "tt_node_locked.png", "Locked");
            LegendChip(legend, 3, "tt_node_planned.png", "Designed, not built");

            var viewport = Ugui.Place(Ugui.Rect("ChartScroll", new Color(0.06f, 0.05f, 0.04f, 1f)), 0f, 0f, 1f, 1f, 0f, -26f, 0f, -52f);
            viewport.MouseFilter = Control.MouseFilterEnum.Pass;
            holder.AddChild(viewport);
            _scroll = Ugui.Fill(new ScrollContainer { Name = "Scroll" });
            viewport.AddChild(_scroll);
            _chart = new Control { Name = "Chart", MouseFilter = Control.MouseFilterEnum.Stop };
            _chart.GuiInput += PanChart;
            _scroll.AddChild(_chart);
            BuildChart();

            // The readout pane: a sibling of the chart, not a child, so a rebuild keeps it.
            _pane = Ugui.Place(Ugui.Image("RecipeReadout", Ugui.NineSlice(Art + "tt_phase_plate.png", 10), DetailPaneColor), 0f, 0f, 0f, 0f, 12f, 12f, 300f, 92f, 0f, 0f);
            _pane.Visible = false;
            viewport.AddChild(_pane);
            _paneTitle = Ugui.Place(Ugui.Text("Title", "", 13, ResearchedName, bold: true), 0f, 0f, 1f, 1f);
            _paneTitle.OffsetLeft = 10f;
            _paneTitle.OffsetTop = 8f;
            _paneTitle.OffsetRight = -10f;
            _paneTitle.OffsetBottom = -68f;
            _paneTitle.VerticalAlignment = VerticalAlignment.Top;
            _pane.AddChild(_paneTitle);
            _paneBody = Ugui.Place(Ugui.Text("Body", "", 10, ResearchedDetail), 0f, 0f, 1f, 1f);
            _paneBody.OffsetLeft = 10f;
            _paneBody.OffsetTop = 28f;
            _paneBody.OffsetRight = -10f;
            _paneBody.OffsetBottom = -8f;
            _paneBody.VerticalAlignment = VerticalAlignment.Top;
            _paneBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _paneBody.ClipText = true;
            _pane.AddChild(_paneBody);
            return holder;
        }

        private void BuildChart()
        {
            IReadOnlyList<TechTreeNode> nodes = TechTreeCatalog.Nodes;
            Compat.Vector2 size = TechTreeChartLayout.ChartSize(TechTreeCatalog.Phases.Count, TechTreeCatalog.MaxRowsInAnyPhase());
            _chart.CustomMinimumSize = new Vector2(size.x, size.y);

            // The graticule is a 32px square, tiled -- stretched it would smear into bands.
            var field = new TextureRect
            {
                Name = "Field", Texture = GD.Load<Texture2D>(Art + "tt_field.png"),
                StretchMode = TextureRect.StretchModeEnum.Tile, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            Ugui.Fill(field);
            _chart.AddChild(field);

            // Routes first, so every card draws over them.
            TechTreeChartLayout.BuildEdges(nodes, _edges);
            _edgeViews.Clear();
            foreach (TechTreeEdge edge in _edges)
            {
                _edgeViews.Add(new[] { Segment(edge.FromSource.Rect), Segment(edge.Lane.Rect), Segment(edge.ToTarget.Rect) });
            }

            for (int p = 0; p < TechTreeCatalog.Phases.Count; p++)
            {
                PhaseHeader(p, TechTreeCatalog.Phases[p]);
            }

            _nodes = new NodeView[nodes.Count];
            _states = new TechTreeNodeState[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                _nodes[i] = NodePlaque(nodes[i]);
            }
        }

        public void Refresh(ManagementTab active)
        {
            if (active != ManagementTab.TechTree || _nodes == null)
            {
                return;
            }
            TechTreeProgressLedger ledger = _tracker?.Ledger;
            int version = ledger?.Version ?? 0;
            if (version != _appliedVersion)
            {
                _appliedVersion = version;
                ApplyStates(ledger);
            }
            RefreshPane();
        }

        private void ApplyStates(TechTreeProgressLedger ledger)
        {
            IReadOnlyList<TechTreeNode> nodes = TechTreeCatalog.Nodes;
            TechTreeStatusRules.Resolve(nodes, ledger, _states);
            for (int i = 0; i < _nodes.Length; i++)
            {
                ApplyNodeState(_nodes[i], nodes[i], _states[i]);
            }
            for (int i = 0; i < _edges.Count; i++)
            {
                // Coloured by where the route ARRIVES: what the player wants lit is the approach
                // to the thing they can do next.
                Color colour = _states[_edges[i].TargetIndex] switch
                {
                    TechTreeNodeState.Researched => LineResearched,
                    TechTreeNodeState.Available => LineAvailable,
                    _ => LineLocked,
                };
                if (_edges[i].IsLongHaul)
                {
                    colour.A *= LongHaulFade;
                }
                foreach (ColorRect segment in _edgeViews[i])
                {
                    segment.Color = colour;
                }
            }
            int researched = TechTreeStatusRules.CountResearched(_states);
            TechTreeNode next = TechTreeStatusRules.FindNextObjective(nodes, _states);
            _header.Text = $"THE ARTIFICER'S LEDGER    {researched} / {nodes.Count} researched    ·    " +
                           $"Next: {(next != null ? next.DisplayName : "everything on the track")}    ·    drag the chart to pan";
        }

        private void ApplyNodeState(NodeView view, TechTreeNode node, TechTreeNodeState state)
        {
            string plaque;
            Color tint = Colors.White;
            Color name;
            Color detail;
            Color badge;
            if (node.IsPlanned)
            {
                bool reachable = state != TechTreeNodeState.Locked;
                plaque = "tt_node_planned.png";
                name = reachable ? PlannedName : Dim(PlannedName);
                detail = reachable ? PlannedDetail : Dim(PlannedDetail);
                badge = name;
                view.Name.Text = node.DisplayName + "  PLANNED";
            }
            else
            {
                switch (state)
                {
                    case TechTreeNodeState.Researched:
                        plaque = "tt_node_researched.png";
                        tint = ResearchedPlaqueTint;
                        name = ResearchedName;
                        detail = ResearchedDetail;
                        badge = BadgeInk;
                        break;
                    case TechTreeNodeState.Available:
                        plaque = "tt_node_available.png";
                        name = AvailableName;
                        detail = AvailableDetail;
                        badge = BadgeInk;
                        break;
                    default:
                        plaque = "tt_node_locked.png";
                        name = LockedName;
                        detail = LockedDetail;
                        badge = new Color(1f, 1f, 1f, 0.55f);
                        break;
                }
                view.Name.Text = node.DisplayName;
            }
            StyleBoxTexture box = Ugui.NineSlice(Art + plaque, 10);
            foreach (string s in new[] { "normal", "hover", "pressed", "focus" })
            {
                view.Plaque.AddThemeStyleboxOverride(s, box);
            }
            view.Plaque.SelfModulate = tint;
            view.Name.AddThemeColorOverride("font_color", name);
            view.Detail.AddThemeColorOverride("font_color", detail);
            view.Badge.Modulate = badge;
        }

        private static Color Dim(Color c) => new Color(c.R * 0.55f, c.G * 0.55f, c.B * 0.55f, c.A);

        private void RefreshPane()
        {
            if (!_readout.HasSelection)
            {
                _pane.Visible = false;
                return;
            }
            _pane.Visible = true;
            _paneTitle.Text = _readout.Title(_states);
            // The body carries a LIVE rate, so it is refreshed every frame, not only on a click.
            _paneBody.Text = _readout.Body;
        }

        // --- Pieces -------------------------------------------------------------------------

        private ColorRect Segment(CoreRect rect)
        {
            var line = new ColorRect
            {
                Name = "Line", Position = new Vector2(rect.x, rect.y), Size = new Vector2(rect.width, rect.height),
                MouseFilter = Control.MouseFilterEnum.Ignore, Color = LineLocked,
            };
            _chart.AddChild(line);
            return line;
        }

        private void PhaseHeader(int index, TechTreePhase phase)
        {
            CoreRect rect = TechTreeChartLayout.PhaseHeaderRect(index);
            Panel plate = Ugui.Image("Phase" + index, Ugui.NineSlice(Art + "tt_phase_plate.png", 10), Colors.White);
            plate.Position = new Vector2(rect.x, rect.y);
            plate.Size = new Vector2(rect.width, rect.height);
            _chart.AddChild(plate);
            Label title = Ugui.Text("Title", phase.Title, 14, PhaseTitle, align: 2, bold: true);
            title.Position = new Vector2(6f, 4f);
            title.Size = new Vector2(rect.width - 12f, rect.height * 0.5f - 4f);
            plate.AddChild(title);
            Label goal = Ugui.Text("Goal", phase.Goal, 9, PhaseGoal, align: 2);
            goal.Position = new Vector2(6f, rect.height * 0.5f);
            goal.Size = new Vector2(rect.width - 12f, rect.height * 0.5f - 3f);
            goal.VerticalAlignment = VerticalAlignment.Top;
            goal.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            plate.AddChild(goal);
        }

        private NodeView NodePlaque(TechTreeNode node)
        {
            CoreRect rect = TechTreeChartLayout.NodeRect(node);
            var plaque = new Button
            {
                Name = "Node_" + node.Id, Position = new Vector2(rect.x, rect.y), Size = new Vector2(rect.width, rect.height),
                FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Stop,
            };
            string id = node.Id;
            plaque.Pressed += () => _readout.Select(id);
            _chart.AddChild(plaque);

            var badge = new TextureRect
            {
                Name = "Badge", Texture = GD.Load<Texture2D>(Art + BadgeFor(node.Kind)),
                Position = new Vector2(9f, (rect.height - BadgeSize) * 0.5f), Size = new Vector2(BadgeSize, BadgeSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            plaque.AddChild(badge);
            float textLeft = BadgeSize + 14f;
            Label name = Ugui.Text("Name", node.DisplayName, 13, LockedName, bold: node.IsKeystone);
            name.Position = new Vector2(textLeft, 6f);
            name.Size = new Vector2(rect.width - textLeft - 8f, rect.height * 0.5f - 6f);
            name.VerticalAlignment = VerticalAlignment.Bottom;
            plaque.AddChild(name);
            Label detail = Ugui.Text("Detail", node.Detail, 10, LockedDetail);
            detail.Position = new Vector2(textLeft, rect.height * 0.5f);
            detail.Size = new Vector2(rect.width - textLeft - 8f, rect.height * 0.5f - 5f);
            detail.VerticalAlignment = VerticalAlignment.Top;
            plaque.AddChild(detail);
            return new NodeView { Plaque = plaque, Badge = badge, Name = name, Detail = detail };
        }

        private static string BadgeFor(TechTreeNodeKind kind) => kind switch
        {
            TechTreeNodeKind.Chassis => "tt_badge_cog.png",
            TechTreeNodeKind.Recipe => "tt_badge_crucible.png",
            TechTreeNodeKind.Building => "tt_badge_anvil.png",
            TechTreeNodeKind.Technique => "tt_badge_hand.png",
            _ => "tt_badge_tower.png",
        };

        private static void LegendChip(Control legend, int index, string sprite, string caption)
        {
            const float chipWidth = 150f;
            var chip = new Control { Name = "Legend" + index, Position = new Vector2(index * chipWidth, 0f), Size = new Vector2(chipWidth, 18f), MouseFilter = Control.MouseFilterEnum.Ignore };
            legend.AddChild(chip);
            Panel swatch = Ugui.Image("Swatch", Ugui.NineSlice(Art + sprite, 10), Colors.White);
            swatch.Position = new Vector2(0f, 1f);
            swatch.Size = new Vector2(22f, 16f);
            chip.AddChild(swatch);
            Label label = Ugui.Text("Label", caption, 11, LegendCaption);
            label.Position = new Vector2(28f, 0f);
            label.Size = new Vector2(chipWidth - 28f, 18f);
            chip.AddChild(label);
        }

        /// <summary>
        /// Drag the chart to pan, as Unity's ScrollRect did: the chart is ~1720px across and has
        /// no visible scrollbars. A press that releases without moving still clicks a plaque.
        /// </summary>
        private void PanChart(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            {
                _panning = button.Pressed;
            }
            else if (e is InputEventMouseMotion motion && _panning)
            {
                _scroll.ScrollHorizontal -= (int)motion.Relative.X;
                _scroll.ScrollVertical -= (int)motion.Relative.Y;
            }
        }
    }
}
