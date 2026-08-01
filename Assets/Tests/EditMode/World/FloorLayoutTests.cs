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
