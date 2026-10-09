using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.AssemblyLine;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    /// <summary>How a drop zone is lit while a card is held over the screen.</summary>
    public enum DropZoneHighlight
    {
        Neutral,
        Valid,
        Invalid,
    }

    /// <summary>
    /// A card as the Workbench sees it: what it carries, and where it was picked up from.
    /// Exactly one of <see cref="LogicCore"/> / <see cref="Appendage"/> is set.
    /// </summary>
    public readonly struct WorkbenchCardRef
    {
        public readonly LogicCoreDefinition LogicCore;
        public readonly AppendageActionDefinition Appendage;
        public readonly bool IsVaultOrigin;
        public readonly int SourceAppendageIndex;

        private WorkbenchCardRef(LogicCoreDefinition core, AppendageActionDefinition appendage, bool vault, int source)
        {
            LogicCore = core;
            Appendage = appendage;
            IsVaultOrigin = vault;
            SourceAppendageIndex = source;
        }

        public static WorkbenchCardRef FromVault(LogicCoreDefinition core) => new WorkbenchCardRef(core, null, true, -1);
        public static WorkbenchCardRef FromVault(AppendageActionDefinition appendage) => new WorkbenchCardRef(null, appendage, true, -1);
        public static WorkbenchCardRef FromTriggerSocket(LogicCoreDefinition core) => new WorkbenchCardRef(core, null, false, -1);
        public static WorkbenchCardRef FromSocket(AppendageActionDefinition appendage, int index) => new WorkbenchCardRef(null, appendage, false, index);
    }

    /// <summary>Where a card was released: the trigger socket, an action socket, or nowhere (null).</summary>
    public readonly struct WorkbenchZone
    {
        public readonly DropZoneKind Kind;
        public readonly int AppendageIndex;

        public WorkbenchZone(DropZoneKind kind, int appendageIndex)
        {
            Kind = kind;
            AppendageIndex = appendageIndex;
        }

        public static WorkbenchZone Trigger => new WorkbenchZone(DropZoneKind.LogicCore, -1);
        public static WorkbenchZone Socket(int index) => new WorkbenchZone(DropZoneKind.Appendage, index);
    }

    /// <summary>
    /// The Workbench's state and every decision it makes, without a screen: Unity's
    /// WorkbenchController with its UGUI taken out (milestone G7).
    ///
    /// <para>
    /// THE RULE THE WHOLE SCREEN EXISTS FOR: dragging cards edits a local DRAFT only. Nothing
    /// touches the golem's real program until <see cref="Engage"/> -- the Engage Gears lever --
    /// commits it, and then only if the draft can represent the whole program (a program with
    /// more steps than this Workbench has sockets is refused, never truncated). Opening the
    /// screen or retargeting re-reads the target's committed program, so a stale draft is
    /// never committed over a golem it did not come from.
    /// </para>
    ///
    /// <para>
    /// The view (Godot's WorkbenchScreen) draws from this and turns gestures into its calls;
    /// <see cref="Version"/> ticks whenever what a rebuild would draw has changed, which is the
    /// "always re-render from data" idiom Unity's RebuildUI followed. The lever's throw and the
    /// rejected-chassis flash are presentation, signalled through <see cref="LeverPulled"/>,
    /// <see cref="LeverRefused"/> and <see cref="ChassisRejected"/>.
    /// </para>
    /// </summary>
    public sealed class WorkbenchSession : IWorkbenchTarget
    {
        public const string NoTargetMessage = "No golem selected. Walk up to a golem and interact to program it.";

        private ChassisDefinition[] _chassisRoster = new ChassisDefinition[0];
        private LogicCoreDefinition[] _coreRoster = new LogicCoreDefinition[0];
        private AppendageActionDefinition[] _appendageRoster = new AppendageActionDefinition[0];
        private PatentRegistry _patents;
        private AssemblyLineState _line;
        private string _claimUserId = PlaceableBuilding.LocalPlayerOwnerId;

        private ChassisDefinition _draftChassis;
        private LogicCoreDefinition _draftLogicCore;
        private readonly AppendageActionDefinition[] _draftAppendages;
        private readonly int[] _draftQuantities;
        private readonly string[] _draftItemTypes;
        private int _nextBlueprintNumber = 1;

        private float _statusShownSeconds;
        private int _statusChassisSlotLimit;

        public WorkbenchSession(int socketCount)
        {
            _draftAppendages = new AppendageActionDefinition[socketCount];
            _draftQuantities = Enumerable.Repeat(WorkbenchQuantityPolicy.MinQuantity, socketCount).ToArray();
            _draftItemTypes = Enumerable.Repeat("", socketCount).ToArray();
            LogicHighlight = DropZoneHighlight.Neutral;
            AppendageHighlights = new DropZoneHighlight[socketCount];
        }

        // --- Events (presentation hooks) ---------------------------------------------------

        /// <summary>A commit went through: run the lever's full throw.</summary>
        public event Action LeverPulled;

        /// <summary>A commit was refused: the lever's judder.</summary>
        public event Action LeverRefused;

        /// <summary>A chassis button was refused (too few slots): flash it at the click.</summary>
        public event Action<ChassisDefinition> ChassisRejected;

        // --- Wiring ------------------------------------------------------------------------

        public void ConfigureRoster(ChassisDefinition[] chassis, LogicCoreDefinition[] cores, AppendageActionDefinition[] appendages)
        {
            _chassisRoster = chassis ?? new ChassisDefinition[0];
            _coreRoster = cores ?? new LogicCoreDefinition[0];
            _appendageRoster = appendages ?? new AppendageActionDefinition[0];
            Version++;
        }

        public void ConfigurePatents(PatentRegistry patents) => _patents = patents;

        /// <summary>
        /// §8.3's gate on the vault: with a line wired, only cards the player has CLAIMED are
        /// offered. Null leaves the Workbench ungated, offering the whole roster.
        /// </summary>
        public void ConfigureCardGating(AssemblyLineState line, string userId)
        {
            _line = line;
            if (!string.IsNullOrEmpty(userId))
            {
                _claimUserId = userId;
            }
            Version++;
        }

        /// <summary>Sets the target WITHOUT re-reading its program -- Unity's ConfigureGolem.</summary>
        public void ConfigureGolem(GolemEntity golem)
        {
            TargetGolem = golem;
            Version++;
        }

        // --- Availability ------------------------------------------------------------------

        public bool IsRosterGated => _line != null;

        public bool IsCardAvailable(AppendageActionDefinition card) =>
            card != null && (!IsRosterGated || _line.GetClaimedCards(_claimUserId).Any(c => c != null && c.appendage == card));

        public bool IsChassisAvailable(ChassisDefinition chassis) =>
            chassis != null && (!IsRosterGated || _line.GetClaimedCards(_claimUserId).Any(c => c != null && c.chassis == chassis));

        public IEnumerable<ChassisDefinition> RackChassis => _chassisRoster.Where(IsChassisAvailable);
        public IEnumerable<LogicCoreDefinition> VaultLogicCores => _coreRoster.Where(c => c != null);
        public IEnumerable<AppendageActionDefinition> VaultAppendages => _appendageRoster.Where(IsCardAvailable);

        // --- The screen and its target -----------------------------------------------------

        public bool IsOpen { get; private set; }

        public GolemEntity TargetGolem { get; private set; }

        /// <summary>Bumped whenever a redraw would show something different.</summary>
        public int Version { get; private set; }

        public void Open()
        {
            IsOpen = true;
            LoadDraftFromGolem();
        }

        public void Close() => IsOpen = false;

        public void RetargetGolem(GolemEntity golem)
        {
            TargetGolem = golem;
            LoadDraftFromGolem();
        }

        private void LoadDraftFromGolem()
        {
            Version++;
            if (TargetGolem == null)
            {
                DraftOverflowCount = 0;
                return;
            }

            GolemProgram program = TargetGolem.Program;
            _draftChassis = program.chassis;
            _draftLogicCore = program.logicCore;
            // EVERY socket is cleared before the incoming program fills any: the draft used to
            // overwrite only the indices the new program filled, so a retarget onto a golem with
            // fewer steps leaked the old golem's tail onto it.
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = null;
                _draftQuantities[i] = WorkbenchQuantityPolicy.MinQuantity;
                _draftItemTypes[i] = "";
            }
            for (int i = 0; i < program.appendages.Count && i < _draftAppendages.Length; i++)
            {
                _draftAppendages[i] = program.appendages[i];
                _draftQuantities[i] = WorkbenchQuantityPolicy.Clamp(program.GetQuantityAt(i));
                _draftItemTypes[i] = program.GetItemTypeAt(i);
            }
            DraftOverflowCount = Math.Max(0, program.appendages.Count - _draftAppendages.Length);
        }

        // --- The draft ---------------------------------------------------------------------

        public int SocketCount => _draftAppendages.Length;
        public ChassisDefinition DraftChassis => _draftChassis;
        public LogicCoreDefinition DraftLogicCore => _draftLogicCore;

        /// <summary>Program steps the draft could not hold (more steps than sockets).</summary>
        public int DraftOverflowCount { get; private set; }

        public AppendageActionDefinition DraftAppendageAt(int index) =>
            index >= 0 && index < _draftAppendages.Length ? _draftAppendages[index] : null;

        public int DraftQuantityAt(int index) =>
            index >= 0 && index < _draftQuantities.Length ? _draftQuantities[index] : WorkbenchQuantityPolicy.MinQuantity;

        public int AssignedAppendageCount => _draftAppendages.Count(a => a != null);

        private int DraftMaxSlots => _draftChassis != null ? _draftChassis.maxAppendageSlots : 0;

        private bool SlotActive(int index) => WorkbenchDropRules.SlotWithinChassis(index, DraftMaxSlots);

        /// <summary>
        /// Whether a socket is drawn: inside the chassis, or holding a card anyway (a step with
        /// no chassis -- old demo golems -- must still be visible and draggable back out).
        /// </summary>
        public bool SlotVisible(int index) => WorkbenchDropRules.SlotVisible(index, DraftMaxSlots, DraftAppendageAt(index) != null);

        /// <summary>Only a socketed card whose quantity changes something gets a batch-size dial.</summary>
        public bool SlotHasStepper(int index)
        {
            AppendageActionDefinition card = DraftAppendageAt(index);
            return card != null && WorkbenchQuantityPolicy.TakesQuantity(card);
        }

        // --- Drag and drop -----------------------------------------------------------------

        public DropZoneHighlight LogicHighlight { get; private set; }
        public DropZoneHighlight[] AppendageHighlights { get; }

        /// <summary>Lights the sockets that would take <paramref name="card"/>, and reds the rest.</summary>
        public void BeginCardDrag(WorkbenchCardRef card)
        {
            bool isCore = card.LogicCore != null;
            int maxSlots = DraftMaxSlots;
            LogicHighlight = WorkbenchDropRules.AcceptsCard(DropZoneKind.LogicCore, -1, isCore, maxSlots)
                ? DropZoneHighlight.Valid : DropZoneHighlight.Invalid;
            for (int i = 0; i < AppendageHighlights.Length; i++)
            {
                AppendageHighlights[i] = WorkbenchDropRules.AcceptsCard(DropZoneKind.Appendage, i, isCore, maxSlots)
                    ? DropZoneHighlight.Valid : DropZoneHighlight.Invalid;
            }
        }

        public void EndCardDrag()
        {
            LogicHighlight = DropZoneHighlight.Neutral;
            for (int i = 0; i < AppendageHighlights.Length; i++)
            {
                AppendageHighlights[i] = DropZoneHighlight.Neutral;
            }
        }

        /// <summary>
        /// A card released over <paramref name="zone"/>, or over nothing (null): a socketed card
        /// dropped on nothing leaves its socket; a vault card dropped on nothing changes nothing.
        /// A card carries its batch size with it between sockets; a fresh vault card starts at
        /// its own authored default.
        /// </summary>
        public void HandleDrop(WorkbenchCardRef card, WorkbenchZone? zone)
        {
            if (zone == null)
            {
                if (!card.IsVaultOrigin)
                {
                    if (card.LogicCore != null)
                    {
                        _draftLogicCore = null;
                    }
                    else if (card.SourceAppendageIndex >= 0 && card.SourceAppendageIndex < _draftAppendages.Length)
                    {
                        _draftAppendages[card.SourceAppendageIndex] = null;
                        _draftQuantities[card.SourceAppendageIndex] = WorkbenchQuantityPolicy.MinQuantity;
                        _draftItemTypes[card.SourceAppendageIndex] = "";
                    }
                }
            }
            else if (zone.Value.Kind == DropZoneKind.LogicCore && card.LogicCore != null)
            {
                _draftLogicCore = card.LogicCore;
            }
            else if (zone.Value.Kind == DropZoneKind.Appendage && card.Appendage != null
                     && zone.Value.AppendageIndex >= 0 && zone.Value.AppendageIndex < _draftAppendages.Length
                     && SlotActive(zone.Value.AppendageIndex))
            {
                int target = zone.Value.AppendageIndex;
                bool fromSocket = !card.IsVaultOrigin && card.SourceAppendageIndex >= 0;
                int arriving = fromSocket
                    ? _draftQuantities[card.SourceAppendageIndex]
                    : WorkbenchQuantityPolicy.Clamp(card.Appendage.haulQuantity);
                string arrivingType = fromSocket ? _draftItemTypes[card.SourceAppendageIndex] : card.Appendage.inputItemType ?? "";
                if (fromSocket && card.SourceAppendageIndex != target)
                {
                    _draftAppendages[card.SourceAppendageIndex] = null;
                    _draftQuantities[card.SourceAppendageIndex] = WorkbenchQuantityPolicy.MinQuantity;
                    _draftItemTypes[card.SourceAppendageIndex] = "";
                }
                _draftAppendages[target] = card.Appendage;
                _draftQuantities[target] = arriving;
                _draftItemTypes[target] = arrivingType;
            }
            EndCardDrag();
            Version++;
        }

        public void RemoveFromSlot(WorkbenchCardRef card) => HandleDrop(card, null);

        /// <summary>The good the draft's Haul in <paramref name="index"/> takes ("" for a non-Haul slot).</summary>
        public string DraftItemTypeAt(int index) =>
            IsDraftHaul(index) ? (_draftItemTypes[index] ?? "") : "";

        /// <summary>Whether the draft slot holds a Haul -- the one card with a good to pick.</summary>
        public bool IsDraftHaul(int index) =>
            index >= 0 && index < _draftAppendages.Length && _draftAppendages[index] != null
            && _draftAppendages[index].actionType == AppendageActionType.Haul;

        /// <summary>
        /// The goods a Haul can be set to, in the economy's own order: every good the factory has
        /// made (the tech tree's ledger, which only grows), plus Scrap, which every Haul starts on.
        /// Unknown context -- a test, or a scene with no ledger -- offers every good.
        /// </summary>
        public Func<string, bool> HaulableGood { get; set; }

        public IReadOnlyList<string> HaulOptions =>
            ItemTiers.CanonicalOrder.Where(t => t == ItemType.Scrap || HaulableGood == null || HaulableGood(t)).ToList();

        /// <summary>The good picker's arrows: step a Haul slot's good through <see cref="HaulOptions"/>.</summary>
        public void CycleDraftItemType(int index, int delta)
        {
            if (!IsDraftHaul(index))
            {
                return;
            }
            IReadOnlyList<string> options = HaulOptions;
            if (options.Count == 0)
            {
                return;
            }
            int at = -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == _draftItemTypes[index])
                {
                    at = i;
                }
            }
            int next = at < 0 ? 0 : ((at + delta) % options.Count + options.Count) % options.Count;
            _draftItemTypes[index] = options[next];
            Version++;
        }

        public void AdjustDraftQuantity(int index, int delta)
        {
            if (index < 0 || index >= _draftQuantities.Length || _draftAppendages[index] == null)
            {
                return;
            }
            _draftQuantities[index] = WorkbenchQuantityPolicy.Step(_draftQuantities[index], delta);
            Version++;
        }

        /// <summary>Fits <paramref name="chassis"/> to the draft, unless the draft already uses more slots than it has.</summary>
        public bool SelectChassis(ChassisDefinition chassis)
        {
            int assigned = AssignedAppendageCount;
            if (chassis != null && assigned > chassis.maxAppendageSlots)
            {
                ChassisRejected?.Invoke(chassis);
                SetStatus(
                    $"Cannot switch chassis: {WorkbenchDiagnostics.Humanize(chassis.name)} has {chassis.maxAppendageSlots} slots, the draft uses {assigned}.",
                    WorkbenchStatusReason.ChassisTooSmall, chassis.maxAppendageSlots);
                return false;
            }
            _draftChassis = chassis;
            ClearStatus();
            Version++;
            return true;
        }

        // --- Commit: the lever, the patent, a loaded blueprint ------------------------------

        /// <summary>
        /// The Engage Gears lever: rebuilds the target's program from the draft. Refused --
        /// with a status saying why, and the lever's judder -- when there is no target or when
        /// the draft could not hold the whole program.
        /// </summary>
        public bool Engage()
        {
            if (TargetGolem == null)
            {
                SetStatus(NoTargetMessage, WorkbenchStatusReason.NoTarget);
                LeverRefused?.Invoke();
                return false;
            }

            if (DraftOverflowCount > 0)
            {
                SetStatus(
                    $"Cannot engage: this golem's program has {DraftOverflowCount} more " +
                    $"appendage{(DraftOverflowCount == 1 ? "" : "s")} than this Workbench has " +
                    $"sockets ({_draftAppendages.Length}). Engaging would discard them.",
                    WorkbenchStatusReason.DraftTruncated);
                LeverRefused?.Invoke();
                return false;
            }

            GolemProgram program = TargetGolem.Program;
            while (program.appendages.Count > 0)
            {
                program.RemoveAppendageAt(0);
            }
            if (_draftChassis != null && !program.TryAssignChassis(_draftChassis))
            {
                SetStatus("Cannot engage: chassis rejected the current appendage count.", WorkbenchStatusReason.Info);
                LeverRefused?.Invoke();
                return false;
            }

            int committed = 0;
            for (int i = 0; i < _draftAppendages.Length; i++)
            {
                if (_draftAppendages[i] != null && program.TryAddAppendage(_draftAppendages[i]))
                {
                    program.SetQuantityAt(committed, _draftQuantities[i]);
                    program.SetItemTypeAt(committed, _draftItemTypes[i]);
                    committed++;
                }
            }
            program.logicCore = _draftLogicCore;
            program.CurrentStepIndex = 0;
            program.StepProgressTicks = 0;
            program.State = GolemState.Idle;

            SetStatus("Gears engaged. New configuration is live.", WorkbenchStatusReason.Info);
            LeverPulled?.Invoke();
            Version++;
            return true;
        }

        /// <summary>Stamps the draft into the patent registry as the next BP-nnn. Free (root CLAUDE.md).</summary>
        public Blueprint Patent()
        {
            if (DraftOverflowCount > 0)
            {
                SetStatus(
                    $"Cannot patent: the draft is missing {DraftOverflowCount} appendage" +
                    $"{(DraftOverflowCount == 1 ? "" : "s")} this Workbench has no socket for.",
                    WorkbenchStatusReason.DraftTruncated);
                return null;
            }

            string id = $"BP-{_nextBlueprintNumber:D3}";
            _nextBlueprintNumber++;
            var filled = Enumerable.Range(0, _draftAppendages.Length).Where(i => _draftAppendages[i] != null).ToList();
            var blueprint = new Blueprint(id, PlaceableBuilding.LocalPlayerOwnerId, _draftChassis, _draftLogicCore,
                filled.Select(i => _draftAppendages[i]).ToList(),
                filled.Select(i => _draftItemTypes[i]).ToList(),
                filled.Select(i => _draftQuantities[i]).ToList());
            _patents?.TryPatent(blueprint);
            SetStatus($"Patented as {id}.", WorkbenchStatusReason.Info);
            return blueprint;
        }

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
                AppendageActionDefinition card = i < blueprint.Appendages.Count ? blueprint.Appendages[i] : null;
                _draftAppendages[i] = card;
                // A patent keeps each slot's good and batch (G10); one from before falls back to the card's.
                _draftItemTypes[i] = card == null ? ""
                    : blueprint.ItemTypes != null && i < blueprint.ItemTypes.Count ? blueprint.ItemTypes[i] : card.inputItemType ?? "";
                _draftQuantities[i] = card == null ? WorkbenchQuantityPolicy.MinQuantity
                    : blueprint.Quantities != null && i < blueprint.Quantities.Count ? WorkbenchQuantityPolicy.Clamp(blueprint.Quantities[i])
                    : WorkbenchQuantityPolicy.Clamp(card.haulQuantity);
            }
            DraftOverflowCount = Math.Max(0, blueprint.Appendages.Count - _draftAppendages.Length);
            SetStatus($"Loaded {blueprint.BlueprintId} into the draft.", WorkbenchStatusReason.Info);
            Version++;
        }

        // --- Status line -------------------------------------------------------------------

        public string StatusText { get; private set; } = "";
        public WorkbenchStatusReason StatusReason { get; private set; } = WorkbenchStatusReason.None;
        public bool StatusIsInfo => StatusReason == WorkbenchStatusReason.Info;

        /// <summary>The lever is live only with a golem to program.</summary>
        public bool CanEngage => TargetGolem != null;

        /// <summary>
        /// Per frame: retires a status line once what it described stops being true (or, for a
        /// one-off report, once it has gone stale), and says so when there is nothing targeted.
        /// </summary>
        public void Tick(float seconds)
        {
            if (StatusReason != WorkbenchStatusReason.None)
            {
                _statusShownSeconds += seconds;
                if (WorkbenchStatusPolicy.ShouldClear(StatusReason, _statusShownSeconds, AssignedAppendageCount,
                        _statusChassisSlotLimit, TargetGolem != null, DraftOverflowCount > 0))
                {
                    ClearStatus();
                }
            }
            if (TargetGolem == null && StatusReason == WorkbenchStatusReason.None)
            {
                SetStatus(NoTargetMessage, WorkbenchStatusReason.NoTarget);
            }
        }

        private void SetStatus(string message, WorkbenchStatusReason reason, int chassisSlotLimit = 0)
        {
            StatusReason = string.IsNullOrEmpty(message) ? WorkbenchStatusReason.None : reason;
            StatusText = message ?? "";
            _statusShownSeconds = 0f;
            _statusChassisSlotLimit = chassisSlotLimit;
        }

        private void ClearStatus() => SetStatus(string.Empty, WorkbenchStatusReason.None);

        // --- Readouts ----------------------------------------------------------------------

        /// <summary>The loop label under socket <paramref name="index"/>: "STEP 2  ·  loops back to 1".</summary>
        public string SlotCaption(int index) =>
            WorkbenchLoopLabels.Compose(index + 1, DraftAppendageAt(index) != null, LastFilledStep, DraftMaxSlots);

        /// <summary>The trigger socket's caption, teaching whichever thing is missing.</summary>
        public string TriggerCaption =>
            WorkbenchLoopLabels.Compose(WorkbenchLoopLabels.TriggerRow, _draftLogicCore != null, LastFilledStep, DraftMaxSlots);

        private int LastFilledStep => WorkbenchLoopLabels.LastFilledStep(
            _draftAppendages.Length, step => step >= 1 && step <= _draftAppendages.Length && _draftAppendages[step - 1] != null);

        /// <summary>The diagnostic tape along the bottom.</summary>
        public string Ticker(float ticksPerSecond = 2f)
        {
            int steps = AssignedAppendageCount;
            int cycle = _draftAppendages.Where(a => a != null)
                .Sum(a => Math.Max(a.durationTicks, WorkbenchDiagnostics.MinimumStepTicks));
            int tier = _draftChassis != null ? _draftChassis.tier : 1;
            return WorkbenchDiagnostics.ComposeTicker(
                _draftChassis?.name, steps, DraftMaxSlots,
                _draftLogicCore != null ? CardDisplayName(_draftLogicCore, null) : null,
                cycle, WorkbenchDiagnostics.ComputeSteamDraw(steps, tier),
                WorkbenchDiagnostics.ComputeCyclesPerMinute(cycle, ticksPerSecond));
        }

        public string ChassisNameLine =>
            _draftChassis != null ? WorkbenchDiagnostics.Humanize(_draftChassis.name) : "No chassis fitted";

        public string ChassisStatsLine =>
            _draftChassis != null
                ? $"{AssignedAppendageCount} / {_draftChassis.maxAppendageSlots} slots filled  ·  tier {_draftChassis.tier}"
                : "Pick a chassis from the rack";

        /// <summary>"TARGET  ·  PlayerGolem-003" -- named by GolemId, never by the object's name.</summary>
        public string TargetHeader => TargetGolem != null ? $"TARGET  ·  {GolemDisplayName(TargetGolem)}" : "TARGET  ·  none";

        public static string GolemDisplayName(GolemEntity golem) =>
            !string.IsNullOrEmpty(golem.GolemId) ? golem.GolemId : golem.name;

        public static string CardDisplayName(LogicCoreDefinition core, AppendageActionDefinition appendage)
        {
            if (core != null)
            {
                return WorkbenchDiagnostics.DisplayName(core.name, core.triggerType + "Core");
            }
            return appendage != null ? WorkbenchDiagnostics.DisplayName(appendage.name, appendage.actionType.ToString()) : string.Empty;
        }

        public static string CardSubtitle(LogicCoreDefinition core, AppendageActionDefinition appendage)
        {
            if (core != null)
            {
                switch (core.triggerType)
                {
                    case TriggerType.Interval: return $"trigger · every {core.intervalTicks} ticks";
                    case TriggerType.Threshold: return $"trigger · {core.thresholdBufferId} ≥ {core.thresholdQuantity}";
                    case TriggerType.Signal: return $"trigger · on signal from {core.signalGolemId}";
                    default: return "trigger · always on";
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

        public static string ChassisSubtitle(ChassisDefinition chassis)
        {
            string cost = chassis.cost != null && chassis.cost.Count > 0 ? $"  ·  {chassis.cost.Count} goods" : string.Empty;
            return $"{chassis.maxAppendageSlots} slots  ·  tier {chassis.tier}{cost}";
        }
    }
}
