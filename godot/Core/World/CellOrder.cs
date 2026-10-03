using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// THE total order over grid cells. One definition, shared by every system that has to
    /// answer a "which one first?" question reproducibly.
    ///
    /// <para>
    /// Established by the steam network (docs/progression-design.md §3.1, and the three
    /// determinism notes at the top of <c>Steam/SteamNetwork.cs</c>): a golem's cell is factory
    /// LAYOUT, so it is identical between two identically-built factories, it survives a
    /// save/load, and it does not move unless the player moves something. That is what makes it
    /// the right key, as against a <c>Dictionary</c> bucket (not contractual, and rehashing
    /// silently reshuffles it), registration or scene order (two identical factories built in a
    /// different order diverge), or distance-to-something (ties are the common case on a grid
    /// and need this underneath anyway).
    /// </para>
    ///
    /// <para>
    /// Column-major (x, then y) -- arbitrary but FIXED. Lives in World/ rather than in Steam/
    /// because §3.2's two-extractor-per-node cap needs exactly the same order and neither system
    /// should own it; <c>SteamPipeRules.CompareCells</c> now delegates here so the two can never
    /// drift apart.
    /// </para>
    /// </summary>
    public static class CellOrder
    {
        public static int Compare(Vector2Int a, Vector2Int b)
        {
            if (a.x != b.x)
            {
                return a.x < b.x ? -1 : 1;
            }

            if (a.y != b.y)
            {
                return a.y < b.y ? -1 : 1;
            }

            return 0;
        }
    }
}
