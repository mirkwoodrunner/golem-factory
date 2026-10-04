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
        [Test]
        public void SlotWithinChassis_IndexInsideCapacity_IsTrue()
        {
            Assert.IsTrue(WorkbenchDropRules.SlotWithinChassis(0, 3));
            Assert.IsTrue(WorkbenchDropRules.SlotWithinChassis(2, 3));
        }

        [Test]
        public void SlotWithinChassis_IndexAtOrBeyondCapacity_IsFalse()
        {
            Assert.IsFalse(WorkbenchDropRules.SlotWithinChassis(3, 3));
            Assert.IsFalse(WorkbenchDropRules.SlotWithinChassis(4, 3));
        }

        [Test]
        public void SlotWithinChassis_NegativeIndexOrNoChassis_IsFalse()
        {
            Assert.IsFalse(WorkbenchDropRules.SlotWithinChassis(-1, 3));
            Assert.IsFalse(WorkbenchDropRules.SlotWithinChassis(0, 0));
        }

        [Test]
        public void AcceptsCard_LogicCoreCardOntoLogicCoreSocket_IsAccepted()
        {
            Assert.IsTrue(WorkbenchDropRules.AcceptsCard(DropZoneKind.LogicCore, -1, cardIsLogicCore: true, chassisMaxAppendageSlots: 0));
        }

        [Test]
        public void AcceptsCard_AppendageCardOntoLogicCoreSocket_IsRejected()
        {
            // This is precisely the case the socket highlighting has to make visible: the
            // trigger socket rejects an action card.
            Assert.IsFalse(WorkbenchDropRules.AcceptsCard(DropZoneKind.LogicCore, -1, cardIsLogicCore: false, chassisMaxAppendageSlots: 5));
        }

        [Test]
        public void AcceptsCard_LogicCoreCardOntoAppendageSocket_IsRejected()
        {
            Assert.IsFalse(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 0, cardIsLogicCore: true, chassisMaxAppendageSlots: 5));
        }

        [Test]
        public void AcceptsCard_AppendageCardOntoSlotBeyondChassisCapacity_IsRejected()
        {
            Assert.IsTrue(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 1, cardIsLogicCore: false, chassisMaxAppendageSlots: 2));
            Assert.IsFalse(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 2, cardIsLogicCore: false, chassisMaxAppendageSlots: 2));
        }

        [Test]
        public void AcceptsCard_NoChassisFitted_RejectsEveryAppendageSocket()
        {
            Assert.IsFalse(WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, 0, cardIsLogicCore: false, chassisMaxAppendageSlots: 0));
        }

        [Test]
        public void SlotVisible_WithinChassisCapacity_IsVisibleEvenWhenEmpty()
        {
            Assert.IsTrue(WorkbenchDropRules.SlotVisible(0, 2, slotOccupied: false));
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

        [Test]
        public void SlotVisible_EmptySlotBeyondCapacity_IsHidden()
        {
            Assert.IsFalse(WorkbenchDropRules.SlotVisible(3, 2, slotOccupied: false));
        }
    }
}
