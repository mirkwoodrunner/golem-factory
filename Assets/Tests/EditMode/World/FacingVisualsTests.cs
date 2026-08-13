using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Under the ISOMETRIC projection the grid and the screen genuinely disagreed, and getting the
    // diagonal wrong was the exact mistake that would make every facing arrow point at the wrong
    // tile. Top-down makes them agree, so these now assert the plain cardinal directions -- but
    // the suite is kept (rather than deleted as trivial) because FacingVisuals still DERIVES the
    // angle from the projection, and a future projection change should break these loudly again.
    public class FacingVisualsTests
    {
        // Square since the top-down switch; was 1 x 0.5 for the 2:1 isometric run.
        private static readonly Vector2 CellSize = new Vector2(1f, 1f);

        [Test]
        public void NorthPointsStraightUpOnScreen()
        {
            // Grid north is (0,+1). Isometric rendered that up-and-LEFT; top-down renders it
            // straight up, so a naive 90-degree rotation is now the CORRECT answer.
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.North, CellSize);

            Assert.AreEqual(0f, direction.x, 1e-4f, "north should not lean sideways on screen");
            Assert.Greater(direction.y, 0f, "north should point up on screen");
        }

        [Test]
        public void EastPointsStraightRightOnScreen()
        {
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.East, CellSize);

            Assert.Greater(direction.x, 0f);
            Assert.AreEqual(0f, direction.y, 1e-4f, "east should not lean up or down on screen");
        }

        [Test]
        public void SouthPointsStraightDownOnScreen()
        {
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.South, CellSize);

            Assert.AreEqual(0f, direction.x, 1e-4f, "south should not lean sideways on screen");
            Assert.Less(direction.y, 0f);
        }

        [Test]
        public void WestPointsStraightLeftOnScreen()
        {
            Vector2 direction = FacingVisuals.ScreenDirection(Facing.West, CellSize);

            Assert.Less(direction.x, 0f);
            Assert.AreEqual(0f, direction.y, 1e-4f, "west should not lean up or down on screen");
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

        // DELETED WITH THE ISOMETRIC PROJECTION: TheAngleTracksTheCellAspectRatio.
        //
        // It asserted that a taller cell raises the on-screen angle, and that a square cell
        // projects east at 45 degrees. Both were properties of the isometric transform mixing x
        // and y. Top-down maps each axis independently, so after normalisation a facing's screen
        // direction is aspect-INDEPENDENT by construction -- east is (1,0) for every cell size.
        // There is no top-down statement of that test that could fail, so it was removed rather
        // than rewritten into a tautology. The property it really guarded (the angle is derived,
        // not hardcoded) is now covered by the cardinal tests above plus
        // TheScreenAngleMatchesTheScreenDirection.
        [Test]
        public void ScreenDirectionIsAspectIndependentUnderTopDown()
        {
            // The replacement statement worth making: cell size scales the world step but not the
            // normalised direction. If someone reintroduces an axis-mixing projection, this fails.
            foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
            {
                Vector2 square = FacingVisuals.ScreenDirection(facing, new Vector2(1f, 1f));
                Vector2 tall = FacingVisuals.ScreenDirection(facing, new Vector2(1f, 3f));

                Assert.AreEqual(square.x, tall.x, 1e-4f, $"{facing} x changed with cell aspect");
                Assert.AreEqual(square.y, tall.y, 1e-4f, $"{facing} y changed with cell aspect");
            }
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
