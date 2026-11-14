using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.Belts
{
    // Pure, scene-free math for belt *presentation* -- same "extract the math into a static
    // class the tests can call without a scene" idiom as GridCoordinateConverter /
    // GolemAnimationUtility. Nothing in here mutates a BeltSegment; ConveyorSystem.Tick remains
    // the only thing that advances the simulation.
    //
    // The reason this exists: BeltSegment.Progress is an integer-stepped tick value, so
    // rendering items straight from it makes them *teleport* once per tick instead of flowing.
    // PredictProgressAfterAdvance replays BeltSegment.Advance's head-first cap propagation
    // WITHOUT writing anything back, which lets the visual interpolate each item between where
    // it is now and where the next tick will actually put it. An item that is blocked predicts
    // to its own current progress, so it visibly stops dead -- which is exactly the backpressure
    // read we want, for free.
    public static class BeltFlowUtility
    {
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

        /// <summary>Sub-tick interpolated progress used for the item's on-screen position.</summary>
        public static float ComputeDisplayProgress(float currentProgress, float predictedProgress, float tickFraction)
        {
            return Mathf.Lerp(currentProgress, predictedProgress, Mathf.Clamp01(tickFraction));
        }
    }
}
