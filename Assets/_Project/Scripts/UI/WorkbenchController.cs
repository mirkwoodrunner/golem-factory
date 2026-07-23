using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // M8's "full Workbench UI": a mahogany-and-brass blueprint viewport (the logic-core
    // and appendage slots) plus a Card Vault of draggable teal (Logic Core) / copper
    // (Appendage) cards, per digital-design.md. Supersedes M3's GolemProgrammingPanel
    // (OnGUI, apply-immediately) with a real UGUI drag-and-drop staging workflow:
    // dragging cards only edits a local *draft* copy of the program; nothing touches the
    // real GolemEntity.Program until the "Engage Gears" lever (EngageGears()) commits it,
    // gated by ArtificerFocusMeter -- matching "pulling it locks in the current card
    // configuration and boots the golem into the game world."
    // Chassis selection stays button-based (not a draggable card) since the design doc's
    // card color coding only covers Logic Cores/Appendages.
    public sealed class WorkbenchController : MonoBehaviour
    {
        private static readonly Color TealColor = new Color(0.25f, 0.55f, 0.55f);
        private static readonly Color CopperColor = new Color(0.72f, 0.45f, 0.2f);
        private static readonly Color SelectedChassisColor = new Color(0.9f, 0.8f, 0.4f);
        private static readonly Color UnselectedChassisColor = new Color(0.45f, 0.45f, 0.45f);

        [SerializeField] private GolemEntity targetGolem;
        [SerializeField] private ArtificerFocusMeterHolder focusMeterHolder;
        [SerializeField] private PatentRegistryHolder patentRegistryHolder;
        [SerializeField] private ChassisDefinition[] availableChassis = new ChassisDefinition[0];
        [SerializeField] private LogicCoreDefinition[] availableLogicCores = new LogicCoreDefinition[0];
        [SerializeField] private AppendageActionDefinition[] availableAppendages = new AppendageActionDefinition[0];

        [SerializeField] private RectTransform vaultContent;
        [SerializeField] private RectTransform chassisButtonRow;
        [SerializeField] private RectTransform dragLayer;
        [SerializeField] private WorkbenchDropZone logicCoreSlotZone;
        [SerializeField] private WorkbenchDropZone[] appendageSlotZones = new WorkbenchDropZone[0];
        [SerializeField] private Text tapeTickerText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button engageGearsButton;
        [SerializeField] private Button patentButton;

        [SerializeField] private float reprogramFocusCost = 10f;
        [SerializeField] private float patentFocusCost = 20f;

        private ChassisDefinition _draftChassis;
        private LogicCoreDefinition _draftLogicCore;
        private AppendageActionDefinition[] _draftAppendages = new AppendageActionDefinition[0];
        private readonly Dictionary<ChassisDefinition, Image> _chassisButtonImages = new Dictionary<ChassisDefinition, Image>();
        private int _nextBlueprintNumber = 1;

        // Test/bootstrap-friendly setup, split into logical groups mirroring
        // GolemEntity.Configure/ConfigureEconomy -- avoids requiring Inspector-assigned
        // references for every one of this component's many fields.
        public void ConfigureGolem(GolemEntity golem) => targetGolem = golem;

        public void ConfigureSystems(ArtificerFocusMeterHolder focus, PatentRegistryHolder patents)
        {
            focusMeterHolder = focus;
            patentRegistryHolder = patents;
        }

        public void ConfigureRoster(
            ChassisDefinition[] chassisRoster, LogicCoreDefinition[] logicCoreRoster, AppendageActionDefinition[] appendageRoster)
        {
            availableChassis = chassisRoster ?? new ChassisDefinition[0];
            availableLogicCores = logicCoreRoster ?? new LogicCoreDefinition[0];
            availableAppendages = appendageRoster ?? new AppendageActionDefinition[0];
        }

        public void ConfigureUI(
            RectTransform vault, RectTransform chassisRow, RectTransform drag,
            WorkbenchDropZone logicSlot, WorkbenchDropZone[] appendageSlots,
            Text tapeTicker, Text status, Button engageButton, Button patentBtn)
        {
            vaultContent = vault;
            chassisButtonRow = chassisRow;
            dragLayer = drag;
            logicCoreSlotZone = logicSlot;
            appendageSlotZones = appendageSlots ?? new WorkbenchDropZone[0];
            tapeTickerText = tapeTicker;
            statusText = status;
            engageGearsButton = engageButton;
            patentButton = patentBtn;
        }

        private void Start()
        {
            _draftAppendages = new AppendageActionDefinition[appendageSlotZones.Length];
            LoadDraftFromGolem();
            BuildChassisButtons();

            if (engageGearsButton != null)
            {
                engageGearsButton.onClick.AddListener(EngageGears);
            }
            if (patentButton != null)
            {
                patentButton.onClick.AddListener(Patent);
            }

            RebuildUI();
        }

        private void Update()
        {
            UpdateTapeTicker();
        }

        private void LoadDraftFromGolem()
        {
            if (targetGolem == null)
            {
                return;
            }

            GolemProgram program = targetGolem.Program;
            _draftChassis = program.chassis;
            _draftLogicCore = program.logicCore;
            for (int i = 0; i < program.appendages.Count && i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = program.appendages[i];
            }
        }

        private bool SlotActive(int appendageIndex) =>
            _draftChassis != null && appendageIndex >= 0 && appendageIndex < _draftChassis.maxAppendageSlots;

        // Called by WorkbenchCard.OnEndDrag. zone is null when the card was dropped
        // somewhere that isn't a valid drop zone.
        public void HandleDrop(WorkbenchCard card, WorkbenchDropZone zone)
        {
            if (zone == null)
            {
                if (!card.IsVaultOrigin)
                {
                    if (card.LogicCore != null)
                    {
                        _draftLogicCore = null;
                    }
                    else if (card.SourceAppendageIndex >= 0)
                    {
                        _draftAppendages[card.SourceAppendageIndex] = null;
                    }
                }
                // Vault-origin card dropped nowhere valid: cancel, nothing to undo.
            }
            else if (zone.Kind == DropZoneKind.LogicCore && card.LogicCore != null)
            {
                _draftLogicCore = card.LogicCore;
            }
            else if (zone.Kind == DropZoneKind.Appendage && card.Appendage != null && SlotActive(zone.AppendageIndex))
            {
                int targetIndex = zone.AppendageIndex;
                if (!card.IsVaultOrigin && card.SourceAppendageIndex >= 0 && card.SourceAppendageIndex != targetIndex)
                {
                    _draftAppendages[card.SourceAppendageIndex] = null;
                }
                _draftAppendages[targetIndex] = card.Appendage;
            }
            // Any other combination (wrong card kind for the zone, or an inactive
            // appendage slot beyond the current chassis's capacity) is a no-op: the card
            // just snaps back to where it was once RebuildUI regenerates everything.

            RebuildUI();
        }

        public void RemoveFromSlot(WorkbenchCard card) => HandleDrop(card, null);

        private void EngageGears()
        {
            if (targetGolem == null)
            {
                return;
            }

            if (!focusMeterHolder.Meter.TryConsume(reprogramFocusCost))
            {
                SetStatus($"Not enough Focus to reprogram (need {reprogramFocusCost:F0}).");
                return;
            }

            GolemProgram program = targetGolem.Program;
            while (program.appendages.Count > 0)
            {
                program.RemoveAppendageAt(0);
            }

            if (_draftChassis != null && !program.TryAssignChassis(_draftChassis))
            {
                // Shouldn't happen -- the draft's own appendage count is already gated to
                // fit _draftChassis via SlotActive -- but refund and report if it does.
                focusMeterHolder.Meter.Refund(reprogramFocusCost);
                SetStatus("Cannot engage: chassis rejected the current appendage count.");
                return;
            }

            foreach (AppendageActionDefinition appendage in _draftAppendages)
            {
                if (appendage != null)
                {
                    program.TryAddAppendage(appendage);
                }
            }

            program.logicCore = _draftLogicCore;
            program.CurrentStepIndex = 0;
            program.StepProgressTicks = 0;
            program.State = GolemState.Idle;

            SetStatus("Engaged! New configuration is live.");
        }

        private void Patent()
        {
            if (!focusMeterHolder.Meter.TryConsume(patentFocusCost))
            {
                SetStatus($"Not enough Focus to patent (need {patentFocusCost:F0}).");
                return;
            }

            string blueprintId = $"BP-{_nextBlueprintNumber:D3}";
            _nextBlueprintNumber++;

            var appendages = _draftAppendages.Where(a => a != null).ToList();
            var blueprint = new Blueprint(blueprintId, PlaceableBuilding.LocalPlayerOwnerId, _draftChassis, _draftLogicCore, appendages);
            patentRegistryHolder.Registry.TryPatent(blueprint);

            SetStatus($"Patented as {blueprintId}.");
        }

        // M9: the other half of Patent() -- loads a previously-patented blueprint back
        // into the draft (called by UI/PatentBrowserPanel's "Load" button). Like every
        // other draft mutation, this doesn't touch the real GolemEntity.Program; the
        // loaded config still has to go through Engage Gears (and its Focus cost) to
        // take effect, same as a manually-dragged configuration would.
        public void LoadBlueprintIntoDraft(Blueprint blueprint)
        {
            if (blueprint == null)
            {
                return;
            }

            _draftChassis = blueprint.Chassis;
            _draftLogicCore = blueprint.LogicCore;
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = i < blueprint.Appendages.Count ? blueprint.Appendages[i] : null;
            }

            SetStatus($"Loaded {blueprint.BlueprintId} into the draft.");
            RebuildUI();
        }

        private void SelectChassis(ChassisDefinition chassis)
        {
            int assignedAppendages = _draftAppendages.Count(a => a != null);
            if (chassis != null && assignedAppendages > chassis.maxAppendageSlots)
            {
                SetStatus("Cannot switch chassis: remove appendages to fit its slot count first.");
                return;
            }

            _draftChassis = chassis;
            SetStatus(string.Empty);
            RebuildUI();
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void UpdateTapeTicker()
        {
            if (tapeTickerText == null)
            {
                return;
            }

            int cycleTicks = _draftAppendages.Where(a => a != null).Sum(a => Mathf.Max(1, a.durationTicks));
            int stepCount = _draftAppendages.Count(a => a != null);
            float focus = focusMeterHolder != null ? focusMeterHolder.Meter.CurrentFocus : 0f;
            float maxFocus = focusMeterHolder != null ? focusMeterHolder.Meter.MaxFocus : 0f;

            tapeTickerText.text =
                $"Cycle: {cycleTicks} ticks | Steps: {stepCount} | Focus: {focus:F0}/{maxFocus:F0}";
        }

        private void BuildChassisButtons()
        {
            if (chassisButtonRow == null)
            {
                return;
            }

            foreach (ChassisDefinition chassis in availableChassis)
            {
                var go = new GameObject(chassis.name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(chassisButtonRow, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(170f, 28f);

                Image image = go.GetComponent<Image>();
                _chassisButtonImages[chassis] = image;

                ChassisDefinition captured = chassis;
                go.GetComponent<Button>().onClick.AddListener(() => SelectChassis(captured));

                CreateLabel(go.transform, $"{chassis.name} ({chassis.maxAppendageSlots} slots)");
            }
        }

        private void RebuildUI()
        {
            ClearChildren(vaultContent);
            ClearChildren(logicCoreSlotZone != null ? logicCoreSlotZone.transform : null);
            foreach (WorkbenchDropZone zone in appendageSlotZones)
            {
                if (zone != null)
                {
                    ClearChildren(zone.transform);
                }
            }

            foreach (LogicCoreDefinition logicCore in availableLogicCores)
            {
                CreateCard(vaultContent, logicCore, null, isVaultOrigin: true, sourceAppendageIndex: -1);
            }
            foreach (AppendageActionDefinition appendage in availableAppendages)
            {
                CreateCard(vaultContent, null, appendage, isVaultOrigin: true, sourceAppendageIndex: -1);
            }

            if (_draftLogicCore != null && logicCoreSlotZone != null)
            {
                CreateCard(logicCoreSlotZone.transform, _draftLogicCore, null, isVaultOrigin: false, sourceAppendageIndex: -1);
            }

            for (int i = 0; i < appendageSlotZones.Length; i++)
            {
                WorkbenchDropZone zone = appendageSlotZones[i];
                if (zone == null)
                {
                    continue;
                }

                bool active = SlotActive(i);
                zone.gameObject.SetActive(active);
                if (active && _draftAppendages[i] != null)
                {
                    CreateCard(zone.transform, null, _draftAppendages[i], isVaultOrigin: false, sourceAppendageIndex: i);
                }
            }

            foreach (var entry in _chassisButtonImages)
            {
                entry.Value.color = entry.Key == _draftChassis ? SelectedChassisColor : UnselectedChassisColor;
            }

            UpdateTapeTicker();
        }

        private void CreateCard(
            Transform parent, LogicCoreDefinition logicCore, AppendageActionDefinition appendage,
            bool isVaultOrigin, int sourceAppendageIndex)
        {
            if (parent == null)
            {
                return;
            }

            string cardName = logicCore != null ? logicCore.name : appendage.name;
            var go = new GameObject(cardName, typeof(RectTransform), typeof(Image), typeof(WorkbenchCard));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(150f, 36f);
            go.GetComponent<Image>().color = logicCore != null ? TealColor : CopperColor;

            CreateLabel(go.transform, cardName);

            WorkbenchCard card = go.GetComponent<WorkbenchCard>();
            card.LogicCore = logicCore;
            card.Appendage = appendage;
            card.IsVaultOrigin = isVaultOrigin;
            card.SourceAppendageIndex = sourceAppendageIndex;
            card.Init(this, dragLayer);
        }

        private static void CreateLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text label = go.GetComponent<Text>();
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 13;
            label.raycastTarget = false;
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
