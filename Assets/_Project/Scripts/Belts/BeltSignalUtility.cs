using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Belts
{
    // Pure, scene-free rules for the belt's *readout layer* -- who draws over whom, and what a
    // jam is allowed to look like. Same "extract the math into a static class the tests can call
    // without a scene" idiom as BeltFlowUtility / YSortUtility / WorkbenchDropRules.
    //
    // This exists because the first belt pass got the layering wrong in a way that could not be
    // caught by looking at either number on its own: cargo sorted at YSort(groundY) while the
    // direction arrows sorted at YSort(backmostLaneY) - 3, which is *always* smaller, so cargo
    // occluded the arrows on every belt in the project. The failure compounds: a jam means the
    // belt is full, a full belt is wall-to-wall cargo, so the jam signal was hidden precisely
    // when it fired. Expressing "the flow signal outranks any cargo anywhere on this lane" as one
    // function makes that a property a test can assert instead of two constants a reader has to
    // mentally subtract.
    public static class BeltSignalUtility
    {
        // Cargo rides ON the belt, so at equal world Y it is deterministically in front of
        // anything standing on the floor beside it (a feeder golem at the belt's mouth used to
        // tie with the item at progress 0 and flicker). One sorting unit == 0.01 world units of
        // Y, i.e. far below any real depth difference -- this only ever breaks exact ties.
        public const int CargoSortingBias = 1;

        // The flow signal is a HUD element painted on the world; it must clear the highest
        // possible cargo order on its own lane, never merely the lane decal's order.
        public const int FlowSignalSortingBias = 2;

        // Flat ground decals bias behind so anything standing on/beside them draws over.
        public const int LaneSortingBias = -4;
        public const int RollerSortingBias = -1;

        /// <summary>
        /// Sorting order for one piece of cargo, from its point ON the lane (never the raised
        /// sprite position, so the cosmetic "sits on top of the belt" offset can't reorder
        /// anything).
        /// </summary>
        public static int ComputeCargoSortingOrder(float cargoGroundY)
        {
            return YSortUtility.ComputeSortingOrder(cargoGroundY) + CargoSortingBias;
        }

        /// <summary>
        /// Sorting order for the lane decal. Taken from the lane's FURTHEST-BACK end so that
        /// anything standing on or beside it (which necessarily has Y &lt;= that maximum, hence a
        /// strictly larger order) draws on top. Sorting from the lane's centre instead put the
        /// lane in front of the cargo riding its far half.
        /// </summary>
        public static int ComputeLaneSortingOrder(float laneStartY, float laneEndY)
        {
            return YSortUtility.ComputeSortingOrder(Mathf.Max(laneStartY, laneEndY)) + LaneSortingBias;
        }

        public static int ComputeRollerSortingOrder(float rollerY)
        {
            return YSortUtility.ComputeSortingOrder(rollerY) + RollerSortingBias;
        }

        /// <summary>
        /// Sorting order for the direction/jam signal. Strictly greater than
        /// <see cref="ComputeCargoSortingOrder"/> for EVERY point on the lane, which is the whole
        /// contract: direction and backpressure have to survive a wall-to-wall loaded belt,
        /// because "loaded" is both the normal working state and the jam state.
        ///
        /// Cargo Y ranges over [min(start,end), max(start,end)] and ComputeSortingOrder is
        /// monotonically DEcreasing in Y, so the largest cargo order on the lane is the one at
        /// the frontmost (smallest-Y) end. Clear that, and the signal clears all of them.
        /// </summary>
        public static int ComputeFlowSignalSortingOrder(float laneStartY, float laneEndY)
        {
            float frontmostY = Mathf.Min(laneStartY, laneEndY);
            return ComputeCargoSortingOrder(frontmostY) + FlowSignalSortingBias;
        }

        // Clearing the cargo means the signal's order lands a few units above the lane's frontmost
        // Y, which can collide EXACTLY with a character standing right at the belt's mouth
        // (Main.unity's feeder golem sorts at 3; so does ScrapBeltA's signal). No integer bias can
        // rule that out in general, so the tie is broken in the remaining channel: with an
        // orthographic camera and Default transparency sorting, equal sortingOrder resolves by
        // distance along the view axis. Pushing the signal a hair AWAY from the camera makes it
        // lose those ties, which is the correct answer -- anything standing level with or in front
        // of the lane mouth should occlude a decal painted on the lane. Far too small to affect
        // anything else (the camera sits 10 units back).
        public const float FlowSignalDepthTieBreak = 0.01f;

        /// <summary>Places a flow-signal decal on the lane with its depth tiebreak applied.</summary>
        public static Vector3 ComputeFlowSignalPosition(Vector3 laneStart, Vector3 laneDirection, float distanceAlongLane)
        {
            Vector3 point = laneStart + laneDirection * distanceAlongLane;
            point.z += FlowSignalDepthTieBreak;
            return point;
        }

        // --- Jam salience -------------------------------------------------------------------
        //
        // The workshop floor is warm brown planks (~0.51, 0.33, 0.20 -> relative luminance 0.36).
        // The first pass alarmed by shifting the arrows from amber (luminance 0.77) to a dark
        // signal red (0.44), which against that floor is a contrast of 0.08 versus the healthy
        // amber's 0.41 -- the alarm state was FIVE TIMES less visible than the healthy one, so a
        // jam read as "the belt dimmed", not "something is wrong". Red-on-brown is simply a bad
        // alarm channel here.
        //
        // The fix is to alarm with brightness and motion instead of hue alone: keep a red base
        // for semantics, but pulse it toward hot white so the signal's contrast oscillates ABOVE
        // the healthy state's, while the arrows themselves freeze. Static brightness plus
        // temporal change, both of which survive a warm background that eats saturated red.

        public const float JamPulseHz = 2.4f;

        // The pulse never drops all the way back to the base red: even caught at its dimmest
        // (e.g. in a still screenshot) a jam has to stay well clear of the floor rather than
        // sinking back into it.
        // Measured on rendered frames: a 0.4 floor left the trough carrying only 1.05x the healthy
        // state's contrast energy once the chevron's dark rim is counted, which is too thin a
        // margin for a still frame. 0.5 puts it at ~1.5x while keeping a clearly visible flash.
        public const float JamPulseFloor = 0.5f;

        // Per-pixel contrast alone cannot win this argument: against warm brown, NO red is as
        // luminous as the healthy amber, so hue+brightness on their own always leave the alarm
        // dimmer than the OK state at some phase of the pulse. AREA is the channel that does
        // win -- the chevrons swell as the belt jams, and a 1.6x chevron carries 2.56x the
        // signal pixels. Contrast-weighted area is what ComputeSignalSalience measures, and it
        // is what the tests assert on.
        public const float JamSignalScaleGain = 0.6f;

        /// <summary>Wraps the jam pulse phase into [0, 1). Frozen when the clock is paused.</summary>
        public static float AdvancePulsePhase(float previousPhase, float deltaSeconds, float hz, float clockSpeed)
        {
            return Mathf.Repeat(previousPhase + deltaSeconds * hz * Mathf.Max(0f, clockSpeed), 1f);
        }

        /// <summary>
        /// 0 = no alarm, 1 = full hot-white flash. Scales with congestion so the readout stays
        /// continuous (a lightly backed-up belt glows faintly, not a binary warning light).
        /// </summary>
        public static float ComputeJamPulse(float congestion, float phase01)
        {
            float intensity = Mathf.Clamp01(congestion);
            if (intensity <= 0f)
            {
                return 0f;
            }

            float wave = 0.5f - 0.5f * Mathf.Cos(Mathf.Repeat(phase01, 1f) * 2f * Mathf.PI);
            return intensity * (JamPulseFloor + (1f - JamPulseFloor) * wave);
        }

        /// <summary>Colour of the direction arrows / flow lamps for a given congestion + pulse.</summary>
        public static Color ComputeFlowSignalColor(
            Color flow, Color jamBase, Color jamPulse, float congestion, float pulse)
        {
            Color settled = Color.Lerp(flow, jamBase, Mathf.Clamp01(congestion));
            return Color.Lerp(settled, jamPulse, Mathf.Clamp01(pulse));
        }

        /// <summary>
        /// Uniform scale for a flow-signal chevron. 1 while healthy, swelling with the jam pulse
        /// so the alarm gains screen area, not just a hue nobody can see against warm wood.
        /// </summary>
        public static float ComputeSignalScale(float pulse)
        {
            return 1f + JamSignalScaleGain * Mathf.Clamp01(pulse);
        }

        /// <summary>
        /// Contrast-weighted signal area: how loud a signal of this colour, drawn at this scale,
        /// actually is against this background. Squared because scale applies in both axes.
        /// </summary>
        public static float ComputeSignalSalience(Color signal, Color background, float scale)
        {
            return scale * scale * LuminanceContrast(signal, background);
        }

        /// <summary>Rec.709 relative luminance -- the channel this whole palette argument is about.</summary>
        public static float RelativeLuminance(Color color)
        {
            return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        }

        /// <summary>How far a signal colour stands off its background in luminance.</summary>
        public static float LuminanceContrast(Color signal, Color background)
        {
            return Mathf.Abs(RelativeLuminance(signal) - RelativeLuminance(background));
        }

        /// <summary>
        /// Vertex colour for a cargo sprite. Queued cargo is DARKENED and cooled rather than
        /// warm-flushed: the previous warm flush multiplied into already-brown scrap for a ~14%
        /// luminance change that measured as nothing, and pushing the same hue harder just made a
        /// jammed Brass ingot read as a different item. A luminance drop keeps every item's hue
        /// relationships intact while being unmissable, and composes correctly with the signal
        /// above it -- stalled cargo goes dark under bright flashing chevrons.
        /// </summary>
        public static Color ComputeCargoRenderColor(bool queued, Color queuedTint)
        {
            return queued ? queuedTint : Color.white;
        }

        /// <summary>What a SpriteRenderer vertex colour actually does to the authored art.</summary>
        public static Color ApplyTint(Color artColor, Color tint)
        {
            return artColor * tint;
        }
    }
}
