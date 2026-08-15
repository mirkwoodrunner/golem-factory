using NUnit.Framework;
using GolemFactory.Player;

namespace GolemFactory.Tests.EditMode
{
    public class ArtificerWalkAnimationTests
    {
        private const float Stride = ArtificerWalkAnimation.DefaultStrideLength;

        // --- ComputeFrameIndex: the distance-driven cycle -------------------------------------

        [Test]
        public void ComputeFrameIndex_ZeroDistance_IsStandingFrame()
        {
            Assert.AreEqual(
                ArtificerWalkAnimation.StandingFrameIndex,
                ArtificerWalkAnimation.ComputeFrameIndex(distanceTravelled: 0f, strideLength: Stride));
        }

        [Test]
        public void ComputeFrameIndex_AdvancesOneFramePerStride()
        {
            for (int step = 0; step < ArtificerWalkAnimation.FramesPerDirection; step++)
            {
                // Half a stride past each boundary, so we're clear of floating-point edges.
                float distance = (step + 0.5f) * Stride;

                Assert.AreEqual(
                    step,
                    ArtificerWalkAnimation.ComputeFrameIndex(distance, Stride),
                    "distance {0} should sit on frame {1}", distance, step);
            }
        }

        [Test]
        public void ComputeFrameIndex_WrapsAfterFourFrames()
        {
            float oneCycle = ArtificerWalkAnimation.FramesPerDirection * Stride;

            Assert.AreEqual(
                ArtificerWalkAnimation.ComputeFrameIndex(0.5f * Stride, Stride),
                ArtificerWalkAnimation.ComputeFrameIndex(oneCycle + (0.5f * Stride), Stride));
        }

        [Test]
        public void ComputeFrameIndex_IsAlwaysInRange_AcrossManyCycles()
        {
            // The seam between cycles is where a modulo bug shows up, so walk a long way.
            for (int i = 0; i < 500; i++)
            {
                int frame = ArtificerWalkAnimation.ComputeFrameIndex(i * 0.17f, Stride);

                Assert.GreaterOrEqual(frame, 0);
                Assert.Less(frame, ArtificerWalkAnimation.FramesPerDirection);
            }
        }

        [Test]
        public void ComputeFrameIndex_NonPositiveStride_IsStandingFrame()
        {
            Assert.AreEqual(
                ArtificerWalkAnimation.StandingFrameIndex,
                ArtificerWalkAnimation.ComputeFrameIndex(distanceTravelled: 5f, strideLength: 0f));
            Assert.AreEqual(
                ArtificerWalkAnimation.StandingFrameIndex,
                ArtificerWalkAnimation.ComputeFrameIndex(distanceTravelled: 5f, strideLength: -1f));
        }

        [Test]
        public void ComputeFrameIndex_NegativeDistance_IsStandingFrame()
        {
            Assert.AreEqual(
                ArtificerWalkAnimation.StandingFrameIndex,
                ArtificerWalkAnimation.ComputeFrameIndex(distanceTravelled: -3f, strideLength: Stride));
        }

        [Test]
        public void ComputeFrameIndex_NonFiniteDistance_IsStandingFrame()
        {
            Assert.AreEqual(
                ArtificerWalkAnimation.StandingFrameIndex,
                ArtificerWalkAnimation.ComputeFrameIndex(float.NaN, Stride));
            Assert.AreEqual(
                ArtificerWalkAnimation.StandingFrameIndex,
                ArtificerWalkAnimation.ComputeFrameIndex(float.PositiveInfinity, Stride));
        }

        // --- IsWalking: standing vs actually covering ground ----------------------------------

        [Test]
        public void IsWalking_NoMovement_IsFalse()
        {
            // The wall case: input held, displacement zero, legs must not move.
            Assert.IsFalse(ArtificerWalkAnimation.IsWalking(0f));
        }

        [Test]
        public void IsWalking_FloatNoise_IsFalse()
        {
            Assert.IsFalse(ArtificerWalkAnimation.IsWalking(1e-9f));
        }

        [Test]
        public void IsWalking_ARealFrameOfWalking_IsTrue()
        {
            // 4 u/s at 60fps is ~0.067 units in a frame, four orders of magnitude clear of noise.
            Assert.IsTrue(ArtificerWalkAnimation.IsWalking(4f / 60f));
        }

        // --- AdvanceDistance: the bounded accumulator -----------------------------------------

        [Test]
        public void AdvanceDistance_AccumulatesTravel()
        {
            float d = ArtificerWalkAnimation.AdvanceDistance(0f, 0.1f, Stride);
            Assert.AreEqual(0.1f, d, 1e-5f);

            d = ArtificerWalkAnimation.AdvanceDistance(d, 0.1f, Stride);
            Assert.AreEqual(0.2f, d, 1e-5f);
        }

        [Test]
        public void AdvanceDistance_StaysWithinOneCycle()
        {
            float cycle = ArtificerWalkAnimation.FramesPerDirection * Stride;
            float d = 0f;

            for (int i = 0; i < 10000; i++)
            {
                d = ArtificerWalkAnimation.AdvanceDistance(d, 0.067f, Stride);

                Assert.GreaterOrEqual(d, 0f);
                Assert.Less(d, cycle);
            }
        }

        [Test]
        public void AdvanceDistance_WrappingPreservesTheFrameItLandsOn()
        {
            // Wrapping is an optimisation; it must not change which frame shows.
            float cycle = ArtificerWalkAnimation.FramesPerDirection * Stride;
            float wrapped = ArtificerWalkAnimation.AdvanceDistance(cycle - 0.01f, 0.1f, Stride);
            float unwrapped = (cycle - 0.01f) + 0.1f;

            Assert.AreEqual(
                ArtificerWalkAnimation.ComputeFrameIndex(unwrapped, Stride),
                ArtificerWalkAnimation.ComputeFrameIndex(wrapped, Stride));
        }

        [Test]
        public void AdvanceDistance_NonFiniteOrNegativeStep_IsIgnored()
        {
            Assert.AreEqual(0.5f, ArtificerWalkAnimation.AdvanceDistance(0.5f, float.NaN, Stride), 1e-5f);
            Assert.AreEqual(0.5f, ArtificerWalkAnimation.AdvanceDistance(0.5f, float.PositiveInfinity, Stride), 1e-5f);
            Assert.AreEqual(0.5f, ArtificerWalkAnimation.AdvanceDistance(0.5f, -2f, Stride), 1e-5f);
        }

        [Test]
        public void AdvanceDistance_NonPositiveStride_IsZero()
        {
            Assert.AreEqual(0f, ArtificerWalkAnimation.AdvanceDistance(5f, 1f, 0f));
        }

        // --- ComputeFacing: the row, from the last non-zero movement --------------------------

        [Test]
        public void ComputeFacing_ZeroMovement_KeepsPreviousFacing()
        {
            // The stop case: letting go of the stick must not spin him to face front.
            Assert.AreEqual(
                ArtificerFacing.Up,
                ArtificerWalkAnimation.ComputeFacing(0f, 0f, ArtificerFacing.Up));
            Assert.AreEqual(
                ArtificerFacing.Left,
                ArtificerWalkAnimation.ComputeFacing(0f, 0f, ArtificerFacing.Left));
        }

        [Test]
        public void ComputeFacing_CardinalDirections()
        {
            Assert.AreEqual(ArtificerFacing.Right, ArtificerWalkAnimation.ComputeFacing(1f, 0f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Left, ArtificerWalkAnimation.ComputeFacing(-1f, 0f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Up, ArtificerWalkAnimation.ComputeFacing(0f, 1f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Down, ArtificerWalkAnimation.ComputeFacing(0f, -1f, ArtificerFacing.Up));
        }

        [Test]
        public void ComputeFacing_DirectionChange_TakesTheNewDirection()
        {
            ArtificerFacing facing = ArtificerFacing.Down;

            facing = ArtificerWalkAnimation.ComputeFacing(-1f, 0f, facing);
            Assert.AreEqual(ArtificerFacing.Left, facing);

            facing = ArtificerWalkAnimation.ComputeFacing(0f, 1f, facing);
            Assert.AreEqual(ArtificerFacing.Up, facing);

            // ...and a release mid-turn holds the direction he actually ended up facing.
            facing = ArtificerWalkAnimation.ComputeFacing(0f, 0f, facing);
            Assert.AreEqual(ArtificerFacing.Up, facing);
        }

        [Test]
        public void ComputeFacing_DominantAxisWins()
        {
            Assert.AreEqual(ArtificerFacing.Right, ArtificerWalkAnimation.ComputeFacing(0.9f, 0.2f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Up, ArtificerWalkAnimation.ComputeFacing(0.2f, 0.9f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Down, ArtificerWalkAnimation.ComputeFacing(-0.2f, -0.9f, ArtificerFacing.Up));
        }

        [Test]
        public void ComputeFacing_ExactDiagonal_ResolvesToProfileRow()
        {
            // Documented tie-break: the profile art carries more silhouette than front/back.
            Assert.AreEqual(ArtificerFacing.Right, ArtificerWalkAnimation.ComputeFacing(0.5f, 0.5f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Left, ArtificerWalkAnimation.ComputeFacing(-0.5f, 0.5f, ArtificerFacing.Down));
            Assert.AreEqual(ArtificerFacing.Left, ArtificerWalkAnimation.ComputeFacing(-0.5f, -0.5f, ArtificerFacing.Up));
        }

        // --- ComputeSpriteIndex: the flat 16-frame layout -------------------------------------

        [Test]
        public void ComputeSpriteIndex_RowMajorOverTheSixteenFrames()
        {
            Assert.AreEqual(0, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Down, 0));
            Assert.AreEqual(3, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Down, 3));
            Assert.AreEqual(4, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Left, 0));
            Assert.AreEqual(8, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Right, 0));
            Assert.AreEqual(15, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Up, 3));
        }

        [Test]
        public void ComputeSpriteIndex_LeftAndRightNeverShareAFrame()
        {
            // Guards the "left is not a mirror of right" property at the indexing level: if these
            // ever collided, one whole row of hand-drawn art would go unused on screen.
            for (int frame = 0; frame < ArtificerWalkAnimation.FramesPerDirection; frame++)
            {
                Assert.AreNotEqual(
                    ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Left, frame),
                    ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Right, frame));
            }
        }

        [Test]
        public void ComputeSpriteIndex_CoversEveryFrameExactlyOnce()
        {
            var seen = new bool[ArtificerWalkAnimation.DirectionCount * ArtificerWalkAnimation.FramesPerDirection];

            foreach (ArtificerFacing facing in new[]
                     { ArtificerFacing.Down, ArtificerFacing.Left, ArtificerFacing.Right, ArtificerFacing.Up })
            {
                for (int frame = 0; frame < ArtificerWalkAnimation.FramesPerDirection; frame++)
                {
                    int index = ArtificerWalkAnimation.ComputeSpriteIndex(facing, frame);

                    Assert.IsFalse(seen[index], "sprite index {0} was produced twice", index);
                    seen[index] = true;
                }
            }

            CollectionAssert.DoesNotContain(seen, false, "some authored frame is unreachable");
        }

        [Test]
        public void ComputeSpriteIndex_OutOfRangeFrame_StaysInsideTheRow()
        {
            Assert.AreEqual(4, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Left, 4));
            Assert.AreEqual(5, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Left, 9));
            Assert.AreEqual(7, ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Left, -1));
        }

        [Test]
        public void ComputeSpriteIndex_UnknownFacing_FallsBackToDownRow()
        {
            Assert.AreEqual(0, ArtificerWalkAnimation.ComputeSpriteIndex((ArtificerFacing)99, 0));
        }
    }
}
