using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Workbench's hover and press states, and the belt arrow's rotation. Both were logged
    /// as presentation gaps; both turn on a measurement rather than on taste.
    /// </summary>
    public class WorkbenchInteractionColorsTests
    {
        // wb_card_face.png's mean colour, measured off the asset.
        private static readonly Color CardFace = new Color(185f / 255f, 185f / 255f, 185f / 255f, 1f);

        [Test]
        public void UnitysDefaultHoverIsTheProblemBeingFixed()
        {
            // The backlog measured "~4% colour shift, effectively invisible". Unity's default
            // highlighted multiplier is 0.9607843 -- this is that number, in a test, so nobody
            // reintroduces the default block and calls it a hover state.
            Color defaultHover = CardFace * 0.9607843f;
            float separation = WorkbenchInteractionColors.Separation(CardFace, defaultHover);

            Assert.Less(separation, 0.05f, "Unity's default hover really is a ~4% shift");
        }

        [Test]
        public void HoverIsVisiblyBrighterThanRest()
        {
            Color rest = WorkbenchInteractionColors.Apply(CardFace, WorkbenchInteractionState.Normal);
            Color hover = WorkbenchInteractionColors.Apply(CardFace, WorkbenchInteractionState.Hovered);

            Assert.Greater(
                WorkbenchInteractionColors.Luminance(hover),
                WorkbenchInteractionColors.Luminance(rest),
                "hover has to be a LIFT, which is only possible because rest leaves headroom");
            Assert.Greater(
                WorkbenchInteractionColors.Separation(rest, hover), 0.15f,
                "and by a margin nobody has to look for");
        }

        [Test]
        public void PressIsBelowRest_NotMerelyBelowHover()
        {
            // So a click reads as the control going down even for a player who never hovered
            // first -- a touch or a trackpad tap.
            Color rest = WorkbenchInteractionColors.Apply(CardFace, WorkbenchInteractionState.Normal);
            Color pressed = WorkbenchInteractionColors.Apply(CardFace, WorkbenchInteractionState.Pressed);

            Assert.Less(
                WorkbenchInteractionColors.Luminance(pressed),
                WorkbenchInteractionColors.Luminance(rest));
            Assert.Greater(WorkbenchInteractionColors.Separation(rest, pressed), 0.15f);
        }

        [Test]
        public void EveryStateIsDistinguishableFromEveryOther()
        {
            var states = new[]
            {
                WorkbenchInteractionState.Normal,
                WorkbenchInteractionState.Hovered,
                WorkbenchInteractionState.Pressed,
                WorkbenchInteractionState.Disabled,
            };

            for (int i = 0; i < states.Length; i++)
            {
                for (int j = i + 1; j < states.Length; j++)
                {
                    Color a = WorkbenchInteractionColors.Apply(CardFace, states[i]);
                    Color b = WorkbenchInteractionColors.Apply(CardFace, states[j]);

                    Assert.Greater(
                        WorkbenchInteractionColors.Separation(a, b), 0.10f,
                        states[i] + " vs " + states[j] + " must not be a rounding error");
                }
            }
        }

        [Test]
        public void AStateModulatesTheCardsOwnTint_RatherThanReplacingIt()
        {
            // A hovered logic core has to stay teal.
            var teal = new Color(0.36f, 0.78f, 0.76f, 1f);
            Color hovered = WorkbenchInteractionColors.Apply(teal, WorkbenchInteractionState.Hovered);

            Assert.Greater(hovered.g, hovered.r, "still teal");
            Assert.Greater(hovered.b, hovered.r);
        }

        [Test]
        public void OnlyDisabledTouchesAlpha()
        {
            Assert.AreEqual(
                1f, WorkbenchInteractionColors.Apply(CardFace, WorkbenchInteractionState.Hovered).a,
                0.0001f);
            Assert.Less(
                WorkbenchInteractionColors.Apply(CardFace, WorkbenchInteractionState.Disabled).a, 1f);
        }

        // --- the belt arrow ---------------------------------------------------------------

        [Test]
        public void EveryBeltFacingIsAnExactQuarterTurn()
        {
            // The backlog asks for "a proper mirrored NE/NW ISOMETRIC pair". That requirement
            // belonged to the diamond: on an isometric grid a chevron rotated to a diagonal
            // shears against the tile it sits on, so each diagonal needed its own art. Top-down
            // put every facing on a world axis, so the four are exact 90-degree turns of one
            // sprite and a mirrored pair would be two copies of the same picture.
            //
            // Pinned rather than assumed, because the moment this stops being true -- a
            // projection change, an eight-way facing -- the rotated chevron stops working and
            // the mirrored pair becomes real work again.
            foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
            {
                float angle = FacingVisuals.ScreenAngleDegrees(facing, Vector2.one);
                float quarterTurns = angle / 90f;

                Assert.AreEqual(
                    Mathf.Round(quarterTurns), quarterTurns, 0.0001f,
                    facing + " must be an exact quarter turn for one chevron to serve all four");
            }
        }

        [Test]
        public void TheFourFacingsAreDistinctDirections()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
            {
                int quarterTurn = Mathf.RoundToInt(
                    FacingVisuals.ScreenAngleDegrees(facing, Vector2.one) / 90f);
                Assert.IsTrue(seen.Add(((quarterTurn % 4) + 4) % 4), facing + " duplicates another facing");
            }

            Assert.AreEqual(4, seen.Count);
        }
    }
}
