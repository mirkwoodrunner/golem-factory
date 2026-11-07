using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Contracts
{
    /// <summary>The generated contracts' settings (<c>sandbox.json</c>'s <c>endlessContracts</c>).</summary>
    public sealed class EndlessContractsSetup
    {
        public List<string> goods = new List<string>();
        public int secondOffset = 3;
        public float firstShare = 0.6f;
        public int plateauRaw = 3300;
        public int growthContracts = 8;
        public int parSecondsFirst = 300;
        public int parSecondsPlateau = 600;
        public List<string> rewardGoods = new List<string>();
        public float rewardShare = 0.5f;
        public float bonusShare = 0.25f;
        public int rungBonuses = 20;
        public List<string> rungGoods = new List<string>();
        public List<RequesterLines> requesters = new List<RequesterLines>();

        public sealed class RequesterLines
        {
            public string name;
            public List<string> lines = new List<string>();
        }
    }

    /// <summary>
    /// Contracts after the authored sequence (docs/endless-design.md §1.1): a formula, never chance,
    /// so contract k is the same in every game (invariant 4). It rotates who asks and what, grows
    /// its amounts over the first contracts and then holds them at a plateau of raw goods a factory
    /// at the golem cap fills well inside ten minutes, so variety comes from the rotation and not
    /// from ever-bigger numbers. Each posts once the Clock Tower stands, and only then.
    /// </summary>
    public sealed class ContractGenerator
    {
        /// <summary>A generated contract's name: <c>Endless_1</c>, <c>Endless_2</c>... What a save keeps.</summary>
        public const string NamePrefix = "Endless_";

        /// <summary>Generated contracts run after every authored one.</summary>
        public const int FirstOrder = 1000;

        private readonly EndlessContractsSetup _setup;
        private readonly IReadOnlyDictionary<string, double> _raw;
        private readonly Dictionary<int, ContractDefinition> _made = new Dictionary<int, ContractDefinition>();

        public ContractGenerator(EndlessContractsSetup setup, IEnumerable<RecipeDefinition> recipes)
        {
            _setup = setup ?? new EndlessContractsSetup();
            _raw = RawCosts(recipes ?? Enumerable.Empty<RecipeDefinition>());
        }

        public EndlessContractsSetup Setup => _setup;

        public bool CanGenerate => _setup.goods.Count >= 2 && _setup.requesters.Count > 0 && _setup.rewardGoods.Count > 0;

        /// <summary>The raw goods a contract of index <paramref name="k"/> asks for: growing, then level.</summary>
        public int BudgetRaw(int k)
        {
            int growth = Math.Max(1, _setup.growthContracts);
            return (int)Math.Round(_setup.plateauRaw * Math.Min(1.0, (k + 1) / (double)growth));
        }

        /// <summary>Whether <paramref name="name"/> is a generated contract's, and which.</summary>
        public static bool TryIndexOf(string name, out int k)
        {
            k = -1;
            return name != null && name.StartsWith(NamePrefix, StringComparison.Ordinal)
                && int.TryParse(name.Substring(NamePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                && (k = n - 1) >= 0;
        }

        /// <summary>The contract of index <paramref name="k"/> (0 first); the same object every time it is asked for.</summary>
        public ContractDefinition Generate(int k)
        {
            if (k < 0 || !CanGenerate)
            {
                return null;
            }
            if (_made.TryGetValue(k, out ContractDefinition made))
            {
                return made;
            }

            int budget = BudgetRaw(k);
            List<string> goods = _setup.goods;
            string first = goods[k % goods.Count];
            string second = goods[(k + Math.Max(1, _setup.secondOffset)) % goods.Count];
            var asks = new List<RecipeIngredient>
            {
                new RecipeIngredient(first, Amount(first, budget * _setup.firstShare)),
            };
            if (second != first)
            {
                asks.Add(new RecipeIngredient(second, Amount(second, budget * (1f - _setup.firstShare))));
            }
            double askedRaw = asks.Sum(a => Raw(a.itemType) * a.quantity);

            EndlessContractsSetup.RequesterLines who = _setup.requesters[k % _setup.requesters.Count];
            string line = who.lines.Count == 0 ? "The city needs {goods}." : who.lines[k / _setup.requesters.Count % who.lines.Count];

            var contract = new ContractDefinition
            {
                name = NamePrefix + (k + 1).ToString(CultureInfo.InvariantCulture),
                order = FirstOrder + k,
                requester = who.name,
                words = line.Replace("{goods}", ContractText.Goods(asks)),
                asks = asks,
                parSeconds = Par(k),
                reward = new ContractReward { goods = Share(_setup.rewardGoods[k % _setup.rewardGoods.Count], askedRaw * _setup.rewardShare) },
                bonus = Bonus(k, askedRaw),
                postWhenTowerComplete = true,
            };
            contract.postWhenSeen.AddRange(asks.Select(a => a.itemType));
            _made[k] = contract;
            return contract;
        }

        private int Par(int k)
        {
            int growth = Math.Max(1, _setup.growthContracts);
            double t = Math.Min(1.0, k / (double)Math.Max(1, growth - 1));
            return (int)Math.Round(_setup.parSecondsFirst + (_setup.parSecondsPlateau - _setup.parSecondsFirst) * t);
        }

        // The first rungBonuses bonuses are endless cart rungs, Rate then Capacity, round the carts'
        // goods; after that, goods in the next reward good.
        private ContractReward Bonus(int k, double askedRaw)
        {
            if (k < _setup.rungBonuses && _setup.rungGoods.Count > 0)
            {
                return new ContractReward
                {
                    rungGood = _setup.rungGoods[k % _setup.rungGoods.Count],
                    rungKind = k / _setup.rungGoods.Count % 2 == 0 ? ContractRungKind.Rate : ContractRungKind.Capacity,
                };
            }
            string good = _setup.rewardGoods[(k + 1) % _setup.rewardGoods.Count];
            return new ContractReward { goods = Share(good, askedRaw * _setup.bonusShare) };
        }

        // At least one, else a multiple of five when there are ten or more: amounts that read as
        // a city's order, not a calculation.
        private int Amount(string good, double raw)
        {
            double n = raw / Raw(good);
            if (n < 10)
            {
                return Math.Max(1, (int)Math.Floor(n));
            }
            return (int)Math.Floor(n / 5) * 5;
        }

        // Rounded down, so a reward never exceeds its share (§4: never worth more than the contract).
        private List<RecipeIngredient> Share(string good, double raw)
        {
            int n = (int)Math.Floor(raw / Raw(good));
            return n > 0 ? new List<RecipeIngredient> { new RecipeIngredient(good, n) } : new List<RecipeIngredient>();
        }

        /// <summary>A good's cost in raw goods, as the contracts design counts it (§4).</summary>
        public double Raw(string good) => _raw.TryGetValue(good ?? "", out double r) && r > 0 ? r : 1.0;

        private static readonly string[] RawGoods =
        {
            ItemType.Scrap, ItemType.Coal, ItemType.CopperOre, ItemType.ZincOre, ItemType.Aether,
        };

        /// <summary>
        /// Every good's cost in raw goods (docs/contracts-design.md §4): a raw good is 1, a made good
        /// its cheapest recipe's inputs over its output, and Slag, which only a byproduct makes, 1.
        /// </summary>
        public static Dictionary<string, double> RawCosts(IEnumerable<RecipeDefinition> recipes)
        {
            var cost = RawGoods.ToDictionary(g => g, g => 1.0);
            cost[ItemType.Slag] = 1.0;
            List<RecipeDefinition> list = recipes.Where(r => r != null && r.outputQuantity > 0).ToList();
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (RecipeDefinition recipe in list)
                {
                    if (!recipe.inputs.All(i => cost.ContainsKey(i.itemType)))
                    {
                        continue;
                    }
                    double each = recipe.inputs.Sum(i => cost[i.itemType] * i.quantity) / recipe.outputQuantity;
                    if (!cost.TryGetValue(recipe.outputItemType, out double known) || each < known - 1e-9)
                    {
                        cost[recipe.outputItemType] = each;
                        grew = true;
                    }
                }
            }
            return cost;
        }
    }
}
