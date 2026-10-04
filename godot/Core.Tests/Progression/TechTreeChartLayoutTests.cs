using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Progression;

namespace GolemFactory.Tests.EditMode
{
    // The chart's arithmetic, tested without a Canvas -- the same reason GridCoordinateConverter's
    // world/cell math is a pure function rather than a Tilemap call. What matters here is that no
    // two cards overlap, that every prerequisite line actually joins the two cards it claims to,
    // and that the picture is identical between two runs of the same catalog.
    public class TechTreeChartLayoutTests
    {
        private static TechTreeNode At(string id, int phase, int row, string[] prerequisites = null) =>
            new TechTreeNode(
                id, id, "", TechTreeNodeKind.Recipe, phase, row,
                TechTreeUnlockSignal.None, null, prerequisites ?? new string[0]);

        [Test]
        public void NoTwoCatalogCardsOverlap()
        {
            var rects = new List<Rect>();
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                Rect rect = TechTreeChartLayout.NodeRect(node);
                foreach (Rect other in rects)
                {
                    Assert.IsFalse(rect.Overlaps(other), $"'{node.Id}' overlaps another card.");
                }
                rects.Add(rect);
            }
        }

        [Test]
        public void EveryCatalogCardFitsInsideTheChart()
        {
            Vector2 size = TechTreeChartLayout.ChartSize(
                TechTreeCatalog.Phases.Count, TechTreeCatalog.MaxRowsInAnyPhase());

            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                Rect rect = TechTreeChartLayout.NodeRect(node);
                Assert.LessOrEqual(rect.xMax, size.x, $"'{node.Id}' runs off the right edge.");
                Assert.LessOrEqual(rect.yMax, size.y, $"'{node.Id}' runs off the bottom edge.");
            }
        }

        [Test]
        public void PhaseHeadersSitAboveEveryCardInTheirColumn()
        {
            for (int phase = 0; phase < TechTreeCatalog.Phases.Count; phase++)
            {
                Rect header = TechTreeChartLayout.PhaseHeaderRect(phase);
                Assert.LessOrEqual(header.yMax, TechTreeChartLayout.RowY(0), "header overlaps row 0");
                Assert.AreEqual(TechTreeChartLayout.ColumnX(phase), header.x, "header is column-aligned");
            }
        }

        [Test]
        public void EdgeJoinsItsSourceAndTargetCards()
        {
            var nodes = new[] { At("a", 0, 0), At("b", 1, 3, new[] { "a" }) };
            var edges = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(nodes, edges);

            Assert.AreEqual(1, edges.Count);
            TechTreeEdge edge = edges[0];
            Rect source = TechTreeChartLayout.NodeRect(nodes[0]);
            Rect target = TechTreeChartLayout.NodeRect(nodes[1]);

            // The route leaves the source's right edge, and arrives at the target's left edge, at
            // each card's own vertical midline.
            Assert.AreEqual(source.xMax, edge.FromSource.Rect.xMin, 0.01f);
            Assert.AreEqual(source.center.y, edge.FromSource.Rect.center.y, 0.01f);
            Assert.AreEqual(target.xMin, edge.ToTarget.Rect.xMax, 0.01f);
            Assert.AreEqual(target.center.y, edge.ToTarget.Rect.center.y, 0.01f);

            // And the vertical run spans exactly between the two horizontals.
            Assert.AreEqual(edge.FromSource.Rect.xMax, edge.Lane.Rect.center.x, 0.01f);
            Assert.AreEqual(edge.ToTarget.Rect.xMin, edge.Lane.Rect.center.x, 0.01f);
        }

        [Test]
        public void SameColumnEdgeLeavesBySourceLeftEdge()
        {
            // Routing it out of the right edge would draw a line straight back across the card it
            // had just left, which is what the layout's same-column branch exists to avoid.
            var nodes = new[] { At("a", 2, 0), At("b", 2, 2, new[] { "a" }) };
            var edges = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(nodes, edges);

            Rect source = TechTreeChartLayout.NodeRect(nodes[0]);
            Assert.AreEqual(source.xMin, edges[0].FromSource.Rect.xMax, 0.01f);
            Assert.Less(edges[0].Lane.Rect.center.x, source.xMin, "lane runs in the left gutter");
        }

        [Test]
        public void LanesStayInsideTheGutterTheyBelongTo()
        {
            var edges = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(TechTreeCatalog.Nodes, edges);

            for (int i = 0; i < edges.Count; i++)
            {
                TechTreeNode target = TechTreeCatalog.Nodes[edges[i].TargetIndex];
                float gutterRight = TechTreeChartLayout.ColumnX(target.PhaseIndex);
                float gutterLeft = gutterRight - TechTreeChartLayout.Gutter;
                float laneX = edges[i].Lane.Rect.center.x;

                Assert.GreaterOrEqual(laneX, gutterLeft, $"edge into '{target.Id}' escaped its gutter");
                Assert.LessOrEqual(laneX, gutterRight, $"edge into '{target.Id}' escaped its gutter");
            }
        }

        [Test]
        public void EveryCatalogPrerequisiteIsDrawn()
        {
            int expected = 0;
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                expected += node.Prerequisites.Count;
            }

            var edges = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(TechTreeCatalog.Nodes, edges);

            Assert.AreEqual(expected, edges.Count, "a prerequisite with no line is an invisible gate");
        }

        [Test]
        public void RoutingIsDeterministic()
        {
            // Lanes are handed out in enumeration order, so the same catalog must produce the same
            // picture every time -- a chart that reshuffles its lines between two openings of the
            // same save reads as a different chart.
            var first = new List<TechTreeEdge>();
            var second = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(TechTreeCatalog.Nodes, first);
            TechTreeChartLayout.BuildEdges(TechTreeCatalog.Nodes, second);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Lane.Rect, second[i].Lane.Rect);
            }
        }

        [Test]
        public void LongHaulIsFlaggedOnlyWhenAnEdgeSkipsAColumn()
        {
            var nodes = new[]
            {
                At("a", 0, 0),
                At("adjacent", 1, 0, new[] { "a" }),
                At("skipper", 2, 1, new[] { "a" })
            };
            var edges = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(nodes, edges);

            Assert.IsFalse(edges[0].IsLongHaul);
            Assert.IsTrue(edges[1].IsLongHaul);
        }

        [Test]
        public void BuildEdgesClearsWhatWasThereBefore()
        {
            var edges = new List<TechTreeEdge>();
            TechTreeChartLayout.BuildEdges(TechTreeCatalog.Nodes, edges);
            int once = edges.Count;
            TechTreeChartLayout.BuildEdges(TechTreeCatalog.Nodes, edges);

            Assert.AreEqual(once, edges.Count, "a rebuild must not stack a second set of lines");
        }
    }
}
