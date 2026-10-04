using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Workbench's socket captions (docs/cozy-automation-design.md §3), which exist because
    /// playtest §3y asked <i>"What are steps 1-6?"</i> and the screen never said.
    /// </summary>
    public class WorkbenchLoopLabelsTests
    {
        // A three-slot chassis with two cards in, which is the shape of the very first program
        // the design expects a player to build.
        private const int Capacity = 3;

        [Test]
        public void RowZeroIsTheTrigger_AndTheRestAreNumberedSteps()
        {
            Assert.AreEqual("TRIGGER", WorkbenchLoopLabels.Caption(WorkbenchLoopLabels.TriggerRow));
            Assert.AreEqual("STEP 1", WorkbenchLoopLabels.Caption(1));
            Assert.AreEqual("STEP 6", WorkbenchLoopLabels.Caption(6));
        }

        [Test]
        public void TheLastFilledStepIsWhereTheCycleTurnsAround()
        {
            // THE SENTENCE THE FINDING ASKED FOR.
            Assert.AreEqual(
                "STEP 3  ·  loops back to 1",
                WorkbenchLoopLabels.Compose(3, rowFilled: true, lastFilledStep: 3, capacitySteps: Capacity));
        }

        [Test]
        public void AFilledStepBeforeTheLastOneSaysThen()
        {
            Assert.AreEqual(
                "STEP 1  ·  then",
                WorkbenchLoopLabels.Compose(1, rowFilled: true, lastFilledStep: 3, capacitySteps: Capacity));
            Assert.AreEqual(
                "STEP 2  ·  then",
                WorkbenchLoopLabels.Compose(2, rowFilled: true, lastFilledStep: 3, capacitySteps: Capacity));
        }

        [Test]
        public void AnEmptySocketInsideTheChassisSaysUnused()
        {
            Assert.AreEqual(
                "STEP 3  ·  unused",
                WorkbenchLoopLabels.Compose(3, rowFilled: false, lastFilledStep: 2, capacitySteps: Capacity));
        }

        [Test]
        public void AGapDoesNotMoveTheLoopMarker()
        {
            // Step 1 filled, step 2 empty, step 3 filled: the cycle still turns at 3, and the
            // gap reads as what it is. The Workbench compacts on commit; the label describes the
            // sockets as drawn.
            Assert.AreEqual(
                "STEP 1  ·  then",
                WorkbenchLoopLabels.Compose(1, true, lastFilledStep: 3, capacitySteps: Capacity));
            Assert.AreEqual(
                "STEP 2  ·  unused",
                WorkbenchLoopLabels.Compose(2, false, lastFilledStep: 3, capacitySteps: Capacity));
            Assert.AreEqual(
                "STEP 3  ·  loops back to 1",
                WorkbenchLoopLabels.Compose(3, true, lastFilledStep: 3, capacitySteps: Capacity));
        }

        [Test]
        public void AOneStepProgramLoopsBackToItself()
        {
            Assert.AreEqual(
                "STEP 1  ·  loops back to 1",
                WorkbenchLoopLabels.Compose(1, true, lastFilledStep: 1, capacitySteps: Capacity));
        }

        [Test]
        public void AnEmptyProgramHasNoLoopMarkerAtAll()
        {
            for (int step = 1; step <= Capacity; step++)
            {
                Assert.AreEqual(
                    "STEP " + step + "  ·  unused",
                    WorkbenchLoopLabels.Compose(step, false, lastFilledStep: 0, capacitySteps: Capacity),
                    "step " + step);
            }
        }

        [Test]
        public void TheTriggerRowTeachesWhicheverThingIsMissing()
        {
            // A progressive teacher: the trigger row is always on screen, so it carries the
            // sentence the player needs at their current stage.
            Assert.AreEqual(
                "TRIGGER  ·  fit a chassis first",
                WorkbenchLoopLabels.Compose(0, false, lastFilledStep: 0, capacitySteps: 0));
            Assert.AreEqual(
                "TRIGGER  ·  drop cards below to build a cycle",
                WorkbenchLoopLabels.Compose(0, false, lastFilledStep: 0, capacitySteps: Capacity));
            Assert.AreEqual(
                "TRIGGER  ·  when to start",
                WorkbenchLoopLabels.Compose(0, true, lastFilledStep: 1, capacitySteps: Capacity));
        }

        [Test]
        public void ASocketBeyondTheChassisGetsNoClause()
        {
            // Such a socket is only drawn when it holds a card the fitted chassis can no longer
            // carry. It is genuinely not part of the cycle, so it gets nothing rather than a
            // wrong clause.
            Assert.AreEqual(
                "STEP 5",
                WorkbenchLoopLabels.Compose(5, true, lastFilledStep: 5, capacitySteps: Capacity));
        }

        [Test]
        public void LastFilledStepReadsFromTheTop()
        {
            Assert.AreEqual(0, WorkbenchLoopLabels.LastFilledStep(6, step => false));
            Assert.AreEqual(1, WorkbenchLoopLabels.LastFilledStep(6, step => step == 1));
            Assert.AreEqual(4, WorkbenchLoopLabels.LastFilledStep(6, step => step == 1 || step == 4));
            Assert.AreEqual(6, WorkbenchLoopLabels.LastFilledStep(6, step => true));
            Assert.AreEqual(0, WorkbenchLoopLabels.LastFilledStep(6, null));
        }

        [Test]
        public void EveryLabelIsAsciiOrLatin1()
        {
            // TMP's default LiberationSans SDF atlas has no arrows: a caption made of
            // missing-glyph boxes would be a worse answer to the finding than saying nothing.
            // The middle dot (U+00B7) is in the atlas and already used by the vault headings.
            string[] all =
            {
                WorkbenchLoopLabels.Separator, WorkbenchLoopLabels.TriggerCaption,
                WorkbenchLoopLabels.TriggerHint, WorkbenchLoopLabels.NoChassisHint,
                WorkbenchLoopLabels.EmptyProgramHint, WorkbenchLoopLabels.ThenHint,
                WorkbenchLoopLabels.LoopHint, WorkbenchLoopLabels.UnusedHint,
            };

            foreach (string label in all)
            {
                foreach (char c in label)
                {
                    Assert.LessOrEqual((int)c, 0xFF, "non-Latin-1 glyph in \"" + label + "\"");
                }
            }

            Assert.AreEqual("  ·  ", WorkbenchLoopLabels.Separator);
        }
    }
}
