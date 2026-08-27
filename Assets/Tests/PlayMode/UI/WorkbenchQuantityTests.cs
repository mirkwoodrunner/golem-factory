using System.Collections;
using NUnit.Framework;
using UnityEngine;
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
    /// <summary>
    /// Per-slot batch size, end to end through the Workbench -- §2's "Consequence 4".
    ///
    /// <para>
    /// It was stored on <c>GolemProgram.appendageQuantities</c> and saved from the moment the
    /// machine model landed, and nothing could set it. Worse than merely absent: because
    /// <c>EngageGears</c> rebuilds a program with <c>TryAddAppendage</c>, and that seeds each slot
    /// from the CARD's authored default, every reprogram silently reset every batch size in the
    /// golem. So the one value a save round-trip carefully preserved was one the game itself
    /// overwrote the next time the player touched the lever.
    /// </para>
    ///
    /// <para>
    /// Same rig and same reasoning as <c>WorkbenchControllerTests</c>: the controller's decisions
    /// are exercised directly rather than through simulated pointer drags, and it is PlayMode
    /// because <c>Start()</c> does not run outside it.
    /// </para>
    /// </summary>
    public class WorkbenchQuantityTests
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

        private (WorkbenchController controller, GolemEntity golem) Build(
            ChassisDefinition[] chassisRoster, AppendageActionDefinition[] appendageRoster)
        {
            _root = new GameObject("Root");

            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", null);

            var patents = new GameObject("Patents").AddComponent<PatentRegistryHolder>();
            patents.transform.SetParent(_root.transform);

            RectTransform vault = NewRect("Vault", _root.transform);
            RectTransform chassisRow = NewRect("ChassisRow", _root.transform);
            RectTransform dragLayer = NewRect("DragLayer", _root.transform);

            var logicSlotGo = new GameObject("LogicSlot", typeof(RectTransform));
            logicSlotGo.transform.SetParent(_root.transform, false);
            var logicSlot = logicSlotGo.AddComponent<WorkbenchDropZone>();
            logicSlot.Configure(DropZoneKind.LogicCore, -1);

            var zones = new WorkbenchDropZone[3];
            for (int i = 0; i < zones.Length; i++)
            {
                var zoneGo = new GameObject($"AppendageSlot{i}", typeof(RectTransform));
                zoneGo.transform.SetParent(_root.transform, false);
                WorkbenchDropZone zone = zoneGo.AddComponent<WorkbenchDropZone>();
                zone.Configure(DropZoneKind.Appendage, i);
                zones[i] = zone;
            }

            var ticker = new GameObject("Ticker", typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            ticker.transform.SetParent(_root.transform, false);
            var status = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            status.transform.SetParent(_root.transform, false);
            var engage = new GameObject("Engage", typeof(RectTransform), typeof(Image), typeof(Button))
                .GetComponent<Button>();
            engage.transform.SetParent(_root.transform, false);
            var patent = new GameObject("Patent", typeof(RectTransform), typeof(Image), typeof(Button))
                .GetComponent<Button>();
            patent.transform.SetParent(_root.transform, false);

            var controller = new GameObject("Controller").AddComponent<WorkbenchController>();
            controller.transform.SetParent(_root.transform);
            controller.ConfigureGolem(golem);
            controller.ConfigureSystems(patents);
            controller.ConfigureRoster(chassisRoster, new LogicCoreDefinition[0], appendageRoster);
            controller.ConfigureUI(vault, chassisRow, dragLayer, logicSlot, zones, ticker, status, engage, patent);

            return (controller, golem);
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

        private static AppendageActionDefinition MakeHaul(int authoredDefault)
        {
            var card = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            card.actionType = AppendageActionType.Haul;
            card.haulQuantity = authoredDefault;
            return card;
        }

        private static WorkbenchCard VaultCard(AppendageActionDefinition appendage) =>
            MakeCard(appendage, isVaultOrigin: true, sourceIndex: -1);

        private static WorkbenchCard SlotCard(AppendageActionDefinition appendage, int sourceIndex) =>
            MakeCard(appendage, isVaultOrigin: false, sourceIndex: sourceIndex);

        private static WorkbenchCard MakeCard(
            AppendageActionDefinition appendage, bool isVaultOrigin, int sourceIndex)
        {
            var card = new GameObject("Card").AddComponent<WorkbenchCard>();
            card.Appendage = appendage;
            card.IsVaultOrigin = isVaultOrigin;
            card.SourceAppendageIndex = sourceIndex;
            return card;
        }

        private static WorkbenchDropZone MakeZone(int appendageIndex)
        {
            WorkbenchDropZone zone = new GameObject("Zone", typeof(RectTransform))
                .AddComponent<WorkbenchDropZone>();
            zone.Configure(DropZoneKind.Appendage, appendageIndex);
            return zone;
        }

        private static void Engage(WorkbenchController controller) =>
            controller.transform.parent.Find("Engage").GetComponent<Button>().onClick.Invoke();

        private static void SelectChassis(WorkbenchController controller) =>
            controller.transform.parent.Find("ChassisRow").GetChild(0).GetComponent<Button>().onClick.Invoke();

        // THE HEADLINE: a batch size the player sets actually reaches the golem.
        [UnityTest]
        public IEnumerator EngageGears_CommitsTheDraftedBatchSizeOntoTheGolem()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 1);
            var (controller, golem) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(haul), MakeZone(0));
            controller.AdjustDraftQuantity(0, +3);
            Assert.AreEqual(4, controller.DraftQuantityAt(0), "precondition: the draft moved");

            Engage(controller);

            Assert.AreEqual(4, golem.Program.GetQuantityAt(0),
                "the batch size the player chose never reached the golem");
        }

        // THE REGRESSION THAT MADE THE STORED VALUE POINTLESS: TryAddAppendage seeds each slot
        // from the card's authored default, so before this every Engage quietly reset every batch
        // size in the program -- including one restored from a save moments earlier.
        [UnityTest]
        public IEnumerator EngageGears_DoesNotResetBatchSizesBackToTheCardDefaults()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 1);
            var (controller, golem) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(haul), MakeZone(0));
            controller.AdjustDraftQuantity(0, +7);
            Engage(controller);
            Assert.AreEqual(8, golem.Program.GetQuantityAt(0));

            // Pull the lever again without touching the dial. Nothing about reprogramming a golem
            // should retune it.
            Engage(controller);

            Assert.AreEqual(8, golem.Program.GetQuantityAt(0),
                "a second Engage reset the player's batch size to the card's authored default");
        }

        // Opening the screen on a golem the player has already tuned must show what they set --
        // otherwise the Workbench proposes undoing their work every time they look at it.
        [UnityTest]
        public IEnumerator RetargetGolem_LoadsTheGolemsCurrentBatchSizesNotTheCardDefaults()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 1);
            var (controller, golem) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;

            golem.Program.TryAssignChassis(MakeChassis(3));
            golem.Program.TryAddAppendage(haul);
            golem.Program.SetQuantityAt(0, 6);

            controller.RetargetGolem(golem);

            Assert.AreEqual(6, controller.DraftQuantityAt(0));
        }

        [UnityTest]
        public IEnumerator ACardFromTheVault_StartsAtItsOwnAuthoredDefault()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 5);
            var (controller, _) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(haul), MakeZone(0));

            Assert.AreEqual(5, controller.DraftQuantityAt(0),
                "a fresh card has no history, so the card's own default is the right starting point");
        }

        // Moving step 3 up to step 2 is a REORDER, not a retune.
        [UnityTest]
        public IEnumerator ACardMovedBetweenSockets_CarriesItsBatchSizeWithIt()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 1);
            var (controller, _) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(haul), MakeZone(0));
            controller.AdjustDraftQuantity(0, +5);

            controller.HandleDrop(SlotCard(haul, sourceIndex: 0), MakeZone(2));

            Assert.AreEqual(6, controller.DraftQuantityAt(2), "the batch size did not follow the card");
            Assert.AreEqual(WorkbenchQuantityPolicy.MinQuantity, controller.DraftQuantityAt(0),
                "the vacated socket must not keep the old size for whatever lands there next");
        }

        [UnityTest]
        public IEnumerator RemovingACard_ClearsThatSlotsBatchSize()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 1);
            var (controller, _) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(haul), MakeZone(0));
            controller.AdjustDraftQuantity(0, +4);
            controller.RemoveFromSlot(SlotCard(haul, sourceIndex: 0));

            Assert.AreEqual(WorkbenchQuantityPolicy.MinQuantity, controller.DraftQuantityAt(0));
        }

        // Nothing reaches the golem until the lever is pulled -- the same contract dragging a
        // card already honours, so trying 8 and putting it back costs nothing at all.
        [UnityTest]
        public IEnumerator AdjustingTheDial_DoesNotTouchTheGolemUntilTheLeverIsPulled()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 1);
            var (controller, golem) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;
            SelectChassis(controller);

            golem.Program.TryAssignChassis(MakeChassis(3));
            golem.Program.TryAddAppendage(haul);
            golem.Program.SetQuantityAt(0, 2);

            controller.RetargetGolem(golem);
            controller.AdjustDraftQuantity(0, +6);

            Assert.AreEqual(2, golem.Program.GetQuantityAt(0),
                "the dial edited the live program instead of the draft");
        }

        // STRUCTURAL, because everything above would pass with no control on screen at all.
        // This session has twice found UI that was present, correct and invisible; asserting the
        // widget exists is the cheap half of not doing it a third time. (What it LOOKS like still
        // needs a human in front of the game.)
        [UnityTest]
        public IEnumerator AHaulCardInASlot_GetsAStepperAndAnAssembleCardDoesNot()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 2);
            var assemble = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            assemble.actionType = AppendageActionType.Assemble;

            var (controller, _) = Build(new[] { MakeChassis(3) }, new[] { haul, assemble });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(haul), MakeZone(0));
            controller.HandleDrop(VaultCard(assemble), MakeZone(1));

            Assert.IsNotNull(FindStepper(controller, slotIndex: 0),
                "a Haul slot must carry a batch-size control");
            Assert.IsNull(FindStepper(controller, slotIndex: 1),
                "an Assemble slot must not -- its quantity changes nothing, so a dial there is a lie");
        }

        // The vault is a palette, not a program: a card sitting in it has no slot to have a batch
        // size for, and a stepper on 23 vault rows would be 23 controls that commit nothing.
        [UnityTest]
        public IEnumerator VaultCards_HaveNoStepper()
        {
            AppendageActionDefinition haul = MakeHaul(authoredDefault: 2);
            var (controller, _) = Build(new[] { MakeChassis(3) }, new[] { haul });
            yield return null;

            Transform vault = controller.transform.parent.Find("Vault");
            foreach (Transform child in vault)
            {
                Assert.IsNull(child.Find("Quantity"),
                    "a vault card must not offer a batch size it cannot commit");
            }
        }

        private static Transform FindStepper(WorkbenchController controller, int slotIndex)
        {
            Transform slot = controller.transform.parent.Find($"AppendageSlot{slotIndex}");
            foreach (Transform card in slot)
            {
                Transform stepper = card.Find("Quantity");
                if (stepper != null)
                {
                    return stepper;
                }
            }

            return null;
        }

        // A program packs its appendages, so a gap in the sockets must not misalign the sizes.
        [UnityTest]
        public IEnumerator WithAnEmptySocketBetweenTwoCards_EachBatchSizeLandsOnItsOwnStep()
        {
            AppendageActionDefinition first = MakeHaul(authoredDefault: 1);
            AppendageActionDefinition second = MakeHaul(authoredDefault: 1);
            var (controller, golem) = Build(new[] { MakeChassis(3) }, new[] { first, second });
            yield return null;
            SelectChassis(controller);

            controller.HandleDrop(VaultCard(first), MakeZone(0));
            controller.AdjustDraftQuantity(0, +2);   // 3
            controller.HandleDrop(VaultCard(second), MakeZone(2));
            controller.AdjustDraftQuantity(2, +8);   // 9

            Engage(controller);

            Assert.AreEqual(2, golem.Program.appendages.Count);
            Assert.AreEqual(3, golem.Program.GetQuantityAt(0));
            Assert.AreEqual(9, golem.Program.GetQuantityAt(1),
                "socket 2's size landed on the wrong step -- the program packs, the sockets do not");
        }
    }
}
