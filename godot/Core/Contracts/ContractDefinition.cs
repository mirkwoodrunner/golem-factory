using System.Collections.Generic;
using System.Linq;
using GolemFactory.PunchCards;

namespace GolemFactory.Contracts
{
    /// <summary>Which of a cart's two ladders a rung reward climbs (docs/supply-design.md §2). Serialized by value.</summary>
    public enum ContractRungKind
    {
        None = 0,
        Rate = 1,
        Capacity = 2,
    }

    /// <summary>
    /// What finishing a contract gives (docs/contracts-design.md §4): goods, and at most one
    /// cart rung, the next rung of <see cref="rungKind"/> on a cart of <see cref="rungGood"/> the
    /// player chooses when collecting it.
    /// </summary>
    public sealed class ContractReward
    {
        public List<RecipeIngredient> goods = new List<RecipeIngredient>();
        public string rungGood;
        public ContractRungKind rungKind;

        public bool HasRung => rungKind != ContractRungKind.None && !string.IsNullOrEmpty(rungGood);

        public bool IsEmpty => !HasRung && goods.All(g => g.quantity <= 0);
    }

    /// <summary>
    /// One contract in the campaign's sequence (docs/contracts-design.md §2, §3), authored in
    /// <c>data/contracts.json</c>. The city asks for an amount of one or two goods, from anywhere;
    /// finishing within its par adds the bonus. Its name is what a save records, so a contract is
    /// never renamed (<c>godot/data/CLAUDE.md</c>).
    /// </summary>
    public sealed class ContractDefinition
    {
        public string name;

        /// <summary>The sequence runs in this order, 1 first.</summary>
        public int order;

        /// <summary>Who asks: "City Watch", "Lamplighters' Guild". Flavour, never an id.</summary>
        public string requester;

        /// <summary>Their words, one sentence.</summary>
        public string words;

        /// <summary>One or two lines of goods, in the order the notice lists them.</summary>
        public List<RecipeIngredient> asks = new List<RecipeIngredient>();

        /// <summary>Finishing within this many seconds of accepting it earns the bonus. On the simulation clock.</summary>
        public int parSeconds;

        public ContractReward reward = new ContractReward();
        public ContractReward bonus = new ContractReward();

        /// <summary>
        /// Posted once the Ledger has seen every one of these goods (invariant 8): always every good
        /// it asks for, so a contract never asks for a good the factory hasn't made.
        /// </summary>
        public List<string> postWhenSeen = new List<string>();

        /// <summary>And, when above 0, once this Clock Tower stage has started.</summary>
        public int postWhenTowerStage;

        /// <summary>How many of <paramref name="itemType"/> it asks for; 0 for a good it doesn't.</summary>
        public int Asked(string itemType) => asks.Where(a => a.itemType == itemType).Sum(a => a.quantity);

        public bool AsksFor(string itemType) => Asked(itemType) > 0;
    }
}
