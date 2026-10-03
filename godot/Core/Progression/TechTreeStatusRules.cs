using System.Collections.Generic;

namespace GolemFactory.Progression
{
    /// <summary>
    /// Turns a <see cref="TechTreeProgressLedger"/> into one <see cref="TechTreeNodeState"/> per
    /// catalog node. Pure and engine-free: the panel does nothing but colour the answer.
    ///
    /// <para>The three rules, in order:</para>
    /// <list type="number">
    /// <item>A node whose unlock signal has been observed is <b>Researched</b>, whatever its
    /// prerequisites say. The world is the authority -- a player who somehow holds a Lens has made
    /// Lenses, and a chart that called that Locked would be arguing with the game.</item>
    /// <item>A node whose every prerequisite is Researched is <b>Available</b>. A signal-less
    /// Technique node is Researched here instead, because "you may now do this" is all such a node
    /// ever claims and there is nothing else to wait for.</item>
    /// <item>Anything else is <b>Locked</b>.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Planned nodes stop at Available</b> (<see cref="TechTreeNode.IsPlanned"/>). They describe
    /// things docs/progression-design.md specifies and the build does not implement, so no signal
    /// for them can ever arrive; promoting one to Researched on prerequisites alone would have the
    /// chart report the Freight Link as unlocked in a game that has no Freight Link.
    /// </para>
    /// </summary>
    public static class TechTreeStatusRules
    {
        /// <summary>
        /// Resolves every node in <paramref name="nodes"/> into <paramref name="states"/>, which
        /// must be at least as long. Writing into a caller-owned array rather than returning a new
        /// dictionary keeps a per-poll refresh allocation-free.
        /// </summary>
        public static void Resolve(
            IReadOnlyList<TechTreeNode> nodes,
            TechTreeProgressLedger ledger,
            TechTreeNodeState[] states)
        {
            if (nodes == null || states == null)
            {
                return;
            }

            var byId = new Dictionary<string, int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                states[i] = TechTreeNodeState.Locked;
                byId[nodes[i].Id] = i;
            }

            // Rule 1 first and in one pass: an observed signal never depends on anything else.
            for (int i = 0; i < nodes.Count; i++)
            {
                if (ledger != null && ledger.SignalObserved(nodes[i]) && !nodes[i].IsPlanned)
                {
                    states[i] = TechTreeNodeState.Researched;
                }
            }

            // Rules 2 and 3 by fixpoint rather than by walking the table in order. The catalog
            // happens to be authored in dependency order today; relying on that would make a
            // future reordering silently wrong instead of merely slower.
            bool changed;
            int guard = 0;
            do
            {
                changed = false;
                guard++;

                for (int i = 0; i < nodes.Count; i++)
                {
                    if (states[i] == TechTreeNodeState.Researched)
                    {
                        continue;
                    }

                    if (!PrerequisitesMet(nodes[i], byId, states))
                    {
                        continue;
                    }

                    TechTreeNodeState resolved =
                        nodes[i].Signal == TechTreeUnlockSignal.None && !nodes[i].IsPlanned
                            ? TechTreeNodeState.Researched
                            : TechTreeNodeState.Available;

                    if (states[i] != resolved)
                    {
                        states[i] = resolved;
                        changed = true;
                    }
                }
            }
            while (changed && guard <= nodes.Count);
        }

        /// <summary>
        /// The single node the chart nominates as "what now?" -- the first Available node in
        /// catalog order, skipping planned ones, which reads phase by phase and row by row exactly
        /// as the chart is drawn. Returns null when nothing is available (a fresh save with an
        /// empty ledger still has its two rootless phase-I nodes, so that means everything is
        /// done). Mirrors §8's "Next Objective" line rather than inventing a second notion of it.
        /// </summary>
        public static TechTreeNode FindNextObjective(
            IReadOnlyList<TechTreeNode> nodes, TechTreeNodeState[] states)
        {
            if (nodes == null || states == null)
            {
                return null;
            }

            for (int i = 0; i < nodes.Count && i < states.Length; i++)
            {
                if (states[i] == TechTreeNodeState.Available && !nodes[i].IsPlanned)
                {
                    return nodes[i];
                }
            }

            return null;
        }

        /// <summary>Counts researched nodes, for the chart's "12 / 45 researched" header.</summary>
        public static int CountResearched(TechTreeNodeState[] states)
        {
            if (states == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] == TechTreeNodeState.Researched)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool PrerequisitesMet(
            TechTreeNode node, Dictionary<string, int> byId, TechTreeNodeState[] states)
        {
            for (int p = 0; p < node.Prerequisites.Count; p++)
            {
                // An unknown prerequisite id is treated as unmet rather than ignored: a typo in
                // the catalog should show as a locked branch, not as a node that unlocks itself.
                if (!byId.TryGetValue(node.Prerequisites[p], out int index)
                    || states[index] != TechTreeNodeState.Researched)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
