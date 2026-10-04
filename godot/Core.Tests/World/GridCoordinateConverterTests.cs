using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class GridCoordinateConverterTests
    {
        // Square since the top-down switch; was 1 x 0.5 for the 2:1 isometric run.
        private static readonly Vector2 CellSize = new Vector2(1f, 1f);

        // THE ONE TEST THAT ACTUALLY OWNS THE PROJECTION'S SHAPE.
        //
        // Everything else in this file is a round-trip or an inequality, and every one of them
        // passed under isometric AND passes under top-down -- so the suite nominally covering the
        // projection had no assertion that would have noticed it change. Under the 2:1 diamond,
        // cell (1,0) landed at (0.5, 0.25) and cell (0,1) at (-0.5, 0.25): the axes were MIXED,
        // and a unit step along either one moved you diagonally on screen. Top-down maps each
        // axis independently, which is the whole content of the switch.
        [Test]
        public void CellToWorldCenter_MapsEachAxisIndependently_NotAsADiamond()
        {
            var converter = new GridCoordinateConverter(CellSize);

            Vector3 east = converter.CellToWorldCenter(new Vector2Int(1, 0));
            Vector3 north = converter.CellToWorldCenter(new Vector2Int(0, 1));

            Assert.AreEqual(1f, east.x, 1e-4f, "a step in +X must move only in world X");
            Assert.AreEqual(0f, east.y, 1e-4f, "a step in +X must not move in world Y");
            Assert.AreEqual(0f, north.x, 1e-4f, "a step in +Y must not move in world X");
            Assert.AreEqual(1f, north.y, 1e-4f, "a step in +Y must move only in world Y");
        }

        // The corollary, and the property the floor/wall geometry actually leans on: the cell
        // grid's boundary lines sit at half-integers in world space.
        [Test]
        public void CellFractionToWorld_PutsTheCellBoundaryAtAHalfStep()
        {
            var converter = new GridCoordinateConverter(CellSize);

            Vector3 boundary = converter.CellFractionToWorld(new Vector2(12.5f, 0f));

            Assert.AreEqual(12.5f, boundary.x, 1e-4f);
            Assert.AreEqual(0f, boundary.y, 1e-4f);
        }

        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(0, 3)]
        [TestCase(2, -4)]
        [TestCase(-5, -5)]
        [TestCase(7, 2)]
        public void CellToWorldCenter_ThenWorldToCell_RoundTrips(int x, int y)
        {
            var converter = new GridCoordinateConverter(CellSize);
            var cell = new Vector2Int(x, y);

            Vector3 world = converter.CellToWorldCenter(cell);
            Vector2Int result = converter.WorldToCell(world);

            Assert.AreEqual(cell, result);
        }

        [Test]
        public void CellToWorldCenter_OriginIsWorldOrigin()
        {
            var converter = new GridCoordinateConverter(CellSize);

            Vector3 world = converter.CellToWorldCenter(Vector2Int.zero);

            Assert.AreEqual(Vector3.zero, world);
        }

        [Test]
        public void CellToWorldCenter_DifferentCells_MapToDifferentWorldPositions()
        {
            var converter = new GridCoordinateConverter(CellSize);

            Vector3 a = converter.CellToWorldCenter(new Vector2Int(1, 0));
            Vector3 b = converter.CellToWorldCenter(new Vector2Int(0, 1));

            Assert.AreNotEqual(a, b);
        }

        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(2, -4)]
        [TestCase(-5, -5)]
        public void CellFractionToWorld_ThenWorldToCellFraction_RoundTrips(int x, int y)
        {
            var converter = new GridCoordinateConverter(CellSize);
            var cellFraction = new Vector2(x, y);

            Vector3 world = converter.CellFractionToWorld(cellFraction);
            Vector2 result = converter.WorldToCellFraction(world);

            Assert.AreEqual(cellFraction.x, result.x, 0.0001f);
            Assert.AreEqual(cellFraction.y, result.y, 0.0001f);
        }

        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(2, -4)]
        [TestCase(-5, -5)]
        public void WorldToCellFraction_OnIntegerCell_MatchesWorldToCellRoundedResult(int x, int y)
        {
            var converter = new GridCoordinateConverter(CellSize);
            Vector3 world = converter.CellToWorldCenter(new Vector2Int(x, y));

            Vector2 fraction = converter.WorldToCellFraction(world);
            Vector2Int rounded = converter.WorldToCell(world);

            Assert.AreEqual(rounded.x, Mathf.RoundToInt(fraction.x));
            Assert.AreEqual(rounded.y, Mathf.RoundToInt(fraction.y));
        }
    }
}
