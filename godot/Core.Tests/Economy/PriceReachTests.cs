using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/pack-upgrades-design.md §0.1 and §1: every price fits the pack cap that's reachable
    /// when the price opens, measured along the guide's steps as the playtest kit plays them, the
    /// pack alone (no price assumes the Post). Replaces CrateTests' "every price fits a full 100 pack".
    /// </summary>
    public class PriceReachTests
    {
        [Test]
        public void EveryPriceInTheGame_FitsThePackReachableWhenItOpens()
        {
            SandboxWorld world = Compose();
            List<HashSet<string>> seen = SeenAfterEachStep(world);
            int Opens(IReadOnlyList<RecipeIngredient> price)
            {
                for (int s = 0; s < seen.Count; s++)
                {
                    if ((price ?? new RecipeIngredient[0]).Where(c => c.quantity > 0).All(c => seen[s].Contains(c.itemType)))
                    {
                        return s;
                    }
                }
                return seen.Count; // never made on the guide's path: after the last step
            }

            IReadOnlyList<PackUpgradeRung> rungs = world.PackUpgrade.Rungs;
            int baseCap = world.Setup.packCapacityPerType;
            int Reachable(int opens, int rungsBelow)
            {
                int j = 0;
                while (j < rungsBelow && Opens(rungs[j].cost) <= opens)
                {
                    j++;
                }
                return j == 0 ? baseCap : rungs[j - 1].capacityPerType;
            }

            for (int k = 1; k < rungs.Count; k++)
            {
                Assert.GreaterOrEqual(Opens(rungs[k].cost), Opens(rungs[k - 1].cost), $"rung {k + 1} opens no sooner than rung {k}");
            }

            var prices = new List<(string what, IReadOnlyList<RecipeIngredient> price, int rungsBelow)>();
            void Add(string what, IReadOnlyList<RecipeIngredient> price) => prices.Add((what, price, rungs.Count));
            foreach (PlaceableEntry p in world.Placeables)
            {
                Add("placeable " + p.Key, p.Prefab.Cost);
            }
            foreach (ChassisDefinition c in world.Definitions.Chassis.Values)
            {
                Add("chassis " + c.name, c.cost);
            }
            foreach (DraftableCardCatalog deck in world.Definitions.Decks.Values)
            {
                foreach (DraftableCardDefinition card in deck.Cards.Concat(deck.OpeningHand).Where(c => c != null))
                {
                    Add("card " + card.name, card.HasBundleCost ? card.claimCost
                        : card.baseCost > 0 ? new[] { new RecipeIngredient(ItemType.Scrap, card.baseCost) } : new RecipeIngredient[0]);
                }
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
            for (int k = 0; k < rungs.Count; k++)
            {
                prices.Add(("pack rung " + (k + 1), rungs[k].cost, k));
            }

            foreach ((string what, IReadOnlyList<RecipeIngredient> price, int rungsBelow) in prices)
            {
                int opens = Opens(price);
                int cap = Reachable(opens, rungsBelow);
                string when = opens < world.Tutorial.Steps.Count ? world.Tutorial.Steps[opens].Id : "after the guide";
                TestContext.Out.WriteLine($"{what}: opens at {when}, reachable {cap}");
                // sandbox.json is not read by the strict DefinitionLoader, and the wallet skips a
                // line with an empty good: a typo'd or blank good would open "after the guide",
                // fit the top cap, and pass. Every line names a real good (critique).
                foreach (RecipeIngredient line in price ?? new RecipeIngredient[0])
                {
                    Assert.IsTrue(ItemTiers.IsKnown(line.itemType), $"{what} names an unknown good '{line.itemType}'");
                    Assert.Greater(line.quantity, 0, $"{what} has a line of {line.quantity} {line.itemType}");
                }
                foreach (IGrouping<string, RecipeIngredient> good in (price ?? new RecipeIngredient[0]).GroupBy(c => c.itemType))
                {
                    int sum = good.Sum(c => c.quantity);
                    Assert.LessOrEqual(sum, cap, $"{what} asks for {sum} {good.Key}, more than the {cap} pack reachable when it opens ({when})");
                }
            }
        }

        [Test]
        public void TheFloorHasOnePricePerPurchaseTheLandAllows()
        {
            SandboxWorld world = Compose();
            int allowed = (FloorLayout.MaxNorthExtent - world.Setup.startingNorthExtent) / world.FloorExpansion.RowsPerPurchase;
            Assert.AreEqual(allowed, world.Setup.floorExpansion.purchases.Count);
            Assert.AreEqual(6, allowed, "+12 rows, 2 a purchase");
        }

        /// <summary>
        /// Seen(s): the goods the Ledger holds once step <c>s</c> is done, before the kit performs
        /// the next. The kit plays every chapter from a fresh game; the Ledger is polled each frame.
        /// </summary>
        private static List<HashSet<string>> SeenAfterEachStep(SandboxWorld world)
        {
            world.Clock.Play();
            var seen = new List<HashSet<string>>();
            for (int chapter = 0; chapter < world.Tutorial.ChapterCount && world.Tutorial.Current?.Id != "done"; chapter++)
            {
                Assert.IsTrue(world.Tutorial.StartKit(), "the kit started");
                for (int frame = 0; frame < 3600 && world.Tutorial.KitRunning; frame++)
                {
                    // A step the world finished mid-frame has the next one performed in the same
                    // Update, so it is credited with what the Ledger held before that frame.
                    var before = new HashSet<string>(world.TechTree.Ledger.Items);
                    world.Advance(1f / 30f);
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
            return seen;
        }
    }
}
