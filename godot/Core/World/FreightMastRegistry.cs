using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Every Freight Mast standing in the world, keyed by cell
    /// (docs/progression-design.md §6, "The Zeppelin's verb: the Freight Link").
    ///
    /// <para>
    /// A registry rather than a scene sweep because a Zeppelin binds to <b>exactly one</b> mast
    /// at placement and keeps it: the binding is a fixed pair, like a belt's <c>Next</c>, not a
    /// search repeated every tick. Something has to be able to answer "which masts exist" at the
    /// moment of binding, and a <c>FindObjectsByType</c> at that moment would answer differently
    /// depending on scene order.
    /// </para>
    ///
    /// <para>
    /// Plain C# behind a Holder, and the nearest-mast choice is broken by
    /// <see cref="CellOrder"/> for the reason §1.4 settled for steam: a tie decided by dictionary
    /// iteration is not contractual, so two identically-built factories could bind their
    /// Zeppelins to different masts and diverge for good.
    /// </para>
    /// </summary>
    public sealed class FreightMastRegistry
    {
        private readonly Dictionary<Vector2Int, string> _masts = new Dictionary<Vector2Int, string>();

        public int Count => _masts.Count;

        public void Register(Vector2Int cell, string mastId) => _masts[cell] = mastId ?? "";

        public bool Unregister(Vector2Int cell) => _masts.Remove(cell);

        public bool Contains(Vector2Int cell) => _masts.ContainsKey(cell);

        public bool TryGetId(Vector2Int cell, out string mastId) => _masts.TryGetValue(cell, out mastId);

        public void Clear() => _masts.Clear();

        /// <summary>
        /// The mast a golem standing on <paramref name="from"/> binds to: nearest by Chebyshev
        /// distance, ties broken by cell order.
        ///
        /// <para>
        /// CHEBYSHEV, not Euclidean, because the grid is what the player reads and a diagonal
        /// step costs what an orthogonal one does everywhere else in this game. Distance decides
        /// the binding and NOTHING ELSE -- §6 is explicit that the link works "regardless of
        /// distance", so this picks a partner and then stops mattering.
        /// </para>
        /// </summary>
        public bool TryFindNearest(Vector2Int from, out Vector2Int mastCell)
        {
            mastCell = default;
            bool found = false;
            int bestDistance = int.MaxValue;

            foreach (KeyValuePair<Vector2Int, string> pair in _masts)
            {
                int distance = Mathf.Max(
                    Mathf.Abs(pair.Key.x - from.x), Mathf.Abs(pair.Key.y - from.y));

                if (!found || distance < bestDistance ||
                    (distance == bestDistance && CellOrder.Compare(pair.Key, mastCell) < 0))
                {
                    mastCell = pair.Key;
                    bestDistance = distance;
                    found = true;
                }
            }

            return found;
        }
    }
}
