using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.Belts
{
    // Pure, scene-free math for belt *presentation* -- same "extract the math into a static
    // class the tests can call without a scene" idiom as GridCoordinateConverter / YSortUtility /
    // GolemAnimationUtility. Nothing in here mutates a BeltSegment; ConveyorSystem.Tick remains
    // the only thing that advances the simulation.
    //
    // The reason most of this exists: BeltSegment.Progress is an integer-stepped tick value, so
    // rendering items straight from it makes them *teleport* once per tick instead of flowing.
    // PredictProgressAfterAdvance replays BeltSegment.Advance's head-first cap propagation
    // WITHOUT writing anything back, which lets the visual interpolate each item between where
    // it is now and where the next tick will actually put it. An item that is blocked predicts
    // to its own current progress, so it visibly stops dead -- which is exactly the backpressure
    // read we want, for free.
    public static class BeltFlowUtility
    {
        // Progress is compared against caps that are computed by subtraction, so an exact
        // equality test would miss items that are blocked by a hair of float error.
        public const float BlockedEpsilon = 0.0001f;

        /// <summary>
        /// The progress item <paramref name="index"/> will have after the next
        /// <see cref="BeltSegment.Advance"/> of <paramref name="step"/>, mirroring that method's
        /// head-first cap propagation exactly (each item is capped by the ALREADY-ADVANCED item
        /// ahead of it, which is what enforces no-passing and no-overlap).
        /// </summary>
        public static float PredictProgressAfterAdvance(
            IReadOnlyList<ItemStack> items, int index, int segmentLength, float step)
        {
            if (items == null || index < 0 || index >= items.Count)
            {
                return 0f;
            }

            float ahead = 0f;
            for (int i = 0; i <= index; i++)
            {
                float cap = i == 0 ? segmentLength : ahead - BeltSegment.MinSpacing;
                ahead = Mathf.Min(items[i].Progress + step, cap);
            }

            return ahead;
        }

        /// <summary>
        /// True when this item cannot advance a full <paramref name="step"/> next tick -- either
        /// it is the head parked at the segment end, or the item ahead of it is in the way.
        /// </summary>
        public static bool IsBlocked(IReadOnlyList<ItemStack> items, int index, int segmentLength, float step)
        {
            if (items == null || index < 0 || index >= items.Count)
            {
                return false;
            }

            float predicted = PredictProgressAfterAdvance(items, index, segmentLength, step);
            return predicted - items[index].Progress < step - BlockedEpsilon;
        }

        /// <summary>
        /// True when this item is queued *behind another item* rather than merely being the head
        /// waiting at the end of the lane. The distinction matters for readability: a terminal
        /// segment always parks its head at the end waiting for a golem to pull it, and painting
        /// that as a jam would mean the belt is permanently red and the signal means nothing.
        /// </summary>
        public static bool IsQueuedBehindAnother(
            IReadOnlyList<ItemStack> items, int index, int segmentLength, float step)
        {
            return index >= 1 && IsBlocked(items, index, segmentLength, step);
        }

        public static int CountQueued(IReadOnlyList<ItemStack> items, int segmentLength, float step)
        {
            if (items == null)
            {
                return 0;
            }

            int queued = 0;
            for (int i = 1; i < items.Count; i++)
            {
                if (IsBlocked(items, i, segmentLength, step))
                {
                    queued++;
                }
            }

            return queued;
        }

        /// <summary>
        /// 0 = free-flowing, 1 = every slot behind the head is jammed. Drives the lane's
        /// arrow colour/scroll so a backed-up belt reads as backed up with nothing selected.
        /// </summary>
        public static float ComputeCongestion(
            IReadOnlyList<ItemStack> items, int segmentCapacity, int segmentLength, float step)
        {
            int queueSlots = Mathf.Max(1, segmentCapacity - 1);
            return Mathf.Clamp01(CountQueued(items, segmentLength, step) / (float)queueSlots);
        }

        /// <summary>Sub-tick interpolated progress used for the item's on-screen position.</summary>
        public static float ComputeDisplayProgress(float currentProgress, float predictedProgress, float tickFraction)
        {
            return Mathf.Lerp(currentProgress, predictedProgress, Mathf.Clamp01(tickFraction));
        }

        /// <summary>
        /// World units per second the tread/arrows should scroll so they move at exactly the
        /// speed an unobstructed item does: one Progress unit per tick, <paramref name="segmentLength"/>
        /// units across the whole lane.
        /// </summary>
        public static float ComputeTreadSpeed(
            float laneWorldLength, int segmentLength, float ticksPerSecond, float clockSpeed)
        {
            if (segmentLength <= 0)
            {
                return 0f;
            }

            return laneWorldLength / segmentLength * ticksPerSecond * clockSpeed;
        }

        /// <summary>Wraps the scrolling arrow phase into [0, spacing).</summary>
        public static float AdvanceScrollPhase(float previousPhase, float deltaSeconds, float speed, float spacing)
        {
            if (spacing <= 0f)
            {
                return 0f;
            }

            return Mathf.Repeat(previousPhase + deltaSeconds * speed, spacing);
        }

        /// <summary>
        /// How many pooled arrow renderers a lane of this length needs. Fixed at resolve time and
        /// never grown afterwards, matching the item-slot pool's no-allocation-at-runtime rule.
        /// </summary>
        public static int ComputeArrowCount(float laneWorldLength, float spacing)
        {
            if (spacing <= 0f)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.CeilToInt(laneWorldLength / spacing) + 1);
        }

        /// <summary>
        /// Uniform scale for the cargo sprites so a *fully packed* lane still reads as a row of
        /// separate objects. A segment holds Length+1 items spaced BeltSegment.MinSpacing apart,
        /// which on a short lane is far tighter than the authored sprite size -- at native scale
        /// a backed-up belt merges into one continuous smear, which is precisely the state the
        /// player most needs to be able to count. <paramref name="fitRatio"/> above 1 allows a
        /// deliberate slight overlap so items still look like they are touching, not floating.
        /// </summary>
        public static float ComputeItemScale(
            float laneWorldLength, int segmentLength, float itemSpriteWorldSize,
            float fitRatio, float minScale, float maxScale)
        {
            if (segmentLength <= 0 || itemSpriteWorldSize <= 0f)
            {
                return maxScale;
            }

            float spacingWorld = laneWorldLength / segmentLength;
            return Mathf.Clamp(spacingWorld * fitRatio / itemSpriteWorldSize, minScale, maxScale);
        }

        /// <summary>Z rotation, in degrees, that points a +X-facing lane sprite from -> to.</summary>
        public static float ComputeLaneAngleDegrees(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude <= 0f)
            {
                return 0f;
            }

            return Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Fades an arrow out as it approaches either end of the lane so the pooled renderers
        /// recycle invisibly instead of popping in and out at the lane's mouth.
        /// </summary>
        public static float ComputeArrowFade(float distanceAlongLane, float laneWorldLength, float fadeDistance)
        {
            if (laneWorldLength <= 0f)
            {
                return 0f;
            }

            if (distanceAlongLane < 0f || distanceAlongLane > laneWorldLength)
            {
                return 0f;
            }

            if (fadeDistance <= 0f)
            {
                return 1f;
            }

            float fromNearEnd = Mathf.Min(distanceAlongLane, laneWorldLength - distanceAlongLane);
            return Mathf.Clamp01(fromNearEnd / fadeDistance);
        }
    }
}
