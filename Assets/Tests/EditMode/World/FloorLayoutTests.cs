using System.Linq;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class FloorLayoutTests
    {
        // Square since the top-down switch; was 1 x 0.5 for the 2:1 isometric run.
        private static readonly Vector2 CellSize = new Vector2(1f, 1f);

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

            // Top-down: consecutive segments are one WHOLE cell apart along a single axis. The
            // isometric run this used to assert (0.5 x 0.25, the 2:1 diagonal) is exactly what
            // the projection switch removed -- an edge now runs straight down a world axis.
            foreach (FloorLayout.Edge edge in System.Enum.GetValues(typeof(FloorLayout.Edge)))
            {
                Vector3 a = converter.CellFractionToWorld(FloorLayout.GetEdgeAnchor(edge, 0, 5));
                Vector3 b = converter.CellFractionToWorld(FloorLayout.GetEdgeAnchor(edge, 1, 5));
                Vector3 step = b - a;

                Assert.AreEqual(1f, step.magnitude, 0.0001f, "edge " + edge + " must step one cell");
                Assert.IsTrue(
                    Mathf.Approximately(step.x, 0f) || Mathf.Approximately(step.y, 0f),
                    "edge " + edge + " must run along a world axis, not a diagonal");
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

            // Y-sorting is what makes a wall read as behind or in front, so the edges that matter
            // here are the ones that differ in Y. Under top-down those are the +Y and -Y edges --
            // NorthWest and SouthWest under the enum's inherited isometric names (see the note on
            // FloorLayout.Edge). The +X/-X edges share a Y with the row they border by
            // construction, so asserting a sort order on them would be asserting nothing.
            for (int i = -halfExtent; i <= halfExtent; i++)
            {
                float occupantY = converter.CellToWorldCenter(new Vector2Int(i, halfExtent)).y;
                float wallY = converter.CellFractionToWorld(
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.NorthWest, i, halfExtent)).y;
                Assert.Greater(wallY, occupantY, "north wall must be further back than the cell it borders");

                float frontOccupantY = converter.CellToWorldCenter(new Vector2Int(i, -halfExtent)).y;
                float skirtY = converter.CellFractionToWorld(
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.SouthWest, i, halfExtent)).y;
                Assert.Less(skirtY, frontOccupantY, "south skirt must be nearer than the cell it borders");
            }
        }

        // Only where two runs meet, which under top-down is the two NORTH corners. The south
        // corners are explicitly excluded: the post sprite is a front-on elevation whose body
        // rises in +Y, so anchoring one at a south corner stands it inside the room, on top of
        // the floor, sorted in front of everything (see the note on GetWallPostAnchors).
        [Test]
        public void GetWallPostAnchors_CapsOnlyTheCornersWhereTwoRunsMeet()
        {
            var anchors = FloorLayout.GetWallPostAnchors(5).ToList();

            Assert.AreEqual(2, anchors.Count);
            CollectionAssert.Contains(anchors, new Vector2(5.5f, 5.5f));
            CollectionAssert.Contains(anchors, new Vector2(-5.5f, 5.5f));
        }

        // The regression that test exists for: a post must never be anchored south of the floor,
        // because its body would rise into the room instead of away from it.
        [Test]
        public void GetWallPostAnchors_NeverAnchorsAPostSouthOfTheFloor()
        {
            foreach (Vector2 anchor in FloorLayout.GetWallPostAnchors(5))
            {
                Assert.Greater(anchor.y, 0f, "a post rising in +Y must be anchored on a north corner");
            }
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
