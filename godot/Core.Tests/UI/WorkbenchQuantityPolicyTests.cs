using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Workbench's batch-size control -- §2's "Consequence 4", which was data-only until now:
    /// per-slot quantity has been stored and saved since the machine model landed, and nothing
    /// exposed it, so the one decision left to the signature screen was unplayable.
    /// </summary>
    public class WorkbenchQuantityPolicyTests
    {
        private static AppendageActionDefinition Card(AppendageActionType type)
        {
            var card = new AppendageActionDefinition();
            card.actionType = type;
            return card;
        }

        // Only the two verbs whose behaviour actually reads the quantity. A stepper on an
        // Assemble or a Push would advertise a decision that changes nothing, which is worse than
        // no control -- the player spends attention on it and gets no effect.
        [Test]
        public void TakesQuantity_OnlyHaulAndExtract()
        {
            Assert.IsTrue(WorkbenchQuantityPolicy.TakesQuantity(Card(AppendageActionType.Haul)));
            Assert.IsTrue(WorkbenchQuantityPolicy.TakesQuantity(Card(AppendageActionType.ExtractFromNode)));

            Assert.IsFalse(WorkbenchQuantityPolicy.TakesQuantity(Card(AppendageActionType.Assemble)));
            Assert.IsFalse(WorkbenchQuantityPolicy.TakesQuantity(Card(AppendageActionType.Push)));
            Assert.IsFalse(WorkbenchQuantityPolicy.TakesQuantity(Card(AppendageActionType.LoadIntoBuffer)));
            Assert.IsFalse(WorkbenchQuantityPolicy.TakesQuantity(Card(AppendageActionType.Refine)));
            Assert.IsFalse(WorkbenchQuantityPolicy.TakesQuantity(null));
        }

        // The ceiling is the golem's own per-item-type stock cap, not a number picked for the UI:
        // above it the extra units have nowhere to land.
        [Test]
        public void MaxQuantity_IsTheGolemsOwnStockCap()
        {
            Assert.AreEqual(GolemInventory.CapacityPerType, WorkbenchQuantityPolicy.MaxQuantity);
        }

        [Test]
        public void Step_SaturatesAtBothEndsRatherThanWrapping()
        {
            Assert.AreEqual(WorkbenchQuantityPolicy.MinQuantity,
                WorkbenchQuantityPolicy.Step(WorkbenchQuantityPolicy.MinQuantity, -1),
                "holding minus past 1 must stop, not wrap to the cap");
            Assert.AreEqual(WorkbenchQuantityPolicy.MaxQuantity,
                WorkbenchQuantityPolicy.Step(WorkbenchQuantityPolicy.MaxQuantity, 1),
                "holding plus past the cap must stop, not drop back to 1 and retune the factory");

            Assert.AreEqual(5, WorkbenchQuantityPolicy.Step(4, 1));
            Assert.AreEqual(3, WorkbenchQuantityPolicy.Step(4, -1));
        }

        // THE POINT OF SHOWING ANYTHING AT ALL. §2's trade is throughput against buffer pressure,
        // and a stepper that showed only the number would hide the half that makes it a decision.
        [Test]
        public void Describe_QuotesTheTickCostTheSimulationWillActuallyCharge()
        {
            AppendageActionDefinition haul = Card(AppendageActionType.Haul);
            AppendageActionDefinition extract = Card(AppendageActionType.ExtractFromNode);

            Assert.AreEqual("×4 · " + StepDurationRules.Haul(4) + "t",
                WorkbenchQuantityPolicy.Describe(haul, 4));
            Assert.AreEqual("×4 · " + StepDurationRules.ExtractFromNode(4) + "t",
                WorkbenchQuantityPolicy.Describe(extract, 4));

            // And the two verbs must not quote the same cost, or the control is telling the
            // player their choice of verb is free.
            Assert.AreNotEqual(
                WorkbenchQuantityPolicy.Describe(haul, 4),
                WorkbenchQuantityPolicy.Describe(extract, 4));
        }

        [Test]
        public void Describe_ACardWithNoQuantity_SaysNothing()
        {
            Assert.AreEqual(string.Empty,
                WorkbenchQuantityPolicy.Describe(Card(AppendageActionType.Assemble), 4));
        }
    }
}
