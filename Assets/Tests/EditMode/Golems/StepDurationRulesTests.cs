using NUnit.Framework;
using GolemFactory.Golems;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The two quantity-driven step durations from docs/progression-design.md §2, extracted from
    /// <c>GolemEntity</c> so the Workbench's batch-size control can quote the same number the
    /// simulation charges rather than keeping a second copy of the arithmetic.
    /// </summary>
    public class StepDurationRulesTests
    {
        // max(2, qty): a floor of 2 ticks of overhead, then a tick per unit.
        [Test]
        public void Haul_HasAFloorOfTwoTicks()
        {
            Assert.AreEqual(2, StepDurationRules.Haul(1), "a batch of 1 still pays the overhead");
            Assert.AreEqual(2, StepDurationRules.Haul(2));
            Assert.AreEqual(8, StepDurationRules.Haul(8));
        }

        // 6 + qty: the design's worked example is a 4-unit extractor at 10 ticks.
        [Test]
        public void ExtractFromNode_IsSixPlusQuantity()
        {
            Assert.AreEqual(10, StepDurationRules.ExtractFromNode(4), "§2's worked example");
            Assert.AreEqual(7, StepDurationRules.ExtractFromNode(1));
        }

        // WHY THE TWO FORMULAS DIFFER, stated as a property rather than left in a comment:
        // extraction's setup cost is much larger, so batching pays off far more strongly there.
        // A retune that flattened them would quietly remove the reason to batch an extractor.
        [Test]
        public void ExtractionRewardsBatchingMoreStronglyThanHauling()
        {
            float haulPerUnitAtOne = StepDurationRules.Haul(1) / 1f;
            float haulPerUnitAtEight = StepDurationRules.Haul(8) / 8f;
            float extractPerUnitAtOne = StepDurationRules.ExtractFromNode(1) / 1f;
            float extractPerUnitAtEight = StepDurationRules.ExtractFromNode(8) / 8f;

            Assert.Greater(
                extractPerUnitAtOne - extractPerUnitAtEight,
                haulPerUnitAtOne - haulPerUnitAtEight,
                "batching an extractor must save more per unit than batching a haul");
        }

        [Test]
        public void BothAreMonotonic_ABiggerBatchNeverTakesLessTime()
        {
            for (int q = 1; q < 12; q++)
            {
                Assert.LessOrEqual(StepDurationRules.Haul(q), StepDurationRules.Haul(q + 1));
                Assert.Less(StepDurationRules.ExtractFromNode(q), StepDurationRules.ExtractFromNode(q + 1));
            }
        }
    }
}
