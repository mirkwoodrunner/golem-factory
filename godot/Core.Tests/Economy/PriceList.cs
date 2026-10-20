using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Every price in the game, and when each opens, measured along the guide's steps as the
    /// playtest kit plays them (docs/pack-upgrades-design.md §1, docs/tuning-data-spec.md
    /// §15.2-§15.3). Shared by <c>PriceReachTests</c>, <c>PriceCeilingTests</c> and
    /// <c>StepGoodsReachTests</c>, so the three read one list and one harness.
    /// </summary>
    internal sealed class PriceList
    {
        /// <summary>One price: what it buys, its lines, and how many pack rungs may be bought before it.</summary>
        public sealed class Price
        {
            public string What;
            public IReadOnlyList<RecipeIngredient> Lines;
            public int RungsBelow;
        }

        public SandboxWorld World { get; }

        /// <summary>Seen(s): the goods the Ledger holds once step s is done, before the kit performs the next.</summary>
        public IReadOnlyList<HashSet<string>> Seen { get; }

        /// <summary>What the kit put into the pack while step s was current: the goods the step asks for.</summary>
        public IReadOnlyList<Dictionary<string, int>> Deposited { get; }

        public List<Price> Prices { get; } = new List<Price>();

        public IReadOnlyList<PackUpgradeRung> Rungs => World.PackUpgrade.Rungs;

        private PriceList(SandboxWorld world, List<HashSet<string>> seen, List<Dictionary<string, int>> deposited)
        {
            World = world;
            Seen = seen;
            Deposited = deposited;
            ListPrices();
        }

        /// <summary>A fresh game, played by the kit through every chapter to "done".</summary>
        public static PriceList Play()
        {
            SandboxWorld world = Compose();
            world.Clock.Play();
            var seen = new List<HashSet<string>>();
            var deposited = new List<Dictionary<string, int>>();
            StorageBuffer pack = world.Wallet.Pack;
            IReadOnlyList<string> goods = ItemTiers.CanonicalOrder;
            for (int chapter = 0; chapter < world.Tutorial.ChapterCount && world.Tutorial.Current?.Id != "done"; chapter++)
            {
                Assert.IsTrue(world.Tutorial.StartKit(), "the kit started");
                for (int frame = 0; frame < 3600 && world.Tutorial.KitRunning; frame++)
                {
                    // A step the world finished mid-frame has the next one performed in the same
                    // Update, so it is credited with what the Ledger held before that frame.
                    var before = new HashSet<string>(world.TechTree.Ledger.Items);
                    int[] given = goods.Select(pack.TotalDeposited).ToArray();
                    world.Advance(1f / 30f);
                    // The kit performs the step current at the end of the frame's Update.
                    int performed = world.Tutorial.Index;
                    while (deposited.Count <= performed)
                    {
                        deposited.Add(new Dictionary<string, int>());
                    }
                    for (int g = 0; g < goods.Count; g++)
                    {
                        int more = pack.TotalDeposited(goods[g]) - given[g];
                        if (more > 0)
                        {
                            deposited[performed].TryGetValue(goods[g], out int held);
                            deposited[performed][goods[g]] = held + more;
                        }
                    }
                    while (seen.Count < world.Tutorial.Index)
                    {
                        seen.Add(before);
                    }
                    world.TechTree.Poll();
                    world.Tutorial.Settle();
                    while (seen.Count < world.Tutorial.Index)
                    {
                        seen.Add(new HashSet<string>(world.TechTree.Ledger.Items));
                    }
                }
                Assert.IsFalse(world.Tutorial.KitRunning, $"the kit finished (stuck on '{world.Tutorial.Current?.Title}')");
            }
            Assert.AreEqual("done", world.Tutorial.Current.Id, "the kit played the whole guide");
            while (deposited.Count < world.Tutorial.Steps.Count)
            {
                deposited.Add(new Dictionary<string, int>());
            }
            return new PriceList(world, seen, deposited);
        }

        /// <summary>The first step after which every good of <paramref name="price"/> has been seen.</summary>
        public int Opens(IReadOnlyList<RecipeIngredient> price)
        {
            for (int s = 0; s < Seen.Count; s++)
            {
                if ((price ?? new RecipeIngredient[0]).Where(c => c.quantity > 0).All(c => Seen[s].Contains(c.itemType)))
                {
                    return s;
                }
            }
            return Seen.Count; // never made on the guide's path: after the last step
        }

        /// <summary>The pack's cap at step <paramref name="opens"/>, counting at most <paramref name="rungsBelow"/> rungs.</summary>
        public int Reachable(int opens, int rungsBelow)
        {
            int j = 0;
            while (j < rungsBelow && j < Rungs.Count && Opens(Rungs[j].cost) <= opens)
            {
                j++;
            }
            return j == 0 ? World.Setup.packCapacityPerType : Rungs[j - 1].capacityPerType;
        }

        public string StepAt(int s) => s < World.Tutorial.Steps.Count ? World.Tutorial.Steps[s].Id : "after the guide";

        public int ChapterAt(int s) => World.Tutorial.ChapterOf(s);

        /// <summary>Each good's lines summed, as <c>TryCharge</c> sums them.</summary>
        public static IEnumerable<(string good, int sum)> Summed(IReadOnlyList<RecipeIngredient> price) =>
            (price ?? new RecipeIngredient[0]).GroupBy(c => c.itemType).Select(g => (g.Key, g.Sum(c => c.quantity)));

        public static IReadOnlyList<RecipeIngredient> ClaimPrice(DraftableCardDefinition card) =>
            card.HasBundleCost ? card.claimCost
                : card.baseCost > 0 ? new[] { new RecipeIngredient(ItemType.Scrap, card.baseCost) } : new RecipeIngredient[0];

        public IEnumerable<DraftableCardDefinition> Cards =>
            World.Definitions.Decks.Values.SelectMany(d => d.Cards.Concat(d.OpeningHand)).Where(c => c != null).Distinct();

        public Price Find(string what) => Prices.Single(p => p.What == what);

        private void Add(string what, IReadOnlyList<RecipeIngredient> price, int rungsBelow = -1) =>
            Prices.Add(new Price { What = what, Lines = price ?? new RecipeIngredient[0], RungsBelow = rungsBelow < 0 ? Rungs.Count : rungsBelow });

        private void ListPrices()
        {
            SandboxWorld world = World;
            foreach (PlaceableEntry p in world.Placeables)
            {
                Add("placeable " + p.Key, p.Prefab.Cost);
            }
            foreach (ChassisDefinition c in world.Definitions.Chassis.Values)
            {
                Add("chassis " + c.name, c.cost);
            }
            foreach (DraftableCardDefinition card in Cards)
            {
                Add("card " + card.name, ClaimPrice(card));
            }
            foreach (string stall in world.Market.StallIds)
            {
                world.Market.TryGetOffer(stall, out MarketOffer offer);
                Add("truckload " + stall, offer.Price);
            }
            Add("assembly bays", world.AssemblyBay.UpgradeCost);
            List<List<RecipeIngredient>> purchases = world.Setup.floorExpansion.purchases;
            for (int n = 0; n < purchases.Count; n++)
            {
                Add("floor expansion " + (n + 1), purchases[n]);
            }
            for (int k = 0; k < Rungs.Count; k++)
            {
                Add("pack rung " + (k + 1), Rungs[k].cost, k);
            }

            // docs/tuning-data-spec.md §15.2 (open-items §4): a crank's inputs, and the boiler's
            // hand-load, are charged to the wallet like any price.
            foreach (RecipeDefinition recipe in world.StarterBench.CrankableRecipes)
            {
                Add("bench " + recipe.name, recipe.inputs);
            }
            Add("boiler hand-load", new[] { new RecipeIngredient(ItemType.Coke, BoilerRefuelPolicy.HandLoadBatch) });
        }
    }
}
