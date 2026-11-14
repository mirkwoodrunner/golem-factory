using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Belts;

namespace GolemFactory.Tests.EditMode.Belts
{
    public sealed class BeltFlowUtilityTests
    {
        private const int Length = 5;

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
    }
}
