using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // The Workbench's status line was write-only: every message it ever showed stayed up
    // for the rest of the session, so "Not enough Focus (need 10)" persisted while the
    // tape ticker read FOCUS 42/100, and "remove appendages to fit its slot count first"
    // survived removing every appendage. These pin the retirement rule for each reason.
    public class WorkbenchStatusPolicyTests
    {
        private static bool ShouldClear(
            WorkbenchStatusReason reason, float shownSeconds = 0f, float focus = 0f,
            int assignedAppendages = 0, int chassisSlotLimit = 0, bool hasTarget = true)
        {
            return WorkbenchStatusPolicy.ShouldClear(
                reason, shownSeconds, focus,
                engageCost: 10f, patentCost: 20f,
                assignedAppendages: assignedAppendages,
                chassisSlotLimit: chassisSlotLimit,
                hasTarget: hasTarget);
        }

        [Test]
        public void None_NeverClears()
        {
            Assert.IsFalse(ShouldClear(WorkbenchStatusReason.None, shownSeconds: 999f));
        }

        [Test]
        public void Info_ClearsOnlyAfterItsLifetime()
        {
            Assert.IsFalse(ShouldClear(WorkbenchStatusReason.Info, shownSeconds: WorkbenchStatusPolicy.InfoSeconds - 0.1f));
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.Info, shownSeconds: WorkbenchStatusPolicy.InfoSeconds));
        }

        [Test]
        public void InsufficientFocusEngage_ClearsOnceFocusCoversTheCost()
        {
            Assert.IsFalse(ShouldClear(WorkbenchStatusReason.InsufficientFocusEngage, focus: 4.5f));
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.InsufficientFocusEngage, focus: 10f));
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.InsufficientFocusEngage, focus: 42f));
        }

        [Test]
        public void InsufficientFocusPatent_UsesThePatentCostNotTheEngageCost()
        {
            // 12 Focus pays for a reprogram but not a patent, so the patent warning must
            // still stand at a level that would retire the reprogram one.
            Assert.IsFalse(ShouldClear(WorkbenchStatusReason.InsufficientFocusPatent, focus: 12f));
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.InsufficientFocusPatent, focus: 20f));
        }

        [Test]
        public void ChassisTooSmall_ClearsOnceTheDraftFitsTheRejectedChassis()
        {
            // Rejected a 1-slot chassis while the draft held 5 appendages...
            Assert.IsFalse(ShouldClear(WorkbenchStatusReason.ChassisTooSmall, assignedAppendages: 5, chassisSlotLimit: 1));
            // ...removing them down to the limit retires the message.
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.ChassisTooSmall, assignedAppendages: 1, chassisSlotLimit: 1));
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.ChassisTooSmall, assignedAppendages: 0, chassisSlotLimit: 1));
        }

        [Test]
        public void NoTarget_ClearsOnceAGolemIsTargeted()
        {
            Assert.IsFalse(ShouldClear(WorkbenchStatusReason.NoTarget, hasTarget: false));
            Assert.IsTrue(ShouldClear(WorkbenchStatusReason.NoTarget, hasTarget: true));
        }
    }
}
