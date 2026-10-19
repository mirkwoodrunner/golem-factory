using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Whether a golem's source and target tiles are drawn, and from which cell they hang
    /// (docs/onboarding-feedback-design.md §4.2). The ONE answer for the carry preview, the
    /// player's proximity focus and mouse hover, so the three can't drift: carried, the tiles
    /// hang off the landing cell; otherwise off the golem's own cell while it is focused or
    /// hovered. A hovered golem's tiles show only while hovered: no dwell, no fade.
    /// </summary>
    public static class GolemTileOverlay
    {
        public readonly struct Decision
        {
            public bool Show { get; }
            public Vector2Int Cell { get; }

            public Decision(bool show, Vector2Int cell)
            {
                Show = show;
                Cell = cell;
            }
        }

        /// <summary>
        /// Whether a building standing on <paramref name="cell"/> hides the floor there: a
        /// crate, a stall, a boiler. The routing tile under it is covered, so its arrow is drawn
        /// above the building instead (Phase 2 critique; the hover's arrows vanished under the
        /// very crate a golem hauls from). A belt is floor-level and hides nothing.
        /// </summary>
        public static bool Covered(SpatialEndpointRegistry endpoints, Vector2Int cell) =>
            endpoints != null && endpoints.TryGetEndpoint(cell, out IItemEndpoint endpoint) &&
            endpoint != null && !(endpoint is BeltSegmentEndpoint);

        public static Decision Decide(bool hovered, bool focused, Vector2Int? carryLanding, bool carried, Vector2Int cell)
        {
            if (carried)
            {
                return carryLanding is Vector2Int landing ? new Decision(true, landing) : new Decision(false, cell);
            }
            return new Decision(hovered || focused, cell);
        }
    }
}
