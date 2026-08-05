using UnityEngine;

namespace GolemFactory.World
{
    /// <summary>
    /// How a <see cref="ResourceNodeMarker"/> should look for a given remaining quantity.
    /// </summary>
    public readonly struct ResourceNodeVisual
    {
        public readonly Color Tint;
        public readonly float Scale;
        public readonly bool IsDepleted;

        public ResourceNodeVisual(Color tint, float scale, bool isDepleted)
        {
            Tint = tint;
            Scale = scale;
            IsDepleted = isDepleted;
        }
    }

    /// <summary>
    /// Pure mapping from a node's remaining quantity to its on-screen appearance -- the
    /// recorded scope cut "no node-depletion visual feedback" (see the implementation plan's
    /// Deliberate scope cuts), where harvesting a spent node produced only a status string.
    /// <para>
    /// The channels are <b>brightness and size</b>, deliberately not hue. The world is warm
    /// wood-and-brass and the node icons are already warm (rust, brass ingot); a red "empty"
    /// tint measured barely above the floor's own luminance in this palette and reads as
    /// "dimmer", not "spent". Draining brightness and shrinking the pile says the same thing
    /// in two channels that both survive the warm background, and the depleted end point is
    /// desaturated toward cold ash so it separates from every other warm object on screen.
    /// </para>
    /// </summary>
    public static class ResourceNodeVisualState
    {
        /// <summary>Fully stocked: the sprite's own authored colour, untouched.</summary>
        public static readonly Color FullTint = Color.white;

        /// <summary>
        /// Spent: cold, dim ash. Luminance is roughly a quarter of the full tint's, which is
        /// well outside the band any lighting variation moves a sprite through, so "empty" can
        /// never be mistaken for "in shadow".
        /// </summary>
        public static readonly Color DepletedTint = new Color(0.34f, 0.33f, 0.35f, 1f);

        public const float FullScale = 1f;
        public const float DepletedScale = 0.66f;

        /// <summary>
        /// Below this fraction the node is "running low" -- the point at which the prompt
        /// starts calling it out in words as well, so the visual and the text agree.
        /// </summary>
        public const float LowFraction = 0.25f;

        /// <summary>
        /// <paramref name="peak"/> is the largest quantity this node has been observed to
        /// hold, not a configured capacity: ResourceNode has no capacity concept, so the only
        /// honest denominator is the high-water mark. An infinite node
        /// (<see cref="ResourceNode.Infinite"/>) always reads as full -- it never drains, so
        /// draining it visually would be a lie.
        /// </summary>
        public static ResourceNodeVisual Evaluate(int remaining, int peak)
        {
            if (remaining == ResourceNode.Infinite)
            {
                return new ResourceNodeVisual(FullTint, FullScale, false);
            }

            if (remaining <= 0)
            {
                return new ResourceNodeVisual(DepletedTint, DepletedScale, true);
            }

            float fraction = peak > 0 ? Mathf.Clamp01((float)remaining / peak) : 1f;
            // Eased so the visible change is front-loaded toward empty: the difference between
            // 100 and 90 left doesn't matter, the difference between 10 and 1 does.
            float t = Mathf.Sqrt(fraction);
            return new ResourceNodeVisual(
                Color.Lerp(DepletedTint, FullTint, t),
                Mathf.Lerp(DepletedScale, FullScale, t),
                false);
        }

        /// <summary>
        /// The remaining-quantity phrase shown in the interaction prompt. Infinite nodes say
        /// so rather than printing a meaningless -1.
        /// </summary>
        public static string DescribeRemaining(int remaining)
        {
            if (remaining == ResourceNode.Infinite)
            {
                return "unlimited";
            }

            if (remaining <= 0)
            {
                return "depleted";
            }

            return remaining + " left";
        }

        public static bool IsRunningLow(int remaining, int peak) =>
            remaining != ResourceNode.Infinite && remaining > 0 && peak > 0 &&
            (float)remaining / peak <= LowFraction;
    }
}
