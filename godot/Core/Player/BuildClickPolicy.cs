namespace GolemFactory.Player
{
    /// <summary>
    /// Whether a click should reach the world, or belongs to the UI that was under the cursor.
    ///
    /// <para>
    /// Pure and engine-free so the rule is testable without a scene, an EventSystem or a synthetic
    /// pointer -- the same split <c>HudScreenPolicy</c>, <c>BuildGhostVisuals</c> and
    /// <c>WorkbenchDropRules</c> already use for their own arbitration.
    /// </para>
    ///
    /// <para>
    /// FOUND IN PLAYTEST, and it was never only about one panel. <c>BuildModeController</c>
    /// subscribed to the Click action directly, and an <c>InputAction</c> fires wherever the
    /// cursor happens to be -- so every click on every piece of UI ALSO tried to place a building
    /// at whatever cell was under the menu. Closing the golem construction panel attempted a
    /// Depot; so did picking a row in the build menu itself, which meant selecting a placeable
    /// immediately tried to place one. It went unnoticed while everything was free and silent:
    /// restoring the placeables' costs is what made it start refusing out loud.
    /// </para>
    /// </summary>
    public static class BuildClickPolicy
    {
        /// <summary>
        /// True only when the click was over open world with a placeable in hand.
        ///
        /// <para>
        /// <paramref name="pointerOverUi"/> must be sampled during the frame's normal update, not
        /// read inside the input callback: Unity evaluates UI hit-testing once per frame, and
        /// querying it from within event processing answers with the previous frame's state.
        /// </para>
        /// </summary>
        public static bool ShouldPlace(bool hasPlaceableInHand, bool pointerOverUi) =>
            hasPlaceableInHand && !pointerOverUi;
    }
}
