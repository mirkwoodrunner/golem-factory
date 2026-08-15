using System.Collections.Generic;
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
            Assert.AreEqual(5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.East, 2, 5).x, 0.0001f);
            Assert.AreEqual(2f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.East, 2, 5).y, 0.0001f);
            Assert.AreEqual(5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.North, -3, 5).y, 0.0001f);
            Assert.AreEqual(-5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.West, 0, 5).x, 0.0001f);
            Assert.AreEqual(-5.5f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.South, 0, 5).y, 0.0001f);
        }

        // The rename's own regression test: each Edge must sit on the axis its NAME claims. The
        // enum spent the projection switch meaning the opposite of what it said (SouthEast was
        // the west edge), and the only thing keeping the generator correct was that it had been
        // written by someone who knew.
        [Test]
        public void GetEdgeAnchor_EachEdgeSitsOnTheAxisItsNameClaims()
        {
            const int halfExtent = 5;
            const float outer = 5.5f;

            Assert.AreEqual(outer, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.North, 0, halfExtent).y, 0.0001f);
            Assert.AreEqual(outer, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.East, 0, halfExtent).x, 0.0001f);
            Assert.AreEqual(-outer, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.South, 0, halfExtent).y, 0.0001f);
            Assert.AreEqual(-outer, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.West, 0, halfExtent).x, 0.0001f);

            // And the index runs along the OTHER axis, so an edge is a line and not a point.
            Assert.AreEqual(3f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.North, 3, halfExtent).x, 0.0001f);
            Assert.AreEqual(3f, FloorLayout.GetEdgeAnchor(FloorLayout.Edge.East, 3, halfExtent).y, 0.0001f);
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
            // here are the ones that differ in Y: North and South. The East/West edges share a Y
            // with the row they border by construction, so asserting a sort order on them would
            // be asserting nothing.
            for (int i = -halfExtent; i <= halfExtent; i++)
            {
                float occupantY = converter.CellToWorldCenter(new Vector2Int(i, halfExtent)).y;
                float wallY = converter.CellFractionToWorld(
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.North, i, halfExtent)).y;
                Assert.Greater(wallY, occupantY, "north wall must be further back than the cell it borders");

                float frontOccupantY = converter.CellToWorldCenter(new Vector2Int(i, -halfExtent)).y;
                float skirtY = converter.CellFractionToWorld(
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.South, i, halfExtent)).y;
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
            Vector2 lastEast = FloorLayout.GetEdgeAnchor(FloorLayout.Edge.East, halfExtent, halfExtent);
            Vector2 lastNorth = FloorLayout.GetEdgeAnchor(FloorLayout.Edge.North, halfExtent, halfExtent);

            // Each covers [index - 0.5, index + 0.5] along the run, so the far end of the last
            // segment is half a cell past its anchor -- exactly the shared corner post.
            Assert.AreEqual(5.5f, lastEast.y + 0.5f, 0.0001f);
            Assert.AreEqual(5.5f, lastNorth.x + 0.5f, 0.0001f);
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
        public void ClampToFloor_NorthCorner_StaysInsideTheWorkshop()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 pastCorner = converter.CellToWorldCenter(new Vector2Int(20, 20));
            Vector3 expectedCorner = converter.CellToWorldCenter(new Vector2Int(12, 12));

            Vector3 result = FloorLayout.ClampToFloor(pastCorner, converter, 12);

            Assert.AreEqual(expectedCorner.x, result.x, 0.0001f);
            Assert.AreEqual(expectedCorner.y, result.y, 0.0001f);
        }

        // CHANGED WITH THE MARKET STREET, deliberately. This used to expect (12, -12) -- the
        // workshop's south wall -- because the room was the whole world. The street is ground
        // now, so the player walks out of the open front and is stopped at the far kerb instead.
        // A clamp that still pinned them at -12 would leave the stalls visible and unreachable.
        [Test]
        public void ClampToFloor_SouthOfTheWorkshop_StopsAtTheFarKerbNotTheShopFront()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 pastCorner = converter.CellToWorldCenter(new Vector2Int(20, -40));
            Vector3 expectedCorner = converter.CellToWorldCenter(
                new Vector2Int(12, FloorLayout.WorldMinY));

            Vector3 result = FloorLayout.ClampToFloor(pastCorner, converter, 12);

            Assert.AreEqual(expectedCorner.x, result.x, 0.0001f);
            Assert.AreEqual(expectedCorner.y, result.y, 0.0001f);
        }

        [Test]
        public void ClampToFloor_OnTheStreet_IsUnchanged()
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 atAStall = converter.CellToWorldCenter(new Vector2Int(3, -16));

            Vector3 result = FloorLayout.ClampToFloor(atAStall, converter, 12);

            Assert.AreEqual(atAStall.x, result.x, 0.0001f, "the street is walkable ground");
            Assert.AreEqual(atAStall.y, result.y, 0.0001f);
        }

        // --- The two regions -----------------------------------------------------------------

        [Test]
        public void GetStreetCells_SitSouthOfTheWorkshopAndNeverOverlapIt()
        {
            var workshop = new HashSet<Vector2Int>(FloorLayout.GetFloorCells());
            var street = FloorLayout.GetStreetCells().ToList();

            Assert.IsNotEmpty(street);
            foreach (Vector2Int cell in street)
            {
                Assert.Less(cell.y, -FloorLayout.HalfExtent, "street row " + cell + " is inside the shop");
                Assert.IsFalse(workshop.Contains(cell), "cell " + cell + " is painted twice");
            }
        }

        [Test]
        public void GetWorldCells_IsExactlyTheWorkshopPlusTheStreet()
        {
            int workshop = FloorLayout.GetFloorCells().Count();
            int street = FloorLayout.GetStreetCells().Count();
            var world = FloorLayout.GetWorldCells().ToList();

            Assert.AreEqual(workshop + street, world.Count);
            Assert.AreEqual(world.Count, new HashSet<Vector2Int>(world).Count, "no cell may repeat");
        }

        [Test]
        public void TheStreetIsWideEnoughForAStallRowWithApproachTilesBothSides()
        {
            // §3.2 caps a node at two extractors, which needs two free approach tiles. A stall
            // row with only one clear row either side would silently cap at one golem.
            Assert.GreaterOrEqual(FloorLayout.StreetDepth, 4,
                "a stall row needs clear ground in front of and behind it");
        }

        // --- The world's boundary, as distinct from the workshop's ---------------------------

        [Test]
        public void GetWorldEdgeIndices_RunPastTheShopFrontAndDownTheStreet()
        {
            var indices = FloorLayout.GetWorldEdgeIndices(5, 3).ToList();

            Assert.AreEqual(-8, indices.First(), "the run has to reach the far kerb");
            Assert.AreEqual(5, indices.Last(), "and still reach the back wall");
            Assert.AreEqual(14, indices.Count, "one piece per world row, no gaps");
        }

        [Test]
        public void GetWorldEdgeIndices_CoverEveryRowOfGroundOnTheSideWalls()
        {
            // The defect this pins: the side runs used to walk the WORKSHOP's indices, so they
            // stopped dead at the shop front and the street's west and east edges were undrawn.
            var covered = new HashSet<int>(FloorLayout.GetWorldEdgeIndices());
            foreach (Vector2Int cell in FloorLayout.GetWorldCells())
            {
                Assert.IsTrue(covered.Contains(cell.y),
                    "row " + cell.y + " of ground has no side-wall piece beside it");
            }
        }

        [Test]
        public void GetWorldEdgeAnchor_DiffersFromTheWorkshopOnlyOnTheSouth()
        {
            const int halfExtent = 5;
            const int streetDepth = 3;

            foreach (FloorLayout.Edge edge in new[]
                     { FloorLayout.Edge.North, FloorLayout.Edge.East, FloorLayout.Edge.West })
            {
                Assert.AreEqual(
                    FloorLayout.GetEdgeAnchor(edge, 2, halfExtent),
                    FloorLayout.GetWorldEdgeAnchor(edge, 2, halfExtent, streetDepth),
                    edge + " is the building's own wall, so the two boundaries coincide");
            }

            Assert.AreEqual(-5.5f,
                FloorLayout.GetEdgeAnchor(FloorLayout.Edge.South, 2, halfExtent).y, 0.0001f,
                "the workshop's skirting stays at the shop front");
            Assert.AreEqual(-8.5f,
                FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.South, 2, halfExtent, streetDepth).y,
                0.0001f, "the kerb sits at the far side of the street");
        }

        [Test]
        public void GetWorldEdgeAnchor_KerbHangsBelowEveryCobbleItEdges()
        {
            // Same relationship the skirting has to the plank floor: the band sits half a cell
            // below the southernmost row's centre, so it hangs off the ground rather than
            // covering it. If this inverts, the kerb draws on top of the last row of street.
            float kerbY = FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.South, 0).y;

            foreach (Vector2Int cell in FloorLayout.GetStreetCells())
            {
                Assert.Less(kerbY, cell.y, "kerb is not south of street row " + cell.y);
            }

            Assert.AreEqual(FloorLayout.WorldMinY - 0.5f, kerbY, 0.0001f);
        }

        [Test]
        public void IsInsideWorkshop_SeparatesTheRoomFromTheStreet()
        {
            Assert.IsTrue(FloorLayout.IsInsideWorkshop(new Vector2Int(0, 0)));
            Assert.IsTrue(FloorLayout.IsInsideWorkshop(new Vector2Int(12, -12)), "the shop front");
            Assert.IsFalse(FloorLayout.IsInsideWorkshop(new Vector2Int(0, -13)), "one step outside");
            Assert.IsFalse(FloorLayout.IsInsideWorkshop(new Vector2Int(13, 0)), "past the east wall");
        }
    }
}
