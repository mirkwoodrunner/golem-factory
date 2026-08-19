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
        // Brass keys on a copper card: a value shift rather than a hue shift, because the card
        // under them is already warm and a second warm hue would vanish into it -- the same
        // "raise value, not hue" finding the routing markers landed on.
        private static readonly Color StepperFaceColor = new Color(0.96f, 0.82f, 0.58f);
        private static readonly Color StepperDisabledColor = new Color(0.72f, 0.62f, 0.50f, 0.55f);
        // The chassis rack keeps cream lettering in both states so the label never has to
        // be recolored alongside the plate: an unselected plate is dark enough for cream
        // to read, a selected one is a hot brass-orange that still is. (The first pass at
        // this used near-black ink on a mid-brown plate and was unreadable in Play mode.)
        private static readonly Color SelectedChassisColor = new Color(0.95f, 0.62f, 0.20f);
        private static readonly Color UnselectedChassisColor = new Color(0.40f, 0.36f, 0.33f);
        private static readonly Color ChassisInkColor = new Color(0.98f, 0.94f, 0.86f);
        private static readonly Color ChassisSubInkColor = new Color(0.84f, 0.77f, 0.65f);
        private static readonly Color VaultHeadingColor = new Color(0.86f, 0.70f, 0.40f);
        // The rejected-chassis flash (see WorkbenchRejectFlash): feedback at the click
        // point, not only on a status line at the other end of the screen.
        private static readonly Color RejectedChassisColor = new Color(0.78f, 0.22f, 0.16f);
        // Status-line inks. The old line was dark orange on a dark grille and read as
        // decoration; these are picked to sit on the BottomBar plate at real contrast.
        private static readonly Color StatusErrorColor = new Color(1f, 0.55f, 0.42f);
        private static readonly Color StatusInfoColor = new Color(0.72f, 0.94f, 0.72f);
        // Cost readouts on the lever / patent button: the *persistent* affordability
        // signal, so unaffordability is visible before clicking rather than discovered by
        // clicking and hunting for a message.
        private static readonly Color AffordableCostColor = new Color(0.86f, 0.70f, 0.40f);
        private static readonly Color UnaffordableCostColor = new Color(0.95f, 0.36f, 0.30f);

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

        // LEGACY, and now only a floor for scenes that never opted into §8's scaling. The live
        // cost is WorkbenchFocusPolicy.EngageCost -- 8 + 6 x appendageCount, or a flat 10 for a
        // draft stamped from a patent. Kept serialized because both numbers are authored into
        // two scenes and the readout still quotes the patent cost from here.
        [SerializeField] private float reprogramFocusCost = 10f;
        [SerializeField] private float patentFocusCost = 20f;

        // Set when a draft is loaded from a patented blueprint and cleared the moment the player
        // edits it, because a stamped program that has been changed is not the patented one any
        // more -- charging it the flat rate would make patenting a way to buy a discount on
        // arbitrary programs rather than on repetition.
        private bool _draftIsStamped;

        /// <summary>
        /// What Engage Gears would cost right now (§8). Public so the readout and the button's
        /// enabled state quote the same number the lever charges.
        /// </summary>
        public float CurrentEngageFocusCost =>
            WorkbenchFocusPolicy.EngageCost(CountDraftAppendages(), _draftIsStamped);

        private int CountDraftAppendages()
        {
            if (_draftAppendages == null)
            {
                return 0;
            }

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

        // The "Engage Gears" lever's presentation half. Driven from the commit *result*
        // below rather than from Button.onClick, so a rejected pull can no longer animate
        // a full satisfying throw (see WorkbenchLever's class comment).
        [SerializeField] private WorkbenchLever engageLever;

        // Persistent cost readouts recolored by affordability each frame.
        [SerializeField] private TextMeshProUGUI engageCostText;
        [SerializeField] private TextMeshProUGUI patentCostText;

        public void ConfigureLever(WorkbenchLever lever) => engageLever = lever;

        public void ConfigureCostLabels(TextMeshProUGUI engageCost, TextMeshProUGUI patentCost)
        {
            engageCostText = engageCost;
            patentCostText = patentCost;
        }

        private ChassisDefinition _draftChassis;
        private LogicCoreDefinition _draftLogicCore;
        private AppendageActionDefinition[] _draftAppendages = new AppendageActionDefinition[0];

        // Per-slot batch size, parallel to _draftAppendages. Part of the DRAFT for the same
        // reason the appendages are: nothing reaches GolemEntity.Program until the lever is
        // pulled, so turning a dial has to be as reversible as picking a card up and putting it
        // back. Sized alongside _draftAppendages and reset with it.
        private int[] _draftQuantities = new int[0];
        private int _draftOverflowCount;
        private readonly Dictionary<ChassisDefinition, Image> _chassisButtonImages = new Dictionary<ChassisDefinition, Image>();
        private int _nextBlueprintNumber = 1;

        // Status-line bookkeeping. The line used to be write-only, so every message it
        // ever showed stayed on screen for the rest of the session; it now carries why it
        // is up and retires itself once WorkbenchStatusPolicy says the condition resolved.
        private WorkbenchStatusReason _statusReason = WorkbenchStatusReason.None;
        private float _statusShownSeconds;
        private int _statusChassisSlotLimit;

        // The rejected-chassis flash, driven from Update against WorkbenchRejectFlash.
        private ChassisDefinition _flashChassis;
        private float _flashElapsed = -1f;

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

        // --- §8.1: the vault shows what the player has CLAIMED ------------------------------
        // Optional, and that is the whole compatibility story: with no line wired the roster is
        // the authored one and every card is available from the start, which is the state this
        // screen shipped in for four milestones. Wiring it turns the authored roster into the
        // full CATALOGUE and the claimed set into what is actually offered.
        [SerializeField] private GolemFactory.AssemblyLine.AssemblyLineStateHolder assemblyLineHolder;
        [SerializeField] private string claimUserId = "LocalPlayer";

        public void ConfigureCardGating(
            GolemFactory.AssemblyLine.AssemblyLineStateHolder line, string userId)
        {
            assemblyLineHolder = line;
            if (!string.IsNullOrEmpty(userId))
            {
                claimUserId = userId;
            }

            // The vault is rebuilt from data on every open, so a claim made while this screen is
            // shut is picked up with no event plumbing -- "always re-render from data", the
            // idiom RebuildUI already follows.
            if (IsOpen)
            {
                RebuildUI();
            }
        }

        /// <summary>
        /// Whether card gating is in play at all. False in every scene that never wires a line,
        /// which is what keeps the ungated roster valid rather than deprecated.
        /// </summary>
        public bool IsRosterGated =>
            assemblyLineHolder != null && assemblyLineHolder.State != null;

        /// <summary>
        /// Whether this appendage is offered right now: always, when ungated; only when claimed,
        /// when gated. Public so a test can ask the question the vault asks.
        /// </summary>
        public bool IsCardAvailable(AppendageActionDefinition card)
        {
            if (card == null)
            {
                return false;
            }

            if (!IsRosterGated)
            {
                return true;
            }

            System.Collections.Generic.IReadOnlyList<GolemFactory.AssemblyLine.DraftableCardDefinition>
                claimed = assemblyLineHolder.State.GetClaimedCards(claimUserId);
            for (int i = 0; i < claimed.Count; i++)
            {
                if (claimed[i] != null && claimed[i].appendage == card)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Same question for a chassis, which the Assembly Line also drafts.</summary>
        public bool IsChassisAvailable(ChassisDefinition chassis)
        {
            if (chassis == null)
            {
                return false;
            }

            if (!IsRosterGated)
            {
                return true;
            }

            System.Collections.Generic.IReadOnlyList<GolemFactory.AssemblyLine.DraftableCardDefinition>
                claimed = assemblyLineHolder.State.GetClaimedCards(claimUserId);
            for (int i = 0; i < claimed.Count; i++)
            {
                if (claimed[i] != null && claimed[i].chassis == chassis)
                {
                    return true;
                }
            }

            return false;
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

            // Re-read the target's committed program every time the screen is shown.
            // Start()'s one-shot load raced the demo bootstraps that populate a golem's
            // program in their own Start(), so Main.unity's Workbench could open showing a
            // draft that had nothing to do with the golem it was pointed at. Uncommitted
            // draft edits are meant to be lost when the screen closes anyway -- that is
            // what "nothing reaches GolemEntity.Program until EngageGears" means.
            LoadDraftFromGolem();
            RebuildUI();
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

        /// <summary>
        /// Shows or hides the always-on world HUD -- the build menu, the fuel gauge, the tower
        /// panel, the alerts strip. Public because the <b>Management</b> screen needs the same
        /// thing and there must not be a second list: <c>hideWhileOpen</c> is authored once, on
        /// this component, and <see cref="HudScreenPolicy.ShouldShowWorldHud"/> already says the
        /// rule is per-screen-open rather than per-screen-identity. Without this the fuel gauge
        /// and the alerts strip drew straight over the Management modal -- the same class of
        /// overlap the UGUI conversion was done to kill, surviving in the one screen that never
        /// got the list.
        /// </summary>
        public void SetWorldHudVisible(bool visible) => SetHiddenChromeActive(visible);

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
            _draftQuantities = new int[appendageSlotZones.Length];
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
            UpdateStatusLifetime();
            UpdateAffordability();
            UpdateChassisFlash();
        }

        private void LoadDraftFromGolem()
        {
            if (targetGolem == null)
            {
                // Cleared even on the early-out, or retargeting away from an overflowing golem
                // to no golem at all would leave the lever refusing for a program that is no
                // longer loaded.
                _draftOverflowCount = 0;
                return;
            }

            GolemProgram program = targetGolem.Program;
            // Read off a golem, not stamped from a patent: §8's flat rate is for repetition,
            // and this is whatever this particular golem already runs.
            _draftIsStamped = false;
            _draftChassis = program.chassis;
            _draftLogicCore = program.logicCore;
            // Blank first: this only ever overwrote the indices the incoming program
            // happened to fill, so retargeting from a 3-appendage golem to a 1-appendage
            // one used to leave the previous golem's steps 2 and 3 sitting in the draft.
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = null;
                _draftQuantities[i] = WorkbenchQuantityPolicy.MinQuantity;
            }
            for (int i = 0; i < program.appendages.Count && i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = program.appendages[i];
                // The golem's CURRENT batch sizes, not the cards' authored defaults -- opening
                // the Workbench on a golem the player has already tuned must show what they set,
                // or the screen quietly proposes undoing their work every time they look at it.
                _draftQuantities[i] = WorkbenchQuantityPolicy.Clamp(program.GetQuantityAt(i));
            }

            // THE TRUNCATION IS NOW RECORDED RATHER THAN SILENT. The loop above deliberately
            // stops at the socket count, because there is nowhere on screen to put step six of
            // a five-socket Workbench. What used to be missing is the consequence: EngageGears
            // rebuilds the program from the draft, so opening a 6-appendage Zeppelin in a
            // 5-socket UI and pulling the lever committed a 5-appendage golem and threw the
            // sixth step away, with no warning and no undo.
            //
            // Refusing to engage (below) is the only honest option. Committing a program the
            // player cannot see is destructive; committing the visible five is the same thing
            // with extra steps. With the sixth socket authored this should never fire in
            // normal play -- it is the guard that makes a future 7-slot chassis a refusal
            // rather than a data loss.
            _draftOverflowCount = Mathf.Max(0, program.appendages.Count - _draftAppendages.Length);
        }

        /// <summary>
        /// How many appendages the targeted golem's program has beyond what this Workbench has
        /// sockets for. Zero in every normal case. Exposed so a test can assert the condition
        /// directly rather than inferring it from a status string.
        /// </summary>
        public int DraftOverflowCount => _draftOverflowCount;

        /// <summary>
        /// The batch size currently drafted for a slot. Exposed for the same reason
        /// <see cref="DraftOverflowCount"/> is: a test should assert the draft directly rather
        /// than inferring it from rendered card text.
        /// </summary>
        public int DraftQuantityAt(int slotIndex) =>
            slotIndex >= 0 && slotIndex < _draftQuantities.Length
                ? _draftQuantities[slotIndex]
                : WorkbenchQuantityPolicy.MinQuantity;

        private int DraftMaxSlots => _draftChassis != null ? _draftChassis.maxAppendageSlots : 0;

        // Whether a drop onto this slot may be committed. Deliberately distinct from
        // SlotVisible below -- see WorkbenchDropRules.
        private bool SlotActive(int appendageIndex) =>
            WorkbenchDropRules.SlotWithinChassis(appendageIndex, DraftMaxSlots);

        // Called by WorkbenchCard.OnBeginDrag: light up every socket that would accept the
        // card now in hand, and visibly dim the ones that would reject it. Without this,
        // all five appendage sockets looked identical while an appendage was held and
        // nothing signalled that the logic-core socket would refuse it -- the screen's
        // core verb had no affordance at all.
        //
        // Uses the same WorkbenchDropRules.AcceptsCard that HandleDrop commits against, so
        // a socket can never glow green and then reject the drop.
        public void BeginCardDrag(WorkbenchCard card)
        {
            if (card == null)
            {
                return;
            }

            bool cardIsLogicCore = card.LogicCore != null;
            int maxSlots = DraftMaxSlots;

            if (logicCoreSlotZone != null)
            {
                logicCoreSlotZone.SetHighlight(
                    WorkbenchDropRules.AcceptsCard(DropZoneKind.LogicCore, -1, cardIsLogicCore, maxSlots)
                        ? DropZoneHighlight.Valid
                        : DropZoneHighlight.Invalid);
            }

            for (int i = 0; i < appendageSlotZones.Length; i++)
            {
                WorkbenchDropZone zone = appendageSlotZones[i];
                if (zone == null)
                {
                    continue;
                }

                zone.SetHighlight(
                    WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, zone.AppendageIndex, cardIsLogicCore, maxSlots)
                        ? DropZoneHighlight.Valid
                        : DropZoneHighlight.Invalid);
            }
        }

        // Called by WorkbenchCard.OnEndDrag before the drop is resolved.
        public void EndCardDrag()
        {
            if (logicCoreSlotZone != null)
            {
                logicCoreSlotZone.SetHighlight(DropZoneHighlight.Neutral);
            }

            for (int i = 0; i < appendageSlotZones.Length; i++)
            {
                if (appendageSlotZones[i] != null)
                {
                    appendageSlotZones[i].SetHighlight(DropZoneHighlight.Neutral);
                }
            }
        }

        // Called by WorkbenchCard.OnEndDrag. zone is null when the card was dropped
        // somewhere that isn't a valid drop zone.
        public void HandleDrop(WorkbenchCard card, WorkbenchDropZone zone)
        {
            // ANY hand edit ends the flat rate. A stamped program that has been changed is not
            // the patented one any more, and charging it 10 would turn patenting into a discount
            // on arbitrary programs rather than on repetition.
            _draftIsStamped = false;

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
                        _draftQuantities[card.SourceAppendageIndex] = WorkbenchQuantityPolicy.MinQuantity;
                    }
                }
                // Vault-origin card dropped nowhere valid: cancel, nothing to undo. The
                // card GameObject itself is cleaned up by RebuildUI's DragLayer sweep
                // below (and by WorkbenchCard.OnEndDrag's own guard) -- leaving it alive
                // under DragLayer is what used to orphan a GameObject per failed drag.
            }
            else if (zone.Kind == DropZoneKind.LogicCore && card.LogicCore != null)
            {
                _draftLogicCore = card.LogicCore;
            }
            else if (zone.Kind == DropZoneKind.Appendage && card.Appendage != null && SlotActive(zone.AppendageIndex))
            {
                int targetIndex = zone.AppendageIndex;

                // A card dragged BETWEEN sockets carries its batch size with it; one arriving
                // from the vault starts at its own authored default. Both are what the player
                // means: moving step 3 up to step 2 is a reorder, not a retune, and a fresh card
                // has no history to keep.
                int arrivingQuantity = card.IsVaultOrigin || card.SourceAppendageIndex < 0
                    ? WorkbenchQuantityPolicy.Clamp(card.Appendage.haulQuantity)
                    : _draftQuantities[card.SourceAppendageIndex];

                if (!card.IsVaultOrigin && card.SourceAppendageIndex >= 0 && card.SourceAppendageIndex != targetIndex)
                {
                    _draftAppendages[card.SourceAppendageIndex] = null;
                    _draftQuantities[card.SourceAppendageIndex] = WorkbenchQuantityPolicy.MinQuantity;
                }
                _draftAppendages[targetIndex] = card.Appendage;
                _draftQuantities[targetIndex] = arrivingQuantity;
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

        // Every exit path is now reported, and the lever animation is driven from the
        // result rather than from Button.onClick. Previously WorkbenchLever.Pull was
        // registered on the same onClick as this method, so the handle ran its full
        // throw/hold/spring-back on failure too -- and the targetGolem == null path
        // (Sandbox's default state) returned without setting any status at all, so the
        // lever pulled and absolutely nothing happened.
        private void EngageGears()
        {
            if (targetGolem == null)
            {
                SetStatus("No golem selected. Walk up to a golem and interact to program it.", WorkbenchStatusReason.NoTarget);
                RefuseLever();
                return;
            }

            // Checked BEFORE Focus is consumed, like the NoTarget path above and unlike the
            // chassis-rejection path below, which has already spent and therefore has to
            // refund. A refusal the player cannot act on must not also cost them anything.
            if (_draftOverflowCount > 0)
            {
                SetStatus(
                    $"Cannot engage: this golem's program has {_draftOverflowCount} more " +
                    $"appendage{(_draftOverflowCount == 1 ? "" : "s")} than this Workbench has " +
                    $"sockets ({_draftAppendages.Length}). Engaging would discard them.",
                    WorkbenchStatusReason.DraftTruncated);
                RefuseLever();
                return;
            }

            // §8: 8 + 6 x appendageCount, or a flat 10 for a draft stamped from a patent.
            float engageCost = CurrentEngageFocusCost;
            ArtificerFocusMeter meter = focusMeterHolder != null ? focusMeterHolder.Meter : null;
            if (meter == null || !meter.TryConsume(engageCost))
            {
                SetStatus(
                    $"Not enough Focus to reprogram (need {engageCost:F0}).",
                    WorkbenchStatusReason.InsufficientFocusEngage);
                RefuseLever();
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
                meter.Refund(engageCost);
                SetStatus("Cannot engage: chassis rejected the current appendage count.", WorkbenchStatusReason.Info);
                RefuseLever();
                return;
            }

            // Added first, then re-quantified. TryAddAppendage seeds each new slot from the
            // CARD's authored default (a shared asset), so applying the player's sizes has to
            // come after -- and it has to happen at all: before this, every Engage silently
            // reset every batch size in the program back to the card defaults, which made the
            // saved-and-restored quantity a value nothing could ever set and reprogramming a
            // golem an invisible retune of it.
            int committed = 0;
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                if (_draftAppendages[i] == null)
                {
                    continue;
                }

                if (program.TryAddAppendage(_draftAppendages[i]))
                {
                    // Indexed by where it LANDED, not by its socket: a program packs its
                    // appendages, so leaving socket 2 empty puts socket 3's card at index 2.
                    program.SetQuantityAt(committed, _draftQuantities[i]);
                    committed++;
                }
            }

            program.logicCore = _draftLogicCore;
            program.CurrentStepIndex = 0;
            program.StepProgressTicks = 0;
            program.State = GolemState.Idle;

            SetStatus("Gears engaged. New configuration is live.", WorkbenchStatusReason.Info);
            if (engageLever != null)
            {
                engageLever.Pull();
            }
        }

        private void RefuseLever()
        {
            if (engageLever != null)
            {
                engageLever.Refuse();
            }
        }

        private void Patent()
        {
            // Same refusal as EngageGears, and for the same reason: a blueprint taken from a
            // truncated draft is a silently incomplete copy of the golem, which is worse than
            // the truncation itself -- it outlives the golem and can be stamped onto others.
            if (_draftOverflowCount > 0)
            {
                SetStatus(
                    $"Cannot patent: the draft is missing {_draftOverflowCount} appendage" +
                    $"{(_draftOverflowCount == 1 ? "" : "s")} this Workbench has no socket for.",
                    WorkbenchStatusReason.DraftTruncated);
                return;
            }

            ArtificerFocusMeter meter = focusMeterHolder != null ? focusMeterHolder.Meter : null;
            if (meter == null || !meter.TryConsume(patentFocusCost))
            {
                SetStatus(
                    $"Not enough Focus to patent (need {patentFocusCost:F0}).",
                    WorkbenchStatusReason.InsufficientFocusPatent);
                return;
            }

            string blueprintId = $"BP-{_nextBlueprintNumber:D3}";
            _nextBlueprintNumber++;

            var appendages = _draftAppendages.Where(a => a != null).ToList();
            var blueprint = new Blueprint(blueprintId, PlaceableBuilding.LocalPlayerOwnerId, _draftChassis, _draftLogicCore, appendages);
            patentRegistryHolder.Registry.TryPatent(blueprint);

            SetStatus($"Patented as {blueprintId}.", WorkbenchStatusReason.Info);
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

            // The flat-rate flag. Set here and cleared by any subsequent edit, so the discount
            // follows the patented program rather than the screen it was loaded into.
            _draftIsStamped = true;
            _draftChassis = blueprint.Chassis;
            _draftLogicCore = blueprint.LogicCore;
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = i < blueprint.Appendages.Count ? blueprint.Appendages[i] : null;
            }

            // A patented 6-appendage blueprint loaded into a 5-socket Workbench overflows
            // exactly as a 6-appendage golem does, so it is counted the same way rather than
            // left to look like a clean load that then refuses at the lever.
            _draftOverflowCount = Mathf.Max(0, blueprint.Appendages.Count - _draftAppendages.Length);

            SetStatus($"Loaded {blueprint.BlueprintId} into the draft.", WorkbenchStatusReason.Info);
            RebuildUI();
        }

        private void SelectChassis(ChassisDefinition chassis)
        {
            int assignedAppendages = _draftAppendages.Count(a => a != null);
            if (chassis != null && assignedAppendages > chassis.maxAppendageSlots)
            {
                // Feedback at the interaction point as well as on the status line: the
                // plate the player actually clicked flashes red. A status message alone,
                // at the far corner of the screen, read as the click not registering.
                FlashRejectedChassis(chassis);
                SetStatus(
                    $"Cannot switch chassis: {WorkbenchDiagnostics.Humanize(chassis.name)} has {chassis.maxAppendageSlots} slots, the draft uses {assignedAppendages}.",
                    WorkbenchStatusReason.ChassisTooSmall,
                    chassis.maxAppendageSlots);
                return;
            }

            _draftChassis = chassis;
            ClearStatus();
            RebuildUI();
        }

        private void FlashRejectedChassis(ChassisDefinition chassis)
        {
            _flashChassis = chassis;
            _flashElapsed = 0f;
        }

        private void SetStatus(string message, WorkbenchStatusReason reason, int chassisSlotLimit = 0)
        {
            _statusReason = string.IsNullOrEmpty(message) ? WorkbenchStatusReason.None : reason;
            _statusShownSeconds = 0f;
            _statusChassisSlotLimit = chassisSlotLimit;

            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = reason == WorkbenchStatusReason.Info ? StatusInfoColor : StatusErrorColor;
            }
        }

        private void ClearStatus() => SetStatus(string.Empty, WorkbenchStatusReason.None);

        // Exposed so tests can assert *why* the line says what it says (and that it
        // retires itself) rather than string-matching the message.
        public WorkbenchStatusReason StatusReason => _statusReason;

        private float CurrentFocus() =>
            focusMeterHolder != null && focusMeterHolder.Meter != null ? focusMeterHolder.Meter.CurrentFocus : 0f;

        // The status line was previously write-only: nothing ever cleared it, so a stale
        // "Not enough Focus (need 10)" sat on screen while the tape ticker read FOCUS
        // 42/100, and "remove appendages to fit its slot count first" survived removing
        // every appendage. The staleness rule itself is the engine-free
        // WorkbenchStatusPolicy; this is the applier that feeds it live numbers.
        private void UpdateStatusLifetime()
        {
            if (_statusReason == WorkbenchStatusReason.None)
            {
                return;
            }

            _statusShownSeconds += Time.unscaledDeltaTime;
            if (WorkbenchStatusPolicy.ShouldClear(
                    _statusReason,
                    _statusShownSeconds,
                    CurrentFocus(),
                    reprogramFocusCost,
                    patentFocusCost,
                    CountAssignedAppendages(),
                    _statusChassisSlotLimit,
                    targetGolem != null,
                    _draftOverflowCount > 0))
            {
                ClearStatus();
            }
        }

        // Affordability is a *persistent* readout, not something to discover by clicking:
        // the lever and Patent button go non-interactable and their cost labels turn red
        // the moment the action can't be paid for.
        private void UpdateAffordability()
        {
            float focus = CurrentFocus();
            bool canEngage = targetGolem != null && focus >= reprogramFocusCost;
            bool canPatent = focus >= patentFocusCost;

            if (engageGearsButton != null)
            {
                engageGearsButton.interactable = canEngage;
            }
            if (patentButton != null)
            {
                patentButton.interactable = canPatent;
            }
            // Spell out the shortfall rather than just recoloring: a disabled lever that
            // only turns its cost label red still leaves the player guessing by how much.
            if (engageCostText != null)
            {
                engageCostText.color = canEngage ? AffordableCostColor : UnaffordableCostColor;
                engageCostText.text = focus >= reprogramFocusCost
                    ? $"{reprogramFocusCost:F0} focus"
                    : $"{reprogramFocusCost:F0} focus · have {focus:F0}";
            }
            if (patentCostText != null)
            {
                patentCostText.color = canPatent ? AffordableCostColor : UnaffordableCostColor;
                patentCostText.text = canPatent
                    ? $"{patentFocusCost:F0} focus"
                    : $"{patentFocusCost:F0} focus · have {focus:F0}";
            }

            // A disabled lever with no explanation is its own dead end, so say why once
            // there is nothing to program. NoTarget retires itself as soon as a golem is
            // targeted (WorkbenchStatusPolicy.ShouldClear).
            if (targetGolem == null && _statusReason == WorkbenchStatusReason.None)
            {
                SetStatus("No golem selected. Walk up to a golem and interact to program it.", WorkbenchStatusReason.NoTarget);
            }
        }

        private void UpdateChassisFlash()
        {
            if (_flashElapsed < 0f || _flashChassis == null)
            {
                return;
            }

            Image plate;
            if (!_chassisButtonImages.TryGetValue(_flashChassis, out plate) || plate == null)
            {
                _flashElapsed = -1f;
                _flashChassis = null;
                return;
            }

            _flashElapsed += Time.unscaledDeltaTime;
            if (_flashElapsed >= WorkbenchRejectFlash.TotalSeconds)
            {
                _flashElapsed = -1f;
                plate.color = _flashChassis == _draftChassis ? SelectedChassisColor : UnselectedChassisColor;
                _flashChassis = null;
                return;
            }

            Color restColor = _flashChassis == _draftChassis ? SelectedChassisColor : UnselectedChassisColor;
            plate.color = Color.Lerp(restColor, RejectedChassisColor, WorkbenchRejectFlash.ComputeStrength(_flashElapsed));
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
                // §8.1: the authored roster is the CATALOGUE; what the player has claimed is
                // what is offered. Ungated (no Assembly Line wired) every entry passes, which
                // is the state this screen shipped in.
                if (!IsChassisAvailable(chassis))
                {
                    continue;
                }

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
            // §6's costs are item bundles now, and the Zeppelin's is five goods long -- far too
            // wide for a rack subtitle. The full breakdown is the construction panel's job; this
            // line only says how many DIFFERENT goods the purchase needs, which is the part that
            // reads as "how deep into the tree is this chassis".
            string cost = chassis.cost != null && chassis.cost.Count > 0
                ? $"  ·  {chassis.cost.Count} goods"
                : string.Empty;
            return $"{chassis.maxAppendageSlots} slots  ·  tier {chassis.tier}{cost}";
        }

        private void RebuildUI()
        {
            // First, always: a card reparented onto the DragLayer is outside everything
            // else this method knows how to clear, so a drag released over the mahogany
            // background/chassis rack/title bar used to strand it there permanently --
            // one leaked GameObject per failed drag, surviving Close()/Open() and
            // RetargetGolem() for the whole session. This is the sweep that fixes it;
            // WorkbenchCard.OnEndDrag carries an independent second guard.
            ClearChildren(dragLayer);

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
                // §8.1, the vault half. Filtered here rather than by rewriting the roster, so
                // one authored list stays the catalogue and a claim shows up on the next
                // rebuild with no roster surgery.
                if (!IsCardAvailable(appendage))
                {
                    continue;
                }

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

                // Render rule, not the drop rule: an occupied slot stays on screen even
                // when it sits beyond the fitted chassis's capacity, so the viewport can
                // never refuse to draw a step the tape ticker is counting (the
                // "SLOTS 1/0 over an empty viewport" incoherence). New drops onto it are
                // still refused -- see WorkbenchDropRules.
                bool occupied = _draftAppendages[i] != null;
                bool visible = WorkbenchDropRules.SlotVisible(i, DraftMaxSlots, occupied);
                zone.gameObject.SetActive(visible);
                zone.SetHighlight(DropZoneHighlight.Neutral);
                if (visible && occupied)
                {
                    CreateCard(zone.transform, null, _draftAppendages[i], isVaultOrigin: false, sourceAppendageIndex: i);
                }
            }

            foreach (var entry in _chassisButtonImages)
            {
                // Leave the plate mid-flash alone; UpdateChassisFlash restores it.
                if (_flashElapsed >= 0f && entry.Key == _flashChassis)
                {
                    continue;
                }
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
            // A slot card that carries a batch-size stepper gives up the right-hand third of
            // its subtitle row to it, rather than the two overlapping.
            bool hasStepper = !isVaultOrigin && sourceAppendageIndex >= 0 &&
                              WorkbenchQuantityPolicy.TakesQuantity(appendage);
            CreateLabel(go.transform, CardSubtitle(logicCore, appendage), 11f,
                CardSubInkColor, TextAlignmentOptions.Left, new Vector2(0.06f, 0.04f),
                new Vector2(hasStepper ? 0.60f : 0.98f, 0.42f));

            if (hasStepper)
            {
                CreateQuantityStepper(go.transform, sourceAppendageIndex, appendage);
            }

            WorkbenchCard card = go.GetComponent<WorkbenchCard>();
            card.LogicCore = logicCore;
            card.Appendage = appendage;
            card.IsVaultOrigin = isVaultOrigin;
            card.SourceAppendageIndex = sourceAppendageIndex;
            card.Init(this, dragLayer);
        }

        /// <summary>
        /// The batch-size control: [-] x4 - 10t [+], on the right of a slot card's subtitle row.
        ///
        /// <para>
        /// BUILT ON THE CARD, NOT THE SOCKET, so it is destroyed and recreated with the card by
        /// RebuildUI -- the same "always re-render from data" rule the rest of this screen
        /// follows. A stepper living on the socket would have to be told when the card under it
        /// changed, which is the incremental-choreography this class exists to avoid.
        /// </para>
        ///
        /// <para>
        /// The buttons do not steal the drag. UGUI routes drag events to the nearest ancestor
        /// implementing IDragHandler, which is still the card, so pressing a button adjusts the
        /// batch and dragging from one picks the card up -- both gestures stay available on the
        /// same few pixels.
        /// </para>
        /// </summary>
        private void CreateQuantityStepper(
            Transform card, int slotIndex, AppendageActionDefinition appendage)
        {
            var row = new GameObject("Quantity", typeof(RectTransform));
            row.transform.SetParent(card, false);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.62f, 0.04f);
            rowRect.anchorMax = new Vector2(0.98f, 0.42f);
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;

            int quantity = _draftQuantities[slotIndex];
            CreateStepperButton(row.transform, "-", new Vector2(0f, 0f), new Vector2(0.22f, 1f),
                slotIndex, -1, quantity > WorkbenchQuantityPolicy.MinQuantity);
            CreateLabel(row.transform, WorkbenchQuantityPolicy.Describe(appendage, quantity), 11f,
                CardInkColor, TextAlignmentOptions.Center, new Vector2(0.24f, 0f), new Vector2(0.76f, 1f));
            CreateStepperButton(row.transform, "+", new Vector2(0.78f, 0f), new Vector2(1f, 1f),
                slotIndex, 1, quantity < WorkbenchQuantityPolicy.MaxQuantity);
        }

        // Disabled rather than hidden at the ends of the range: a control that vanishes at 1 and
        // 12 makes the player wonder what they broke, where a greyed one says "this is the end".
        private void CreateStepperButton(
            Transform parent, string glyph, Vector2 anchorMin, Vector2 anchorMax,
            int slotIndex, int delta, bool interactable)
        {
            var go = new GameObject(glyph == "+" ? "Increase" : "Decrease",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.color = interactable ? StepperFaceColor : StepperDisabledColor;

            var button = go.GetComponent<Button>();
            button.interactable = interactable;
            int capturedSlot = slotIndex;
            int capturedDelta = delta;
            button.onClick.AddListener(() => AdjustDraftQuantity(capturedSlot, capturedDelta));

            CreateLabel(go.transform, glyph, 14f, CardInkColor, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one);
        }

        /// <summary>
        /// Changes one slot's batch size in the DRAFT. Nothing reaches the golem until the lever
        /// is pulled, exactly like moving a card -- so a player can try 8, look at the tick cost,
        /// and put it back without having reprogrammed anything or spent any Focus.
        /// </summary>
        public void AdjustDraftQuantity(int slotIndex, int delta)
        {
            if (slotIndex < 0 || slotIndex >= _draftQuantities.Length || _draftAppendages[slotIndex] == null)
            {
                return;
            }

            _draftQuantities[slotIndex] = WorkbenchQuantityPolicy.Step(_draftQuantities[slotIndex], delta);
            RebuildUI();
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
            // Truncate, not Ellipsis: LiberationSans SDF (the TMP default this project
            // ships) has no "…" glyph, so Ellipsis silently degrades to Truncate anyway
            // and logs a warning for every label it builds.
            label.overflowMode = TextOverflowModes.Truncate;
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
        // was dropped into it. Drag placeholders go too: a ghost has no WorkbenchCard, so
        // an interrupted drag would otherwise leave a permanent translucent stripe.
        private static void ClearCards(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.GetComponent<WorkbenchCard>() != null || child.GetComponent<WorkbenchCardGhost>() != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
}
