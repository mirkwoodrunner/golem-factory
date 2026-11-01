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
            PriceList list = PriceList.Play();
            IReadOnlyList<PackUpgradeRung> rungs = list.Rungs;
            for (int k = 1; k < rungs.Count; k++)
            {
                Assert.GreaterOrEqual(list.Opens(rungs[k].cost), list.Opens(rungs[k - 1].cost), $"rung {k + 1} opens no sooner than rung {k}");
            }

            foreach (PriceList.Price price in list.Prices)
            {
                int opens = list.Opens(price.Lines);
                int cap = list.Reachable(opens, price.RungsBelow);
                string when = list.StepAt(opens);
                TestContext.Out.WriteLine($"{price.What}: opens at {when}, reachable {cap}");
                // sandbox.json is not read by the strict DefinitionLoader, and the wallet skips a
                // line with an empty good: a typo'd or blank good would open "after the guide",
                // fit the top cap, and pass. Every line names a real good (critique).
                foreach (RecipeIngredient line in price.Lines)
                {
                    Assert.IsTrue(ItemTiers.IsKnown(line.itemType), $"{price.What} names an unknown good '{line.itemType}'");
                    Assert.Greater(line.quantity, 0, $"{price.What} has a line of {line.quantity} {line.itemType}");
                }
                foreach ((string good, int sum) in PriceList.Summed(price.Lines))
                {
                    Assert.LessOrEqual(sum, cap, $"{price.What} asks for {sum} {good}, more than the {cap} pack reachable when it opens ({when})");
                }
            }
        }

        [Test]
        public void TheList_HasTheBenchAndTheBoilersHandLoad()
        {
            // docs/tuning-data-spec.md §15.2, closing open-items §4.
            PriceList list = PriceList.Play();
            Assert.IsTrue(list.Prices.Any(p => p.What == "bench R2_ScrapReclamation"));
            Assert.IsTrue(list.Prices.Any(p => p.What == "boiler hand-load"));
        }

        [Test]
        public void TheFloorHasOnePricePerPurchaseTheLandAllows()
        {
            SandboxWorld world = Compose();
            int allowed = (FloorLayout.MaxNorthExtent - world.Setup.startingNorthExtent) / world.FloorExpansion.RowsPerPurchase;
            Assert.AreEqual(allowed, world.Setup.floorExpansion.purchases.Count);
            Assert.AreEqual(6, allowed, "+12 rows, 2 a purchase");
        }
    }
}
