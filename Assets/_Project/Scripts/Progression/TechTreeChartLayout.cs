using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.Progression
{
    /// <summary>One straight run of a prerequisite line, in chart space.</summary>
    public readonly struct TechTreeSegment
    {
        public readonly Rect Rect;

        public TechTreeSegment(float x, float y, float width, float height)
        {
            Rect = new Rect(x, y, width, height);
        }
    }

    /// <summary>
    /// A prerequisite drawn as an orthogonal three-segment route: out of the source, down a lane
    /// in the gutter, into the target.
    /// </summary>
    public readonly struct TechTreeEdge
    {
        public readonly int SourceIndex;
        public readonly int TargetIndex;
        public readonly TechTreeSegment FromSource;
        public readonly TechTreeSegment Lane;
        public readonly TechTreeSegment ToTarget;

        /// <summary>
        /// True when the edge crosses one or more whole columns. Those are the routes that have to
        /// pass over other cards, so the chart draws them dimmer -- they are context, not the
        /// local step-to-step reading the eye follows.
        /// </summary>
        public readonly bool IsLongHaul;

        public TechTreeEdge(
            int sourceIndex, int targetIndex,
            TechTreeSegment fromSource, TechTreeSegment lane, TechTreeSegment toTarget,
            bool isLongHaul)
        {
            SourceIndex = sourceIndex;
            TargetIndex = targetIndex;
            FromSource = fromSource;
            Lane = lane;
            ToTarget = toTarget;
            IsLongHaul = isLongHaul;
        }
    }

    /// <summary>
    /// Where every card and every prerequisite line sits on the chart. Pure math over chart space
    /// -- x right, y <b>down</b> from the top-left corner -- following the same
    /// extract-the-math-into-a-testable-function idiom as <c>GridCoordinateConverter</c> and
    /// <c>PlayerMovement.ComputeDisplacement</c>. The panel is a thin applier that flips y into
    /// UGUI's anchored coordinates and nothing more.
    ///
    /// <para>
    /// The chart is drawn from data at runtime rather than published as one baked poster PNG. A
    /// baked chart would be a second copy of §5.2 and §6 that no test could hold to the assets,
    /// and it would drift the first time a recipe was retuned. The pixel art is the
    /// <em>chrome</em> -- plaques, badges, rules, the drafting field -- and the arrangement is
    /// this file.
    /// </para>
    /// </summary>
    public static class TechTreeChartLayout
    {
        public const float NodeWidth = 196f;
        public const float NodeHeight = 52f;
        public const float RowPitch = 66f;

        /// <summary>Space between two columns, and the channel prerequisite lines run down.</summary>
        public const float Gutter = 84f;

        public const float Padding = 20f;
        public const float PhaseHeaderHeight = 44f;
        public const float PhaseHeaderGap = 12f;

        /// <summary>Line thickness, in chart units. Two so a line reads at 100% without blurring.</summary>
        public const float LineThickness = 2f;

        /// <summary>
        /// How many parallel lines one gutter carries before reusing a lane. Four fits 84px at a
        /// comfortable 16.8px spacing; more lanes would be thinner than the gap between two lines
        /// and read as a hatch rather than as routes.
        /// </summary>
        public const int LanesPerGutter = 4;

        public static float ColumnX(int phaseIndex) =>
            Padding + Gutter + phaseIndex * (NodeWidth + Gutter);

        public static float RowY(int row) =>
            Padding + PhaseHeaderHeight + PhaseHeaderGap + row * RowPitch;

        public static Rect NodeRect(TechTreeNode node) =>
            node == null
                ? new Rect(0f, 0f, NodeWidth, NodeHeight)
                : new Rect(ColumnX(node.PhaseIndex), RowY(node.Row), NodeWidth, NodeHeight);

        public static Rect PhaseHeaderRect(int phaseIndex) =>
            new Rect(ColumnX(phaseIndex), Padding, NodeWidth, PhaseHeaderHeight);

        public static Vector2 ChartSize(int phaseCount, int maxRows)
        {
            float width = ColumnX(Mathf.Max(0, phaseCount - 1)) + NodeWidth + Padding;
            float height = RowY(Mathf.Max(0, maxRows - 1)) + NodeHeight + Padding;
            return new Vector2(width, height);
        }

        /// <summary>
        /// Routes every prerequisite in <paramref name="nodes"/> into <paramref name="into"/>,
        /// which is cleared first. Lanes are handed out per gutter in enumeration order, so the
        /// same catalog always produces the same picture -- the chart must not reshuffle its lines
        /// between two runs of the same save.
        /// </summary>
        public static void BuildEdges(IReadOnlyList<TechTreeNode> nodes, List<TechTreeEdge> into)
        {
            if (nodes == null || into == null)
            {
                return;
            }

            into.Clear();

            var indexById = new Dictionary<string, int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                indexById[nodes[i].Id] = i;
            }

            var laneCursor = new Dictionary<int, int>();

            for (int targetIndex = 0; targetIndex < nodes.Count; targetIndex++)
            {
                TechTreeNode target = nodes[targetIndex];
                Rect targetRect = NodeRect(target);

                for (int p = 0; p < target.Prerequisites.Count; p++)
                {
                    if (!indexById.TryGetValue(target.Prerequisites[p], out int sourceIndex))
                    {
                        continue;
                    }

                    TechTreeNode source = nodes[sourceIndex];
                    Rect sourceRect = NodeRect(source);

                    // Lines always run down the gutter immediately left of the TARGET, so every
                    // line arriving at a card converges on that card's own approach rather than
                    // fanning in from wherever its source happened to sit.
                    int gutterIndex = target.PhaseIndex;
                    laneCursor.TryGetValue(gutterIndex, out int cursor);
                    laneCursor[gutterIndex] = cursor + 1;

                    float gutterLeft = ColumnX(gutterIndex) - Gutter;
                    float laneStep = Gutter / (LanesPerGutter + 1);
                    float laneX = gutterLeft + laneStep * ((cursor % LanesPerGutter) + 1);

                    float sourceY = sourceRect.y + sourceRect.height * 0.5f;
                    float targetY = targetRect.y + targetRect.height * 0.5f;

                    bool sameColumn = source.PhaseIndex == target.PhaseIndex;

                    // Same column: the source is to the RIGHT of the lane too, so the stub leaves
                    // by the source's left edge. Routing it out of the right edge instead would
                    // draw a line straight back across the card it just left.
                    TechTreeSegment fromSource = sameColumn
                        ? HorizontalRun(laneX, sourceRect.x, sourceY)
                        : HorizontalRun(sourceRect.xMax, laneX, sourceY);

                    TechTreeSegment lane = VerticalRun(laneX, sourceY, targetY);
                    TechTreeSegment toTarget = HorizontalRun(laneX, targetRect.x, targetY);

                    into.Add(new TechTreeEdge(
                        sourceIndex, targetIndex, fromSource, lane, toTarget,
                        isLongHaul: target.PhaseIndex - source.PhaseIndex > 1));
                }
            }
        }

        private static TechTreeSegment HorizontalRun(float x0, float x1, float y)
        {
            float left = Mathf.Min(x0, x1);
            float width = Mathf.Abs(x1 - x0);
            return new TechTreeSegment(left, y - LineThickness * 0.5f, width, LineThickness);
        }

        private static TechTreeSegment VerticalRun(float x, float y0, float y1)
        {
            float top = Mathf.Min(y0, y1);
            float height = Mathf.Abs(y1 - y0);
            return new TechTreeSegment(x - LineThickness * 0.5f, top, LineThickness, height);
        }
    }
}
