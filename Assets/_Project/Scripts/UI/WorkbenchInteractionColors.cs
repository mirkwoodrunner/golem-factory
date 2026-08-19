using UnityEngine;
using UnityEngine.UI;

namespace GolemFactory.UI
{
    /// <summary>What a pointer is doing to a Workbench control right now.</summary>
    public enum WorkbenchInteractionState
    {
        Normal = 0,
        Hovered = 1,
        Pressed = 2,
        Disabled = 3,
    }

    /// <summary>
    /// Hover and press states for the Workbench's runtime-built controls -- the chassis buttons
    /// and the vault cards.
    ///
    /// <para>
    /// <b>Measured, not eyeballed</b>, in the same spirit as <c>BuildGhostVisuals</c>.
    /// <c>wb_card_face.png</c> has a mean colour of (185, 185, 185); Unity's DEFAULT
    /// <c>ColorBlock</c> multiplies it by 0.961 when highlighted, which composites to a
    /// luminance shift of <b>3.9 %</b> -- the "effectively invisible" figure the backlog
    /// recorded. Its default pressed state (0.784) is a visible 21.6 %, so it was only ever
    /// hover that failed.
    /// </para>
    ///
    /// <para>
    /// <b>The fix is headroom, and it costs a slightly dimmer resting state.</b>
    /// <c>Image.color</c> MULTIPLIES the sprite, so a tint can only ever darken -- there is no
    /// multiplier that brightens, and "highlight" is therefore unreachable from a resting state
    /// of pure white. So the controls now rest at <see cref="NormalMultiplier"/> and hover goes
    /// to full: the lift is real, and the cost is that an untouched card is a little darker than
    /// it used to be. That is the identical move BuildGhostVisuals made when it introduced a
    /// near-white source sprite so a runtime tint could move the composite in both directions.
    /// </para>
    ///
    /// <para>
    /// The alternatives were rejected as more machinery for the same result: swapping to a
    /// second sprite per state doubles the art, and an overlay child per control adds a
    /// GameObject to every card in a list that is rebuilt from data on every refresh.
    /// </para>
    /// </summary>
    public static class WorkbenchInteractionColors
    {
        /// <summary>Resting. Dimmed so hover has somewhere to go -- see the class note.</summary>
        public const float NormalMultiplier = 0.82f;

        /// <summary>Hover: the full authored sprite. A 22 % lift on the resting state.</summary>
        public const float HoveredMultiplier = 1f;

        /// <summary>
        /// Press: clearly below rest rather than merely below hover, so a click reads as the
        /// control going down under the finger even for a player who never hovered first (a
        /// touch or a trackpad tap).
        /// </summary>
        public const float PressedMultiplier = 0.62f;

        public const float DisabledMultiplier = 0.45f;

        /// <summary>Disabled also loses alpha, because a dimmer brass plate is still a plate.</summary>
        public const float DisabledAlpha = 0.6f;

        /// <summary>
        /// Fast enough to feel attached to the pointer, slow enough not to strobe when the
        /// cursor crosses a list of eight cards.
        /// </summary>
        public const float FadeDuration = 0.08f;

        /// <summary>
        /// The multiplier for one state. Public and pure so a test can assert the SEPARATION
        /// between states rather than trusting the numbers by eye -- the 3.9 % that failed here
        /// was a number nobody had checked.
        /// </summary>
        public static float Multiplier(WorkbenchInteractionState state)
        {
            switch (state)
            {
                case WorkbenchInteractionState.Hovered:
                    return HoveredMultiplier;
                case WorkbenchInteractionState.Pressed:
                    return PressedMultiplier;
                case WorkbenchInteractionState.Disabled:
                    return DisabledMultiplier;
                default:
                    return NormalMultiplier;
            }
        }

        /// <summary>
        /// <paramref name="baseColor"/> as it should be drawn in <paramref name="state"/>. The
        /// vault cards carry their own copper/teal tint, so the state has to modulate whatever
        /// colour the card already is rather than replacing it.
        /// </summary>
        public static Color Apply(Color baseColor, WorkbenchInteractionState state)
        {
            float multiplier = Multiplier(state);
            float alpha = state == WorkbenchInteractionState.Disabled
                ? baseColor.a * DisabledAlpha
                : baseColor.a;

            return new Color(
                baseColor.r * multiplier, baseColor.g * multiplier, baseColor.b * multiplier, alpha);
        }

        /// <summary>Relative luminance, for asserting that two states actually look different.</summary>
        public static float Luminance(Color color) =>
            0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;

        /// <summary>
        /// How far apart two states are as a fraction of the brighter one. The backlog's
        /// complaint was that this was 0.039 for hover; anything below ~0.10 is not a state
        /// change, it is a rounding error with a name.
        /// </summary>
        public static float Separation(Color a, Color b)
        {
            float lumA = Luminance(a);
            float lumB = Luminance(b);
            float brighter = Mathf.Max(lumA, lumB);
            return brighter <= 0f ? 0f : Mathf.Abs(lumA - lumB) / brighter;
        }

        /// <summary>The Selectable transition block for a Workbench button.</summary>
        public static ColorBlock ButtonColors()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(NormalMultiplier, NormalMultiplier, NormalMultiplier, 1f);
            colors.highlightedColor = new Color(HoveredMultiplier, HoveredMultiplier, HoveredMultiplier, 1f);
            colors.pressedColor = new Color(PressedMultiplier, PressedMultiplier, PressedMultiplier, 1f);
            // Selected matches hover: a chassis button keeps focus after a click, and letting
            // that read brighter than its neighbours is honest -- it IS the one in hand.
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor =
                new Color(DisabledMultiplier, DisabledMultiplier, DisabledMultiplier, DisabledAlpha);
            colors.fadeDuration = FadeDuration;
            return colors;
        }
    }
}
