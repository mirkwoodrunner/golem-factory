using System.Collections.Generic;
using System.Linq;
using GolemFactory.ClockTower;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/tuning-data-spec.md §4.2 and §15.5: the market is sized so the endgame is automated,
    /// not a toll booth. At stage 1's full demand the factory's raw goods, by truckload, must
    /// need at most 8 orders a minute across the whole street.
    /// </summary>
    public class MarketThroughputTests
    {
        /// <summary>
        /// The golems a stage 1 factory runs, docs/progression-design.md §7's count: each burns
        /// Coke while it works, and Coke is Coal.
        /// </summary>
        private const int GolemsAtStageOne = 63;

        private const double TargetOrdersPerMinute = 8;

        /// <summary>
        /// How each made good is made when the raw demand is back-propagated (§4.2): Iron Plate by
        /// R4, Coke by R1, Brass by R7; Glass is free, the Slag smelters make anyway. Any other
        /// good by the one recipe that makes it.
        /// </summary>
        private static readonly Dictionary<string, string> Chosen = new Dictionary<string, string>
        {
            { ItemType.IronPlate, "R4_IronSmelting" }, { ItemType.Coke, "R1_Coking" }, { ItemType.Brass, "R7_BrassAlloying" },
        };

        private static Dictionary<string, double> Raw(string good, DefinitionSet defs, HashSet<string> rawGoods)
        {
            var raw = new Dictionary<string, double>();
            if (rawGoods.Contains(good))
            {
                raw[good] = 1;
                return raw;
            }
            if (good == ItemType.Glass)
            {
                return raw;
            }
            RecipeDefinition recipe = Chosen.TryGetValue(good, out string name)
                ? defs.Recipes[name]
                : defs.Recipes.Values.Single(r => r.outputItemType == good);
            foreach (RecipeIngredient input in recipe.inputs)
            {
                foreach (KeyValuePair<string, double> part in Raw(input.itemType, defs, rawGoods))
                {
                    raw.TryGetValue(part.Key, out double held);
                    raw[part.Key] = held + part.Value * input.quantity / recipe.outputQuantity;
                }
            }
            return raw;
        }

        [Test]
        public void StageOne_AtFullDemand_NeedsAtMostEightTruckloadsAMinute()
        {
            DefinitionSet defs = AuthoredData.Load();
            SandboxSetup setup = World.SandboxSetupTests.LoadReal();
            var rawGoods = new HashSet<string>(setup.nodes.Select(n => n.itemType));
            ClockTowerStageDefinition stage = defs.ClockTowerStages.Values.Single(s => s.stageNumber == 1);

            var demand = new Dictionary<string, double>();
            foreach (StageDemand line in stage.demands)
            {
                foreach (KeyValuePair<string, double> part in Raw(line.itemType, defs, rawGoods))
                {
                    demand.TryGetValue(part.Key, out double held);
                    demand[part.Key] = held + part.Value * line.ratePerMinute;
                }
            }
            double cokePerGolem = 600.0 / SteamNetwork.TicksPerCokePerPoweredGolem;
            demand.TryGetValue(ItemType.Coal, out double coal);
            demand[ItemType.Coal] = coal + GolemsAtStageOne * cokePerGolem;

            double orders = 0;
            foreach (KeyValuePair<string, double> good in demand.OrderBy(g => g.Key))
            {
                int truckload = setup.market
                    .Where(m => setup.nodes.Single(n => n.id == m.nodeId).itemType == good.Key)
                    .Max(m => m.truckloadSize);
                double perMinute = good.Value / truckload;
                orders += perMinute;
                TestContext.Out.WriteLine($"{good.Key}: {good.Value:0.#}/min in truckloads of {truckload}: {perMinute:0.00} orders/min");
            }
            TestContext.Out.WriteLine($"stage 1: {orders:0.00} orders a minute");
            Assert.LessOrEqual(orders, TargetOrdersPerMinute);
        }
    }
}
