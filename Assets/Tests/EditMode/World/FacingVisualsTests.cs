using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The grid and the screen genuinely disagree, and getting the diagonal wrong is the exact
    // mistake that would make every facing arrow in the game point at the wrong tile.
    public class FacingVisualsTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 0.5f);

        [Test]
        public void NorthPointsUpAndLeftOnScreen_NotStraightUp()
        {
            // The whole reason this class exists. Grid north is (0,+1), which the isometric
            // projection renders as up-and-LEFT -- a naive 90-degree rotation would be wrong.
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.North, CellSize);

            Assert.Less(direction.x, 0f, "north should lean left on screen");
            Assert.Greater(direction.y, 0f, "north should lean up on screen");
        }

        [Test]
        public void EastPointsUpAndRightOnScreen()
        {
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.East, CellSize);

            Assert.Greater(direction.x, 0f);
            Assert.Greater(direction.y, 0f);
        }

        [Test]
        public void SouthPointsDownAndRight()
        {
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.South, CellSize);

            Assert.Greater(direction.x, 0f);
            Assert.Less(direction.y, 0f);
        }

        [Test]
        public void WestPointsDownAndLeft()
        {
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.West, CellSize);

            Assert.Less(direction.x, 0f);
            Assert.Less(direction.y, 0f);
        }

        [Test]
        public void EveryDirectionIsUnitLength()
        {
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Assert.AreEqual(1f, FacingVisuals.ScreenDirection(facing, CellSize).magnitude, 1e-4f,
                    $"{facing} was not normalised");
            }
        }

        [Test]
        public void OppositeFacingsPointOppositeWaysOnScreen()
        {
            Vector2 north = FacingVisuals.ScreenDirection(Facing.North, CellSize);
            Vector2 south = FacingVisuals.ScreenDirection(Facing.South, CellSize);

            Assert.AreEqual(-north.x, south.x, 1e-4f);
            Assert.AreEqual(-north.y, south.y, 1e-4f);
        }

        [Test]
        public void TheScreenAngleMatchesTheScreenDirection()
        {
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                float degrees = FacingVisuals.ScreenAngleDegrees(facing, CellSize);
                Vector2 fromAngle = new Vector2(
                    Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
                Vector2 direction = FacingVisuals.ScreenDirection(facing, CellSize);

                Assert.AreEqual(direction.x, fromAngle.x, 1e-3f, $"{facing} x");
                Assert.AreEqual(direction.y, fromAngle.y, 1e-3f, $"{facing} y");
            }
        }

        [Test]
        public void TheAngleTracksTheCellAspectRatio()
        {
            // Derived from the projection rather than hardcoded, so a retuned cell size stays
            // correct instead of silently skewing every arrow.
            float flat = FacingVisuals.ScreenAngleDegrees(Facing.East, new Vector2(1f, 0.5f));
            float steep = FacingVisuals.ScreenAngleDegrees(Facing.East, new Vector2(1f, 1f));

            Assert.Less(flat, steep, "a taller cell should raise the on-screen angle");
            Assert.AreEqual(45f, steep, 1e-3f, "a square cell projects east at 45 degrees");
        }

        [Test]
        public void DescribeUsesCompassLetters()
        {
            Assert.AreEqual("N", FacingVisuals.Describe(Facing.North));
            Assert.AreEqual("E", FacingVisuals.Describe(Facing.East));
            Assert.AreEqual("S", FacingVisuals.Describe(Facing.South));
            Assert.AreEqual("W", FacingVisuals.Describe(Facing.West));
        }
    }
}
