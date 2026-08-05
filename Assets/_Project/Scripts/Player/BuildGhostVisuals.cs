using UnityEngine;
using GolemFactory.UI;

namespace GolemFactory.Player
{
    /// <summary>
    /// The three states a hovered build cell can be in. Separating "blocked" from
    /// "unaffordable" matters because they need different actions from the player: move the
    /// cursor, versus go and harvest.
    /// </summary>
    public enum BuildGhostState
    {
        Valid = 0,
        Blocked = 1,
        Unaffordable = 2
    }

    /// <summary>
    /// Pure tint/pulse selection for <see cref="BuildModeController"/>'s placement ghost.
    /// <para>
    /// <b>Measured, not eyeballed.</b> The previous pair (green 0.4,1,0.4 and red 1,0.4,0.4,
    /// both at alpha 0.6, multiplied into building_block.png whose mean colour is 63,42,28)
    /// composited over the warm plank floor (mean 114,76,46) to (61,56,25) and (83,40,25):
    /// a contrast ratio of <b>1.06:1 against each other</b>, and both *darker* than the floor
    /// (1.57:1 and 1.65:1), so the ghost read as a shadow in either state. Those tints were
    /// chosen against the old cold grey floor and did not survive the reskin.
    /// </para>
    /// <para>
    /// The fix is a near-white source sprite (build_ghost_tile.png) so a runtime tint can move
    /// the composite both above and below the floor, plus a second channel for the failure
    /// states. Measured over the same floor: valid composites to (234,185,85) at 4.15:1
    /// *above* the floor, blocked to (234,72,50) at 1.96:1, unaffordable to (141,137,139) at
    /// 2.18:1 -- valid-vs-blocked is now <b>2.11:1</b>. Red on warm brown is quiet by nature
    /// in this palette, so blocked also pulses; valid is steady. Motion carries the difference
    /// that hue alone cannot.
    /// </para>
    /// </summary>
    public static class BuildGhostVisuals
    {
        /// <summary>Warm amber, brighter than the floor. Steady.</summary>
        public static readonly Color ValidTint = new Color(1f, 0.80f, 0.36f, 0.85f);

        /// <summary>Hot red. Pulsed between <see cref="BlockedPulseMinAlpha"/> and its own alpha.</summary>
        public static readonly Color BlockedTint = new Color(1f, 0.30f, 0.22f, 0.95f);

        /// <summary>
        /// Desaturated steel: the only cool, inert colour in a warm scene, so "you cannot pay
        /// for this" never reads as either "go" or "collision".
        /// </summary>
        public static readonly Color UnaffordableTint = new Color(0.60f, 0.64f, 0.70f, 0.70f);

        public const float BlockedPulsePeriod = 0.7f;
        public const float BlockedPulseMinAlpha = 0.45f;

        public static BuildGhostState Classify(bool occupied, bool affordable)
        {
            if (occupied)
            {
                return BuildGhostState.Blocked;
            }

            return affordable ? BuildGhostState.Valid : BuildGhostState.Unaffordable;
        }

        /// <summary>
        /// Final ghost colour including the blocked state's pulse. <paramref name="time"/> is
        /// wall-clock seconds; passing a constant yields a stable colour, which is what makes
        /// this assertable in a test.
        /// </summary>
        public static Color Evaluate(BuildGhostState state, float time)
        {
            switch (state)
            {
                case BuildGhostState.Blocked:
                {
                    Color c = BlockedTint;
                    c.a = Mathf.Lerp(BlockedPulseMinAlpha, BlockedTint.a,
                        FeedbackMotion.Breathe01(time, BlockedPulsePeriod));
                    return c;
                }
                case BuildGhostState.Unaffordable:
                    return UnaffordableTint;
                default:
                    return ValidTint;
            }
        }
    }
}
