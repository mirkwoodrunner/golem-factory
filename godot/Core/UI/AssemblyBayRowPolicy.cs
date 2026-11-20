namespace GolemFactory.UI
{
    /// <summary>
    /// What the Assembly Line tab's golem-cap row says. Pure, so the wording is testable without a
    /// Canvas. The cap is fixed, so the row has no button (2026-11-19).
    /// </summary>
    public static class AssemblyBayRowPolicy
    {
        /// <summary>
        /// The occupancy readout, e.g. <c>"Golems 7 / 112"</c>. Standing first, because the number
        /// that changes is the one the player is watching.
        /// </summary>
        public static string FormatOccupancy(int occupied, int capacity) =>
            "Golems " + occupied + " / " + capacity;
    }
}
