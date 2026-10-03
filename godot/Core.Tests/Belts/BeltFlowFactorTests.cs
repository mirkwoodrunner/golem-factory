using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Belts;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The arrows used to scroll at <c>speed x (1 - congestion)</c>, which measures how FULL a
    /// lane is rather than how fast it is running. It was wrong in both directions: a lane
    /// half-queued behind a parked head still handing off at the front had its arrows halved,
    /// and a lane whose single item could not move at all -- congestion zero, by design -- ran
    /// them at full speed.
    /// </summary>
    public class BeltFlowFactorTests
    {
        private const int Length = 4;
        private const int Capacity = 5;

        private static List<ItemStack> Lane(params float[] progresses)
        {
            var items = new List<ItemStack>();
            for (int i = 0; i < progresses.Length; i++)
            {
                items.Add(new ItemStack { ItemType = "Scrap", Progress = progresses[i] });
            }

            return items;
        }

        [Test]
        public void EmptyLane_FlowsFully()
        {
            // Nothing is being held up, so the arrows run at full speed on an empty belt --
            // which is also how the player reads "this belt is alive but idle".
            Assert.AreEqual(1f, BeltFlowUtility.ComputeFlowFactor(Lane(), Length, 1f), 0.0001f);
        }

        [Test]
        public void UnobstructedItem_FlowsFully()
        {
            Assert.AreEqual(1f, BeltFlowUtility.ComputeFlowFactor(Lane(0f), Length, 1f), 0.0001f);
        }

        [Test]
        public void SingleItemParkedAtTheEnd_StopsTheArrowsWithoutRaisingTheAlarm()
        {
            // The old formula got this exactly backwards: congestion is 0 here (a parked head
            // is deliberately not counted as a jam -- a terminal belt parks its head forever
            // waiting for a golem), so "speed x (1 - congestion)" ran the arrows at FULL speed
            // over cargo that was not moving at all.
            List<ItemStack> parked = Lane(Length);

            Assert.AreEqual(0f, BeltFlowUtility.ComputeFlowFactor(parked, Length, 1f), 0.0001f);
            Assert.AreEqual(0f, BeltFlowUtility.ComputeCongestion(parked, Capacity, Length, 1f), 0.0001f,
                "The alarm channel must stay quiet, or every terminal belt is permanently red.");
        }

        [Test]
        public void PartiallyBlockedLane_RunsAtTheSpeedItsMovingCargoAchieves()
        {
            // Head parked at the end, the one behind it free to close up by a full step. The
            // arrows keep pace with the item that IS moving: freezing them over visibly moving
            // cargo would be a worse lie than the under-report being fixed.
            List<ItemStack> items = Lane(Length, Length - 2f);

            Assert.AreEqual(1f, BeltFlowUtility.ComputeFlowFactor(items, Length, 1f), 0.0001f);
        }

        [Test]
        public void CongestedButStillMovingLane_NoLongerHalvesTheArrowSpeed()
        {
            // Two of four queue slots blocked behind a parked head, with the tail still running
            // free. Congestion says 0.5, so the old formula halved the arrows while the lane
            // was keeping up at the front.
            List<ItemStack> items = Lane(Length, Length - 1f, Length - 2f, 0f);

            Assert.AreEqual(
                0.5f, BeltFlowUtility.ComputeCongestion(items, Capacity, Length, 1f), 0.0001f);
            Assert.AreEqual(1f, BeltFlowUtility.ComputeFlowFactor(items, Length, 1f), 0.0001f);
        }

        [Test]
        public void FullyStackedLane_IsCompletelyStopped()
        {
            List<ItemStack> items = Lane(Length, Length - 1f, Length - 2f, Length - 3f, Length - 4f);

            Assert.AreEqual(0f, BeltFlowUtility.ComputeFlowFactor(items, Length, 1f), 0.0001f);
            Assert.AreEqual(1f, BeltFlowUtility.ComputeCongestion(items, Capacity, Length, 1f), 0.0001f);
        }

        [Test]
        public void ItemNearTheEnd_AdvancesOnlyThePartOfTheStepThatFits()
        {
            // Half a step of room left: the lane is still moving, at half speed.
            Assert.AreEqual(
                0.5f, BeltFlowUtility.ComputeFlowFactor(Lane(Length - 0.5f), Length, 1f), 0.0001f);
        }

        [Test]
        public void SandboxPlacedBeltGeometry_FitsCargoInsideTheLane()
        {
            // A placed belt is one cell (lane 1.0 world units) with BeltNetwork's default
            // 4-tick segment, and every item sprite is 32 px at PPU 64 = 0.5 world units.
            // Pinned because this is the geometry the "cargo overflows the lane" note was
            // written against, before the projection switch changed the lane length: the fit
            // now engages between the clamps instead of being pinned to one of them.
            const float LaneWorldLength = 1f;
            const float ItemSpriteWorldSize = 0.5f;
            const float FitRatio = 1.25f;
            const float MinScale = 0.45f;
            const float MaxScale = 1f;

            float scale = BeltFlowUtility.ComputeItemScale(
                LaneWorldLength, Length, ItemSpriteWorldSize, FitRatio, MinScale, MaxScale);

            Assert.Greater(scale, MinScale, "clamped low means the fit never engaged");
            Assert.Less(scale, MaxScale, "clamped high means the fit never engaged");

            float spacing = LaneWorldLength / Length;
            float drawnSize = ItemSpriteWorldSize * scale;
            Assert.AreEqual(spacing * FitRatio, drawnSize, 0.0001f,
                "Cargo must sit at exactly the authored fit ratio of the slot spacing.");
        }
    }
}
