using System.Linq;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class FloorLayoutTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 0.5f);

        [Test]
        public void GetFloorCells_DefaultHalfExtent_ReturnsExpectedCount()
        {
            int count = FloorLayout.GetFloorCells().Count();

            Assert.AreEqual(625, count);
        }

        [Test]
        public void GetFloorCells_SmallHalfExtent_ReturnsExpectedCount()
        {
            int count = FloorLayout.GetFloorCells(1).Count();

            Assert.AreEqual(9, count);
        }

        [Test]
        public void GetFloorCells_AllCellsWithinBounds()
        {
            foreach (Vector2Int cell in FloorLayout.GetFloorCells(5))
            {
                Assert.LessOrEqual(Mathf.Abs(cell.x), 5);
                Assert.LessOrEqual(Mathf.Abs(cell.y), 5);
            }
        }

        [Test]
        public void GetPerimeterCells_DefaultHalfExtent_ReturnsExpectedCount()
        {
            int count = FloorLayout.GetPerimeterCells().Count();

            Assert.AreEqual(104, count);
        }

        [Test]
        public void GetPerimeterCells_DoesNotOverlapFloorCells()
        {
            var floor = new System.Collections.Generic.HashSet<Vector2Int>(FloorLayout.GetFloorCells(5));

            foreach (Vector2Int cell in FloorLayout.GetPerimeterCells(5))
            {
                Assert.IsFalse(floor.Contains(cell));
            }
        }

        [Test]
        public void GetEdgeIndices_CoversTheFloorExtentExactly()
        {
            var indices = FloorLayout.GetEdgeIndices(5).ToList();

            Assert.AreEqual(11, indices.Count);
            Assert.AreEqual(-5, indices[0]);
            Assert.AreEqual(5, indices[indices.Count - 1]);
        }

        [Test]
        public void GetEdgeAnchor_SitsOnTheBoundaryLineNotAPerimeterCell()
        {
            // Half a cell outside the last floor cell -- on the shared edge, not one cell out.
            Assert.AreEqual(5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthEast, 2, 5).x, 0.0001f);
            Assert.AreEqual(2f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthEast, 2, 5).y, 0.0001f);
            Assert.AreEqual(5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthWest, -3, 5).y, 0.0001f);
            Assert.AreEqual(-5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.SouthEast, 0, 5).x, 0.0001f);
            Assert.AreEqual(-5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.SouthWest, 0, 5).y, 0.0001f);
        }

        // The staircase regression test: consecutive wall segments must be exactly one cell edge
        // apart in world space (0.5 x 0.25 for a 1 x 0.5 cell), which is what lets a sprite that
        // is 0.5 world wide with a 0.25 rise butt against its neighbour into a continuous wall.
        [Test]
        public void GetEdgeAnchor_ConsecutiveSegmentsAreExactlyOneCellEdgeApart()
        {
            var converter = new GridCoordinateConverter(CellSize);

            foreach (FloorLayout.Edge edge in System.Enum.GetValues(typeof(FloorLayout.Edge)))
            {
                Vector3 a = converter.CellFractionToWorld(FloorLayout.GetEdgeAnchor(edge, 0, 5));
                Vector3 b = converter.CellFractionToWorld(FloorLayout.GetEdgeAnchor(edge, 1, 5));
                Vector3 step = b - a;

                Assert.AreEqual(0.5f, Mathf.Abs(step.x), 0.0001f, "edge " + edge);
                Assert.AreEqual(0.25f, Mathf.Abs(step.y), 0.0001f, "edge " + edge);
            }
        }

        // A back-edge wall must always sort behind anything standing on the floor beside it,
        // and a front-edge skirt must always sort in front -- YSortUtility keys purely off
        // world Y, so this is a property of the anchor positions themselves.
        [Test]
        public void GetEdgeAnchor_BackWallsSortBehindTheFloorAndFrontSkirtsSortInFront()
        {
            var converter = new GridCoordinateConverter(CellSize);
            const int halfExtent = 5;

            for (int i = -halfExtent; i <= halfExtent; i++)
            {
                float occupantY = converter.CellToWorldCenter(new Vector2Int(halfExtent, i)).y;
                float wallY = converter.CellFractionToWorld(
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthEast, i, halfExtent)).y;
                Assert.Greater(wallY, occupantY, "NE wall must be further back than the cell it borders");

                float frontOccupantY = converter.CellToWorldCenter(new Vector2Int(-halfExtent, i)).y;
                float skirtY = converter.CellFractionToWorld(
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.SouthEast, i, halfExtent)).y;
                Assert.Less(skirtY, frontOccupantY, "SE skirt must be nearer than the cell it borders");
            }
        }

        [Test]
        public void GetWallPostAnchors_CapsBothWallRunsAndTheirSharedCorner()
        {
            var anchors = FloorLayout.GetWallPostAnchors(5).ToList();

            Assert.AreEqual(3, anchors.Count);
            CollectionAssert.Contains(anchors, new Vector2(5.5f, 5.5f));
            CollectionAssert.Contains(anchors, new Vector2(5.5f, -5.5f));
            CollectionAssert.Contains(anchors, new Vector2(-5.5f, 5.5f));
        }

        // Each wall run must terminate exactly where its post sits, otherwise the run either
        // stops short of the corner or overshoots past it.
        [Test]
        public void GetEdgeAnchor_RunEndsMeetTheCornerPosts()
        {
            const int halfExtent = 5;
            Vector2 lastNorthEast = FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthEast, halfExtent, halfExtent);
            Vector2 lastNorthWest = FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthWest, halfExtent, halfExtent);

            // Each covers [index - 0.5, index + 0.5] along the run, so the far end of the last
            // segment is half a cell past its anchor -- exactly the shared corner post.
            Assert.AreEqual(5.5f, lastNorthEast.y + 0.5f, 0.0001f);
            Assert.AreEqual(5.5f, lastNorthWest.x + 0.5f, 0.0001f);
        }

        [Test]
        public void ClampToFloor_PositionInsideBounds_IsUnchanged()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 inside = converter.CellToWorldCenter(new Vector2Int(3, -2));

            Vector3 result = FloorLayout.ClampToFloor(inside, converter, 12);

            Assert.AreEqual(inside.x, result.x, 0.0001f);
            Assert.AreEqual(inside.y, result.y, 0.0001f);
        }

        [Test]
        public void ClampToFloor_PositionPastFloorEdge_ClampsToNearestValidCell()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 pastEdge = converter.CellToWorldCenter(new Vector2Int(20, 0));
            Vector3 expected = converter.CellToWorldCenter(new Vector2Int(12, 0));

            Vector3 result = FloorLayout.ClampToFloor(pastEdge, converter, 12);

            Assert.AreEqual(expected.x, result.x, 0.0001f);
            Assert.AreEqual(expected.y, result.y, 0.0001f);
        }

        [Test]
        public void ClampToFloor_CornerCase_StaysInsideDiamond()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 pastCorner = converter.CellToWorldCenter(new Vector2Int(20, 20));
            Vector3 expectedCorner = converter.CellToWorldCenter(new Vector2Int(12, 12));

            Vector3 result = FloorLayout.ClampToFloor(pastCorner, converter, 12);

            Assert.AreEqual(expectedCorner.x, result.x, 0.0001f);
            Assert.AreEqual(expectedCorner.y, result.y, 0.0001f);
        }

        [Test]
        public void ClampToFloor_OppositeCornerCase_StaysInsideDiamond()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 pastCorner = converter.CellToWorldCenter(new Vector2Int(20, -20));
            Vector3 expectedCorner = converter.CellToWorldCenter(new Vector2Int(12, -12));

            Vector3 result = FloorLayout.ClampToFloor(pastCorner, converter, 12);

            Assert.AreEqual(expectedCorner.x, result.x, 0.0001f);
            Assert.AreEqual(expectedCorner.y, result.y, 0.0001f);
        }
    }
}
