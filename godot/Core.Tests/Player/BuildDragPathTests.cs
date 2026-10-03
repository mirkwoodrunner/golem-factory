using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // What a click-and-drag actually lays, as pure cell math with no pointer and no scene.
    public class BuildDragPathTests
    {
        private static List<Vector2Int> Walk(Vector2Int from, Vector2Int to)
        {
            var cells = new List<Vector2Int>();
            BuildDragPath.AppendCells(from, to, cells);
            return cells;
        }

        [Test]
        public void AStraightRun_LaysEveryCellButTheAnchor()
        {
            // The anchor is already placed -- it is the click that started the drag.
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0),
                },
                Walk(new Vector2Int(0, 0), new Vector2Int(3, 0)));
        }

        [Test]
        public void ADragThatWentNowhere_LaysNothing()
        {
            CollectionAssert.IsEmpty(Walk(new Vector2Int(2, 2), new Vector2Int(2, 2)));
        }

        [Test]
        public void ADiagonalDrag_BuildsAnLRatherThanAStaircase()
        {
            // x first, then y. A staircase would be a corner on every single cell -- both ugly
            // and, at four belts a turn, far more expensive than the run the player is drawing.
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0),
                    new Vector2Int(2, 1), new Vector2Int(2, 2),
                },
                Walk(new Vector2Int(0, 0), new Vector2Int(2, 2)));
        }

        [Test]
        public void ItWalksBackwardsAndDownwardsToo()
        {
            CollectionAssert.AreEqual(
                new[] { new Vector2Int(-1, 0), new Vector2Int(-1, -1) },
                Walk(new Vector2Int(0, 0), new Vector2Int(-1, -1)));
        }

        [Test]
        public void EveryCellIsOneOrthogonalStepFromTheOneBefore()
        {
            // The property the whole feature rests on: each step has a facing, which is how a
            // dragged belt run comes out pointing along itself.
            var from = new Vector2Int(-4, 7);
            var to = new Vector2Int(9, -3);
            List<Vector2Int> cells = Walk(from, to);

            Vector2Int previous = from;
            foreach (Vector2Int cell in cells)
            {
                Vector2Int delta = cell - previous;
                Assert.AreEqual(1, Mathf.Abs(delta.x) + Mathf.Abs(delta.y),
                    previous + " -> " + cell + " is not one orthogonal step");
                previous = cell;
            }

            Assert.AreEqual(to, previous, "the run must actually reach the cursor");
        }

        [Test]
        public void AStepNamesTheFacingItWentIn()
        {
            var origin = new Vector2Int(0, 0);
            Assert.AreEqual(Facing.North,
                BuildDragPath.StepFacing(origin, new Vector2Int(0, 1), Facing.West));
            Assert.AreEqual(Facing.East,
                BuildDragPath.StepFacing(origin, new Vector2Int(1, 0), Facing.West));
            Assert.AreEqual(Facing.South,
                BuildDragPath.StepFacing(origin, new Vector2Int(0, -1), Facing.West));
            Assert.AreEqual(Facing.West,
                BuildDragPath.StepFacing(origin, new Vector2Int(-1, 0), Facing.North));
        }

        [Test]
        public void ANonStep_FallsBackToTheCursorsOwnFacing()
        {
            // A caller that has lost track of its path gets the facing the ghost is showing
            // rather than a silently wrong one.
            Assert.AreEqual(Facing.South, BuildDragPath.StepFacing(
                new Vector2Int(0, 0), new Vector2Int(3, 4), Facing.South));
            Assert.AreEqual(Facing.South, BuildDragPath.StepFacing(
                new Vector2Int(0, 0), new Vector2Int(0, 0), Facing.South));
        }
    }
}
