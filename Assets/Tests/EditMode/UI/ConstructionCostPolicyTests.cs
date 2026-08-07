using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // Rewritten from the Scrap/Brass int-pair API to the item-bundle one
    // (docs/progression-design.md §6, §11 item 8): the pair could not express a single §6
    // chassis cost from the Brass Presser on -- the Overclocker is 2 Mainspring + 20 Brass +
    // 24 Casing + 12 Gear -- so it was replaced rather than supplemented. Every behaviour the
    // old suite pinned is re-pinned here, including the zero-cost rule, which is load-bearing
    // for M1's free PlaceableBuilding.
    public class ConstructionCostPolicyTests
    {
        private static List<RecipeIngredient> Bundle(params (string type, int quantity)[] entries)
        {
            var bundle = new List<RecipeIngredient>();
            foreach ((string type, int quantity) in entries)
            {
                bundle.Add(new RecipeIngredient(type, quantity));
            }

            return bundle;
        }

        private static System.Func<string, int> Stock(params (string type, int quantity)[] entries)
        {
            var held = new Dictionary<string, int>();
            foreach ((string type, int quantity) in entries)
            {
                held[type] = quantity;
            }

            return itemType => held.TryGetValue(itemType, out int q) ? q : 0;
        }

        [Test]
        public void CanAfford_ExactStockIsEnough()
        {
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(
                Stock(("Scrap", 20), ("Brass", 10)), Bundle(("Scrap", 20), ("Brass", 10))));
        }

        [Test]
        public void CanAfford_FailsWhenAnyOneEntryIsShort()
        {
            Assert.IsFalse(ConstructionCostPolicy.CanAfford(
                Stock(("Scrap", 19), ("Brass", 10)), Bundle(("Scrap", 20), ("Brass", 10))));
            Assert.IsFalse(ConstructionCostPolicy.CanAfford(
                Stock(("Scrap", 20), ("Brass", 9)), Bundle(("Scrap", 20), ("Brass", 10))));
        }

        // §6's Overclocker: four goods, and being short on the LAST one must refuse just as
        // firmly as being short on the first. The int pair could not represent this at all.
        [Test]
        public void CanAfford_HandlesAFourGoodBundle()
        {
            List<RecipeIngredient> overclocker = Bundle(
                ("Mainspring", 2), ("Brass", 20), ("Casing", 24), ("Gear", 12));

            Assert.IsTrue(ConstructionCostPolicy.CanAfford(
                Stock(("Mainspring", 2), ("Brass", 20), ("Casing", 24), ("Gear", 12)), overclocker));
            Assert.IsFalse(ConstructionCostPolicy.CanAfford(
                Stock(("Mainspring", 2), ("Brass", 20), ("Casing", 24), ("Gear", 11)), overclocker));
        }

        // Mirrors StorageBufferRegistry.TryWithdrawBundle's own zero-cost rule: a free chassis
        // is payable out of a buffer that has never been deposited into. If the panel disagreed
        // with the withdrawal here, a free chassis would render as unbuyable and then build
        // fine when clicked.
        [Test]
        public void CanAfford_EmptyNullAndNonPositiveCostsAreAlwaysPayable()
        {
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(Stock(), Bundle()));
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(Stock(), null));
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(Stock(), Bundle(("Scrap", 0), ("Brass", -5))));
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(Stock(("Scrap", 5)), Bundle(("Scrap", 5))));
        }

        // No stock reader means "you have nothing", not "everything is free" -- the withdrawal
        // this previews would fail on any positive quantity.
        [Test]
        public void CanAfford_NullStockReaderRefusesAPositiveCost()
        {
            Assert.IsFalse(ConstructionCostPolicy.CanAfford(null, Bundle(("Scrap", 1))));
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(null, Bundle()));
        }

        [Test]
        public void FormatCost_NamesEveryChargedGood()
        {
            string text = ConstructionCostPolicy.FormatCost(Bundle(("Scrap", 30), ("Brass", 10)));

            StringAssert.Contains("30 Scrap", text);
            StringAssert.Contains("10 Brass", text);
        }

        [Test]
        public void FormatCost_OmitsAGoodThatIsNotCharged()
        {
            Assert.AreEqual("20 Scrap", ConstructionCostPolicy.FormatCost(Bundle(("Scrap", 20), ("Brass", 0))));
            Assert.AreEqual("15 Brass", ConstructionCostPolicy.FormatCost(Bundle(("Brass", 15))));
        }

        [Test]
        public void FormatCost_SaysFreeRatherThanRenderingNothing()
        {
            Assert.AreEqual("Free", ConstructionCostPolicy.FormatCost(Bundle()));
            Assert.AreEqual("Free", ConstructionCostPolicy.FormatCost(null));
        }

        // Item ids are PascalCase bare strings by project convention; printing "CopperIngot"
        // raw in a cost line reads as a serialization detail leaking into the UI.
        [Test]
        public void FormatCost_SpacesPascalCaseItemIds()
        {
            Assert.AreEqual("3 Aether Cell", ConstructionCostPolicy.FormatCost(Bundle(("AetherCell", 3))));
            Assert.AreEqual("Copper Ingot", ConstructionCostPolicy.DisplayName("CopperIngot"));
            Assert.AreEqual("Scrap", ConstructionCostPolicy.DisplayName("Scrap"));
        }

        [Test]
        public void FormatShortfall_IsEmptyWhenAffordable()
        {
            Assert.IsEmpty(ConstructionCostPolicy.FormatShortfall(
                Stock(("Scrap", 50), ("Brass", 50)), Bundle(("Scrap", 20), ("Brass", 10))));
            Assert.IsEmpty(ConstructionCostPolicy.FormatShortfall(Stock(), Bundle()));
        }

        [Test]
        public void FormatShortfall_NamesTheMissingAmountNotJustTheResource()
        {
            Assert.AreEqual("Need 5 more Scrap", ConstructionCostPolicy.FormatShortfall(
                Stock(("Scrap", 15), ("Brass", 50)), Bundle(("Scrap", 20), ("Brass", 10))));
            Assert.AreEqual("Need 4 more Brass", ConstructionCostPolicy.FormatShortfall(
                Stock(("Scrap", 50), ("Brass", 6)), Bundle(("Scrap", 20), ("Brass", 10))));
        }

        // Every short good, not just the first: a four-item chassis cost that named one at a
        // time would send the player off for Casings and back again for Gears.
        [Test]
        public void FormatShortfall_ReportsEveryShortGood()
        {
            string text = ConstructionCostPolicy.FormatShortfall(
                Stock(), Bundle(("Mainspring", 2), ("Brass", 20), ("Casing", 24), ("Gear", 12)));

            StringAssert.Contains("2 more Mainspring", text);
            StringAssert.Contains("20 more Brass", text);
            StringAssert.Contains("24 more Casing", text);
            StringAssert.Contains("12 more Gear", text);
        }
    }
}
