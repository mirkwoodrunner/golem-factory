using NUnit.Framework;
using UnityEngine;
using GolemFactory.Player;

namespace GolemFactory.Tests.EditMode
{
    // These tests are the record of the measurement behind the ghost's colours, not just a
    // check that a switch statement returns three different structs. The previous green/red
    // pair was chosen against the pre-reskin cold grey floor; on the warm plank floor that
    // replaced it, the two composited to a contrast ratio of 1.06:1 against each other, i.e.
    // the "you cannot build here" state was invisible. Asserting the composite contrast is
    // what stops that from silently happening again the next time the floor changes.
    public class BuildGhostVisualsTests
    {
        // Mean colour of Assets/_Project/Art/floor_tile.png, measured over its opaque pixels.
        private static readonly Color Floor = new Color(114f / 255f, 76f / 255f, 46f / 255f, 1f);

        // The ghost sprite (build_ghost_tile.png) is authored pure white so a runtime tint has
        // the full luminance range available in both directions.
        private static readonly Color GhostSource = Color.white;

        private static float ToLinear(float channel) =>
            channel <= 0.04045f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);

        private static float RelativeLuminance(Color c) =>
            0.2126f * ToLinear(c.r) + 0.7152f * ToLinear(c.g) + 0.0722f * ToLinear(c.b);

        private static float ContrastRatio(Color a, Color b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float hi = Mathf.Max(la, lb);
            float lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        // A SpriteRenderer multiplies its colour into the sprite and alpha-blends the result
        // over whatever is behind it.
        private static Color CompositeOverFloor(Color tint)
        {
            float a = tint.a * GhostSource.a;
            return new Color(
                a * tint.r * GhostSource.r + (1f - a) * Floor.r,
                a * tint.g * GhostSource.g + (1f - a) * Floor.g,
                a * tint.b * GhostSource.b + (1f - a) * Floor.b,
                1f);
        }

        [Test]
        public void Classify_OccupiedCellIsBlockedRegardlessOfAffordability()
        {
            Assert.AreEqual(BuildGhostState.Blocked, BuildGhostVisuals.Classify(true, true));
            Assert.AreEqual(BuildGhostState.Blocked, BuildGhostVisuals.Classify(true, false));
        }

        [Test]
        public void Classify_SeparatesUnaffordableFromValid()
        {
            Assert.AreEqual(BuildGhostState.Valid, BuildGhostVisuals.Classify(false, true));
            Assert.AreEqual(BuildGhostState.Unaffordable, BuildGhostVisuals.Classify(false, false));
        }

        [Test]
        public void ValidGhost_IsBrighterThanTheFloorItSitsOn()
        {
            Color composite = CompositeOverFloor(BuildGhostVisuals.ValidTint);

            Assert.Greater(RelativeLuminance(composite), RelativeLuminance(Floor),
                "The valid ghost must read as a highlight, not as the shadow the old tint produced.");
            Assert.Greater(ContrastRatio(composite, Floor), 3f);
        }

        [Test]
        public void ValidAndBlockedGhosts_AreSeparableByLuminanceNotJustHue()
        {
            Color valid = CompositeOverFloor(BuildGhostVisuals.ValidTint);
            Color blocked = CompositeOverFloor(BuildGhostVisuals.BlockedTint);

            // The old pair measured 1.06:1 here. Anything under ~1.5:1 is not a readout.
            Assert.Greater(ContrastRatio(valid, blocked), 1.8f);
        }

        [Test]
        public void EveryGhostState_HasRealContrastAgainstTheWarmFloor()
        {
            foreach (BuildGhostState state in new[] { BuildGhostState.Valid, BuildGhostState.Blocked, BuildGhostState.Unaffordable })
            {
                Color composite = CompositeOverFloor(BuildGhostVisuals.Evaluate(state, 0f));
                Assert.Greater(ContrastRatio(composite, Floor), 1.5f,
                    state + " composites too close to the floor to be seen.");
            }
        }

        [Test]
        public void UnaffordableGhost_IsCoolWhereValidAndBlockedAreWarm()
        {
            Color unaffordable = BuildGhostVisuals.UnaffordableTint;

            Assert.Greater(unaffordable.b, unaffordable.r,
                "The only inert state should be the only cool one on a warm-palette screen.");
            Assert.Greater(BuildGhostVisuals.ValidTint.r, BuildGhostVisuals.ValidTint.b);
            Assert.Greater(BuildGhostVisuals.BlockedTint.r, BuildGhostVisuals.BlockedTint.b);
        }

        // Red on warm brown is quiet in this palette no matter how it is tuned, so blocked
        // carries a second channel: motion.
        [Test]
        public void BlockedGhost_Pulses_WhileValidAndUnaffordableHoldStill()
        {
            float period = BuildGhostVisuals.BlockedPulsePeriod;
            float low = BuildGhostVisuals.Evaluate(BuildGhostState.Blocked, period * 0.75f).a;
            float high = BuildGhostVisuals.Evaluate(BuildGhostState.Blocked, period * 0.25f).a;

            Assert.Greater(high - low, 0.4f);

            Assert.AreEqual(
                BuildGhostVisuals.Evaluate(BuildGhostState.Valid, 0f),
                BuildGhostVisuals.Evaluate(BuildGhostState.Valid, period * 0.25f));
            Assert.AreEqual(
                BuildGhostVisuals.Evaluate(BuildGhostState.Unaffordable, 0f),
                BuildGhostVisuals.Evaluate(BuildGhostState.Unaffordable, period * 0.5f));
        }

        [Test]
        public void BlockedGhost_NeverFadesOutCompletely()
        {
            for (float t = 0f; t < BuildGhostVisuals.BlockedPulsePeriod * 2f; t += 0.02f)
            {
                Assert.GreaterOrEqual(
                    BuildGhostVisuals.Evaluate(BuildGhostState.Blocked, t).a,
                    BuildGhostVisuals.BlockedPulseMinAlpha - 0.0001f);
            }
        }
    }
}
