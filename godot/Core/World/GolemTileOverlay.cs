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
