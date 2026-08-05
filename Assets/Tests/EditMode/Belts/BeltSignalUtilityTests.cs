using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode.Belts
{
    public sealed class BeltSignalUtilityTests
    {
        // The two lanes that actually exist in the shipped scenes, plus the degenerate cases.
        // Main.unity's ScrapBeltA runs (4.50, 0.00) -> (3.65, 0.85); ScrapBeltB runs back down.
        private static readonly float[][] LaneGeometries =
        {
            new[] { 0.00f, 0.85f },   // Main ScrapBeltA, uphill
            new[] { 0.85f, 0.00f },   // reversed, downhill
            new[] { -1.56f, -0.88f }, // Main ScrapBeltB's measured span, both ends negative
            new[] { 1.20f, 1.20f },   // perfectly horizontal lane
            new[] { -2.0f, 3.0f },    // long lane straddling y = 0
        };

        // The workshop plank floor these signals are read against: PLANK_TONES[3] from
        // Tools/Art/generate_placeholder_art.py, (129, 85, 50).
        private static readonly Color PlankFloor = new Color(129f / 255f, 85f / 255f, 50f / 255f, 1f);

        private static readonly Color Flow = new Color(1f, 0.74f, 0.32f, 1f);
        private static readonly Color JamBase = new Color(1f, 0.34f, 0.28f, 1f);
        private static readonly Color JamPulse = new Color(1f, 0.95f, 0.9f, 1f);

        // What the first belt pass shipped, kept here so the regression is stated as a
        // comparison and not just as a magic threshold.
        private static readonly Color OldJamRed = new Color(0.93f, 0.31f, 0.22f, 1f);
        private static readonly Color OldItemJamTint = new Color(1f, 0.8f, 0.72f, 1f);

        // --- Layering: the regression that made the whole readout useless -------------------

        [Test]
        public void FlowSignal_OutranksCargoAtEveryPointOnEveryLane()
        {
            // THE contract. A jam means the belt is full, a full belt is wall-to-wall cargo, so
            // a flow signal that any cargo can occlude is hidden precisely when it fires.
            foreach (float[] lane in LaneGeometries)
            {
                int signal = BeltSignalUtility.ComputeFlowSignalSortingOrder(lane[0], lane[1]);
                for (int step = 0; step <= 200; step++)
                {
                    float y = Mathf.Lerp(lane[0], lane[1], step / 200f);
                    Assert.Greater(signal, BeltSignalUtility.ComputeCargoSortingOrder(y),
                        "cargo at y=" + y + " occludes the flow signal on lane "
                        + lane[0] + " -> " + lane[1]);
                }
            }
        }

        [Test]
        public void FlowSignal_WouldHaveLostUnderTheOldLaneRelativeRule()
        {
            // The old rule was laneSortingOrder + 1, i.e. anchored to the lane's BACKMOST end and
            // biased behind -- which is below the cargo order at every point on the lane. This
            // pins the actual defect so nobody re-derives the arrows from the lane decal again.
            foreach (float[] lane in LaneGeometries)
            {
                int oldSignal = BeltSignalUtility.ComputeLaneSortingOrder(lane[0], lane[1]) + 1;
                int frontmostCargo = BeltSignalUtility.ComputeCargoSortingOrder(Mathf.Min(lane[0], lane[1]));
                Assert.Less(oldSignal, frontmostCargo,
                    "old rule was expected to lose to cargo on lane " + lane[0] + " -> " + lane[1]);
            }
        }

        [Test]
        public void Lane_DrawsBehindCargoAtEveryPointOnEveryLane()
        {
            foreach (float[] lane in LaneGeometries)
            {
                int laneOrder = BeltSignalUtility.ComputeLaneSortingOrder(lane[0], lane[1]);
                for (int step = 0; step <= 200; step++)
                {
                    float y = Mathf.Lerp(lane[0], lane[1], step / 200f);
                    Assert.Less(laneOrder, BeltSignalUtility.ComputeCargoSortingOrder(y),
                        "lane decal drew over cargo at y=" + y);
                }
            }
        }

        [Test]
        public void Cargo_BreaksTiesAgainstAGroundObjectAtTheSameY()
        {
            // An item at progress 0 sits exactly on the feed point, which is exactly where the
            // feeder golem stands -- both resolved to 0 and the draw order was arbitrary.
            float[] ties = { 0f, 0.03f, -1.56f, 2.5f };
            foreach (float y in ties)
            {
                Assert.Greater(BeltSignalUtility.ComputeCargoSortingOrder(y),
                    YSortUtility.ComputeSortingOrder(y),
                    "cargo tied with a ground object at y=" + y);
            }
        }

        [Test]
        public void FlowSignal_RecedesOnAnExactSortingOrderTieWithAGroundObject()
        {
            // ScrapBeltA's signal lands on 3; Main.unity's feeder golem also sorts at 3. Equal
            // sortingOrder is resolved by view-axis distance, and the decal must be the one that
            // gives way -- a character at the belt's mouth stands in front of the lane, not
            // behind a chevron painted on it.
            Vector3 placed = BeltSignalUtility.ComputeFlowSignalPosition(
                new Vector3(4.5f, 0f, 0f), new Vector3(-0.707f, 0.707f, 0f), 0.6f);
            Assert.Greater(placed.z, 0f, "flow signal must sit further from the camera on a tie");
            Assert.Less(placed.z, 0.05f, "the tiebreak must be too small to reorder anything real");
            Assert.AreEqual(4.5f - 0.707f * 0.6f, placed.x, 0.0001f);
            Assert.AreEqual(0.707f * 0.6f, placed.y, 0.0001f);
        }

        [Test]
        public void Cargo_TiebreakIsSmallerThanAnyRealDepthDifference()
        {
            // The bias must only ever settle exact ties: an object genuinely one cell (0.25 world
            // Y on this isometric grid) in front of the belt still has to win.
            int cargoAtZero = BeltSignalUtility.ComputeCargoSortingOrder(0f);
            int golemOneCellInFront = YSortUtility.ComputeSortingOrder(-0.25f);
            Assert.Greater(golemOneCellInFront, cargoAtZero);
        }

        [Test]
        public void Cargo_OrderStillDecreasesMonotonicallyWithDepth()
        {
            // The bias is a constant offset, so per-item Y-sorting (verified working) is intact.
            int front = BeltSignalUtility.ComputeCargoSortingOrder(0f);
            int middle = BeltSignalUtility.ComputeCargoSortingOrder(0.4f);
            int back = BeltSignalUtility.ComputeCargoSortingOrder(0.85f);
            Assert.Greater(front, middle);
            Assert.Greater(middle, back);
        }

        // --- Jam salience -------------------------------------------------------------------

        [Test]
        public void OldJamRed_WasLessVisibleThanHealthyAmberAgainstThePlankFloor()
        {
            // Documents the defect being fixed: the alarm state was quieter than the OK state.
            float healthy = BeltSignalUtility.LuminanceContrast(Flow, PlankFloor);
            float oldAlarm = BeltSignalUtility.LuminanceContrast(OldJamRed, PlankFloor);
            Assert.Less(oldAlarm, healthy * 0.5f,
                "expected the old jam red to be dramatically less salient; healthy=" + healthy
                + " oldAlarm=" + oldAlarm);
        }

        [Test]
        public void JamSignal_PeaksMoreVisibleThanHealthyAmberAgainstThePlankFloor()
        {
            float healthy = BeltSignalUtility.LuminanceContrast(Flow, PlankFloor);
            float peak = BeltSignalUtility.LuminanceContrast(
                BeltSignalUtility.ComputeFlowSignalColor(Flow, JamBase, JamPulse, 1f, 1f), PlankFloor);
            Assert.Greater(peak, healthy,
                "jam peak must out-contrast the healthy state; healthy=" + healthy + " peak=" + peak);
        }

        [Test]
        public void JamSignal_NeverSinksBackTowardTheFloorEvenAtItsDimmestPhase()
        {
            // A still frame caught mid-pulse must still read as an alarm, not as "the belt dimmed".
            float oldAlarm = BeltSignalUtility.LuminanceContrast(OldJamRed, PlankFloor);
            float dimmest = float.MaxValue;
            for (int i = 0; i < 200; i++)
            {
                float pulse = BeltSignalUtility.ComputeJamPulse(1f, i / 200f);
                Color c = BeltSignalUtility.ComputeFlowSignalColor(Flow, JamBase, JamPulse, 1f, pulse);
                dimmest = Mathf.Min(dimmest, BeltSignalUtility.LuminanceContrast(c, PlankFloor));
            }

            Assert.Greater(dimmest, oldAlarm * 3f,
                "dimmest jam phase=" + dimmest + " vs old steady jam=" + oldAlarm);
        }

        [Test]
        public void JamSignal_IsLouderThanTheHealthySignalAtEVERYPhaseOfThePulse()
        {
            // THE defect-2 contract, and the reason the chevrons swell rather than only changing
            // hue: against warm brown NO red out-luminates the healthy amber, so per-pixel
            // contrast alone leaves the alarm quieter than the OK state at the bottom of the
            // pulse. Contrast-weighted AREA is the measure that has to hold at all times.
            float healthy = BeltSignalUtility.ComputeSignalSalience(Flow, PlankFloor, 1f);
            float quietest = float.MaxValue;
            float loudest = float.MinValue;
            for (int i = 0; i < 200; i++)
            {
                float pulse = BeltSignalUtility.ComputeJamPulse(1f, i / 200f);
                Color c = BeltSignalUtility.ComputeFlowSignalColor(Flow, JamBase, JamPulse, 1f, pulse);
                float salience = BeltSignalUtility.ComputeSignalSalience(
                    c, PlankFloor, BeltSignalUtility.ComputeSignalScale(pulse));
                quietest = Mathf.Min(quietest, salience);
                loudest = Mathf.Max(loudest, salience);
            }

            Assert.Greater(quietest, healthy,
                "the alarm went quieter than the healthy state; quietest=" + quietest
                + " healthy=" + healthy);
            Assert.Greater(loudest, healthy * 3f, "loudest=" + loudest + " healthy=" + healthy);
        }

        [Test]
        public void OldJamSignal_WasFiveTimesQUIETERThanTheHealthyState()
        {
            // The shipped regression, stated as the number it actually was.
            float healthy = BeltSignalUtility.ComputeSignalSalience(Flow, PlankFloor, 1f);
            float oldAlarm = BeltSignalUtility.ComputeSignalSalience(OldJamRed, PlankFloor, 1f);
            Assert.Less(oldAlarm, healthy * 0.25f, "healthy=" + healthy + " oldAlarm=" + oldAlarm);
        }

        [Test]
        public void ComputeSignalScale_IsUnityWhileHealthyAndGrowsWithTheJam()
        {
            Assert.AreEqual(1f, BeltSignalUtility.ComputeSignalScale(0f), 0.0001f);
            Assert.Greater(BeltSignalUtility.ComputeSignalScale(1f), 1.3f);
            Assert.Greater(BeltSignalUtility.ComputeSignalScale(1f), BeltSignalUtility.ComputeSignalScale(0.4f));
            Assert.AreEqual(1f, BeltSignalUtility.ComputeSignalScale(-1f), 0.0001f);
        }

        [Test]
        public void JamSignal_FlickersWhileTheHealthySignalIsPerfectlySteady()
        {
            // Temporal change is the salience channel that a warm background cannot eat.
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < 200; i++)
            {
                float pulse = BeltSignalUtility.ComputeJamPulse(1f, i / 200f);
                float lum = BeltSignalUtility.RelativeLuminance(
                    BeltSignalUtility.ComputeFlowSignalColor(Flow, JamBase, JamPulse, 1f, pulse));
                min = Mathf.Min(min, lum);
                max = Mathf.Max(max, lum);
            }

            Assert.Greater(max - min, 0.15f, "jam luminance swing was only " + (max - min));

            for (int i = 0; i < 200; i++)
            {
                float pulse = BeltSignalUtility.ComputeJamPulse(0f, i / 200f);
                Assert.AreEqual(0f, pulse, 0.0001f, "a healthy belt must not flash");
            }
        }

        [Test]
        public void ComputeJamPulse_ScalesWithCongestionAndIsSilentWhenClear()
        {
            Assert.AreEqual(0f, BeltSignalUtility.ComputeJamPulse(0f, 0.5f), 0.0001f);
            Assert.Greater(BeltSignalUtility.ComputeJamPulse(1f, 0.5f),
                BeltSignalUtility.ComputeJamPulse(0.4f, 0.5f));
            for (int i = 0; i <= 20; i++)
            {
                float pulse = BeltSignalUtility.ComputeJamPulse(1f, i / 20f);
                Assert.GreaterOrEqual(pulse, 0f);
                Assert.LessOrEqual(pulse, 1f);
            }
        }

        [Test]
        public void AdvancePulsePhase_WrapsAndFreezesWithAPausedClock()
        {
            float phase = BeltSignalUtility.AdvancePulsePhase(0.9f, 0.1f, 2f, 1f);
            Assert.AreEqual(0.1f, phase, 0.0001f);
            Assert.GreaterOrEqual(phase, 0f);
            Assert.Less(phase, 1f);

            Assert.AreEqual(0.4f, BeltSignalUtility.AdvancePulsePhase(0.4f, 0.5f, 2f, 0f), 0.0001f);
        }

        [Test]
        public void ComputeFlowSignalColor_IsTheHealthyAmberWhenNothingIsWrong()
        {
            Color c = BeltSignalUtility.ComputeFlowSignalColor(Flow, JamBase, JamPulse, 0f, 0f);
            Assert.AreEqual(Flow.r, c.r, 0.0001f);
            Assert.AreEqual(Flow.g, c.g, 0.0001f);
            Assert.AreEqual(Flow.b, c.b, 0.0001f);
        }

        // --- Queued cargo tint ---------------------------------------------------------------

        // The authored item palette from Tools/Art/generate_placeholder_art.py: RUST, INGOT,
        // AETHER. The tint has to be perceptible on all three, not just on the darkest.
        private static readonly Color[] ItemArtColors =
        {
            new Color(150f / 255f, 88f / 255f, 52f / 255f, 1f),
            new Color(214f / 255f, 168f / 255f, 76f / 255f, 1f),
            new Color(96f / 255f, 214f / 255f, 200f / 255f, 1f),
        };

        private static readonly Color QueuedTint = new Color(0.58f, 0.62f, 0.78f, 1f);

        [Test]
        public void OldWarmFlush_WasImperceptibleOnTheAuthoredItemPalette()
        {
            foreach (Color art in ItemArtColors)
            {
                float baseLum = BeltSignalUtility.RelativeLuminance(art);
                float tinted = BeltSignalUtility.RelativeLuminance(
                    BeltSignalUtility.ApplyTint(art, OldItemJamTint));
                float drop = (baseLum - tinted) / baseLum;
                Assert.Less(drop, 0.2f,
                    "the old warm flush is being credited with more change than it made: " + drop);
            }
        }

        [Test]
        public void QueuedCargoTint_DropsLuminanceEnoughToActuallySee()
        {
            foreach (Color art in ItemArtColors)
            {
                float baseLum = BeltSignalUtility.RelativeLuminance(art);
                float tinted = BeltSignalUtility.RelativeLuminance(
                    BeltSignalUtility.ApplyTint(art, QueuedTint));
                float drop = (baseLum - tinted) / baseLum;
                Assert.Greater(drop, 0.35f,
                    "queued cargo only lost " + drop + " of its luminance, which measured as "
                    + "nothing last time");
            }
        }

        [Test]
        public void QueuedCargoTint_CoolsRatherThanReinforcingTheWarmItemHues()
        {
            // Pushing warm items warmer is what made the previous attempt invisible: it moved the
            // colour along the axis the art already occupies.
            Assert.Greater(QueuedTint.b, QueuedTint.r);
            Assert.Greater(QueuedTint.b, QueuedTint.g);
        }

        [Test]
        public void ComputeCargoRenderColor_LeavesFlowingCargoUntouched()
        {
            Assert.AreEqual(Color.white, BeltSignalUtility.ComputeCargoRenderColor(false, QueuedTint));
            Assert.AreEqual(QueuedTint, BeltSignalUtility.ComputeCargoRenderColor(true, QueuedTint));
        }
    }
}
