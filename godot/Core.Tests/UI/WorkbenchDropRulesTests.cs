using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // WorkbenchDropRules is the single source of truth shared by three call sites that
    // used to answer "does this card fit here" independently: HandleDrop's commit gate,
    // RebuildUI's slot rendering, and the mid-drag socket highlighting. The tests below
    // pin the two rules apart on purpose -- AcceptsCard and SlotVisible deliberately
    // disagree for an occupied out-of-capacity slot.
    public class WorkbenchDropRulesTests
    {
        [TestCase(0, 3, true)]
        [TestCase(2, 3, true)]
        [TestCase(3, 3, false)] // at capacity
        [TestCase(4, 3, false)]
        [TestCase(-1, 3, false)]
        [TestCase(0, 0, false)] // no chassis
        public void SlotWithinChassis_IsTrueOnlyForAnIndexInsideTheCapacity(int index, int slots, bool expected)
        {
            Assert.AreEqual(expected, WorkbenchDropRules.SlotWithinChassis(index, slots));
        }

        [TestCase(DropZoneKind.LogicCore, -1, true, 0, true,
            TestName = "AcceptsCard_LogicCoreCardOntoLogicCoreSocket_IsAccepted")]
        // Precisely the case the socket highlighting has to make visible: the trigger socket
        // rejects an action card.
        [TestCase(DropZoneKind.LogicCore, -1, false, 5, false,
            TestName = "AcceptsCard_AppendageCardOntoLogicCoreSocket_IsRejected")]
        [TestCase(DropZoneKind.Appendage, 0, true, 5, false,
            TestName = "AcceptsCard_LogicCoreCardOntoAppendageSocket_IsRejected")]
        [TestCase(DropZoneKind.Appendage, 0, false, 0, false,
            TestName = "AcceptsCard_NoChassisFitted_RejectsEveryAppendageSocket")]
        public void AcceptsCard_MatchesTheCardToTheSocket(
            DropZoneKind zone, int index, bool cardIsLogicCore, int slots, bool expected)
        {
            Assert.AreEqual(expected, WorkbenchDropRules.AcceptsCard(
                zone, index, cardIsLogicCore: cardIsLogicCore, chassisMaxAppendageSlots: slots));
        }

        [Test]
        public void AcceptsCard_AppendageCardOntoSlotBeyondChassisCapacity_IsRejected()
        {
            Assert.IsTrue(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 1, cardIsLogicCore: false, chassisMaxAppendageSlots: 2));
            Assert.IsFalse(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 2, cardIsLogicCore: false, chassisMaxAppendageSlots: 2));
        }

        [TestCase(0, 2, true, TestName = "SlotVisible_WithinChassisCapacity_IsVisibleEvenWhenEmpty")]
        [TestCase(3, 2, false, TestName = "SlotVisible_EmptySlotBeyondCapacity_IsHidden")]
        public void SlotVisible_AnEmptySlot_ShowsOnlyWithinCapacity(int index, int slots, bool expected)
        {
            Assert.AreEqual(expected, WorkbenchDropRules.SlotVisible(index, slots, slotOccupied: false));
        }

        [Test]
        public void SlotVisible_OccupiedSlotBeyondCapacity_StaysVisible()
        {
            // The incoherent default state: a draft holding a step with no chassis to hold
            // it. The ticker counts it, so the viewport must draw it -- otherwise the
            // player is told about a step they can neither see nor remove.
            Assert.IsTrue(WorkbenchDropRules.SlotVisible(0, 0, slotOccupied: true));
            // ...but it still must not accept anything new.
            Assert.IsFalse(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 0, cardIsLogicCore: false, chassisMaxAppendageSlots: 0));
        }
    }
}
