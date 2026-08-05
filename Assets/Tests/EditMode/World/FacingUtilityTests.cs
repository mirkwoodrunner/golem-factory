using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class FacingUtilityTests
    {
        [Test]
        public void Delta_UsesTopDownGridAxes()
        {
            // The simulation grid is a plain square grid (GridMap is Vector2Int-indexed and
            // decoupled from rendering); isometric is presentation only.
            Assert.AreEqual(new Vector2Int(0, 1), FacingUtility.Delta(Facing.North));
            Assert.AreEqual(new Vector2Int(1, 0), FacingUtility.Delta(Facing.East));
            Assert.AreEqual(new Vector2Int(0, -1), FacingUtility.Delta(Facing.South));
            Assert.AreEqual(new Vector2Int(-1, 0), FacingUtility.Delta(Facing.West));
        }

        [Test]
        public void TargetCell_IsTheTileInFront()
        {
            var cell = new Vector2Int(3, 5);
            Assert.AreEqual(new Vector2Int(3, 6), FacingUtility.TargetCell(cell, Facing.North));
            Assert.AreEqual(new Vector2Int(4, 5), FacingUtility.TargetCell(cell, Facing.East));
            Assert.AreEqual(new Vector2Int(3, 4), FacingUtility.TargetCell(cell, Facing.South));
            Assert.AreEqual(new Vector2Int(2, 5), FacingUtility.TargetCell(cell, Facing.West));
        }

        [Test]
        public void SourceCell_IsTheTileBehind()
        {
            var cell = new Vector2Int(3, 5);
            Assert.AreEqual(new Vector2Int(3, 4), FacingUtility.SourceCell(cell, Facing.North));
            Assert.AreEqual(new Vector2Int(2, 5), FacingUtility.SourceCell(cell, Facing.East));
            Assert.AreEqual(new Vector2Int(3, 6), FacingUtility.SourceCell(cell, Facing.South));
            Assert.AreEqual(new Vector2Int(4, 5), FacingUtility.SourceCell(cell, Facing.West));
        }

        [Test]
        public void SourceAndTarget_AreCollinearThroughTheGolem()
        {
            // "source -> golem -> target" must read as one straight line; that single-axis
            // property is the whole readability argument for spatial routing.
            var cell = new Vector2Int(-2, 7);
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Vector2Int source = FacingUtility.SourceCell(cell, facing);
                Vector2Int target = FacingUtility.TargetCell(cell, facing);
                Assert.AreEqual(cell - source, target - cell,
                    "source and target are not on the same axis for " + facing);
                Assert.AreEqual(target - source, FacingUtility.Delta(facing) * 2);
            }
        }

        [Test]
        public void Opposite_FlipsEachDirection()
        {
            Assert.AreEqual(Facing.South, FacingUtility.Opposite(Facing.North));
            Assert.AreEqual(Facing.West, FacingUtility.Opposite(Facing.East));
            Assert.AreEqual(Facing.North, FacingUtility.Opposite(Facing.South));
            Assert.AreEqual(Facing.East, FacingUtility.Opposite(Facing.West));
        }

        [Test]
        public void Opposite_SwapsSourceAndTarget()
        {
            var cell = new Vector2Int(1, 1);
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Facing flipped = FacingUtility.Opposite(facing);
                Assert.AreEqual(FacingUtility.TargetCell(cell, facing), FacingUtility.SourceCell(cell, flipped));
                Assert.AreEqual(FacingUtility.SourceCell(cell, facing), FacingUtility.TargetCell(cell, flipped));
            }
        }

        [Test]
        public void RotateClockwise_CyclesNorthEastSouthWest()
        {
            Assert.AreEqual(Facing.East, FacingUtility.RotateClockwise(Facing.North));
            Assert.AreEqual(Facing.South, FacingUtility.RotateClockwise(Facing.East));
            Assert.AreEqual(Facing.West, FacingUtility.RotateClockwise(Facing.South));
            Assert.AreEqual(Facing.North, FacingUtility.RotateClockwise(Facing.West));
        }

        [Test]
        public void RotateClockwise_FourTimes_IsIdentity()
        {
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Facing rotated = facing;
                for (int i = 0; i < 4; i++)
                {
                    rotated = FacingUtility.RotateClockwise(rotated);
                }

                Assert.AreEqual(facing, rotated);
            }
        }

        [Test]
        public void RotateClockwise_TwiceIsOpposite()
        {
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Assert.AreEqual(
                    FacingUtility.Opposite(facing),
                    FacingUtility.RotateClockwise(FacingUtility.RotateClockwise(facing)));
            }
        }
    }
}
