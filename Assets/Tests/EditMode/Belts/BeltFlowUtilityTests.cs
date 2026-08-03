using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;

namespace GolemFactory.Tests.EditMode.Belts
{
    public sealed class BeltFlowUtilityTests
    {
        private const int Length = 5;
        private const int Capacity = Length + 1;

        private static List<ItemStack> Lane(params float[] progresses)
        {
            var items = new List<ItemStack>();
            foreach (float p in progresses)
            {
                items.Add(new ItemStack { ItemType = "Scrap", Progress = p });
            }

            return items;
        }

        // --- PredictProgressAfterAdvance --------------------------------------------------
        // The whole point of this function is that it must agree with BeltSegment.Advance
        // exactly, otherwise interpolated items drift away from where the simulation puts them.

        [Test]
        public void PredictProgressAfterAdvance_MatchesBeltSegmentAdvance()
        {
            var segment = new BeltSegment("Test", Length);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            segment.Advance(1f);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            segment.Advance(1f);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            var before = new List<ItemStack>(segment.Items);
            var predicted = new float[before.Count];
            for (int i = 0; i < before.Count; i++)
            {
                predicted[i] = BeltFlowUtility.PredictProgressAfterAdvance(before, i, Length, 1f);
            }

            segment.Advance(1f);

            for (int i = 0; i < segment.Items.Count; i++)
            {
                Assert.AreEqual(segment.Items[i].Progress, predicted[i], 0.0001f,
                    "predicted progress diverged from BeltSegment.Advance at index " + i);
            }
        }

        [Test]
        public void PredictProgressAfterAdvance_ClampsHeadToSegmentLength()
        {
            var items = Lane(Length);
            Assert.AreEqual(Length, BeltFlowUtility.PredictProgressAfterAdvance(items, 0, Length, 1f), 0.0001f);
        }

        [Test]
        public void PredictProgressAfterAdvance_FollowerIsCappedByTheItemAhead()
        {
            // Head is parked at the end; the follower can only close to MinSpacing behind it.
            var items = Lane(Length, Length - 1.5f);
            Assert.AreEqual(Length - BeltSegment.MinSpacing,
                BeltFlowUtility.PredictProgressAfterAdvance(items, 1, Length, 1f), 0.0001f);
        }

        [Test]
        public void PredictProgressAfterAdvance_OutOfRangeIndexReturnsZero()
        {
            var items = Lane(1f);
            Assert.AreEqual(0f, BeltFlowUtility.PredictProgressAfterAdvance(items, 3, Length, 1f), 0.0001f);
            Assert.AreEqual(0f, BeltFlowUtility.PredictProgressAfterAdvance(null, 0, Length, 1f), 0.0001f);
        }

        // --- IsBlocked / IsQueuedBehindAnother --------------------------------------------

        [Test]
        public void IsBlocked_FreeRunningItemIsNotBlocked()
        {
            var items = Lane(2f);
            Assert.IsFalse(BeltFlowUtility.IsBlocked(items, 0, Length, 1f));
        }

        [Test]
        public void IsBlocked_HeadParkedAtEndIsBlocked()
        {
            var items = Lane(Length);
            Assert.IsTrue(BeltFlowUtility.IsBlocked(items, 0, Length, 1f));
        }

        [Test]
        public void IsQueuedBehindAnother_HeadParkedAtEndIsNotAJam()
        {
            // A terminal segment always parks its head at the end waiting for a golem to pull
            // it. Reporting that as congestion would make the jam signal permanently on.
            var items = Lane(Length);
            Assert.IsFalse(BeltFlowUtility.IsQueuedBehindAnother(items, 0, Length, 1f));
        }

        [Test]
        public void IsQueuedBehindAnother_FollowerStackedBehindParkedHeadIsAJam()
        {
            var items = Lane(Length, Length - BeltSegment.MinSpacing);
            Assert.IsTrue(BeltFlowUtility.IsQueuedBehindAnother(items, 1, Length, 1f));
        }

        [Test]
        public void IsQueuedBehindAnother_FollowerWithRoomAheadIsNotAJam()
        {
            var items = Lane(Length, 1f);
            Assert.IsFalse(BeltFlowUtility.IsQueuedBehindAnother(items, 1, Length, 1f));
        }

        // --- Congestion --------------------------------------------------------------------

        [Test]
        public void ComputeCongestion_EmptyLaneIsZero()
        {
            Assert.AreEqual(0f, BeltFlowUtility.ComputeCongestion(Lane(), Capacity, Length, 1f), 0.0001f);
        }

        [Test]
        public void ComputeCongestion_RisesWithEachQueuedItem()
        {
            float one = BeltFlowUtility.ComputeCongestion(
                Lane(Length, Length - 1f), Capacity, Length, 1f);
            float two = BeltFlowUtility.ComputeCongestion(
                Lane(Length, Length - 1f, Length - 2f), Capacity, Length, 1f);

            Assert.Greater(one, 0f);
            Assert.Greater(two, one);
        }

        [Test]
        public void ComputeCongestion_FullyStackedLaneIsOne()
        {
            var items = Lane(Length, Length - 1f, Length - 2f, Length - 3f, Length - 4f, Length - 5f);
            Assert.AreEqual(1f, BeltFlowUtility.ComputeCongestion(items, Capacity, Length, 1f), 0.0001f);
        }

        // --- Display interpolation ---------------------------------------------------------

        [Test]
        public void ComputeDisplayProgress_InterpolatesAcrossTheTick()
        {
            Assert.AreEqual(2f, BeltFlowUtility.ComputeDisplayProgress(2f, 3f, 0f), 0.0001f);
            Assert.AreEqual(2.5f, BeltFlowUtility.ComputeDisplayProgress(2f, 3f, 0.5f), 0.0001f);
            Assert.AreEqual(3f, BeltFlowUtility.ComputeDisplayProgress(2f, 3f, 1f), 0.0001f);
        }

        [Test]
        public void ComputeDisplayProgress_BlockedItemDoesNotMoveAtAnyTickFraction()
        {
            Assert.AreEqual(4f, BeltFlowUtility.ComputeDisplayProgress(4f, 4f, 0.5f), 0.0001f);
            Assert.AreEqual(4f, BeltFlowUtility.ComputeDisplayProgress(4f, 4f, 1f), 0.0001f);
        }

        [Test]
        public void ComputeDisplayProgress_ClampsTickFractionOutsideZeroToOne()
        {
            Assert.AreEqual(2f, BeltFlowUtility.ComputeDisplayProgress(2f, 3f, -1f), 0.0001f);
            Assert.AreEqual(3f, BeltFlowUtility.ComputeDisplayProgress(2f, 3f, 4f), 0.0001f);
        }

        // --- Tread / arrow maths -----------------------------------------------------------

        [Test]
        public void ComputeTreadSpeed_MatchesTheSpeedAnUnobstructedItemTravels()
        {
            // 2 world units of lane, 4 ticks to cross it, 10 ticks/sec => 5 world units/sec.
            Assert.AreEqual(5f, BeltFlowUtility.ComputeTreadSpeed(2f, 4, 10f, 1f), 0.0001f);
        }

        [Test]
        public void ComputeTreadSpeed_ScalesWithClockSpeedAndIsZeroWhenPaused()
        {
            Assert.AreEqual(10f, BeltFlowUtility.ComputeTreadSpeed(2f, 4, 10f, 2f), 0.0001f);
            Assert.AreEqual(0f, BeltFlowUtility.ComputeTreadSpeed(2f, 4, 10f, 0f), 0.0001f);
        }

        [Test]
        public void ComputeTreadSpeed_ZeroLengthSegmentDoesNotDivideByZero()
        {
            Assert.AreEqual(0f, BeltFlowUtility.ComputeTreadSpeed(2f, 0, 10f, 1f), 0.0001f);
        }

        [Test]
        public void AdvanceScrollPhase_WrapsWithinSpacing()
        {
            float phase = BeltFlowUtility.AdvanceScrollPhase(0.3f, 1f, 0.4f, 0.5f);
            Assert.AreEqual(0.2f, phase, 0.0001f);
            Assert.GreaterOrEqual(phase, 0f);
            Assert.Less(phase, 0.5f);
        }

        [Test]
        public void AdvanceScrollPhase_ZeroSpacingDoesNotDivideByZero()
        {
            Assert.AreEqual(0f, BeltFlowUtility.AdvanceScrollPhase(0.3f, 1f, 0.4f, 0f), 0.0001f);
        }

        [Test]
        public void ComputeArrowCount_CoversTheWholeLanePlusOneForWraparound()
        {
            Assert.AreEqual(6, BeltFlowUtility.ComputeArrowCount(2f, 0.4f));
            Assert.AreEqual(1, BeltFlowUtility.ComputeArrowCount(2f, 0f));
            Assert.GreaterOrEqual(BeltFlowUtility.ComputeArrowCount(0f, 0.4f), 1);
        }

        // --- Cargo fit ----------------------------------------------------------------------

        [Test]
        public void ComputeItemScale_ShrinksCargoToTheLanesItemSpacing()
        {
            // 1.2 world of lane / 5 ticks = 0.24 world per item; a 0.5-world sprite has to come
            // down to ~0.6 scale before a packed lane stops merging into one smear.
            float scale = BeltFlowUtility.ComputeItemScale(1.2f, 5, 0.5f, 1.25f, 0.45f, 1f);
            Assert.AreEqual(0.6f, scale, 0.0001f);
        }

        [Test]
        public void ComputeItemScale_NeverExceedsMaxScaleOnARoomyLane()
        {
            Assert.AreEqual(1f, BeltFlowUtility.ComputeItemScale(20f, 5, 0.5f, 1.25f, 0.45f, 1f), 0.0001f);
        }

        [Test]
        public void ComputeItemScale_NeverGoesBelowMinScaleOnACrampedLane()
        {
            Assert.AreEqual(0.45f, BeltFlowUtility.ComputeItemScale(0.2f, 20, 0.5f, 1.25f, 0.45f, 1f), 0.0001f);
        }

        [Test]
        public void ComputeItemScale_DegenerateInputsFallBackToMaxScale()
        {
            Assert.AreEqual(1f, BeltFlowUtility.ComputeItemScale(1.2f, 0, 0.5f, 1.25f, 0.45f, 1f), 0.0001f);
            Assert.AreEqual(1f, BeltFlowUtility.ComputeItemScale(1.2f, 5, 0f, 1.25f, 0.45f, 1f), 0.0001f);
        }

        [Test]
        public void ComputeLaneAngleDegrees_PointsAlongTheLane()
        {
            Assert.AreEqual(0f, BeltFlowUtility.ComputeLaneAngleDegrees(Vector2.zero, new Vector2(1f, 0f)), 0.001f);
            Assert.AreEqual(90f, BeltFlowUtility.ComputeLaneAngleDegrees(Vector2.zero, new Vector2(0f, 1f)), 0.001f);
            Assert.AreEqual(135f, BeltFlowUtility.ComputeLaneAngleDegrees(Vector2.zero, new Vector2(-1f, 1f)), 0.001f);
        }

        [Test]
        public void ComputeLaneAngleDegrees_DegenerateLaneReturnsZero()
        {
            Assert.AreEqual(0f, BeltFlowUtility.ComputeLaneAngleDegrees(Vector2.one, Vector2.one), 0.0001f);
        }

        [Test]
        public void ComputeArrowFade_IsZeroAtBothMouthsAndFullInTheMiddle()
        {
            Assert.AreEqual(0f, BeltFlowUtility.ComputeArrowFade(0f, 2f, 0.5f), 0.0001f);
            Assert.AreEqual(1f, BeltFlowUtility.ComputeArrowFade(1f, 2f, 0.5f), 0.0001f);
            Assert.AreEqual(0f, BeltFlowUtility.ComputeArrowFade(2f, 2f, 0.5f), 0.0001f);
        }

        [Test]
        public void ComputeArrowFade_OutsideTheLaneIsInvisible()
        {
            Assert.AreEqual(0f, BeltFlowUtility.ComputeArrowFade(-0.1f, 2f, 0.5f), 0.0001f);
            Assert.AreEqual(0f, BeltFlowUtility.ComputeArrowFade(2.1f, 2f, 0.5f), 0.0001f);
            Assert.AreEqual(0f, BeltFlowUtility.ComputeArrowFade(1f, 0f, 0.5f), 0.0001f);
        }
    }
}
