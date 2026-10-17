using System.Collections.Generic;
using System.Linq;
using GolemFactory.Blueprints;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode WorkbenchControllerTests, ported onto Core's <see cref="WorkbenchSession"/>
    /// (milestone G7). Unity's suite already tested HandleDrop/EngageGears/Patent/SelectChassis
    /// directly rather than through pointer drags, so those tests port nearly line for line; the
    /// rig's three sockets are the session's socket count.
    ///
    /// <para>
    /// Nine tests were about UGUI plumbing -- the canvas root's active flag, closing the other
    /// panels, and real pointer drags that fail over nothing, repeat, are destroyed mid-drag or
    /// commit through a socket -- and are checked by the `workbench` scenario against the Godot
    /// screen instead. SlotCaptions_SurviveARowWithNoCaptionChild is retired: the session
    /// computes captions as strings, and there is no caption child to be missing.
    /// docs/history/godot-test-ledger.md lists each.
    /// </para>
    /// </summary>
    public class WorkbenchControllerTests
    {
        private const int Sockets = 3;

        private sealed class Rig
        {
            public WorkbenchSession Session;
            public GolemEntity Golem;
            public PatentRegistry Patents;
            public ChassisDefinition[] Chassis;
            public int LeverPulls;
            public int LeverRefusals;

            public void SelectChassis(int index) => Session.SelectChassis(Chassis[index]);
        }

        private static Rig Build(ChassisDefinition[] chassis, LogicCoreDefinition[] cores, AppendageActionDefinition[] appendages)
        {
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            var rig = new Rig
            {
                Session = new WorkbenchSession(Sockets),
                Golem = golem,
                Patents = new PatentRegistry(),
                Chassis = chassis,
            };
            rig.Session.ConfigureGolem(golem);
            rig.Session.ConfigurePatents(rig.Patents);
            rig.Session.ConfigureRoster(chassis, cores, appendages);
            rig.Session.LeverPulled += () => rig.LeverPulls++;
            rig.Session.LeverRefused += () => rig.LeverRefusals++;
            return rig;
        }

        private static ChassisDefinition MakeChassis(int maxSlots) => new ChassisDefinition { maxAppendageSlots = maxSlots };
        private static LogicCoreDefinition MakeLogicCore() => new LogicCoreDefinition();
        private static AppendageActionDefinition MakeAppendage() => new AppendageActionDefinition();

        private static WorkbenchCardRef VaultCard(LogicCoreDefinition core) => WorkbenchCardRef.FromVault(core);
        private static WorkbenchCardRef VaultCard(AppendageActionDefinition appendage) => WorkbenchCardRef.FromVault(appendage);
        private static WorkbenchCardRef SlotCard(AppendageActionDefinition appendage, int index) => WorkbenchCardRef.FromSocket(appendage, index);

        private static GolemEntity NewGolem(string id)
        {
            var golem = new GolemEntity();
            golem.Configure(id, null);
            return golem;
        }

        [Test]
        public void EngageGears_CommitsDraftAppendagesAndLogicCoreOntoGolem()
        {
            ChassisDefinition chassis = MakeChassis(3);
            LogicCoreDefinition core = MakeLogicCore();
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new[] { core }, new[] { appendage });

            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(0));
            rig.Session.HandleDrop(VaultCard(core), WorkbenchZone.Trigger);
            rig.Session.Engage();

            Assert.AreEqual(core, rig.Golem.Program.logicCore);
            Assert.AreEqual(1, rig.Golem.Program.appendages.Count);
            Assert.AreEqual(appendage, rig.Golem.Program.appendages[0]);
        }

        [Test]
        public void HandleDrop_DraggedFromOneAppendageSlotToAnother_MovesIt()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });

            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(0));
            rig.Session.HandleDrop(SlotCard(appendage, 0), WorkbenchZone.Socket(2));
            rig.Session.Engage();

            Assert.AreEqual(1, rig.Golem.Program.appendages.Count);
        }

        [Test]
        public void HandleDrop_SlotCardDroppedOnEmptySpace_ClearsSlot()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });

            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(0));
            rig.Session.HandleDrop(SlotCard(appendage, 0), null);
            rig.Session.Engage();

            Assert.AreEqual(0, rig.Golem.Program.appendages.Count);
        }

        [Test]
        public void HandleDrop_OntoInactiveSlotBeyondChassisCapacity_IsNoOp()
        {
            ChassisDefinition chassis = MakeChassis(1);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });

            rig.SelectChassis(0);
            // Slot index 2 is beyond this 1-slot chassis's capacity.
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(2));
            rig.Session.Engage();

            Assert.AreEqual(0, rig.Golem.Program.appendages.Count);
        }

        [Test]
        public void Patent_RegistersBlueprint()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });

            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(0));
            rig.Session.Patent();

            Assert.AreEqual(1, rig.Patents.Blueprints.Count);
            Blueprint blueprint = rig.Patents.Blueprints.Values.First();
            Assert.AreEqual(appendage, blueprint.Appendages[0]);
        }

        [Test]
        public void SelectChassis_TooFewSlotsForCurrentDraft_IsRejected()
        {
            ChassisDefinition big = MakeChassis(3);
            ChassisDefinition small = MakeChassis(1);
            AppendageActionDefinition a1 = MakeAppendage();
            AppendageActionDefinition a2 = MakeAppendage();
            Rig rig = Build(new[] { big, small }, new LogicCoreDefinition[0], new[] { a1, a2 });

            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(a1), WorkbenchZone.Socket(0));
            rig.Session.HandleDrop(VaultCard(a2), WorkbenchZone.Socket(1));
            rig.SelectChassis(1); // rejected: 2 appendages don't fit 1 slot
            rig.Session.Engage();

            Assert.AreEqual(big, rig.Golem.Program.chassis);
            Assert.AreEqual(2, rig.Golem.Program.appendages.Count);
        }

        [Test]
        public void RetargetGolem_SwitchesTargetSoEngageGearsCommitsOntoTheNewGolem()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            GolemEntity golemB = NewGolem("GolemB");

            rig.Session.RetargetGolem(golemB);
            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(0));
            rig.Session.Engage();

            Assert.AreEqual(chassis, golemB.Program.chassis);
            Assert.AreEqual(1, golemB.Program.appendages.Count);
            Assert.AreEqual(0, rig.Golem.Program.appendages.Count);
        }

        [Test]
        public void RetargetGolem_NewGolemAlreadyHasAProgram_ReloadsDraftFromIt()
        {
            ChassisDefinition chassis = MakeChassis(3);
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            GolemEntity golemB = NewGolem("GolemB");
            golemB.Program.TryAssignChassis(chassis);

            rig.Session.RetargetGolem(golemB);
            rig.Session.Engage();

            Assert.AreEqual(chassis, golemB.Program.chassis);
        }

        [Test]
        public void LoadBlueprintIntoDraft_ThenEngage_CommitsBlueprintOntoGolem()
        {
            ChassisDefinition chassis = MakeChassis(3);
            LogicCoreDefinition core = MakeLogicCore();
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            var blueprint = new Blueprint("BP-001", "LocalPlayer", chassis, core, new List<AppendageActionDefinition> { appendage });

            rig.Session.LoadBlueprintIntoDraft(blueprint);
            rig.Session.Engage();

            Assert.AreEqual(chassis, rig.Golem.Program.chassis);
            Assert.AreEqual(core, rig.Golem.Program.logicCore);
            Assert.AreEqual(1, rig.Golem.Program.appendages.Count);
            Assert.AreEqual(appendage, rig.Golem.Program.appendages[0]);
        }

        [Test]
        public void LoadBlueprintIntoDraft_NullBlueprint_IsNoOp()
        {
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);

            rig.Session.LoadBlueprintIntoDraft(null);
            rig.Session.Engage();

            Assert.IsNull(rig.Golem.Program.chassis);
            Assert.AreEqual(0, rig.Golem.Program.appendages.Count);
        }

        [Test]
        public void Start_WithNoCanvasRootConfigured_StaysUsableAsBefore()
        {
            // The session has no canvas at all; Open/Close are its IsOpen bookkeeping, which is
            // what this test pinned in Unity once the canvas was taken away.
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            Assert.IsFalse(rig.Session.IsOpen);
            rig.Session.Open();
            Assert.IsTrue(rig.Session.IsOpen);
        }

        // --- Socket highlights while a card is held ------------------------------------------

        [Test]
        public void BeginCardDrag_AppendageCard_HighlightsOnlyTheSocketsThatWouldAcceptIt()
        {
            // 2-slot chassis in a rig that has 3 appendage sockets, so socket 2 is a genuine
            // "this one would reject the card" case.
            ChassisDefinition chassis = MakeChassis(2);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            rig.SelectChassis(0);

            rig.Session.BeginCardDrag(VaultCard(appendage));

            Assert.AreEqual(DropZoneHighlight.Valid, rig.Session.AppendageHighlights[0]);
            Assert.AreEqual(DropZoneHighlight.Valid, rig.Session.AppendageHighlights[1]);
            Assert.AreEqual(DropZoneHighlight.Invalid, rig.Session.AppendageHighlights[2]);
            Assert.AreEqual(DropZoneHighlight.Invalid, rig.Session.LogicHighlight,
                "the trigger socket must visibly reject an action card");

            rig.Session.EndCardDrag();
            Assert.AreEqual(DropZoneHighlight.Neutral, rig.Session.AppendageHighlights[0]);
            Assert.AreEqual(DropZoneHighlight.Neutral, rig.Session.LogicHighlight);
        }

        [Test]
        public void BeginCardDrag_LogicCoreCard_HighlightsTheTriggerSocketAndRejectsTheActionSockets()
        {
            ChassisDefinition chassis = MakeChassis(3);
            LogicCoreDefinition core = MakeLogicCore();
            Rig rig = Build(new[] { chassis }, new[] { core }, new AppendageActionDefinition[0]);
            rig.SelectChassis(0);

            rig.Session.BeginCardDrag(VaultCard(core));

            Assert.AreEqual(DropZoneHighlight.Valid, rig.Session.LogicHighlight);
            Assert.AreEqual(DropZoneHighlight.Invalid, rig.Session.AppendageHighlights[0]);
        }

        // --- The lever, driven by the commit result ------------------------------------------

        [Test]
        public void EngageGears_Succeeds_PullsTheLeverForReal()
        {
            ChassisDefinition chassis = MakeChassis(3);
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            rig.SelectChassis(0);

            rig.Session.Engage();

            Assert.AreEqual(1, rig.LeverPulls);
            Assert.AreEqual(0, rig.LeverRefusals, "a committed pull should run the full throw, not the refusal judder");
        }

        [Test]
        public void EngageGears_NoTargetGolem_ReportsItInsteadOfFailingSilently()
        {
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            rig.Session.ConfigureGolem(null);

            rig.Session.Engage();

            Assert.AreEqual(WorkbenchStatusReason.NoTarget, rig.Session.StatusReason);
            Assert.IsNotEmpty(rig.Session.StatusText);
            Assert.AreEqual(1, rig.LeverRefusals);
        }

        [Test]
        public void EngageButton_GoesNonInteractableWithNoGolemToProgram()
        {
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            Assert.IsTrue(rig.Session.CanEngage, "a targeted golem should leave the lever live");

            rig.Session.ConfigureGolem(null);
            rig.Session.Tick(0.016f);

            Assert.IsFalse(rig.Session.CanEngage,
                "a lever that cannot commit anything used to look identical to one that could");
        }

        // --- The status line retires itself ---------------------------------------------------

        [Test]
        public void Status_ChassisTooSmall_RetiresItselfOnceTheAppendagesAreRemoved()
        {
            ChassisDefinition big = MakeChassis(3);
            ChassisDefinition small = MakeChassis(1);
            AppendageActionDefinition a1 = MakeAppendage();
            AppendageActionDefinition a2 = MakeAppendage();
            Rig rig = Build(new[] { big, small }, new LogicCoreDefinition[0], new[] { a1, a2 });

            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(a1), WorkbenchZone.Socket(0));
            rig.Session.HandleDrop(VaultCard(a2), WorkbenchZone.Socket(1));
            rig.SelectChassis(1);
            Assert.AreEqual(WorkbenchStatusReason.ChassisTooSmall, rig.Session.StatusReason);

            rig.Session.Tick(0.016f);
            Assert.AreEqual(WorkbenchStatusReason.ChassisTooSmall, rig.Session.StatusReason,
                "the message must stand while it is still true");

            rig.Session.HandleDrop(SlotCard(a1, 0), null);
            rig.Session.HandleDrop(SlotCard(a2, 1), null);
            rig.Session.Tick(0.016f);
            Assert.AreEqual(WorkbenchStatusReason.None, rig.Session.StatusReason,
                "'remove appendages to fit its slot count first' used to survive removing every appendage");
        }

        // --- Default-state coherence ----------------------------------------------------------

        [Test]
        public void Open_ReloadsTheDraftFromTheTargetsCommittedProgram()
        {
            ChassisDefinition chassis = MakeChassis(2);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            rig.Golem.Program.TryAssignChassis(chassis);
            rig.Golem.Program.TryAddAppendage(appendage);

            rig.Session.Open();
            rig.Session.Engage();

            Assert.AreEqual(chassis, rig.Golem.Program.chassis);
            Assert.AreEqual(1, rig.Golem.Program.appendages.Count,
                "opening the screen must re-read the target's program, not commit a stale empty draft over it");
        }

        [Test]
        public void RetargetGolem_ToAGolemWithFewerAppendages_DropsTheOldGolemsSteps()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition a1 = MakeAppendage();
            AppendageActionDefinition a2 = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { a1, a2 });
            rig.SelectChassis(0);
            rig.Session.HandleDrop(VaultCard(a1), WorkbenchZone.Socket(0));
            rig.Session.HandleDrop(VaultCard(a2), WorkbenchZone.Socket(1));
            rig.Session.Engage();
            Assert.AreEqual(2, rig.Golem.Program.appendages.Count);

            GolemEntity golemB = NewGolem("GolemB");
            golemB.Program.TryAssignChassis(chassis);
            golemB.Program.TryAddAppendage(a1);
            rig.Session.RetargetGolem(golemB);
            rig.Session.Engage();

            // The draft used to only overwrite the indices the incoming program filled, so
            // golem A's second step leaked onto golem B.
            Assert.AreEqual(1, golemB.Program.appendages.Count);
        }

        [Test]
        public void StepsWithNoChassis_ViewportDrawsThemAndTheTapeDoesNotClaimOneOverZero()
        {
            // Exactly Main.unity's old demo-golem state: steps appended straight onto the list,
            // past GolemProgram.TryAddAppendage's no-chassis guard.
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            rig.Golem.Program.appendages.Add(appendage);

            rig.Session.Open();

            Assert.IsTrue(rig.Session.SlotVisible(0), "the tape counts this step, so the viewport must not refuse to draw it");
            Assert.AreSame(appendage, rig.Session.DraftAppendageAt(0),
                "the unfitted step must be visible (and draggable back out), not silently hidden");
            StringAssert.DoesNotContain("SLOTS 1/0", rig.Session.Ticker());
        }

        // --- A program with more appendages than the screen has sockets ----------------------

        [Test]
        public void EngageGears_ProgramHasMoreAppendagesThanSockets_RefusesInsteadOfTruncating()
        {
            ChassisDefinition chassis = MakeChassis(6);
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            GolemEntity wide = NewGolem("WideGolem");
            wide.Program.TryAssignChassis(chassis);
            for (int i = 0; i < 4; i++)
            {
                wide.Program.TryAddAppendage(MakeAppendage());
            }

            rig.Session.RetargetGolem(wide);
            Assert.AreEqual(1, rig.Session.DraftOverflowCount, "the rig has 3 sockets and the program has 4 steps");
            rig.Session.Engage();

            Assert.AreEqual(4, wide.Program.appendages.Count,
                "engaging a truncated draft used to silently discard the steps the UI could not show");
            Assert.AreEqual(WorkbenchStatusReason.DraftTruncated, rig.Session.StatusReason);
            Assert.AreEqual(1, rig.LeverRefusals);
        }

        [Test]
        public void Patent_TruncatedDraft_DoesNotRegisterAPartialBlueprint()
        {
            ChassisDefinition chassis = MakeChassis(6);
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            GolemEntity wide = NewGolem("WideGolem");
            wide.Program.TryAssignChassis(chassis);
            for (int i = 0; i < 4; i++)
            {
                wide.Program.TryAddAppendage(MakeAppendage());
            }

            rig.Session.RetargetGolem(wide);
            rig.Session.Patent();

            Assert.AreEqual(0, rig.Patents.Blueprints.Count);
            Assert.AreEqual(WorkbenchStatusReason.DraftTruncated, rig.Session.StatusReason);
        }

        [Test]
        public void Status_DraftTruncated_RetiresOnceRetargetedOntoAGolemThatFits()
        {
            ChassisDefinition chassis = MakeChassis(6);
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            GolemEntity wide = NewGolem("WideGolem");
            wide.Program.TryAssignChassis(chassis);
            for (int i = 0; i < 4; i++)
            {
                wide.Program.TryAddAppendage(MakeAppendage());
            }

            rig.Session.RetargetGolem(wide);
            rig.Session.Engage();
            Assert.AreEqual(WorkbenchStatusReason.DraftTruncated, rig.Session.StatusReason);
            rig.Session.Tick(0.016f);
            Assert.AreEqual(WorkbenchStatusReason.DraftTruncated, rig.Session.StatusReason,
                "the message must stand while it is still true -- it is not time-based");

            GolemEntity narrow = NewGolem("NarrowGolem");
            narrow.Program.TryAssignChassis(chassis);
            narrow.Program.TryAddAppendage(MakeAppendage());
            rig.Session.RetargetGolem(narrow);
            rig.Session.Tick(0.016f);

            Assert.AreEqual(0, rig.Session.DraftOverflowCount);
            Assert.AreEqual(WorkbenchStatusReason.None, rig.Session.StatusReason);
        }

        [Test]
        public void EngageGears_ProgramExactlyFillsTheSockets_StillCommits()
        {
            ChassisDefinition chassis = MakeChassis(6);
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            GolemEntity exact = NewGolem("ExactGolem");
            exact.Program.TryAssignChassis(chassis);
            for (int i = 0; i < 3; i++)
            {
                exact.Program.TryAddAppendage(MakeAppendage());
            }

            rig.Session.RetargetGolem(exact);
            Assert.AreEqual(0, rig.Session.DraftOverflowCount);
            rig.Session.Engage();

            Assert.AreEqual(3, exact.Program.appendages.Count);
            Assert.AreNotEqual(WorkbenchStatusReason.DraftTruncated, rig.Session.StatusReason);
        }

        // --- The loop labels (docs/cozy-automation-design.md §3) -----------------------------

        [Test]
        public void SlotCaptions_NameTheLoop_AndTheMarkerFollowsTheLastFilledStep()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition first = MakeAppendage();
            AppendageActionDefinition second = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { first, second });
            rig.SelectChassis(0);

            rig.Session.HandleDrop(VaultCard(first), WorkbenchZone.Socket(0));
            Assert.AreEqual("STEP 1  ·  loops back to 1", rig.Session.SlotCaption(0));
            Assert.AreEqual("STEP 2  ·  unused", rig.Session.SlotCaption(1));

            rig.Session.HandleDrop(VaultCard(second), WorkbenchZone.Socket(1));
            // The marker WALKED DOWN, which is the part that demonstrates the cycle.
            Assert.AreEqual("STEP 1  ·  then", rig.Session.SlotCaption(0));
            Assert.AreEqual("STEP 2  ·  loops back to 1", rig.Session.SlotCaption(1));
            Assert.AreEqual("STEP 3  ·  unused", rig.Session.SlotCaption(2));
        }

        [Test]
        public void TriggerCaption_TeachesWhicheverThingIsMissing()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            Rig rig = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });

            Assert.AreEqual("TRIGGER  ·  fit a chassis first", rig.Session.TriggerCaption);
            rig.SelectChassis(0);
            Assert.AreEqual("TRIGGER  ·  drop cards below to build a cycle", rig.Session.TriggerCaption);
            rig.Session.HandleDrop(VaultCard(appendage), WorkbenchZone.Socket(0));
            Assert.AreEqual("TRIGGER  ·  when to start", rig.Session.TriggerCaption);
        }

        // --- The TARGET header ------------------------------------------------------------

        [Test]
        public void TargetHeader_NamesTheGolemById_NotByItsGameObjectName()
        {
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            // The shape a station-built golem really has: named after the PREFAB, identified by
            // the id it was configured with.
            GolemEntity golem = NewGolem("PlayerGolem-003");
            golem.name = "GolemPrefab(Clone)";

            rig.Session.RetargetGolem(golem);

            StringAssert.Contains("PlayerGolem-003", rig.Session.TargetHeader);
            StringAssert.DoesNotContain("Clone", rig.Session.TargetHeader,
                "the header used to show Unity's Instantiate suffix on the one screen that names a golem");
        }

        [Test]
        public void TargetHeader_GolemWithNoId_FallsBackToTheObjectNameRatherThanGoingBlank()
        {
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            var unnamed = new GolemEntity { name = "LooseGolem" };

            rig.Session.RetargetGolem(unnamed);

            StringAssert.Contains("LooseGolem", rig.Session.TargetHeader);
        }

        [Test]
        public void TargetHeader_NoGolem_SaysNone()
        {
            Rig rig = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            rig.Session.RetargetGolem(null);
            StringAssert.Contains("none", rig.Session.TargetHeader);
        }
    }
}
