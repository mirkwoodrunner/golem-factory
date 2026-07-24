using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using GolemFactory.Blueprints;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Exercises WorkbenchController's decision logic (HandleDrop/EngageGears/Patent/
    // SelectChassis) directly rather than simulating real pointer drags through the
    // EventSystem/GraphicRaycaster -- that plumbing (WorkbenchCard's drag handlers) is
    // thin, low-risk Unity event wiring; the logic worth testing is what HandleDrop
    // decides to do with a (card, zone) pair, which doesn't require an actual drag.
    // Needs PlayMode since WorkbenchController.Start() (which builds the initial UI and
    // loads the draft) only runs in Play Mode, same reason Signal-trigger tests moved to
    // PlayMode in M7 -- see GolemSignalTriggerTests.cs.
    public class WorkbenchControllerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private (WorkbenchController controller, GolemEntity golem, ArtificerFocusMeterHolder focus, PatentRegistryHolder patents)
            Build(ChassisDefinition[] chassisRoster, LogicCoreDefinition[] logicCoreRoster, AppendageActionDefinition[] appendageRoster)
        {
            _root = new GameObject("Root");

            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", null);

            var focus = new GameObject("Focus").AddComponent<ArtificerFocusMeterHolder>();
            focus.transform.SetParent(_root.transform);

            var patents = new GameObject("Patents").AddComponent<PatentRegistryHolder>();
            patents.transform.SetParent(_root.transform);

            RectTransform vault = NewRect("Vault", _root.transform);
            RectTransform chassisRow = NewRect("ChassisRow", _root.transform);
            RectTransform dragLayer = NewRect("DragLayer", _root.transform);

            var logicSlotGo = new GameObject("LogicSlot", typeof(RectTransform));
            logicSlotGo.transform.SetParent(_root.transform, false);
            var logicSlot = logicSlotGo.AddComponent<WorkbenchDropZone>();
            logicSlot.Configure(DropZoneKind.LogicCore, -1);

            var appendageZones = new WorkbenchDropZone[3];
            for (int i = 0; i < appendageZones.Length; i++)
            {
                var zoneGo = new GameObject($"AppendageSlot{i}", typeof(RectTransform));
                zoneGo.transform.SetParent(_root.transform, false);
                var zone = zoneGo.AddComponent<WorkbenchDropZone>();
                zone.Configure(DropZoneKind.Appendage, i);
                appendageZones[i] = zone;
            }

            var tapeTicker = new GameObject("Ticker", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            tapeTicker.transform.SetParent(_root.transform, false);
            var status = new GameObject("Status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            status.transform.SetParent(_root.transform, false);
            var engageButton = new GameObject("Engage", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            engageButton.transform.SetParent(_root.transform, false);
            var patentButton = new GameObject("Patent", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            patentButton.transform.SetParent(_root.transform, false);

            var controller = new GameObject("Controller").AddComponent<WorkbenchController>();
            controller.transform.SetParent(_root.transform);
            controller.ConfigureGolem(golem);
            controller.ConfigureSystems(focus, patents);
            controller.ConfigureRoster(chassisRoster, logicCoreRoster, appendageRoster);
            controller.ConfigureUI(vault, chassisRow, dragLayer, logicSlot, appendageZones, tapeTicker, status, engageButton, patentButton);

            return (controller, golem, focus, patents);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static ChassisDefinition MakeChassis(int maxSlots)
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.maxAppendageSlots = maxSlots;
            return chassis;
        }

        private static LogicCoreDefinition MakeLogicCore()
        {
            return ScriptableObject.CreateInstance<LogicCoreDefinition>();
        }

        private static AppendageActionDefinition MakeAppendage()
        {
            return ScriptableObject.CreateInstance<AppendageActionDefinition>();
        }

        private static WorkbenchCard VaultCard(LogicCoreDefinition logicCore, AppendageActionDefinition appendage) =>
            MakeCard(logicCore, appendage, isVaultOrigin: true, sourceIndex: -1);

        private static WorkbenchCard SlotCard(AppendageActionDefinition appendage, int sourceIndex) =>
            MakeCard(null, appendage, isVaultOrigin: false, sourceIndex: sourceIndex);

        private static WorkbenchCard MakeCard(
            LogicCoreDefinition logicCore, AppendageActionDefinition appendage, bool isVaultOrigin, int sourceIndex)
        {
            var card = new GameObject("Card").AddComponent<WorkbenchCard>();
            card.LogicCore = logicCore;
            card.Appendage = appendage;
            card.IsVaultOrigin = isVaultOrigin;
            card.SourceAppendageIndex = sourceIndex;
            return card;
        }

        private static WorkbenchDropZone MakeZone(DropZoneKind kind, int appendageIndex)
        {
            var zone = new GameObject("Zone", typeof(RectTransform)).AddComponent<WorkbenchDropZone>();
            zone.Configure(kind, appendageIndex);
            return zone;
        }

        [UnityTest]
        public IEnumerator EngageGears_CommitsDraftAppendagesAndLogicCoreOntoGolem()
        {
            ChassisDefinition chassis = MakeChassis(3);
            LogicCoreDefinition logicCore = MakeLogicCore();
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new[] { logicCore }, new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));
            controller.HandleDrop(VaultCard(logicCore, null), MakeZone(DropZoneKind.LogicCore, -1));
            EngageViaButton(controller);

            Assert.AreEqual(logicCore, golem.Program.logicCore);
            Assert.AreEqual(1, golem.Program.appendages.Count);
            Assert.AreEqual(appendage, golem.Program.appendages[0]);
        }

        [UnityTest]
        public IEnumerator EngageGears_InsufficientFocus_DoesNotCommit()
        {
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, focus, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            // Drain focus to below the default reprogram cost (10).
            while (focus.Meter.TryConsume(10f)) { }

            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));
            EngageViaButton(controller);

            Assert.AreEqual(0, golem.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator EngageGears_SufficientFocus_ConsumesFocus()
        {
            var (controller, _, focus, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            float before = focus.Meter.CurrentFocus;

            EngageViaButton(controller);

            Assert.Less(focus.Meter.CurrentFocus, before);
        }

        [UnityTest]
        public IEnumerator HandleDrop_DraggedFromOneAppendageSlotToAnother_MovesIt()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));
            controller.HandleDrop(SlotCard(appendage, 0), MakeZone(DropZoneKind.Appendage, 2));
            EngageViaButton(controller);

            Assert.AreEqual(1, golem.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator HandleDrop_SlotCardDroppedOnEmptySpace_ClearsSlot()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));
            controller.HandleDrop(SlotCard(appendage, 0), null);
            EngageViaButton(controller);

            Assert.AreEqual(0, golem.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator HandleDrop_OntoInactiveSlotBeyondChassisCapacity_IsNoOp()
        {
            ChassisDefinition chassis = MakeChassis(1);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            // Slot index 2 is beyond this 1-slot chassis's capacity.
            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 2));
            EngageViaButton(controller);

            Assert.AreEqual(0, golem.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator Patent_SufficientFocus_RegistersBlueprint()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, patents) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));
            PatentViaButton(controller);

            Assert.AreEqual(1, patents.Registry.Blueprints.Count);
            Blueprint blueprint = patents.Registry.Blueprints.Values.First();
            Assert.AreEqual(appendage, blueprint.Appendages[0]);
        }

        [UnityTest]
        public IEnumerator Patent_InsufficientFocus_DoesNotRegister()
        {
            var (controller, _, focus, patents) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            while (focus.Meter.TryConsume(10f)) { }

            PatentViaButton(controller);

            Assert.AreEqual(0, patents.Registry.Blueprints.Count);
        }

        [UnityTest]
        public IEnumerator SelectChassis_TooFewSlotsForCurrentDraft_IsRejected()
        {
            ChassisDefinition bigChassis = MakeChassis(3);
            ChassisDefinition smallChassis = MakeChassis(1);
            AppendageActionDefinition a1 = MakeAppendage();
            AppendageActionDefinition a2 = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { bigChassis, smallChassis }, new LogicCoreDefinition[0], new[] { a1, a2 });
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.HandleDrop(VaultCard(null, a1), MakeZone(DropZoneKind.Appendage, 0));
            controller.HandleDrop(VaultCard(null, a2), MakeZone(DropZoneKind.Appendage, 1));

            SelectChassisViaButton(controller, 1); // rejected: 2 appendages don't fit 1 slot
            EngageViaButton(controller);

            Assert.AreEqual(bigChassis, golem.Program.chassis);
            Assert.AreEqual(2, golem.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator RetargetGolem_SwitchesTargetSoEngageGearsCommitsOntoTheNewGolem()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golemA, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            var golemB = new GameObject("GolemB").AddComponent<GolemEntity>();
            golemB.transform.SetParent(_root.transform);
            golemB.Configure("GolemB", null);

            controller.RetargetGolem(golemB);
            SelectChassisViaButton(controller, 0);
            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));
            EngageViaButton(controller);

            Assert.AreEqual(chassis, golemB.Program.chassis);
            Assert.AreEqual(1, golemB.Program.appendages.Count);
            Assert.AreEqual(0, golemA.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator RetargetGolem_NewGolemAlreadyHasAProgram_ReloadsDraftFromIt()
        {
            ChassisDefinition chassis = MakeChassis(3);
            var (controller, _, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;

            var golemB = new GameObject("GolemB").AddComponent<GolemEntity>();
            golemB.transform.SetParent(_root.transform);
            golemB.Configure("GolemB", null);
            golemB.Program.TryAssignChassis(chassis);

            controller.RetargetGolem(golemB);
            EngageViaButton(controller);

            Assert.AreEqual(chassis, golemB.Program.chassis);
        }

        private static void EngageViaButton(WorkbenchController controller) =>
            FindSibling(controller, "Engage").GetComponent<Button>().onClick.Invoke();

        private static void PatentViaButton(WorkbenchController controller) =>
            FindSibling(controller, "Patent").GetComponent<Button>().onClick.Invoke();

        private static void SelectChassisViaButton(WorkbenchController controller, int chassisIndex) =>
            FindSibling(controller, "ChassisRow").GetChild(chassisIndex).GetComponent<Button>().onClick.Invoke();

        private static Transform FindSibling(WorkbenchController controller, string name) =>
            controller.transform.parent.Find(name);
    }
}
