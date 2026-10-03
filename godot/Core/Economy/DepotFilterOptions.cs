using System.Collections.Generic;

namespace GolemFactory.Economy
{
    /// <summary>
    /// What a depot's <c>[E]</c> key cycles through: the short, live list of labels a crate can
    /// wear right now.
    ///
    /// <para>
    /// <b>Why the list is dynamic rather than the whole roster.</b> There are 24 goods in §5.1.
    /// Cycling all of them one key press at a time is not a control scheme, it is a punishment,
    /// and 20 of the entries would be for goods the player has never seen. The cycle is instead
    /// built from what the stockpile has actually handled: you can only label a crate for
    /// something that has been in it. Four to eight entries a few minutes into a game, growing
    /// with the factory, which is the scale that makes one key work.
    /// </para>
    ///
    /// <para>
    /// Engine-free static so the ordering and the wrap-around are a test rather than a comment --
    /// the same split <c>HandCrankRules.NextIndex</c> uses for the bench's recipe cycle, which
    /// this deliberately mirrors.
    /// </para>
    /// </summary>
    public static class DepotFilterOptions
    {
        /// <summary>
        /// The "accepts anything" entry. <c>null</c> rather than a magic string, because it is
        /// the same value <c>PlaceableDepot.FilterItemType</c> holds when unset -- so a depot
        /// authored before filters existed already reads as this, and no migration is needed.
        /// </summary>
        public const string AnyGoods = null;

        /// <summary>What the readout calls <see cref="AnyGoods"/>.</summary>
        public const string AnyGoodsLabel = "any goods";

        /// <summary>
        /// The options this depot can cycle through, in order.
        ///
        /// <para>
        /// Always leads with <see cref="AnyGoods"/>, so one more press always gets back to the
        /// default from anywhere in the list. Then every type in <paramref name="knownTypes"/>
        /// in <see cref="ItemTiers.CanonicalOrder"/>, deduplicated. Then
        /// <paramref name="currentFilter"/> if it is not already present.
        /// </para>
        ///
        /// <para>
        /// THAT LAST RULE IS THE ONE THAT MATTERS. A depot filtered for Iron Plate whose last
        /// Plate has just been consumed would otherwise find its own setting missing from its own
        /// cycle, and the next press would silently retune it to something else. A label stays on
        /// the crate until the player takes it off.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> BuildCycle(
            IEnumerable<string> knownTypes, string currentFilter)
        {
            var options = new List<string> { AnyGoods };
            var seen = new HashSet<string>();

            if (knownTypes != null)
            {
                foreach (string itemType in knownTypes)
                {
                    if (!string.IsNullOrEmpty(itemType) && seen.Add(itemType))
                    {
                        options.Add(itemType);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentFilter) && seen.Add(currentFilter))
            {
                options.Add(currentFilter);
            }

            // Sorted AFTER collection rather than inserted in order, so the caller may hand over
            // a dictionary's keys (StorageBuffer.Quantities) without the cycle inheriting
            // dictionary iteration order -- which .NET does not make contractual, and which two
            // depots reaching the same contents by different routes could therefore disagree on.
            //
            // Index 0 is left alone: AnyGoods leads by rule, not by sort position.
            options.Sort(1, options.Count - 1, CanonicalComparer.Instance);
            return options;
        }

        /// <summary>
        /// The next option after <paramref name="currentFilter"/>, wrapping at the end.
        /// A filter that is not in the cycle at all lands on the first entry, which is
        /// <see cref="AnyGoods"/> -- the safe answer, since an unlisted filter is one the player
        /// can no longer see.
        /// </summary>
        public static string Next(IReadOnlyList<string> cycle, string currentFilter)
        {
            if (cycle == null || cycle.Count == 0)
            {
                return AnyGoods;
            }

            for (int i = 0; i < cycle.Count; i++)
            {
                if (Same(cycle[i], currentFilter))
                {
                    return cycle[(i + 1) % cycle.Count];
                }
            }

            return cycle[0];
        }

        /// <summary>The label for one option, on the prompt and on the crate's own readout.</summary>
        public static string Describe(string filterItemType) =>
            string.IsNullOrEmpty(filterItemType)
                ? AnyGoodsLabel
                : ItemTiers.DisplayName(filterItemType);

        /// <summary>
        /// Whether two filter values mean the same thing. Null and empty both mean
        /// <see cref="AnyGoods"/>, and a serialized field comes back as <c>""</c> where code
        /// writes <c>null</c> -- so comparing them with <c>==</c> would make a freshly loaded
        /// depot disagree with an identical freshly placed one.
        /// </summary>
        public static bool Same(string a, string b)
        {
            bool aAny = string.IsNullOrEmpty(a);
            bool bAny = string.IsNullOrEmpty(b);
            if (aAny || bAny)
            {
                return aAny && bAny;
            }

            return string.Equals(a, b, System.StringComparison.Ordinal);
        }

        private sealed class CanonicalComparer : IComparer<string>
        {
            public static readonly CanonicalComparer Instance = new CanonicalComparer();

            public int Compare(string x, string y)
            {
                int byTier = ItemTiers.CanonicalPosition(x).CompareTo(ItemTiers.CanonicalPosition(y));
                // Ties are goods ItemTiers has never heard of, which both sort to int.MaxValue.
                // Falling back to ordinal keeps the order total, so the cycle is stable rather
                // than dependent on the input's own arrangement.
                return byTier != 0 ? byTier : string.CompareOrdinal(x, y);
            }
        }
    }
}
