using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
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
        private readonly string _rateSeries;
        private readonly float _ticksPerSecond;

        /// <param name="rateSeriesId">
        /// The rate tracker's series the live rate reads: the Sandbox's is
        /// <see cref="ProductionTally.SeriesId"/>, fresh production (docs/crates-design.md §4.5).
        /// </param>
        public TechTreeReadout(IReadOnlyList<RecipeDefinition> recipes, BufferRateTracker throughput, string rateSeriesId, float ticksPerSecond)
        {
            _recipes = recipes ?? new List<RecipeDefinition>();
            _throughput = throughput;
            _rateSeries = rateSeriesId;
            _ticksPerSecond = ticksPerSecond;
        }

        // --- The plaque's unlock (2026-11-19): a recipe or chassis node claims its card ---------

        private AssemblyLineState _line;
        private AssemblyLineBoard _board;

        /// <summary>Lets a plaque claim its card through the Assembly Line tab's own Claim. Null leaves the pane read-only.</summary>
        public void ConfigureUnlocks(AssemblyLineState line, AssemblyLineBoard board)
        {
            _line = line;
            _board = board;
        }

        /// <summary>The card the selected plaque unlocks, or null: a recipe's Assemble card, a chassis's card, Repeat, Freight Launch.</summary>
        public DraftableCardDefinition SelectedCard =>
            TechTreeCatalog.TryGetNode(SelectedNodeId ?? "", out TechTreeNode node) ? CardFor(node) : null;

        /// <summary>The card a node unlocks in <paramref name="deck"/>, or null.</summary>
        public static DraftableCardDefinition CardFor(TechTreeNode node, IEnumerable<DraftableCardDefinition> deck, IReadOnlyList<RecipeDefinition> recipes)
        {
            if (node == null || deck == null)
            {
                return null;
            }
            switch (node.Kind)
            {
                case TechTreeNodeKind.Chassis:
                    return deck.FirstOrDefault(c => c?.chassis != null && c.chassis.name == node.SignalId);
                case TechTreeNodeKind.Recipe:
                    RecipeDefinition recipe = RecipeLedger.FindByNodeName(recipes, node.DisplayName) ?? RecipeLedger.FindByOutput(recipes, node.SignalId);
                    return recipe == null ? null : deck.FirstOrDefault(c => c?.appendage != null && c.appendage.recipe == recipe);
                default:
                    AppendageActionType? verb = node.Id == "verb.repeat" ? AppendageActionType.Repeat
                        : node.Id == "verb.freightlink" ? AppendageActionType.FreightLaunch : (AppendageActionType?)null;
                    return verb == null ? null : deck.FirstOrDefault(c => c?.appendage != null && c.appendage.actionType == verb.Value);
            }
        }

        private DraftableCardDefinition CardFor(TechTreeNode node) => _line == null ? null : CardFor(node, _line.Deck, _recipes);

        /// <summary>
        /// The pane's unlock line: "Claim for 4 Scrap", "Claim for free", "Claimed", "Locked: needs
        /// Brass", or "" for a plaque with no card.
        /// </summary>
        public string UnlockLine
        {
            get
            {
                DraftableCardDefinition card = SelectedCard;
                if (card == null)
                {
                    return "";
                }
                if (_line.IsClaimed(card))
                {
                    return "Claimed";
                }
                if (!_line.IsUnlocked(card))
                {
                    return "Locked: needs " + WorkbenchDiagnostics.Humanize(_line.DescribeMissingPrerequisites(card));
                }
                IReadOnlyList<RecipeIngredient> price = AssemblyLineState.PriceOf(card);
                return price.Count == 0 ? "Claim for free" : "Claim for " + RecipeLedger.FormatBundle(price);
            }
        }

        /// <summary>Whether the pane's Claim is live: the plaque's card is on offer and the wallet can pay.</summary>
        public bool CanClaim
        {
            get
            {
                DraftableCardDefinition card = SelectedCard;
                return card != null && _board != null && _line.IsOffered(card) && _board.CanAffordCard(card);
            }
        }

        /// <summary>The pane's Claim: the same claim the Assembly Line tab makes.</summary>
        public bool Claim() => SelectedCard is DraftableCardDefinition card && _board != null && _board.Claim(card);

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
            // A plaque with a card says where the card stands, so the title never reads "locked"
            // over a live Claim (the research state is the chart's colour).
            DraftableCardDefinition card = CardFor(node);
            string state = card == null ? DescribeState(node, states)
                : _line.IsClaimed(card) ? "claimed"
                : _line.IsOffered(card) ? "on offer"
                : "locked";
            return node.DisplayName + "  ·  " + state;
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
                bool measured = _throughput != null && _throughput.TryGetRatePerMinute(_rateSeries, recipe.outputItemType, out live);
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
