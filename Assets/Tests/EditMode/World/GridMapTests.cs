using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class GridMapTests
    {
        [Test]
        public void TryOccupy_EmptyCell_Succeeds()
        {
            var map = new GridMap();
            var cell = new Vector2Int(1, 1);

            bool result = map.TryOccupy(cell, new object());

            Assert.IsTrue(result);
            Assert.IsTrue(map.IsOccupied(cell));
        }

        [Test]
        public void TryOccupy_AlreadyOccupiedCell_FailsAndKeepsOriginalOccupant()
        {
            var map = new GridMap();
            var cell = new Vector2Int(0, 0);
            var first = new object();
            map.TryOccupy(cell, first);

            bool result = map.TryOccupy(cell, new object());

            Assert.IsFalse(result);
            map.TryGetOccupant(cell, out object occupant);
            Assert.AreSame(first, occupant);
        }

        [Test]
        public void Free_ThenOccupy_Succeeds()
        {
            var map = new GridMap();
            var cell = new Vector2Int(2, -3);
            map.TryOccupy(cell, new object());

            map.Free(cell);

            Assert.IsFalse(map.IsOccupied(cell));
            Assert.IsTrue(map.TryOccupy(cell, new object()));
        }

        [Test]
        public void Free_EmptyCell_DoesNotThrow()
        {
            var map = new GridMap();

            Assert.DoesNotThrow(() => map.Free(new Vector2Int(5, 5)));
        }

        [Test]
        public void TryGetOccupant_EmptyCell_ReturnsFalse()
        {
            var map = new GridMap();

            bool result = map.TryGetOccupant(new Vector2Int(9, 9), out object occupant);

            Assert.IsFalse(result);
            Assert.IsNull(occupant);
        }
    }
}
