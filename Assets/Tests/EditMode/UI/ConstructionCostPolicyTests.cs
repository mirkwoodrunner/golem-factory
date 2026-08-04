using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    public class ConstructionCostPolicyTests
    {
        [Test]
        public void CanAfford_ExactStockIsEnough()
        {
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(20, 10, 20, 10));
        }

        [Test]
        public void CanAfford_FailsWhenEitherResourceIsShort()
        {
            Assert.IsFalse(ConstructionCostPolicy.CanAfford(19, 10, 20, 10));
            Assert.IsFalse(ConstructionCostPolicy.CanAfford(20, 9, 20, 10));
        }

        // Mirrors StorageBufferRegistry.TryWithdrawScrapAndBrass's own zero-cost rule: a free
        // chassis is payable out of a buffer that has never been deposited into. If the panel
        // disagreed with the withdrawal here, a free chassis would render as unbuyable and
        // then build fine when clicked.
        [Test]
        public void CanAfford_ZeroAndNegativeCostsAreAlwaysPayable()
        {
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(0, 0, 0, 0));
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(0, 0, -5, -5));
            Assert.IsTrue(ConstructionCostPolicy.CanAfford(5, 0, 5, 0));
        }

        [Test]
        public void FormatCost_NamesBothResourcesWhenBothAreCharged()
        {
            string text = ConstructionCostPolicy.FormatCost(30, 10);

            StringAssert.Contains("30 Scrap", text);
            StringAssert.Contains("10 Brass", text);
        }

        [Test]
        public void FormatCost_OmitsAResourceThatIsNotCharged()
        {
            Assert.AreEqual("20 Scrap", ConstructionCostPolicy.FormatCost(20, 0));
            Assert.AreEqual("15 Brass", ConstructionCostPolicy.FormatCost(0, 15));
        }

        [Test]
        public void FormatCost_SaysFreeRatherThanRenderingNothing()
        {
            Assert.AreEqual("Free", ConstructionCostPolicy.FormatCost(0, 0));
        }

        [Test]
        public void FormatShortfall_IsEmptyWhenAffordable()
        {
            Assert.IsEmpty(ConstructionCostPolicy.FormatShortfall(50, 50, 20, 10));
            Assert.IsEmpty(ConstructionCostPolicy.FormatShortfall(0, 0, 0, 0));
        }

        [Test]
        public void FormatShortfall_NamesTheMissingAmountNotJustTheResource()
        {
            Assert.AreEqual("Need 5 more Scrap", ConstructionCostPolicy.FormatShortfall(15, 50, 20, 10));
            Assert.AreEqual("Need 4 more Brass", ConstructionCostPolicy.FormatShortfall(50, 6, 20, 10));
        }

        [Test]
        public void FormatShortfall_ReportsBothWhenBothAreShort()
        {
            string text = ConstructionCostPolicy.FormatShortfall(0, 0, 20, 10);

            StringAssert.Contains("20 more Scrap", text);
            StringAssert.Contains("10 more Brass", text);
        }
    }
}
