using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class GridCoordinateConverterTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 0.5f);

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
