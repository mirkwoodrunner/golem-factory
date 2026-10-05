using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    /// <summary>
    /// The Ledger's recipe readout (docs/cozy-automation-design.md §4a), without the chart: which
    /// node is selected, and what its pane says -- Unity's TechTreePanel.RefreshDetailPane taken
    /// out of the view (G8).
    ///
    /// <para>
    /// A recipe node shows its real ratio, byproduct, cycle and (once there is history) the live
    /// rate, resolved by RECIPE NUMBER rather than by unlock signal (r4.ironsmelting signals on
    /// Slag, which no recipe search by output would match to R4); everything else keeps the
    /// catalog's authored line. Selection is kept by node id, never by index, so a chart rebuild
    /// cannot point it at a different node.
    /// </para>
    /// </summary>
    public sealed class TechTreeReadout
    {
        private readonly IReadOnlyList<RecipeDefinition> _recipes;
        private readonly BufferRateTracker _throughput;
        private readonly string _stockpile;
        private readonly float _ticksPerSecond;

        public TechTreeReadout(IReadOnlyList<RecipeDefinition> recipes, BufferRateTracker throughput, string stockpileBufferId, float ticksPerSecond)
        {
            _recipes = recipes ?? new List<RecipeDefinition>();
            _throughput = throughput;
            _stockpile = stockpileBufferId;
            _ticksPerSecond = ticksPerSecond;
        }

        /// <summary>The selected node's id, or null until a plaque is clicked.</summary>
        public string SelectedNodeId { get; private set; }

        public void Select(string nodeId) => SelectedNodeId = nodeId;

        public bool HasSelection => !string.IsNullOrEmpty(SelectedNodeId) && TechTreeCatalog.TryGetNode(SelectedNodeId, out _);

        /// <summary>"R4 · Iron Smelting  ·  available" -- the pane's title line.</summary>
        public string Title(TechTreeNodeState[] states)
        {
            if (!TechTreeCatalog.TryGetNode(SelectedNodeId ?? "", out TechTreeNode node))
            {
                return "";
            }
            return node.DisplayName + "  ·  " + DescribeState(node, states);
        }

        /// <summary>The pane's body: the recipe's ledger, or the node's own authored line. "" with nothing selected.</summary>
        public string Body
        {
            get
            {
                if (!TechTreeCatalog.TryGetNode(SelectedNodeId ?? "", out TechTreeNode node))
                {
                    return "";
                }
                RecipeDefinition recipe = null;
                if (node.Kind == TechTreeNodeKind.Recipe)
                {
                    recipe = RecipeLedger.FindByNodeName(_recipes, node.DisplayName) ?? RecipeLedger.FindByOutput(_recipes, node.SignalId);
                }
                if (recipe == null)
                {
                    return node.Detail;
                }
                float live = 0f;
                bool measured = _throughput != null && _throughput.TryGetRatePerMinute(_stockpile, recipe.outputItemType, out live);
                return RecipeLedger.Describe(recipe, _ticksPerSecond, measured, live);
            }
        }

        private static string DescribeState(TechTreeNode node, TechTreeNodeState[] states)
        {
            if (node.IsPlanned)
            {
                return "planned";
            }
            for (int i = 0; i < TechTreeCatalog.Nodes.Count; i++)
            {
                if (TechTreeCatalog.Nodes[i] == node && states != null && i < states.Length)
                {
                    return states[i].ToString().ToLowerInvariant();
                }
            }
            return "";
        }
    }
}
