using NUnit.Framework;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // WorkbenchDiagnostics is the engine-free half of the Workbench's "diagnostic tape
    // ticker" -- the numbers and the exact player-visible string. EditMode, no scene: that
    // separation is the whole point of extracting it (same idiom as GridCoordinateConverter
    // and GolemAnimationUtility).
    public class WorkbenchDiagnosticsTests
    {
        [Test]
        public void ComputeCycleTicks_SumsStepDurations()
        {
            Assert.AreEqual(9, WorkbenchDiagnostics.ComputeCycleTicks(new[] { 2, 3, 4 }));
        }

        [Test]
        public void ComputeCycleTicks_FloorsEachStepAtOneTick()
        {
            // A 0-tick (or negative) authoring mistake must not make a cycle look free.
            Assert.AreEqual(3, WorkbenchDiagnostics.ComputeCycleTicks(new[] { 0, -5, 1 }));
        }

        [Test]
        public void ComputeCycleTicks_NoSteps_IsZero()
        {
            Assert.AreEqual(0, WorkbenchDiagnostics.ComputeCycleTicks(new int[0]));
            Assert.AreEqual(0, WorkbenchDiagnostics.ComputeCycleTicks(null));
        }

        [Test]
        public void ComputeSteamDraw_ScalesWithStepsAndTier()
        {
            Assert.AreEqual(3 * WorkbenchDiagnostics.SteamPerStep * 2, WorkbenchDiagnostics.ComputeSteamDraw(3, 2));
        }

        [Test]
        public void ComputeSteamDraw_NoSteps_DrawsNothing()
        {
            Assert.AreEqual(0, WorkbenchDiagnostics.ComputeSteamDraw(0, 5));
        }

        [Test]
        public void ComputeSteamDraw_TierBelowOne_TreatedAsTierOne()
        {
            Assert.AreEqual(WorkbenchDiagnostics.SteamPerStep, WorkbenchDiagnostics.ComputeSteamDraw(1, 0));
        }

        [Test]
        public void ComputeCyclesPerMinute_ConvertsTicksToCycleRate()
        {
            // 2 ticks/second over a 10-tick cycle = one cycle per 5s = 12/min.
            Assert.AreEqual(12f, WorkbenchDiagnostics.ComputeCyclesPerMinute(10, 2f), 0.0001f);
        }

        [Test]
        public void ComputeCyclesPerMinute_EmptyProgram_IsZeroNotDivideByZero()
        {
            Assert.AreEqual(0f, WorkbenchDiagnostics.ComputeCyclesPerMinute(0, 2f));
            Assert.AreEqual(0f, WorkbenchDiagnostics.ComputeCyclesPerMinute(10, 0f));
        }

        [Test]
        public void Humanize_SplitsCamelCaseAssetNames()
        {
            Assert.AreEqual("Mainspring Overclocker", WorkbenchDiagnostics.Humanize("MainspringOverclocker"));
            Assert.AreEqual("Zeppelin Freight Loader", WorkbenchDiagnostics.Humanize("ZeppelinFreightLoader"));
        }

        [TestCase("IntervalCore10", "Interval Core 10", TestName = "Humanize_KeepsDigitRunsTogetherAndSeparateFromWords")]
        [TestCase("PSIValve", "PSI Valve", TestName = "Humanize_KeepsAcronymRunsIntact")]
        public void Humanize_KeepsARunWhole(string name, string expected)
        {
            Assert.AreEqual(expected, WorkbenchDiagnostics.Humanize(name));
        }

        [Test]
        public void Humanize_EmptyInput_IsEmpty()
        {
            Assert.AreEqual(string.Empty, WorkbenchDiagnostics.Humanize(null));
            Assert.AreEqual(string.Empty, WorkbenchDiagnostics.Humanize(string.Empty));
        }

        [Test]
        public void DisplayName_PrefersTheAssetName()
        {
            Assert.AreEqual("Always On Core", WorkbenchDiagnostics.DisplayName("AlwaysOnCore", "IntervalCore"));
        }

        [Test]
        public void DisplayName_UnnamedRuntimeInstance_FallsBackToItsType()
        {
            // Demo bootstraps build definitions via ScriptableObject.CreateInstance, which
            // leaves `name` empty -- those cards used to render as a blank strip.
            Assert.AreEqual("Extract From Node", WorkbenchDiagnostics.DisplayName("", "ExtractFromNode"));
            Assert.AreEqual("Extract From Node", WorkbenchDiagnostics.DisplayName(null, "ExtractFromNode"));
        }

        [Test]
        public void ComposeTicker_ReportsChassisSlotsCycleAndSteam()
        {
            string tape = WorkbenchDiagnostics.ComposeTicker(
                "AetherHauler", 2, 3, 14, 16, 8.57f);

            StringAssert.Contains("Aether Hauler", tape);
            StringAssert.Contains("SLOTS 2/3", tape);
            StringAssert.DoesNotContain("TRIGGER", tape, "no trigger to report since 2026-11-19");
            StringAssert.Contains("14 ticks", tape);
            StringAssert.Contains("16 psi", tape);
        }

        [Test]
        public void ComposeTicker_EmptyDraft_ShowsPlaceholdersNotZeroCycle()
        {
            string tape = WorkbenchDiagnostics.ComposeTicker(null, 0, 0, 0, 0, 0f);

            StringAssert.Contains("CHASSIS -- none --", tape);
            StringAssert.Contains("CYCLE --", tape);
            StringAssert.Contains("SLOTS --", tape);
        }

        [Test]
        public void ComposeTicker_StepsButNoChassis_DoesNotClaimANonsenseSlotRatio()
        {
            // Main.unity's demo golem shipped in exactly this state (steps appended past
            // GolemProgram.TryAddAppendage's no-chassis guard), and the tape read
            // "SLOTS 1/0" while the blueprint viewport -- which has no sockets without a
            // chassis -- drew nothing at all.
            string tape = WorkbenchDiagnostics.ComposeTicker(null, 1, 0, 2, 4, 60f);

            StringAssert.DoesNotContain("SLOTS 1/0", tape);
            StringAssert.Contains("no chassis", tape);
        }
    }
}
