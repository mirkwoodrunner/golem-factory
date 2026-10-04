using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Assembly Line tab's bay row. It exists because §8's cap went into the loop with no
    /// way to raise it: the station refused past ten golems and `TryUpgrade` had no caller
    /// anywhere in the game, so the cap was a wall rather than a decision -- and §9's phases
    /// 4-6 need ~18 golems.
    /// </summary>
    public class AssemblyBayRowPolicyTests
    {
        private static readonly List<RecipeIngredient> Cost = new List<RecipeIngredient>
        {
            new RecipeIngredient(ItemType.Scrap, 40),
            new RecipeIngredient(ItemType.IronPlate, 20),
        };

        [Test]
        public void Occupancy_ReadsOccupiedOverCapacity()
        {
            Assert.AreEqual("Bays 7/10", AssemblyBayRowPolicy.FormatOccupancy(7, 10));
        }

        [Test]
        public void UpgradeEffect_StatesWhatThePurchaseBuys()
        {
            Assert.AreEqual("+6 slots", AssemblyBayRowPolicy.FormatUpgradeEffect(6));
            Assert.AreEqual("+1 slot", AssemblyBayRowPolicy.FormatUpgradeEffect(1));
        }

        [Test]
        public void Cost_IsWrittenTheSameWayEveryOtherCostIs()
        {
            // Delegated rather than restated -- three spellings of "40 Scrap + 20 Iron Plate"
            // would be three places to drift.
            Assert.AreEqual(ConstructionCostPolicy.FormatCost(Cost), AssemblyBayRowPolicy.FormatCost(Cost));
            StringAssert.Contains("40 Scrap", AssemblyBayRowPolicy.FormatCost(Cost));
            StringAssert.Contains("20 Iron Plate", AssemblyBayRowPolicy.FormatCost(Cost));
        }

        [Test]
        public void CanAfford_NeedsBothGoods()
        {
            Assert.IsTrue(AssemblyBayRowPolicy.CanAfford(
                t => t == ItemType.Scrap ? 40 : 20, Cost));
            Assert.IsFalse(AssemblyBayRowPolicy.CanAfford(
                t => t == ItemType.Scrap ? 40 : 19, Cost));
            Assert.IsFalse(AssemblyBayRowPolicy.CanAfford(t => 0, Cost));
        }

        [Test]
        public void RefusedUpgrade_NamesTheShortfallByGoodAndAmount()
        {
            string message = AssemblyBayRowPolicy.DescribeResult(
                upgraded: false, stockOf: t => t == ItemType.Scrap ? 40 : 5, upgradeCost: Cost,
                newCapacity: 6);

            // The shared formatter's wording, quoted rather than guessed: "Need 15 more Iron Plate".
            StringAssert.Contains("15 more Iron Plate", message);
            StringAssert.Contains("6 bay slots", message);
        }

        [Test]
        public void SuccessfulUpgrade_SaysNothing()
        {
            // The readout has already changed; a "done!" line to dismiss adds nothing.
            Assert.AreEqual("", AssemblyBayRowPolicy.DescribeResult(true, t => 999, Cost, 6));
        }

        [Test]
        public void TheRowQuotesTheBaysOwnCost_NotACopyOfIt()
        {
            // If §8's price is ever retuned on AssemblyBayStructure, the row follows it.
            var bay = new AssemblyBayStructure();
            try
            {
                Assert.AreEqual(
                    ConstructionCostPolicy.FormatCost(bay.UpgradeCost),
                    AssemblyBayRowPolicy.FormatCost(bay.UpgradeCost));
                Assert.AreEqual(
                    "Bays 0/" + AssemblyBayStructure.DefaultSlots,
                    AssemblyBayRowPolicy.FormatOccupancy(bay.OccupiedSlots, bay.MaxGolemSlots));
            }
            finally
            {
            }
        }
    }
}
