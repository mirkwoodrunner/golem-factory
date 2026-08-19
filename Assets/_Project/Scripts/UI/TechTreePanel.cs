using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GolemFactory.Progression;

namespace GolemFactory.UI
{
    /// <summary>
    /// The Artificer's Ledger: the research track of docs/progression-design.md drawn as a chart
    /// the player can open mid-game, with each node showing whether it is researched, available
    /// now, or still locked.
    ///
    /// <para>
    /// It is a fifth tab on the Management screen rather than a screen of its own. Mutual
    /// exclusion between full screens is the one thing <see cref="HudScreenPolicy"/> exists to
    /// keep from regressing, and a sixth full screen would be a fourth place to get that wrong for
    /// no gain -- the Ledger belongs with Inventory, Assembly Line and Patents, which are the
    /// other three "what do I have and what next" readouts.
    /// </para>
    ///
    /// <para>
    /// <b>Built once, tinted often.</b> <c>ManagementPanel</c> refreshes its active tab every
    /// frame while open, and this chart is ~45 cards and ~70 line segments. Destroying and
    /// recreating that per frame -- the idiom <c>WorkbenchController.RebuildUI</c> and
    /// <c>AssemblyLinePanel.Refresh</c> both use, correctly, for a handful of rows -- would be
    /// hundreds of allocations a frame. So the chart's <em>shape</em> is rebuilt from data
    /// (<see cref="TechTreeChartLayout"/>) whenever the catalog would change, and its
    /// <em>state</em> is re-applied only when the ledger's version moves.
    /// </para>
    /// </summary>
    public sealed class TechTreePanel : MonoBehaviour
    {
        [SerializeField] private TechTreeProgressTracker tracker;

        /// <summary>The scrollable canvas the chart is drawn on. Resized to the chart.</summary>
        [SerializeField] private RectTransform chartRoot;

        /// <summary>Fixed strip above the chart: the count, the next objective, the legend.</summary>
        [SerializeField] private RectTransform legendRoot;

        [SerializeField] private TextMeshProUGUI headerText;

        [SerializeField] private Sprite fieldSprite;
        [SerializeField] private Sprite phasePlateSprite;
        [SerializeField] private Sprite nodeLockedSprite;
        [SerializeField] private Sprite nodeAvailableSprite;
        [SerializeField] private Sprite nodeResearchedSprite;
        [SerializeField] private Sprite nodePlannedSprite;
        [SerializeField] private Sprite lineHorizontalSprite;
        [SerializeField] private Sprite lineVerticalSprite;
        [SerializeField] private Sprite badgeChassisSprite;
        [SerializeField] private Sprite badgeRecipeSprite;
        [SerializeField] private Sprite badgeBuildingSprite;
        [SerializeField] private Sprite badgeTechniqueSprite;
        [SerializeField] private Sprite badgeMilestoneSprite;

        // THE INK INVERTS WITH THE PLATE, which is the same rule ManagementPanel's tab captions
        // follow and for the same reason: a researched card is a lit brass plaque and a locked one
        // is dark iron, so one fixed text colour is unreadable against one of them. The first pass
        // here used warm parchment on every card and the detail line on every researched card
        // vanished into the brass behind it -- visible immediately in the game view, and in none
        // of the serialized properties.
        //
        // State is then carried by the plaque's silhouette (seal / punch holes / bar) and by its
        // brightness, never by text hue alone.
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
        // Long-haul routes cross other columns, so they are drawn back to context weight.
        private const float LongHaulFade = 0.45f;

        private static readonly Color PhaseTitleColor = new Color(0.14f, 0.10f, 0.06f, 1f);
        private static readonly Color BadgeInk = new Color(0.22f, 0.15f, 0.07f, 1f);

        // Done is muted; next is hot. Both plaques are brass, and the seal-versus-notch silhouette
        // alone was too quiet a cue for the chart's single most important read -- "what can I do
        // now?". Available keeps the sprite's full brass, researched is knocked back, so the
        // handful of live cards are the brightest thing on the page.
        private static readonly Color ResearchedPlaqueTint = new Color(0.78f, 0.74f, 0.68f, 1f);

        // The legend sits on the Management screen's near-black iron panel, NOT on a plaque, so
        // its captions keep the light HUD colour even where the state they describe is dark ink.
        private static readonly Color LegendCaptionColor = new Color(0.85f, 0.80f, 0.72f, 1f);

        private const float NameFontSize = 13f;
        private const float DetailFontSize = 10f;
        private const float PhaseTitleFontSize = 14f;
        private const float PhaseGoalFontSize = 9f;
        private const float BadgeSize = 16f;

        private readonly List<TechTreeEdge> _edges = new List<TechTreeEdge>();
        private NodeView[] _nodeViews;
        private EdgeView[] _edgeViews;
        private TechTreeNodeState[] _states;

        // -1 rather than 0 so the first Refresh always applies: a fresh ledger is version 0.
        private int _appliedVersion = -1;
        private bool _chartBuilt;

        public void Configure(TechTreeProgressTracker progressTracker) => tracker = progressTracker;

        public void ConfigureUI(RectTransform chart, RectTransform legend, TextMeshProUGUI header)
        {
            chartRoot = chart;
            legendRoot = legend;
            headerText = header;
        }

        public void ConfigureSprites(
            Sprite field, Sprite phasePlate,
            Sprite locked, Sprite available, Sprite researched, Sprite planned,
            Sprite lineHorizontal, Sprite lineVertical,
            Sprite badgeChassis, Sprite badgeRecipe, Sprite badgeBuilding,
            Sprite badgeTechnique, Sprite badgeMilestone)
        {
            fieldSprite = field;
            phasePlateSprite = phasePlate;
            nodeLockedSprite = locked;
            nodeAvailableSprite = available;
            nodeResearchedSprite = researched;
            nodePlannedSprite = planned;
            lineHorizontalSprite = lineHorizontal;
            lineVerticalSprite = lineVertical;
            badgeChassisSprite = badgeChassis;
            badgeRecipeSprite = badgeRecipe;
            badgeBuildingSprite = badgeBuilding;
            badgeTechniqueSprite = badgeTechnique;
            badgeMilestoneSprite = badgeMilestone;
        }

        /// <summary>
        /// Called every frame the tab is showing. Builds the chart on the first call and does
        /// nothing at all on the frames where the player's progress has not moved.
        /// </summary>
        public void Refresh()
        {
            if (chartRoot == null)
            {
                return;
            }

            if (!_chartBuilt)
            {
                RebuildChart();
            }

            TechTreeProgressLedger ledger = tracker != null ? tracker.Ledger : null;
            int version = ledger != null ? ledger.Version : 0;
            if (version == _appliedVersion)
            {
                return;
            }

            _appliedVersion = version;
            ApplyStates(ledger);
        }

        /// <summary>
        /// Destroys and redraws every card and line from <see cref="TechTreeCatalog"/>. Public so
        /// an authoring pass or a test can force it; normal play calls it once.
        /// </summary>
        public void RebuildChart()
        {
            for (int i = chartRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(chartRoot.GetChild(i).gameObject);
            }

            IReadOnlyList<TechTreeNode> nodes = TechTreeCatalog.Nodes;

            chartRoot.sizeDelta = TechTreeChartLayout.ChartSize(
                TechTreeCatalog.Phases.Count, TechTreeCatalog.MaxRowsInAnyPhase());
            ApplyFieldBackground();

            // Lines first so every card draws over them: UGUI sorts by sibling order, and a
            // route passing behind a card is the whole reason the routes are legible.
            TechTreeChartLayout.BuildEdges(nodes, _edges);
            _edgeViews = new EdgeView[_edges.Count];
            for (int i = 0; i < _edges.Count; i++)
            {
                _edgeViews[i] = BuildEdgeView(_edges[i]);
            }

            for (int p = 0; p < TechTreeCatalog.Phases.Count; p++)
            {
                BuildPhaseHeader(p, TechTreeCatalog.Phases[p]);
            }

            _nodeViews = new NodeView[nodes.Count];
            _states = new TechTreeNodeState[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                _nodeViews[i] = BuildNodeView(nodes[i]);
            }

            BuildLegend();

            _chartBuilt = true;
            _appliedVersion = -1;
        }

        private void ApplyStates(TechTreeProgressLedger ledger)
        {
            IReadOnlyList<TechTreeNode> nodes = TechTreeCatalog.Nodes;
            TechTreeStatusRules.Resolve(nodes, ledger, _states);

            for (int i = 0; i < _nodeViews.Length; i++)
            {
                ApplyNodeState(_nodeViews[i], nodes[i], _states[i]);
            }

            for (int i = 0; i < _edgeViews.Length; i++)
            {
                TechTreeEdge edge = _edges[i];
                // A route is coloured by where it ARRIVES, not where it leaves: the player reads
                // the chart forwards, and what they want to see lit is the approach to the thing
                // they can do next.
                Color colour = LineColour(_states[edge.TargetIndex]);
                if (edge.IsLongHaul)
                {
                    colour.a *= LongHaulFade;
                }
                _edgeViews[i].Apply(colour);
            }

            if (headerText != null)
            {
                headerText.text = BuildHeadline(nodes, _states);
            }
        }

        private static string BuildHeadline(
            IReadOnlyList<TechTreeNode> nodes, TechTreeNodeState[] states)
        {
            int researched = TechTreeStatusRules.CountResearched(states);
            TechTreeNode next = TechTreeStatusRules.FindNextObjective(nodes, states);
            string objective = next != null ? next.DisplayName : "everything on the track";
            // The pan hint is in the headline because the chart is 1720px wide and the ScrollRect
            // has no visible scrollbars -- without it, phases V and VI are simply off the edge
            // with nothing on screen saying they can be reached.
            return $"THE ARTIFICER'S LEDGER    {researched} / {nodes.Count} researched    ·    " +
                   $"Next: {objective}    ·    <size=10>drag the chart to pan</size>";
        }

        private static Color LineColour(TechTreeNodeState state)
        {
            switch (state)
            {
                case TechTreeNodeState.Researched:
                    return LineResearched;
                case TechTreeNodeState.Available:
                    return LineAvailable;
                default:
                    return LineLocked;
            }
        }

        private void ApplyNodeState(NodeView view, TechTreeNode node, TechTreeNodeState state)
        {
            if (node.IsPlanned)
            {
                // A planned node never wears a brass plaque, whatever its prerequisites say --
                // there is nothing in the build for it to unlock. Its two states differ only by
                // brightness, which is enough for a node that is a note to the designer.
                view.Plaque.sprite = nodePlannedSprite;
                view.Plaque.color = Color.white;
                bool reachable = state != TechTreeNodeState.Locked;
                view.Name.color = reachable ? PlannedName : Dim(PlannedName);
                view.Detail.color = reachable ? PlannedDetail : Dim(PlannedDetail);
                view.Badge.color = reachable ? PlannedName : Dim(PlannedName);
                view.Name.text = node.DisplayName + "  <size=8>PLANNED</size>";
                return;
            }

            switch (state)
            {
                case TechTreeNodeState.Researched:
                    view.Plaque.sprite = nodeResearchedSprite;
                    view.Plaque.color = ResearchedPlaqueTint;
                    view.Name.color = ResearchedName;
                    view.Detail.color = ResearchedDetail;
                    // Tinted to a dark silhouette rather than left white: the badges are drawn in
                    // brass and iron, and brass-on-brass is no badge at all. Each one is drawn
                    // with an outline, so the shape is what survives the tint -- which is the
                    // channel the badge was carrying anyway.
                    view.Badge.color = BadgeInk;
                    break;
                case TechTreeNodeState.Available:
                    view.Plaque.sprite = nodeAvailableSprite;
                    view.Plaque.color = Color.white;
                    view.Name.color = AvailableName;
                    view.Detail.color = AvailableDetail;
                    view.Badge.color = BadgeInk;
                    break;
                default:
                    view.Plaque.sprite = nodeLockedSprite;
                    view.Plaque.color = Color.white;
                    view.Name.color = LockedName;
                    view.Detail.color = LockedDetail;
                    view.Badge.color = new Color(1f, 1f, 1f, 0.55f);
                    break;
            }

            view.Name.text = node.DisplayName;
        }

        private static Color Dim(Color colour) =>
            new Color(colour.r * 0.55f, colour.g * 0.55f, colour.b * 0.55f, colour.a);

        private void ApplyFieldBackground()
        {
            Image field = chartRoot.GetComponent<Image>();
            if (field == null)
            {
                field = chartRoot.gameObject.AddComponent<Image>();
            }

            field.sprite = fieldSprite;
            // Tiled, not stretched: the graticule is a 32px square and stretching it across
            // 1720x888 would smear it into bands rather than draw drafting paper.
            field.type = Image.Type.Tiled;
            field.color = Color.white;
            field.raycastTarget = false;
        }

        private void BuildPhaseHeader(int phaseIndex, TechTreePhase phase)
        {
            Rect rect = TechTreeChartLayout.PhaseHeaderRect(phaseIndex);
            RectTransform plate = CreateChild("Phase" + phaseIndex, rect);
            Image plateImage = plate.gameObject.AddComponent<Image>();
            plateImage.sprite = phasePlateSprite;
            plateImage.type = Image.Type.Sliced;
            plateImage.color = Color.white;
            plateImage.raycastTarget = false;

            TextMeshProUGUI title = CreateLabel(plate, "Title", phase.Title, PhaseTitleFontSize, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = PhaseTitleColor;
            Stretch(title.rectTransform, 6f, 4f, 6f, rect.height * 0.5f);

            TextMeshProUGUI goal = CreateLabel(plate, "Goal", phase.Goal, PhaseGoalFontSize, TextAlignmentOptions.Top);
            goal.color = new Color(0.24f, 0.17f, 0.09f, 1f);
            goal.textWrappingMode = TextWrappingModes.Normal;
            goal.overflowMode = TextOverflowModes.Truncate;
            Stretch(goal.rectTransform, 6f, rect.height * 0.5f, 6f, 3f);
        }

        private NodeView BuildNodeView(TechTreeNode node)
        {
            Rect rect = TechTreeChartLayout.NodeRect(node);
            RectTransform card = CreateChild("Node_" + node.Id, rect);

            Image plaque = card.gameObject.AddComponent<Image>();
            plaque.type = Image.Type.Sliced;
            plaque.raycastTarget = false;

            RectTransform badgeRect = CreateChild("Badge", new Rect(0f, 0f, BadgeSize, BadgeSize), card);
            badgeRect.anchoredPosition = new Vector2(9f, -(rect.height - BadgeSize) * 0.5f);
            Image badge = badgeRect.gameObject.AddComponent<Image>();
            badge.sprite = BadgeFor(node.Kind);
            badge.raycastTarget = false;
            badge.preserveAspect = true;

            float textLeft = BadgeSize + 14f;
            TextMeshProUGUI name = CreateLabel(card, "Name", node.DisplayName, NameFontSize, TextAlignmentOptions.BottomLeft);
            name.fontStyle = node.IsKeystone ? FontStyles.Bold : FontStyles.Normal;
            name.richText = true;
            Stretch(name.rectTransform, textLeft, 6f, 8f, rect.height * 0.5f);

            TextMeshProUGUI detail = CreateLabel(card, "Detail", node.Detail, DetailFontSize, TextAlignmentOptions.TopLeft);
            Stretch(detail.rectTransform, textLeft, rect.height * 0.5f, 8f, 5f);

            return new NodeView(plaque, badge, name, detail);
        }

        private Sprite BadgeFor(TechTreeNodeKind kind)
        {
            switch (kind)
            {
                case TechTreeNodeKind.Chassis:
                    return badgeChassisSprite;
                case TechTreeNodeKind.Recipe:
                    return badgeRecipeSprite;
                case TechTreeNodeKind.Building:
                    return badgeBuildingSprite;
                case TechTreeNodeKind.Technique:
                    return badgeTechniqueSprite;
                default:
                    return badgeMilestoneSprite;
            }
        }

        private EdgeView BuildEdgeView(TechTreeEdge edge)
        {
            return new EdgeView(
                CreateSegment(edge.FromSource, horizontal: true),
                CreateSegment(edge.Lane, horizontal: false),
                CreateSegment(edge.ToTarget, horizontal: true));
        }

        private Image CreateSegment(TechTreeSegment segment, bool horizontal)
        {
            RectTransform rect = CreateChild("Line", segment.Rect);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = horizontal ? lineHorizontalSprite : lineVerticalSprite;
            image.type = Image.Type.Tiled;
            image.raycastTarget = false;
            return image;
        }

        private void BuildLegend()
        {
            if (legendRoot == null)
            {
                return;
            }

            for (int i = legendRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(legendRoot.GetChild(i).gameObject);
            }

            BuildLegendChip(0, nodeResearchedSprite, "Researched");
            BuildLegendChip(1, nodeAvailableSprite, "Available now");
            BuildLegendChip(2, nodeLockedSprite, "Locked");
            BuildLegendChip(3, nodePlannedSprite, "Designed, not built");
        }

        private void BuildLegendChip(int index, Sprite sprite, string caption)
        {
            const float chipWidth = 150f;
            var go = new GameObject("Legend" + index, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(legendRoot, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(chipWidth, 18f);
            rect.anchoredPosition = new Vector2(index * chipWidth, 0f);

            RectTransform swatch = CreateChild("Swatch", new Rect(0f, 0f, 22f, 16f), rect);
            swatch.anchoredPosition = new Vector2(0f, -1f);
            Image image = swatch.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;

            TextMeshProUGUI label = CreateLabel(rect, "Label", caption, 11f, TextAlignmentOptions.MidlineLeft);
            label.color = LegendCaptionColor;
            Stretch(label.rectTransform, 28f, 0f, 0f, 0f);
        }

        /// <summary>
        /// Chart space (y down from the top-left) into UGUI's anchored coordinates (y up). The one
        /// place the flip happens, which is why <see cref="TechTreeChartLayout"/> can stay
        /// readable arithmetic instead of a pile of negations.
        /// </summary>
        private RectTransform CreateChild(string name, Rect chartRect, RectTransform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent != null ? parent : chartRoot, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(chartRect.width, chartRect.height);
            rect.anchoredPosition = new Vector2(chartRect.x, -chartRect.y);
            return rect;
        }

        private static TextMeshProUGUI CreateLabel(
            RectTransform parent, string name, string text, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private readonly struct NodeView
        {
            public readonly Image Plaque;
            public readonly Image Badge;
            public readonly TextMeshProUGUI Name;
            public readonly TextMeshProUGUI Detail;

            public NodeView(Image plaque, Image badge, TextMeshProUGUI name, TextMeshProUGUI detail)
            {
                Plaque = plaque;
                Badge = badge;
                Name = name;
                Detail = detail;
            }
        }

        private readonly struct EdgeView
        {
            private readonly Image _fromSource;
            private readonly Image _lane;
            private readonly Image _toTarget;

            public EdgeView(Image fromSource, Image lane, Image toTarget)
            {
                _fromSource = fromSource;
                _lane = lane;
                _toTarget = toTarget;
            }

            public void Apply(Color colour)
            {
                _fromSource.color = colour;
                _lane.color = colour;
                _toTarget.color = colour;
            }
        }
    }
}
