using GolemFactory.Economy;

namespace GolemFactory.UI
{
    /// <summary>
    /// The wording of a "you just gained this" <see cref="FloatingPopup"/>: <c>"+1 Coke"</c>.
    ///
    /// <para>
    /// One function rather than three hand-written call sites, because the three places goods
    /// land in the player's own stockpile -- harvesting a node, hand-loading a boiler, finishing
    /// a hand-crank -- were written months apart and had already drifted. The harvest line
    /// printed the raw id, so copper ore read "+1 CopperOre" while the boiler's hand-written
    /// line read "+3 Coke"; the bench printed nothing at all.
    /// </para>
    ///
    /// <para>
    /// Pure and engine-free so a test can pin the wording without a scene, the same
    /// "pure function + thin applier" split as <c>InteractionTargeting.BuildPrompt</c>.
    /// </para>
    /// </summary>
    public static class YieldPopupText
    {
        /// <summary>
        /// <c>"+2 Iron Plate"</c>. Names the good the way a person reads it
        /// (<see cref="ItemTiers.DisplayName"/>), not the way the buffer keys it.
        /// </summary>
        /// <remarks>
        /// Returns an empty string for a missing type or a non-positive quantity, and callers
        /// pass that straight to <c>FloatingPopup.Spawn</c>: an empty caption is a popup that
        /// rises and fades showing nothing, which is the right amount of noise for a gain that
        /// did not happen. A "+0" would be worse -- it reads as a bug the player can see.
        /// </remarks>
        public static string Gain(string itemType, int quantity)
        {
            if (string.IsNullOrEmpty(itemType) || quantity <= 0)
            {
                return "";
            }

            return "+" + quantity + " " + ItemTiers.DisplayName(itemType);
        }
    }
}
