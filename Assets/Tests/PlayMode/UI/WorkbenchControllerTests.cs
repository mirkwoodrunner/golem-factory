using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
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

            var tapeTicker = new GameObject("Ticker", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            tapeTicker.transform.SetParent(_root.transform, false);
            var status = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
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

        [UnityTest]
        public IEnumerator LoadBlueprintIntoDraft_ThenEngage_CommitsBlueprintOntoGolem()
        {
            ChassisDefinition chassis = MakeChassis(3);
            LogicCoreDefinition logicCore = MakeLogicCore();
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            var blueprint = new Blueprint("BP-001", "LocalPlayer", chassis, logicCore, new System.Collections.Generic.List<AppendageActionDefinition> { appendage });

            controller.LoadBlueprintIntoDraft(blueprint);
            EngageViaButton(controller);

            Assert.AreEqual(chassis, golem.Program.chassis);
            Assert.AreEqual(logicCore, golem.Program.logicCore);
            Assert.AreEqual(1, golem.Program.appendages.Count);
            Assert.AreEqual(appendage, golem.Program.appendages[0]);
        }

        [UnityTest]
        public IEnumerator LoadBlueprintIntoDraft_NullBlueprint_IsNoOp()
        {
            var (controller, golem, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;

            controller.LoadBlueprintIntoDraft(null);
            EngageViaButton(controller);

            Assert.IsNull(golem.Program.chassis);
            Assert.AreEqual(0, golem.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator Start_WithNoCanvasRootConfigured_StaysUsableAsBefore()
        {
            // Build() (used by every other test in this file) never wires canvasRoot --
            // Open()/Close() must stay pure IsOpen bookkeeping with no visual side effects
            // there, so every pre-existing test above keeps passing unmodified.
            var (controller, _, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;

            Assert.IsFalse(controller.IsOpen);
            controller.Open();
            Assert.IsTrue(controller.IsOpen);
        }

        [UnityTest]
        public IEnumerator Open_ActivatesCanvasRoot_Close_Deactivates()
        {
            var root = new GameObject("VisRoot");
            var controller = root.AddComponent<WorkbenchController>();
            var canvasRoot = new GameObject("CanvasRoot");
            canvasRoot.transform.SetParent(root.transform);
            controller.ConfigureVisibility(canvasRoot, null, null, null);

            controller.Open();
            Assert.IsTrue(controller.IsOpen);
            Assert.IsTrue(canvasRoot.activeSelf);

            controller.Close();
            Assert.IsFalse(controller.IsOpen);
            Assert.IsFalse(canvasRoot.activeSelf);

            Object.DestroyImmediate(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Open_ClosesManagementPanelAndConstructionPanel()
        {
            var root = new GameObject("MutexRoot");
            var controller = root.AddComponent<WorkbenchController>();
            var management = root.AddComponent<ManagementPanel>();
            var construction = root.AddComponent<GolemConstructionPanel>();
            controller.ConfigureVisibility(null, null, management, construction);
            management.Open();
            construction.Open(null);

            controller.Open();

            Assert.IsFalse(management.IsOpen);
            Assert.IsFalse(construction.IsOpen);

            Object.DestroyImmediate(root);
            yield return null;
        }

        // ---------------------------------------------------------------------------
        // Real drag path (OnBeginDrag/OnDrag/OnEndDrag), as opposed to the HandleDrop-only
        // tests above. Added because the drag *plumbing* turned out not to be the "thin,
        // low-risk Unity event wiring" the M8 notes assumed: reparenting a card onto the
        // DragLayer took it outside everything RebuildUI knew how to clear, so every drag
        // released over a non-drop-zone (mahogany background, chassis rack, title bar)
        // leaked a live GameObject that survived Close()/Open() and RetargetGolem() and
        // accumulated for the whole session.
        // ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator FailedDrag_ReleasedOverNothing_DestroysTheCardInsteadOfOrphaningIt()
        {
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            Transform dragLayer = FindSibling(controller, "DragLayer");
            Assert.AreEqual(0, dragLayer.childCount, "precondition: nothing on the DragLayer before any drag");

            WorkbenchCard card = FirstVaultCard(controller);
            Assert.IsNotNull(card, "the vault should have rendered a draggable card");
            GameObject cardGo = card.gameObject;

            card.OnBeginDrag(Pointer(new Vector2(300f, 300f), null));
            // If this ever stops being true the rest of the test proves nothing -- the
            // leak only existed because the card genuinely moves onto the DragLayer.
            Assert.AreEqual(dragLayer, cardGo.transform.parent, "the drag must reparent the card onto the DragLayer");
            Assert.AreEqual(1, dragLayer.childCount);

            card.OnDrag(Pointer(new Vector2(900f, 500f), null));
            // Released over the mahogany background: nothing under the pointer.
            card.OnEndDrag(Pointer(new Vector2(900f, 500f), null));

            // Destroy() is deferred to end of frame.
            yield return null;

            Assert.AreEqual(0, dragLayer.childCount, "a failed drag orphaned a card GameObject on the DragLayer");
            Assert.IsTrue(cardGo == null, "the dragged card must be destroyed, not merely left unparented");
        }

        [UnityTest]
        public IEnumerator RepeatedFailedDrags_DoNotAccumulateOrphans()
        {
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            Transform dragLayer = FindSibling(controller, "DragLayer");

            for (int i = 0; i < 4; i++)
            {
                WorkbenchCard card = FirstVaultCard(controller);
                Assert.IsNotNull(card, "the vault must re-render a card after each failed drag");
                card.OnBeginDrag(Pointer(new Vector2(300f, 300f), null));
                card.OnDrag(Pointer(new Vector2(880f, 460f), null));
                card.OnEndDrag(Pointer(new Vector2(880f, 460f), null));
                yield return null;

                Assert.AreEqual(0, dragLayer.childCount, $"orphan count must return to zero after failed drag {i + 1}");
            }
        }

        [UnityTest]
        public IEnumerator FailedDrag_LeavesNoGhostPlaceholderBehindInTheVault()
        {
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            Transform vault = FindSibling(controller, "Vault");
            WorkbenchCard card = FirstVaultCard(controller);

            card.OnBeginDrag(Pointer(new Vector2(300f, 300f), null));
            // The ghost is what stops the vault list reflowing upward mid-drag...
            Assert.IsNotNull(vault.GetComponentInChildren<WorkbenchCardGhost>(true),
                "a placeholder should hold the card's place in the layout while it is dragged");

            card.OnEndDrag(Pointer(new Vector2(880f, 460f), null));
            yield return null;

            // ...but it must not outlive the drag; a ghost has no WorkbenchCard, so
            // ClearCards used not to sweep it up.
            Assert.IsNull(vault.GetComponentInChildren<WorkbenchCardGhost>(true),
                "the drag placeholder outlived the drag");
        }

        [UnityTest]
        public IEnumerator FailedDrag_OrphansAreGoneAfterCloseOpenAndRetarget()
        {
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            Transform dragLayer = FindSibling(controller, "DragLayer");
            WorkbenchCard card = FirstVaultCard(controller);
            card.OnBeginDrag(Pointer(new Vector2(300f, 300f), null));
            card.OnEndDrag(Pointer(new Vector2(880f, 460f), null));
            yield return null;

            controller.Close();
            controller.Open();
            var golemB = new GameObject("GolemB").AddComponent<GolemEntity>();
            golemB.transform.SetParent(_root.transform);
            golemB.Configure("GolemB", null);
            controller.RetargetGolem(golemB);
            yield return null;

            Assert.AreEqual(0, dragLayer.childCount, "orphans must not survive Close()/Open()/RetargetGolem()");
        }

        [UnityTest]
        public IEnumerator CardDestroyedMidDrag_LeavesNoGhostAndNoStuckSocketHighlight()
        {
            // RebuildUI tears down every card from data and nothing stops it running while
            // the pointer is down, so OnEndDrag may simply never fire for a card. Its
            // ghost would then be stranded in the vault and every socket left frozen in
            // its mid-drag highlight.
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            Transform vault = FindSibling(controller, "Vault");
            WorkbenchCard card = FirstVaultCard(controller);
            card.OnBeginDrag(Pointer(new Vector2(300f, 300f), null));
            Assert.AreEqual(DropZoneHighlight.Valid, AppendageZone(controller, 0).Highlight);

            // The card dies mid-drag; OnEndDrag never runs.
            Object.Destroy(card.gameObject);
            yield return null;

            Assert.IsNull(vault.GetComponentInChildren<WorkbenchCardGhost>(true),
                "a card destroyed mid-drag stranded its ghost placeholder in the vault");
            Assert.AreEqual(DropZoneHighlight.Neutral, AppendageZone(controller, 0).Highlight,
                "sockets were left frozen in their mid-drag highlight");
            Assert.AreEqual(DropZoneHighlight.Neutral, LogicZone(controller).Highlight);
        }

        [UnityTest]
        public IEnumerator RealDrag_ReleasedOverAnAppendageSocket_CommitsThroughTheNormalPath()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            Transform slotZero = FindSibling(controller, "AppendageSlot0");
            WorkbenchCard card = FirstVaultCard(controller);
            card.OnBeginDrag(Pointer(new Vector2(300f, 300f), null));
            card.OnDrag(Pointer(new Vector2(500f, 400f), null));
            card.OnEndDrag(Pointer(new Vector2(500f, 400f), slotZero.gameObject));
            yield return null;

            Transform dragLayer = FindSibling(controller, "DragLayer");
            Assert.AreEqual(0, dragLayer.childCount, "a successful drag must not leave the card on the DragLayer either");

            EngageViaButton(controller);
            Assert.AreEqual(1, golem.Program.appendages.Count, "the real drag path should have staged the card into slot 0");
            Assert.AreEqual(appendage, golem.Program.appendages[0]);
        }

        [UnityTest]
        public IEnumerator RealDrag_SlotCardReleasedOverNothing_ClearsTheSlotAndDestroysTheCard()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);
            controller.HandleDrop(VaultCard(null, appendage), MakeZone(DropZoneKind.Appendage, 0));

            Transform slotZero = FindSibling(controller, "AppendageSlot0");
            WorkbenchCard slotCard = slotZero.GetComponentInChildren<WorkbenchCard>(true);
            Assert.IsNotNull(slotCard, "the drop should have put a card in slot 0");
            GameObject slotCardGo = slotCard.gameObject;

            slotCard.OnBeginDrag(Pointer(new Vector2(400f, 400f), null));
            slotCard.OnEndDrag(Pointer(new Vector2(900f, 100f), null));
            yield return null;

            Transform dragLayer = FindSibling(controller, "DragLayer");
            Assert.AreEqual(0, dragLayer.childCount);
            Assert.IsTrue(slotCardGo == null, "the card dragged out of a slot must be destroyed");

            EngageViaButton(controller);
            Assert.AreEqual(0, golem.Program.appendages.Count, "dragging a card out of a slot should clear it");
        }

        // ---------------------------------------------------------------------------
        // Mid-drag socket highlighting: every appendage socket used to look identical
        // while an appendage was held, and nothing signalled that the logic-core socket
        // would reject it.
        // ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BeginCardDrag_AppendageCard_HighlightsOnlyTheSocketsThatWouldAcceptIt()
        {
            // 2-slot chassis in a rig that has 3 appendage sockets, so socket 2 is a
            // genuine "this one would reject the card" case.
            ChassisDefinition chassis = MakeChassis(2);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, _, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.BeginCardDrag(VaultCard(null, appendage));

            Assert.AreEqual(DropZoneHighlight.Valid, AppendageZone(controller, 0).Highlight);
            Assert.AreEqual(DropZoneHighlight.Valid, AppendageZone(controller, 1).Highlight);
            Assert.AreEqual(DropZoneHighlight.Invalid, AppendageZone(controller, 2).Highlight);
            Assert.AreEqual(DropZoneHighlight.Invalid, LogicZone(controller).Highlight,
                "the trigger socket must visibly reject an action card");

            controller.EndCardDrag();
            Assert.AreEqual(DropZoneHighlight.Neutral, AppendageZone(controller, 0).Highlight);
            Assert.AreEqual(DropZoneHighlight.Neutral, LogicZone(controller).Highlight);
        }

        [UnityTest]
        public IEnumerator BeginCardDrag_LogicCoreCard_HighlightsTheTriggerSocketAndRejectsTheActionSockets()
        {
            ChassisDefinition chassis = MakeChassis(3);
            LogicCoreDefinition logicCore = MakeLogicCore();
            var (controller, _, _, _) = Build(new[] { chassis }, new[] { logicCore }, new AppendageActionDefinition[0]);
            yield return null;
            SelectChassisViaButton(controller, 0);

            controller.BeginCardDrag(VaultCard(logicCore, null));

            Assert.AreEqual(DropZoneHighlight.Valid, LogicZone(controller).Highlight);
            Assert.AreEqual(DropZoneHighlight.Invalid, AppendageZone(controller, 0).Highlight);
        }

        // ---------------------------------------------------------------------------
        // Lever driven by the commit result, not by Button.onClick.
        // ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EngageGears_Succeeds_PullsTheLeverForReal()
        {
            ChassisDefinition chassis = MakeChassis(3);
            var (controller, _, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            WorkbenchLever lever = AttachLever(controller);
            SelectChassisViaButton(controller, 0);

            EngageViaButton(controller);

            Assert.IsTrue(lever.IsAnimating);
            Assert.IsFalse(lever.IsRefusing, "a committed pull should run the full throw, not the refusal judder");
        }

        [UnityTest]
        public IEnumerator EngageGears_InsufficientFocus_RefusesTheLeverInsteadOfPullingIt()
        {
            var (controller, _, focus, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            WorkbenchLever lever = AttachLever(controller);
            while (focus.Meter.TryConsume(10f)) { }

            EngageViaButton(controller);

            Assert.IsTrue(lever.IsRefusing,
                "the lever used to run a full satisfying pull on every failure path -- positive feedback for a no-op");
            Assert.AreEqual(WorkbenchStatusReason.InsufficientFocusEngage, controller.StatusReason);
        }

        [UnityTest]
        public IEnumerator EngageGears_NoTargetGolem_ReportsItInsteadOfFailingSilently()
        {
            var (controller, _, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            WorkbenchLever lever = AttachLever(controller);
            controller.ConfigureGolem(null);

            EngageViaButton(controller);

            // Previously an unconditional early return: no status at all, while the lever
            // (wired straight to onClick) pulled anyway.
            Assert.AreEqual(WorkbenchStatusReason.NoTarget, controller.StatusReason);
            Assert.IsNotEmpty(StatusLabel(controller).text);
            Assert.IsTrue(lever.IsRefusing);
        }

        [UnityTest]
        public IEnumerator EngageButton_GoesNonInteractableWhenFocusCannotCoverTheCost()
        {
            var (controller, _, focus, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            Button engage = FindSibling(controller, "Engage").GetComponent<Button>();
            Assert.IsTrue(engage.interactable, "a full Focus meter should leave the lever live");

            while (focus.Meter.TryConsume(10f)) { }
            yield return null;

            Assert.IsFalse(engage.interactable,
                "unaffordability used to be discoverable only by clicking and hunting for a message");
        }

        // ---------------------------------------------------------------------------
        // Status line retires itself when the condition it describes resolves.
        // ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Status_InsufficientFocus_RetiresItselfOnceFocusRegenerates()
        {
            var (controller, _, focus, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            yield return null;
            while (focus.Meter.TryConsume(10f)) { }
            EngageViaButton(controller);
            Assert.AreEqual(WorkbenchStatusReason.InsufficientFocusEngage, controller.StatusReason);

            focus.Meter.Refund(100f);
            yield return null;

            Assert.AreEqual(WorkbenchStatusReason.None, controller.StatusReason,
                "'Not enough Focus' used to stay on screen while the tape ticker read FOCUS 42/100");
            Assert.IsEmpty(StatusLabel(controller).text);
        }

        [UnityTest]
        public IEnumerator Status_ChassisTooSmall_RetiresItselfOnceTheAppendagesAreRemoved()
        {
            ChassisDefinition bigChassis = MakeChassis(3);
            ChassisDefinition smallChassis = MakeChassis(1);
            AppendageActionDefinition a1 = MakeAppendage();
            AppendageActionDefinition a2 = MakeAppendage();
            var (controller, _, _, _) = Build(new[] { bigChassis, smallChassis }, new LogicCoreDefinition[0], new[] { a1, a2 });
            yield return null;
            SelectChassisViaButton(controller, 0);
            controller.HandleDrop(VaultCard(null, a1), MakeZone(DropZoneKind.Appendage, 0));
            controller.HandleDrop(VaultCard(null, a2), MakeZone(DropZoneKind.Appendage, 1));

            SelectChassisViaButton(controller, 1); // rejected: 2 appendages don't fit 1 slot
            Assert.AreEqual(WorkbenchStatusReason.ChassisTooSmall, controller.StatusReason);
            yield return null;
            Assert.AreEqual(WorkbenchStatusReason.ChassisTooSmall, controller.StatusReason,
                "the message must stand while it is still true");

            controller.HandleDrop(SlotCard(a1, 0), null);
            controller.HandleDrop(SlotCard(a2, 1), null);
            yield return null;

            Assert.AreEqual(WorkbenchStatusReason.None, controller.StatusReason,
                "'remove appendages to fit its slot count first' used to survive removing every appendage");
        }

        // ---------------------------------------------------------------------------
        // Default-state coherence.
        // ---------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Open_ReloadsTheDraftFromTheTargetsCommittedProgram()
        {
            // Reproduces the Start()-order race against the demo bootstraps, which
            // populate a golem's program in their own Start(): the Workbench could open
            // showing a draft that had nothing to do with the golem it pointed at.
            ChassisDefinition chassis = MakeChassis(2);
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { appendage });
            yield return null;

            golem.Program.TryAssignChassis(chassis);
            golem.Program.TryAddAppendage(appendage);

            controller.Open();
            EngageViaButton(controller);

            Assert.AreEqual(chassis, golem.Program.chassis);
            Assert.AreEqual(1, golem.Program.appendages.Count,
                "opening the screen must re-read the target's program, not commit a stale empty draft over it");
        }

        [UnityTest]
        public IEnumerator RetargetGolem_ToAGolemWithFewerAppendages_DropsTheOldGolemsSteps()
        {
            ChassisDefinition chassis = MakeChassis(3);
            AppendageActionDefinition a1 = MakeAppendage();
            AppendageActionDefinition a2 = MakeAppendage();
            var (controller, golemA, _, _) = Build(new[] { chassis }, new LogicCoreDefinition[0], new[] { a1, a2 });
            yield return null;
            SelectChassisViaButton(controller, 0);
            controller.HandleDrop(VaultCard(null, a1), MakeZone(DropZoneKind.Appendage, 0));
            controller.HandleDrop(VaultCard(null, a2), MakeZone(DropZoneKind.Appendage, 1));
            EngageViaButton(controller);
            Assert.AreEqual(2, golemA.Program.appendages.Count);

            var golemB = new GameObject("GolemB").AddComponent<GolemEntity>();
            golemB.transform.SetParent(_root.transform);
            golemB.Configure("GolemB", null);
            golemB.Program.TryAssignChassis(chassis);
            golemB.Program.TryAddAppendage(a1);

            controller.RetargetGolem(golemB);
            EngageViaButton(controller);

            // The draft used to only overwrite the indices the incoming program filled, so
            // golem A's second step leaked onto golem B.
            Assert.AreEqual(1, golemB.Program.appendages.Count);
        }

        [UnityTest]
        public IEnumerator StepsWithNoChassis_ViewportDrawsThemAndTheTapeDoesNotClaimOneOverZero()
        {
            // Exactly Main.unity's old demo-golem state: steps appended straight onto the
            // list, past GolemProgram.TryAddAppendage's no-chassis guard.
            AppendageActionDefinition appendage = MakeAppendage();
            var (controller, golem, _, _) = Build(new ChassisDefinition[0], new LogicCoreDefinition[0], new[] { appendage });
            yield return null;
            golem.Program.appendages.Add(appendage);

            controller.Open();
            yield return null;

            Transform slotZero = FindSibling(controller, "AppendageSlot0");
            Assert.IsTrue(slotZero.gameObject.activeSelf,
                "the tape counts this step, so the viewport must not refuse to draw it");
            Assert.IsNotNull(slotZero.GetComponentInChildren<WorkbenchCard>(true),
                "the unfitted step must be visible (and draggable back out), not silently hidden");

            string tape = FindSibling(controller, "Ticker").GetComponent<TextMeshProUGUI>().text;
            StringAssert.DoesNotContain("SLOTS 1/0", tape);
        }

        // ---------------------------------------------------------------------------

        private static PointerEventData Pointer(Vector2 position, GameObject raycastHit)
        {
            var data = new PointerEventData(EventSystem.current);
            data.position = position;
            var raycast = new RaycastResult();
            raycast.gameObject = raycastHit;
            data.pointerCurrentRaycast = raycast;
            return data;
        }

        private static WorkbenchCard FirstVaultCard(WorkbenchController controller)
        {
            Transform vault = FindSibling(controller, "Vault");
            for (int i = 0; i < vault.childCount; i++)
            {
                WorkbenchCard card = vault.GetChild(i).GetComponent<WorkbenchCard>();
                if (card != null)
                {
                    return card;
                }
            }

            return null;
        }

        private static WorkbenchDropZone AppendageZone(WorkbenchController controller, int index) =>
            FindSibling(controller, $"AppendageSlot{index}").GetComponent<WorkbenchDropZone>();

        private static WorkbenchDropZone LogicZone(WorkbenchController controller) =>
            FindSibling(controller, "LogicSlot").GetComponent<WorkbenchDropZone>();

        private static TextMeshProUGUI StatusLabel(WorkbenchController controller) =>
            FindSibling(controller, "Status").GetComponent<TextMeshProUGUI>();

        private WorkbenchLever AttachLever(WorkbenchController controller)
        {
            var go = new GameObject("Lever", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_root.transform, false);
            var handle = new GameObject("Handle", typeof(RectTransform)).GetComponent<RectTransform>();
            handle.transform.SetParent(go.transform, false);

            WorkbenchLever lever = go.AddComponent<WorkbenchLever>();
            lever.ConfigureHandle(handle, 74f);
            controller.ConfigureLever(lever);
            return lever;
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
