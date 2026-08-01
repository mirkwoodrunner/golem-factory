using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // M8's "full Workbench UI": a mahogany-and-brass blueprint viewport (the selected
    // chassis's portrait plus its logic-core and appendage slots) alongside a Card Vault
    // of draggable teal (Logic Core) / copper (Appendage) cards, per digital-design.md.
    // Supersedes M3's GolemProgrammingPanel (OnGUI, apply-immediately) with a real UGUI
    // drag-and-drop staging workflow: dragging cards only edits a local *draft* copy of
    // the program; nothing touches the real GolemEntity.Program until the "Engage Gears"
    // lever (EngageGears()) commits it, gated by ArtificerFocusMeter -- matching "pulling
    // it locks in the current card configuration and boots the golem into the game world."
    // Chassis selection stays button-based (not a draggable card) since the design doc's
    // card color coding only covers Logic Cores/Appendages.
    public sealed class WorkbenchController : MonoBehaviour
    {
        // Teal = Logic Cores (trigger), copper = Appendages (action), straight from
        // digital-design.md. These are deliberately brighter than the original flat-color
        // pass: they now tint a near-white card *sprite* rather than being the whole card,
        // and dark ink on a light card is what makes the card names legible at a glance.
        // The semantic mapping is unchanged.
        private static readonly Color TealColor = new Color(0.42f, 0.80f, 0.75f);
        private static readonly Color CopperColor = new Color(0.88f, 0.58f, 0.34f);
        private static readonly Color CardInkColor = new Color(0.10f, 0.08f, 0.06f);
        private static readonly Color CardSubInkColor = new Color(0.24f, 0.19f, 0.14f);
        // The chassis rack keeps cream lettering in both states so the label never has to
        // be recolored alongside the plate: an unselected plate is dark enough for cream
        // to read, a selected one is a hot brass-orange that still is. (The first pass at
        // this used near-black ink on a mid-brown plate and was unreadable in Play mode.)
        private static readonly Color SelectedChassisColor = new Color(0.95f, 0.62f, 0.20f);
        private static readonly Color UnselectedChassisColor = new Color(0.40f, 0.36f, 0.33f);
        private static readonly Color ChassisInkColor = new Color(0.98f, 0.94f, 0.86f);
        private static readonly Color ChassisSubInkColor = new Color(0.84f, 0.77f, 0.65f);
        private static readonly Color VaultHeadingColor = new Color(0.86f, 0.70f, 0.40f);

        private const float CardHeight = 44f;
        private const float CardWidth = 250f;
        private const float ChassisButtonHeight = 52f;
        private const float VaultHeadingHeight = 26f;

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
        [SerializeField] private TextMeshProUGUI tapeTickerText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button engageGearsButton;
        [SerializeField] private Button patentButton;

        // The blueprint viewport's chassis readout: the design doc's "blueprint viewport of
        // the selected golem's chassis". Driven from ChassisDefinition.chassisSprite, the
        // same data-driven source GolemVisual.RefreshSpriteFromChassis uses, so the
        // Workbench and the world always show the same art for a chassis.
        [SerializeField] private Image chassisPortrait;
        [SerializeField] private TextMeshProUGUI chassisNameText;
        [SerializeField] private TextMeshProUGUI chassisStatsText;
        [SerializeField] private TextMeshProUGUI targetGolemText;

        // Player-driven HUD wiring: canvasRoot is the WorkbenchScreen wrapper this
        // controller shows/hides on Open()/Close(). Left unset, Open()/Close() only
        // toggle IsOpen's bookkeeping -- existing scenes/tests that never wire this
        // (e.g. WorkbenchControllerTests.Build()) keep the Workbench always-visible,
        // exactly as before this change.
        [SerializeField] private GameObject canvasRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private ManagementPanel managementPanel;
        [SerializeField] private GolemConstructionPanel constructionPanel;

        // Always-on HUD chrome that has no Open/Close of its own (Sandbox's BuildMenuPanel)
        // but would otherwise draw on top of this full-screen modal. Kept as a generic
        // GameObject list owned by the Workbench rather than teaching each panel about the
        // Workbench: the modality is this screen's concern, not theirs. Unwired, it's a
        // no-op.
        [SerializeField] private GameObject[] hideWhileOpen = new GameObject[0];

        [SerializeField] private float reprogramFocusCost = 10f;
        [SerializeField] private float patentFocusCost = 20f;

        // Only used to turn a cycle length in ticks into the "cycles per minute" figure on
        // the diagnostic tape. Mirrors SimulationClock's default rate; the Workbench is a
        // read-only observer here and deliberately doesn't take a dependency on the clock
        // just to render one number.
        [SerializeField] private float ticksPerSecondForReadout = 2f;

        // Reskin sprites for GameObjects built at runtime (chassis buttons/vault cards
        // are instantiated per-entry, not baked into the prefab, so they can't just get a
        // sprite assigned in the Inspector the way the static Workbench chrome can).
        // Left unset, BuildChassisButtons()/CreateCard() fall back to their original flat
        // Image.color-only look -- no behavior change for anything that doesn't wire these.
        [SerializeField] private Sprite chassisButtonSprite;
        [SerializeField] private Sprite vaultCardSprite;

        // Test/bootstrap-friendly setup for the two runtime-instantiated sprite skins,
        // same Configure* idiom as the rest of this class.
        public void ConfigureSprites(Sprite chassisButton, Sprite vaultCard)
        {
            chassisButtonSprite = chassisButton;
            vaultCardSprite = vaultCard;
        }

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
            TextMeshProUGUI tapeTicker, TextMeshProUGUI status, Button engageButton, Button patentBtn)
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

        // The blueprint viewport's readout widgets, kept out of ConfigureUI so the existing
        // nine-argument call sites (tests, bootstrap) don't have to change to opt in --
        // every one of these is null-tolerant.
        public void ConfigureBlueprintPane(
            Image portrait, TextMeshProUGUI chassisName, TextMeshProUGUI chassisStats, TextMeshProUGUI targetLabel)
        {
            chassisPortrait = portrait;
            chassisNameText = chassisName;
            chassisStatsText = chassisStats;
            targetGolemText = targetLabel;
        }

        // Test/bootstrap-friendly setup for the show/hide wiring, same Configure*
        // idiom as the rest of this class.
        public void ConfigureVisibility(GameObject canvas, Button close, ManagementPanel management, GolemConstructionPanel construction)
        {
            canvasRoot = canvas;
            closeButton = close;
            managementPanel = management;
            constructionPanel = construction;
        }

        // Test/bootstrap-friendly setup for the always-on HUD chrome this modal hides.
        public void ConfigureHiddenWhileOpen(GameObject[] hidden) =>
            hideWhileOpen = hidden ?? new GameObject[0];

        public bool IsOpen { get; private set; }

        // Opened by PlayerInteractor.TryProgram when the player interacts with a golem.
        // Closes the other mutually-exclusive HUD screens so only one is ever showing.
        public void Open()
        {
            IsOpen = true;
            if (canvasRoot != null)
            {
                canvasRoot.SetActive(true);
            }
            managementPanel?.Close();
            constructionPanel?.Close();
            SetHiddenChromeActive(false);
        }

        public void Close()
        {
            IsOpen = false;
            if (canvasRoot != null)
            {
                canvasRoot.SetActive(false);
            }
            SetHiddenChromeActive(true);
        }

        private void SetHiddenChromeActive(bool active)
        {
            for (int i = 0; i < hideWhileOpen.Length; i++)
            {
                if (hideWhileOpen[i] != null)
                {
                    hideWhileOpen[i].SetActive(active);
                }
            }
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
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            Close();
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

        // Lets a PlayerInteractor point this already-built Workbench at a different golem at
        // runtime -- e.g. a freshly constructed one, or reprogramming an earlier one -- without
        // re-running Start()'s one-time setup (BuildChassisButtons, button listener wiring).
        public void RetargetGolem(GolemEntity golem)
        {
            targetGolem = golem;
            LoadDraftFromGolem();
            RebuildUI();
        }

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

            SetStatus("Gears engaged. New configuration is live.");
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

        private int CountAssignedAppendages()
        {
            int count = 0;
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                if (_draftAppendages[i] != null)
                {
                    count++;
                }
            }
            return count;
        }

        private int DraftCycleTicks()
        {
            int total = 0;
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                AppendageActionDefinition appendage = _draftAppendages[i];
                if (appendage != null)
                {
                    total += appendage.durationTicks > WorkbenchDiagnostics.MinimumStepTicks
                        ? appendage.durationTicks
                        : WorkbenchDiagnostics.MinimumStepTicks;
                }
            }
            return total;
        }

        // The diagnostic tape ticker. All the arithmetic and formatting lives in the
        // engine-free WorkbenchDiagnostics so it's unit-testable; this just gathers the
        // draft's current numbers and applies the result.
        private void UpdateTapeTicker()
        {
            if (tapeTickerText == null)
            {
                return;
            }

            int stepCount = CountAssignedAppendages();
            int cycleTicks = DraftCycleTicks();
            int tier = _draftChassis != null ? _draftChassis.tier : 1;
            int maxSlots = _draftChassis != null ? _draftChassis.maxAppendageSlots : 0;
            float focus = focusMeterHolder != null ? focusMeterHolder.Meter.CurrentFocus : 0f;
            float maxFocus = focusMeterHolder != null ? focusMeterHolder.Meter.MaxFocus : 0f;

            tapeTickerText.text = WorkbenchDiagnostics.ComposeTicker(
                _draftChassis != null ? _draftChassis.name : null,
                stepCount,
                maxSlots,
                // Same unnamed-runtime-instance fallback the cards use, or a demo golem's
                // core would read as "-- none --" on the tape while visibly sitting in the
                // trigger slot.
                _draftLogicCore != null ? CardDisplayName(_draftLogicCore, null) : null,
                cycleTicks,
                WorkbenchDiagnostics.ComputeSteamDraw(stepCount, tier),
                WorkbenchDiagnostics.ComputeCyclesPerMinute(cycleTicks, ticksPerSecondForReadout),
                focus,
                maxFocus);
        }

        private void BuildChassisButtons()
        {
            if (chassisButtonRow == null)
            {
                return;
            }

            foreach (ChassisDefinition chassis in availableChassis)
            {
                var go = new GameObject(chassis.name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(chassisButtonRow, false);
                var rect = go.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(CardWidth, ChassisButtonHeight);

                // A VerticalLayoutGroup will hand out leftover height to any child whose
                // flexibleHeight is left at -1, even with childForceExpandHeight off -- the
                // exact bug that ballooned AssemblyLinePanel's rows once they had real
                // sprites. Pin it explicitly.
                LayoutElement layout = go.GetComponent<LayoutElement>();
                layout.preferredHeight = ChassisButtonHeight;
                layout.minHeight = ChassisButtonHeight;
                layout.flexibleHeight = 0f;
                layout.flexibleWidth = 1f;

                Image image = go.GetComponent<Image>();
                if (chassisButtonSprite != null)
                {
                    image.sprite = chassisButtonSprite;
                    image.type = Image.Type.Sliced;
                }
                _chassisButtonImages[chassis] = image;

                ChassisDefinition captured = chassis;
                go.GetComponent<Button>().onClick.AddListener(() => SelectChassis(captured));

                CreateLabel(go.transform, WorkbenchDiagnostics.Humanize(chassis.name), 15f,
                    ChassisInkColor, TextAlignmentOptions.Left, new Vector2(0.05f, 0.44f), new Vector2(0.97f, 0.96f));
                CreateLabel(go.transform, ChassisSubtitle(chassis), 10.5f,
                    ChassisSubInkColor, TextAlignmentOptions.Left, new Vector2(0.05f, 0.06f), new Vector2(0.97f, 0.46f));
            }
        }

        private static string ChassisSubtitle(ChassisDefinition chassis)
        {
            string cost = chassis.scrapCost > 0 || chassis.brassCost > 0
                ? $"  ·  {chassis.scrapCost} scrap / {chassis.brassCost} brass"
                : string.Empty;
            return $"{chassis.maxAppendageSlots} slots  ·  tier {chassis.tier}{cost}";
        }

        private void RebuildUI()
        {
            ClearChildren(vaultContent);
            ClearCards(logicCoreSlotZone != null ? logicCoreSlotZone.transform : null);
            foreach (WorkbenchDropZone zone in appendageSlotZones)
            {
                if (zone != null)
                {
                    ClearCards(zone.transform);
                }
            }

            CreateVaultHeading(vaultContent, "LOGIC CORES  ·  triggers");
            foreach (LogicCoreDefinition logicCore in availableLogicCores)
            {
                CreateCard(vaultContent, logicCore, null, isVaultOrigin: true, sourceAppendageIndex: -1);
            }
            CreateVaultHeading(vaultContent, "APPENDAGES  ·  actions");
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

            RefreshBlueprintPane();
            UpdateTapeTicker();
        }

        // "Always re-render from data", same as the card rebuild above: the viewport's
        // portrait and captions are recomputed from the draft rather than incrementally
        // patched, so there is one code path whether the chassis changed via a button, a
        // loaded blueprint, or a golem retarget.
        private void RefreshBlueprintPane()
        {
            if (chassisPortrait != null)
            {
                Sprite art = _draftChassis != null ? _draftChassis.chassisSprite : null;
                chassisPortrait.sprite = art;
                chassisPortrait.preserveAspect = true;
                // An Image with no sprite draws a full white box; hide it instead so an
                // unassigned chassis reads as an empty drafting field, not a blank card.
                chassisPortrait.enabled = art != null;
            }

            if (chassisNameText != null)
            {
                chassisNameText.text = _draftChassis != null
                    ? WorkbenchDiagnostics.Humanize(_draftChassis.name)
                    : "No chassis fitted";
            }

            if (chassisStatsText != null)
            {
                chassisStatsText.text = _draftChassis != null
                    ? $"{CountAssignedAppendages()} / {_draftChassis.maxAppendageSlots} slots filled  ·  tier {_draftChassis.tier}"
                    : "Pick a chassis from the rack";
            }

            if (targetGolemText != null)
            {
                targetGolemText.text = targetGolem != null
                    ? $"TARGET  ·  {targetGolem.name}"
                    : "TARGET  ·  none";
            }
        }

        private static void CreateVaultHeading(Transform parent, string text)
        {
            if (parent == null)
            {
                return;
            }

            var go = new GameObject("Heading", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(CardWidth, VaultHeadingHeight);
            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = VaultHeadingHeight;
            layout.minHeight = VaultHeadingHeight;
            layout.flexibleHeight = 0f;
            layout.flexibleWidth = 1f;

            CreateLabel(go.transform, text, 12f, VaultHeadingColor, TextAlignmentOptions.BottomLeft,
                Vector2.zero, Vector2.one);
        }

        private void CreateCard(
            Transform parent, LogicCoreDefinition logicCore, AppendageActionDefinition appendage,
            bool isVaultOrigin, int sourceAppendageIndex)
        {
            if (parent == null)
            {
                return;
            }

            string cardName = CardDisplayName(logicCore, appendage);
            var go = new GameObject(cardName, typeof(RectTransform), typeof(Image), typeof(WorkbenchCard), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            LayoutElement layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = CardHeight;
            layout.minHeight = CardHeight;
            layout.flexibleHeight = 0f;
            layout.flexibleWidth = 1f;

            // A card sitting in a slot is chrome, not a list row: stretch it over the
            // slot's own "Socket" child so it reads as the card physically filling the
            // socket. Rigs without that child (every existing test) keep the fixed size.
            if (!isVaultOrigin)
            {
                StretchOverSocket(rect, parent);
            }

            Image cardImage = go.GetComponent<Image>();
            cardImage.color = logicCore != null ? TealColor : CopperColor;
            if (vaultCardSprite != null)
            {
                cardImage.sprite = vaultCardSprite;
                cardImage.type = Image.Type.Sliced;
            }

            CreateLabel(go.transform, cardName, 15f,
                CardInkColor, TextAlignmentOptions.Left, new Vector2(0.06f, 0.40f), new Vector2(0.98f, 0.98f));
            CreateLabel(go.transform, CardSubtitle(logicCore, appendage), 11f,
                CardSubInkColor, TextAlignmentOptions.Left, new Vector2(0.06f, 0.04f), new Vector2(0.98f, 0.42f));

            WorkbenchCard card = go.GetComponent<WorkbenchCard>();
            card.LogicCore = logicCore;
            card.Appendage = appendage;
            card.IsVaultOrigin = isVaultOrigin;
            card.SourceAppendageIndex = sourceAppendageIndex;
            card.Init(this, dragLayer);
        }

        private static void StretchOverSocket(RectTransform rect, Transform slot)
        {
            var socket = slot.Find("Socket") as RectTransform;
            if (socket == null)
            {
                return;
            }

            rect.anchorMin = socket.anchorMin;
            rect.anchorMax = socket.anchorMax;
            rect.pivot = socket.pivot;
            rect.offsetMin = socket.offsetMin;
            rect.offsetMax = socket.offsetMax;
        }

        // Demo bootstraps build some definitions with ScriptableObject.CreateInstance, so
        // `name` can legitimately be empty; fall back to the trigger/action type the card
        // represents rather than rendering a nameless strip.
        private static string CardDisplayName(LogicCoreDefinition logicCore, AppendageActionDefinition appendage)
        {
            if (logicCore != null)
            {
                return WorkbenchDiagnostics.DisplayName(logicCore.name, logicCore.triggerType + "Core");
            }

            return appendage != null
                ? WorkbenchDiagnostics.DisplayName(appendage.name, appendage.actionType.ToString())
                : string.Empty;
        }

        // The at-a-glance "what does this card actually do" line. Reads straight off the
        // authored definitions, so a re-authored .asset needs no UI change.
        private static string CardSubtitle(LogicCoreDefinition logicCore, AppendageActionDefinition appendage)
        {
            if (logicCore != null)
            {
                switch (logicCore.triggerType)
                {
                    case TriggerType.Interval:
                        return $"trigger · every {logicCore.intervalTicks} ticks";
                    case TriggerType.Threshold:
                        return $"trigger · {logicCore.thresholdBufferId} ≥ {logicCore.thresholdQuantity}";
                    case TriggerType.Signal:
                        return $"trigger · on signal from {logicCore.signalGolemId}";
                    default:
                        return "trigger · always on";
                }
            }

            if (appendage == null)
            {
                return string.Empty;
            }

            string route = !string.IsNullOrEmpty(appendage.sourceId) && !string.IsNullOrEmpty(appendage.destinationId)
                ? $" · {appendage.sourceId} → {appendage.destinationId}"
                : string.Empty;
            return $"action · {appendage.durationTicks}t{route}";
        }

        private static void CreateLabel(
            Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = alignment;
            label.color = color;
            label.fontSize = fontSize;
            label.raycastTarget = false;
            // Long roster names ("Zeppelin Freight Loader") used to wrap out of their own
            // button and collide with the next one; shrink to fit inside the card instead.
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
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

        // Slots keep their static chrome (caption, socket art, "empty" hint) between
        // rebuilds and only shed the card currently sitting in them -- otherwise the
        // rebuild would strip a slot down to a bare rectangle the first time anything
        // was dropped into it.
        private static void ClearCards(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.GetComponent<WorkbenchCard>() != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
}
