using System;
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
    /// docs/supply-design.md §3: the street's carts can feed the tower. At a stage's full demand,
    /// with steam upkeep for its golems, each raw good needs so many carts at their top rate, each
    /// giving no more than two Scavengers pull from it, and the carts must fit the street's spots.
    /// Stage 4 is the real limit (fifteen of seventeen spots).
    /// </summary>
    public class StreetCoverageTests
    {
        /// <summary>
        /// What two Scavengers pull from one cart: about 60 a minute each (Extract(1) and Push,
        /// tuning-data-spec), and the two-extractor cap (§3.2) allows two.
        /// </summary>
        private const int TwoScavengersPull = 120;

        /// <summary>
        /// How each made good is made when the raw demand is back-propagated (tuning-data-spec
        /// §4.2): Iron Plate by R4, Coke by R1, Brass by R7; Glass is free, the Slag smelters make
        /// anyway. Any other good by the one recipe that makes it.
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

        /// <summary>The raw goods a stage asks for a minute, its golems' Coke included.</summary>
        private static Dictionary<string, double> Demand(int stageNumber, int golems, DefinitionSet defs, HashSet<string> rawGoods)
        {
            ClockTowerStageDefinition stage = defs.ClockTowerStages.Values.Single(s => s.stageNumber == stageNumber);
            var demand = new Dictionary<string, double>();
            void Add(string good, double perMinute)
            {
                foreach (KeyValuePair<string, double> part in Raw(good, defs, rawGoods))
                {
                    demand.TryGetValue(part.Key, out double held);
                    demand[part.Key] = held + part.Value * perMinute;
                }
            }
            foreach (StageDemand line in stage.demands)
            {
                Add(line.itemType, line.ratePerMinute);
            }
            Add(ItemType.Coke, golems * 600.0 / SteamNetwork.TicksPerCokePerPoweredGolem);
            return demand;
        }

        /// <summary>The carts a stage needs, each at its ladder's top rate but pulled no faster than two Scavengers.</summary>
        private static int CartsFor(int stageNumber, int golems)
        {
            DefinitionSet defs = AuthoredData.Load();
            SandboxSetup setup = World.SandboxSetupTests.LoadReal();
            Dictionary<string, SandboxSetup.CartLadderEntry> ladders = setup.supply.carts.ToDictionary(c => c.itemType);
            var rawGoods = new HashSet<string>(ladders.Keys);

            int carts = 0;
            foreach (KeyValuePair<string, double> good in Demand(stageNumber, golems, defs, rawGoods).OrderBy(g => g.Key))
            {
                int perCart = Math.Min(ladders[good.Key].rates.Last(), TwoScavengersPull);
                int needed = (int)Math.Ceiling(good.Value / perCart);
                carts += needed;
                TestContext.Out.WriteLine($"stage {stageNumber}, {good.Key}: {good.Value:0.#}/min at {perCart} a cart: {needed} carts");
            }
            TestContext.Out.WriteLine($"stage {stageNumber}: {carts} carts of {setup.supply.spots.Count} spots");
            return carts;
        }

        [Test]
        public void StageOne_FitsTheStreet()
        {
            // docs/progression-design.md §7: a stage 1 factory runs 63 golems.
            Assert.LessOrEqual(CartsFor(1, 63), World.SandboxSetupTests.LoadReal().supply.spots.Count);
        }

        [Test]
        public void StageFour_AtTheTopRungs_FitsTheStreet()
        {
            // §7 again: about 96 golems at stage 4. If this fails, the street gains spots before
            // the rates rise (supply-design §3).
            Assert.LessOrEqual(CartsFor(4, 96), World.SandboxSetupTests.LoadReal().supply.spots.Count);
        }
    }
}
