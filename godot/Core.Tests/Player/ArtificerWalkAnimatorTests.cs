using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Player;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Player
{
    /// <summary>
    /// Ported from Unity's PlayMode ArtificerWalkAnimatorTests onto <see cref="PlayerWalker"/>.
    /// Unity's tests named the sprite on the renderer; here they name the frame INDEX the walker
    /// picks, which is the same thing one step earlier -- PlayerNode draws frame[SpriteIndex].
    /// UnwiredFrames_LeaveTheSpriteAlone is retired: the Godot node loads its sixteen frames from
    /// res://art at startup, so there is no half-wired frame array to guard against.
    /// </summary>
    public class ArtificerWalkAnimatorTests
    {
        private static PlayerWalker Build() => new PlayerWalker(moveSpeed: 4f);

        [Test]
        public void StandingStill_ShowsTheStandingFrame()
        {
            PlayerWalker walker = Build();
            walker.MoveBy(Vector2.zero, 0.1f);

            Assert.AreEqual(
                ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Down, ArtificerWalkAnimation.StandingFrameIndex),
                walker.SpriteIndex);
        }

        [Test]
        public void WalkingRight_UsesTheRightRow()
        {
            PlayerWalker walker = Build();
            walker.MoveBy(Vector2.right, 0.1f);

            Assert.AreEqual(ArtificerFacing.Right, walker.Facing);
            Assert.GreaterOrEqual(walker.SpriteIndex, 8);
            Assert.LessOrEqual(walker.SpriteIndex, 11);
        }

        [Test]
        public void WalkingLeft_UsesTheLeftRow_NotAFlippedRightRow()
        {
            // The art's own constraint: the two profile rows are drawn separately, so facing left
            // must land in the left row. (PlayerNode never sets FlipH; there is nothing to flip.)
            PlayerWalker walker = Build();
            walker.MoveBy(Vector2.left, 0.1f);

            Assert.AreEqual(ArtificerFacing.Left, walker.Facing);
            Assert.GreaterOrEqual(walker.SpriteIndex, 4);
            Assert.LessOrEqual(walker.SpriteIndex, 7);
        }

        [Test]
        public void WalkingUpAndDown_UseTheirOwnRows()
        {
            PlayerWalker walker = Build();

            walker.MoveBy(Vector2.up, 0.1f);
            Assert.AreEqual(ArtificerFacing.Up, walker.Facing);
            Assert.GreaterOrEqual(walker.SpriteIndex, 12);

            walker.MoveBy(Vector2.down, 0.1f);
            Assert.AreEqual(ArtificerFacing.Down, walker.Facing);
            Assert.LessOrEqual(walker.SpriteIndex, 3);
        }

        [Test]
        public void Walking_AdvancesThroughFrames()
        {
            PlayerWalker walker = Build();
            var seen = new HashSet<int>();
            for (int i = 0; i < 12; i++)
            {
                // A third of a stride per step, so the cycle is walked rather than jumped.
                walker.MoveBy(Vector2.right, ArtificerWalkAnimation.DefaultStrideLength / 3f / 4f);
                seen.Add(walker.SpriteIndex);
            }

            Assert.GreaterOrEqual(seen.Count, 4, "the cycle should visit all four frames of the row");
        }

        [Test]
        public void StoppingMidStride_ReturnsToTheStandingFrame()
        {
            // The stop case the whole distance-driven design exists for: no frozen mid-stride pose.
            PlayerWalker walker = Build();
            walker.MoveBy(Vector2.right, 0.1f);
            walker.MoveBy(Vector2.right, 0.1f);

            walker.MoveBy(Vector2.zero, 0.1f);

            Assert.AreEqual(
                ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Right, ArtificerWalkAnimation.StandingFrameIndex),
                walker.SpriteIndex,
                "stopping should stand, still facing the way he was walking");
        }

        [Test]
        public void BlockedByFloorBounds_DoesNotAnimate()
        {
            // Input held, displacement zero. This is the skate the design is built to prevent, and
            // it is why the frame comes from travel rather than from what the player is pressing.
            PlayerWalker walker = Build();
            var converter = new GridCoordinateConverter(new Vector2(1f, 1f));
            walker.SetFloorBounds(converter, 12);

            walker.Position = converter.CellToWorldCenter(new Vector2Int(20, 0));
            walker.MoveBy(Vector2.zero, 0f);

            for (int i = 0; i < 5; i++)
            {
                walker.MoveBy(Vector2.right, 0.1f);

                Assert.AreEqual(
                    ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Right, ArtificerWalkAnimation.StandingFrameIndex),
                    walker.SpriteIndex,
                    "pressing into a bound must face him at it without walking him on the spot");
            }
        }
    }
}
