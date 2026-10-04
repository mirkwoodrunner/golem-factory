using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The label cycle a depot's <c>[E]</c> walks (docs/cozy-automation-design.md §1). Short and
    /// live, built from what the stockpile has actually handled rather than from all 24 goods.
    /// </summary>
    public class DepotFilterOptionsTests
    {
        [Test]
        public void AnEmptyStockpileOffersOnlyAnyGoods()
        {
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(null, null);

            Assert.AreEqual(1, cycle.Count);
            Assert.IsTrue(DepotFilterOptions.Same(DepotFilterOptions.AnyGoods, cycle[0]));
        }

        [Test]
        public void AnyGoodsAlwaysLeads_SoOneMorePressGetsBackToTheDefault()
        {
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(
                new[] { ItemType.ChronometerCore, ItemType.Scrap }, null);

            Assert.IsTrue(DepotFilterOptions.Same(DepotFilterOptions.AnyGoods, cycle[0]));
        }

        [Test]
        public void KnownTypesAreOrderedByTier_NotByArrivalOrder()
        {
            // Handed deepest-first on purpose: dictionary key order is not contractual, and two
            // depots that reached the same contents by different routes must cycle identically.
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(
                new[] { ItemType.Gear, ItemType.Coke, ItemType.Scrap }, null);

            CollectionAssert.AreEqual(
                new[] { DepotFilterOptions.AnyGoods, ItemType.Scrap, ItemType.Coke, ItemType.Gear },
                cycle);
        }

        [Test]
        public void DuplicatesCollapse()
        {
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(
                new[] { ItemType.Scrap, ItemType.Scrap, null, "" }, null);

            CollectionAssert.AreEqual(new[] { DepotFilterOptions.AnyGoods, ItemType.Scrap }, cycle);
        }

        [Test]
        public void TheCurrentLabelSurvivesItsGoodRunningOut()
        {
            // THE RULE THAT MATTERS. A crate labelled for Iron Plate whose last Plate has just
            // been consumed must still find its own setting in its own cycle -- otherwise the
            // next press would silently retune it to something else.
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(
                new[] { ItemType.Scrap }, ItemType.IronPlate);

            CollectionAssert.Contains(cycle, ItemType.IronPlate);
            // And it still sorts by tier rather than being appended at the end.
            CollectionAssert.AreEqual(
                new[] { DepotFilterOptions.AnyGoods, ItemType.Scrap, ItemType.IronPlate }, cycle);
        }

        [Test]
        public void NextWalksTheCycleAndWrapsBackToAnyGoods()
        {
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(
                new[] { ItemType.Scrap, ItemType.Coke }, null);

            string first = DepotFilterOptions.Next(cycle, DepotFilterOptions.AnyGoods);
            Assert.AreEqual(ItemType.Scrap, first);

            string second = DepotFilterOptions.Next(cycle, first);
            Assert.AreEqual(ItemType.Coke, second);

            string wrapped = DepotFilterOptions.Next(cycle, second);
            Assert.IsTrue(DepotFilterOptions.Same(DepotFilterOptions.AnyGoods, wrapped));
        }

        [Test]
        public void AFilterThatIsNotInTheCycleLandsOnAnyGoods()
        {
            // The safe answer: a label the player can no longer see is one they cannot cycle
            // away from deliberately, so the press has to mean something predictable.
            IReadOnlyList<string> cycle = DepotFilterOptions.BuildCycle(new[] { ItemType.Scrap }, null);

            Assert.IsTrue(DepotFilterOptions.Same(
                DepotFilterOptions.AnyGoods, DepotFilterOptions.Next(cycle, ItemType.Aether)));
        }

        [Test]
        public void NextOnAnEmptyCycleIsAnyGoods()
        {
            Assert.IsTrue(DepotFilterOptions.Same(
                DepotFilterOptions.AnyGoods, DepotFilterOptions.Next(null, ItemType.Scrap)));
            Assert.IsTrue(DepotFilterOptions.Same(
                DepotFilterOptions.AnyGoods, DepotFilterOptions.Next(new string[0], ItemType.Scrap)));
        }

        [Test]
        public void NullAndEmptyBothMeanAnyGoods()
        {
            // A serialized field comes back as "" where code writes null. Comparing them with ==
            // would make a freshly loaded depot disagree with an identical freshly placed one.
            Assert.IsTrue(DepotFilterOptions.Same(null, ""));
            Assert.IsTrue(DepotFilterOptions.Same("", null));
            Assert.IsFalse(DepotFilterOptions.Same("", ItemType.Scrap));
            Assert.IsFalse(DepotFilterOptions.Same(ItemType.Scrap, null));
            Assert.IsTrue(DepotFilterOptions.Same(ItemType.Scrap, ItemType.Scrap));
        }

        [Test]
        public void DescribeNamesTheGoodReadably()
        {
            Assert.AreEqual(DepotFilterOptions.AnyGoodsLabel, DepotFilterOptions.Describe(null));
            Assert.AreEqual(DepotFilterOptions.AnyGoodsLabel, DepotFilterOptions.Describe(""));
            Assert.AreEqual("Iron Plate", DepotFilterOptions.Describe(ItemType.IronPlate));
        }
    }
}
