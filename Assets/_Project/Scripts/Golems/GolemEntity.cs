using UnityEngine;
using GolemFactory.Simulation;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Golems
{
    public sealed class GolemEntity : MonoBehaviour, ITickable
    {
        [SerializeField] private string golemId;
        [SerializeField] private GolemProgram program = new GolemProgram();
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;

        // --- Facing-based spatial routing -------------------------------------------------
        // docs/digital-design.md "Grid & Movement Mechanics": a golem is fixed to a tile,
        // faces one of four directions, pulls from the tile behind it and pushes to the tile
        // in front. Before this, routing was purely by the bare-string sourceId/destinationId
        // baked into the appendage asset, which made a golem's position on the map entirely
        // decorative -- one in the far corner behaved identically to one sitting on the belt.
        [SerializeField] private SpatialEndpointRegistryHolder spatialEndpointHolder;
        [SerializeField] private Vector2Int cell;
        [SerializeField] private Facing facing = Facing.North;

        // --- Steam power (docs/progression-design.md §3.1) -----------------------------------
        // OPT-IN, exactly like spatialEndpointHolder above and bufferRegistryHolder before it.
        // A golem that is never handed a steam network is EXEMPT and always runs -- which is
        // what leaves Main.unity's seven hand-wired demos and every pre-existing test untouched
        // by the single largest addition in the progression design. This is the third use of
        // this same fork (economy registries, spatial routing, now steam) and it is the reason
        // none of the three needed a migration pass over existing content.
        //
        // Not a [SerializeField]: it is wired by ConfigureSteam from a bootstrap or a
        // construction station, so an Inspector cannot accidentally steam-gate a demo golem.
        private SteamNetworkHolder steamNetworkHolder;

        // --- The 2-extractor-per-node cap (docs/progression-design.md §3.2) ------------------
        // OPT-IN on exactly the same fork as steam above, and for the same reason: a golem
        // never handed a NodeExtractorRegistry is outside the mechanic and may work any node it
        // faces, which is what leaves Main.unity's demos and every pre-existing extraction test
        // untouched. Not a [SerializeField], again so an Inspector cannot cap a demo golem.
        private NodeExtractorRegistryHolder nodeExtractorHolder;

        // The tick currently executing, captured at the top of Tick so the precondition check
        // inside BeginStep can ask the network for THIS tick's powered set. Threading a tick
        // parameter through BeginStep and all six Begin* methods was the alternative; it was
        // rejected because exactly one of them would use it.
        private long _currentTick;

        // --- Was this golem built during play, or authored into the scene? -------------------
        // Read only by the save system, which respawns the former and never the latter.
        //
        // The distinction is not cosmetic. A scene golem is hand-wired -- Main.unity's seven
        // demos carry serialized references, deliberately-absent spatial routing, and ids that
        // its bootstraps look up -- so rebuilding one from GolemPrefab would produce a
        // different object wearing the same name. A player-built golem, by contrast, is
        // *entirely* described by GolemPrefab plus the station's wiring plus its saved program,
        // which is exactly what makes it reconstructible.
        //
        // Not a [SerializeField], for the same reason steamNetworkHolder is not: authoring it
        // in the Inspector would let a scene golem claim to be reconstructible when it is not,
        // and false is the answer that preserves today's behaviour.
        private bool _isRuntimeSpawned;

        /// <summary>
        /// True for a golem instantiated during play (by <c>GolemConstructionStation</c>, or
        /// respawned from a save), false for one authored into the scene. Defaults to false, so
        /// a golem nobody marks is treated as scene furniture the save system must not recreate.
        /// </summary>
        public bool IsRuntimeSpawned => _isRuntimeSpawned;

        /// <summary>
        /// Marks this golem as built during play and therefore reconstructible from a save.
        /// One-way on purpose: nothing about loading or reprogramming a golem can turn a scene
        /// object into a spawnable one, and a method that could unset this would be a way to
        /// silently drop a player's golem from their save.
        /// </summary>
        public void MarkRuntimeSpawned() => _isRuntimeSpawned = true;

        public string GolemId => golemId;
        public GolemProgram Program => program;

        public Vector2Int Cell => cell;
        public Facing Facing => facing;

        /// <summary>Tile this golem pulls from -- directly behind it.</summary>
        public Vector2Int SourceCell => FacingUtility.SourceCell(cell, facing);

        /// <summary>Tile this golem pushes to -- directly in front of it.</summary>
        public Vector2Int TargetCell => FacingUtility.TargetCell(cell, facing);

        // Runtime-only diagnostics (deliberately not on GolemProgram, which is savable state):
        // why the current step is blocked and which belt/node/buffer id blocked it. Read by the
        // stall badge and the alerts strip so the player is told *why*, and by
        // UI/StallTracker.Reconcile so "who is stalled" can be re-derived from truth rather
        // than trusted to an event stream the listener may have joined late.
        private StallReason _stallReason;
        private string _stallResourceId;
        public StallReason StallReason => program.State == GolemState.Stalled ? _stallReason : StallReason.None;
        public string StallResourceId => program.State == GolemState.Stalled ? _stallResourceId : null;

        // HOW MANY MORE of _stallResourceId the step needed -- progression-design §8's "Why is
        // this golem stopped?" row requires the specific short ingredient *and amount* for
        // Assemble. On R15 (10 Casing + 6 Iron Plate + 4 Brass) "no Casing available" does not
        // tell the player whether they are one short or nine, and those are very different
        // factories.
        //
        // Carried as its own field rather than encoded into _stallResourceId ("3 Scrap"),
        // because InputFull/OutputFull/MissingItem all established that the resource id is the
        // BARE item type -- the badge, the alerts strip and a dozen tests read it that way, and
        // a smuggled quantity would break every one of them.
        private int _stallShortfall;

        /// <summary>
        /// Units still needed of <see cref="StallResourceId"/>, or 0 when the stall carries no
        /// meaningful amount (a Haul type mismatch, an empty Push hold, any non-Assemble stall).
        /// </summary>
        public int StallShortfall => program.State == GolemState.Stalled ? _stallShortfall : 0;

        // Written by whichever Begin* method is running, read once in Tick. A second out
        // parameter threaded through BeginStep and all six Begin* methods was the obvious
        // alternative; it was rejected because exactly one verb has an amount to report and the
        // other five would each have had to write `shortfall = 0` in every return path. Reset at
        // the top of BeginStep, so it can never survive into a later step's stall.
        private int _pendingShortfall;

        // --- The machine model (docs/progression-design.md §2) -------------------------------
        // The golem's own typed input/output stock. Runtime state, not [SerializeField]: it is
        // saved and restored explicitly through SaveLoadService (like StallReason it is derived
        // working state, and unlike GolemProgram it is not something the Inspector should let
        // you hand-author into an impossible configuration).
        private readonly GolemInventory _inventory = new GolemInventory();
        public GolemInventory Inventory => _inventory;

        // How long the CURRENT step runs for. Captured once, in BeginStep, because three of the
        // five verbs derive their duration from a quantity that is only known at Begin time --
        // Push in particular is `2 + unitCount` where unitCount is what actually left the golem,
        // which cannot be recomputed later once the stock has been emptied. Steps that keep
        // their authored duration (Refine, Assemble, and everything on the id-routed path) just
        // set this to step.durationTicks.
        private int _stepDuration = 1;

        // Programmatic setup used by tests (and available for runtime bootstrapping), mirroring
        // BuildModeController.Configure -- avoids requiring Inspector-assigned references.
        public void Configure(string id, ConveyorSystemHolder holder)
        {
            golemId = id;
            conveyorHolder = holder;
            // No-op unless a steam network was already wired. It is here because the consumer
            // key IS the golem id, so a ConfigureSteam that ran before the id was set would
            // otherwise register nothing and leave the golem permanently unpowered.
            RegisterSteamConsumer();
        }

        // M5: separate from Configure so existing two-arg call sites (M4 tests/bootstrap)
        // are untouched -- the economy registries are opt-in, only ExtractFromNode/
        // LoadIntoBuffer/Refine need them.
        public void ConfigureEconomy(ResourceNodeRegistryHolder nodes, StorageBufferRegistryHolder buffers)
        {
            nodeRegistryHolder = nodes;
            bufferRegistryHolder = buffers;
        }

        // Separate again, for the same reason ConfigureEconomy was separated from Configure:
        // every existing call site (Main.unity's demo bootstraps, the whole test suite) keeps
        // working untouched by simply never calling this. A golem that never gets a spatial
        // registry routes purely by id, exactly as before.
        public void ConfigureSpatial(SpatialEndpointRegistryHolder endpoints, Vector2Int placedCell, Facing placedFacing)
        {
            spatialEndpointHolder = endpoints;
            cell = placedCell;
            facing = placedFacing;
        }

        /// <summary>
        /// Puts this golem on the steam grid. Separate again, for exactly the reason
        /// ConfigureEconomy and ConfigureSpatial are separate: every existing call site keeps
        /// working untouched by simply never calling it, and a golem with no steam network is
        /// exempt from the NoSteam precondition entirely.
        ///
        /// Registering the consumer here rather than in Awake/OnEnable is deliberate: those do
        /// not run in EditMode (no [ExecuteAlways] anywhere in this project), so an EditMode
        /// test would otherwise have a configured golem the network had never heard of.
        /// </summary>
        public void ConfigureSteam(SteamNetworkHolder steam)
        {
            steamNetworkHolder = steam;
            RegisterSteamConsumer();
        }

        /// <summary>
        /// Subjects this golem to §3.2's two-extractor-per-node cap. Separate from
        /// ConfigureSteam for the same reason every other Configure* here is separate: a golem
        /// that never gets one may work any node it faces.
        ///
        /// <para>
        /// Nothing is claimed here, unlike ConfigureSteam. A claim is against a specific NODE,
        /// and which node this golem works is decided by the tile behind it -- which can change
        /// when the player rotates or moves it, and which is not knowable until the endpoint
        /// registry is consulted. So the claim is filed at the moment the golem actually
        /// attempts an Extract (see <c>BeginExtractFromNode</c>), where the node is in hand.
        /// </para>
        /// </summary>
        public void ConfigureNodeExtractorCap(NodeExtractorRegistryHolder cap) =>
            nodeExtractorHolder = cap;

        /// <summary>
        /// Moves/rotates the golem without re-supplying the registry. "Golems cannot pivot" is a
        /// rule about *runtime execution* -- nothing in a program can turn the golem -- not about
        /// the player repositioning one between runs, which is the core spatial puzzle.
        /// </summary>
        public void SetPlacement(Vector2Int placedCell, Facing placedFacing)
        {
            cell = placedCell;
            facing = placedFacing;
            // The steam grid is keyed by cell, so moving a golem has to move its registration or
            // it keeps drawing power from wherever it used to stand. RegisterConsumer is
            // idempotent and re-sorts only when the cell actually changed.
            RegisterSteamConsumer();

            // A node claim is dropped rather than moved: the golem may now face a different
            // node, or none at all, and holding a slot at the seam it used to work would starve
            // whichever golem the cap was refusing. It re-files on its next Extract, at its new
            // cell, against whatever is actually behind it now.
            ReleaseNodeClaim();
        }

        // Re-registration is funnelled through here (ConfigureSteam, SetPlacement, SetHeld,
        // OnEnable) so "which cell is this golem drawing steam on" has exactly one writer.
        private void RegisterSteamConsumer()
        {
            if (steamNetworkHolder == null || string.IsNullOrEmpty(golemId))
            {
                return;
            }

            steamNetworkHolder.Network.RegisterConsumer(golemId, cell);
        }

        private void UnregisterSteamConsumer()
        {
            if (steamNetworkHolder == null || string.IsNullOrEmpty(golemId))
            {
                return;
            }

            steamNetworkHolder.Network.UnregisterConsumer(golemId);
        }

        // Releases this golem's node claim, if it holds one. Called whenever it stops being a
        // candidate crew member: it is disabled/destroyed, the player picks it up, or it turns
        // to face something that is not a node. A claim that outlived the golem holding it
        // would permanently under-crew a seam with nothing on the tile to explain why -- which
        // is precisely the failure mode NodeExtractorRegistry's re-derive-from-claims design
        // exists to keep impossible.
        private void ReleaseNodeClaim()
        {
            if (nodeExtractorHolder == null || string.IsNullOrEmpty(golemId))
            {
                return;
            }

            nodeExtractorHolder.Registry.UnregisterExtractor(golemId);
        }

        // M7: Signal trigger is inherently event-driven (there's no already-held state to
        // poll, unlike Threshold's buffer query), so subscribe/unsubscribe on the
        // MonoBehaviour lifecycle -- same idiom M6's UI listeners established.
        private void OnEnable()
        {
            EventBus.GolemCompleted += OnGolemCompletedForSignal;
            // Paired with the unregister below, so a disabled/re-enabled golem stops and
            // resumes drawing steam. Play-mode only (no [ExecuteAlways]), which is exactly why
            // ConfigureSteam registers directly rather than relying on this.
            RegisterSteamConsumer();
        }

        private void OnDisable()
        {
            EventBus.GolemCompleted -= OnGolemCompletedForSignal;
            UnregisterSteamConsumer();
            ReleaseNodeClaim();
        }

        private void OnGolemCompletedForSignal(GolemCompletedEvent e)
        {
            LogicCoreDefinition logicCore = program.logicCore;
            if (logicCore != null && logicCore.triggerType == TriggerType.Signal && e.GolemId == logicCore.signalGolemId)
            {
                program.PendingSignal = true;
            }
        }

        /// <summary>
        /// True while the player is carrying this golem to a new tile. A held golem does not
        /// run: its Cell is stale by definition (it is in the player's hands, not on the tile
        /// its routing still names), so letting it keep pulling and pushing would move items
        /// between two tiles it is no longer standing between.
        /// </summary>
        public bool IsHeld { get; private set; }

        public void SetHeld(bool held)
        {
            IsHeld = held;
            // A held golem stops costing Coke. Its Cell is stale by definition -- it is in the
            // player's hands, not on the tile its routing names -- so leaving it registered
            // would have a boiler paying upkeep for a golem that is not standing anywhere, and
            // worse, holding one of that boiler's 8 slots against a golem that could use it.
            if (held)
            {
                UnregisterSteamConsumer();
                // And it stops crewing whatever seam it was working, for the identical reason:
                // a golem in the player's hands holding one of a node's two slots against a
                // golem that is actually standing there would be the same bug in a different
                // registry. It re-applies on its first Extract after being put down.
                ReleaseNodeClaim();
                return;
            }

            RegisterSteamConsumer();
        }

        public void Tick(long tick)
        {
            if (IsHeld)
            {
                return;
            }

            _currentTick = tick;

            bool wasStalled = program.State == GolemState.Stalled;

            if (program.State == GolemState.Idle)
            {
                if (!ShouldTrigger(tick))
                {
                    return;
                }

                program.State = GolemState.Running;
            }

            AppendageActionDefinition step = program.CurrentStep;
            if (step == null)
            {
                program.State = GolemState.Idle;
                return;
            }

            // Begin runs exactly once per step attempt (StepProgressTicks == 0): it's where
            // a step's precondition is checked and its side effect (withdraw/enqueue/dequeue)
            // happens. A step that stalls here never touched StepProgressTicks, so retrying
            // next tick re-attempts Begin rather than resuming mid-processing.
            if (program.StepProgressTicks == 0)
            {
                string blockedResourceId;
                StallReason reason = BeginStep(step, out blockedResourceId);
                if (reason != StallReason.None)
                {
                    // Edge-triggered: publish only when entering Stalled, or when the *reason*
                    // changes while already stalled (e.g. the belt drains and the node turns
                    // out to be empty too). Republishing every tick re-armed GolemVisual's
                    // stall shake 10x/second so the "single jolt" never decayed, and buried
                    // any listener that wanted to react once per incident.
                    //
                    // The shortfall amount counts as part of the identity: while stalled the
                    // golem's input stock is frozen (nothing ahead of the blocked step runs), so
                    // it can only change if the underlying situation genuinely did -- no risk of
                    // the per-tick republishing this guard exists to prevent.
                    bool isNewIncident = !wasStalled ||
                        reason != _stallReason ||
                        blockedResourceId != _stallResourceId ||
                        _pendingShortfall != _stallShortfall;

                    program.State = GolemState.Stalled;
                    _stallReason = reason;
                    _stallResourceId = blockedResourceId;
                    _stallShortfall = _pendingShortfall;

                    if (isNewIncident)
                    {
                        EventBus.Publish(new GolemStalledEvent(
                            golemId, reason, blockedResourceId, program.CurrentStepIndex,
                            _stallShortfall));
                    }
                    return;
                }
            }

            // wasStalled can only be true here if StepProgressTicks was 0 (Stalled is only
            // ever set in the guard clause above, which requires StepProgressTicks == 0),
            // so reaching this point means TryBeginStep just succeeded -- a genuine recovery,
            // not a continuation of an already-running multi-tick step.
            if (wasStalled)
            {
                EventBus.Publish(new GolemResumedEvent(golemId));
            }

            // Recovers a golem from Stalled/mid-cycle back to Running -- the M4 code never
            // did this explicitly, which was harmless when every step resolved in one tick
            // but would leave a resumed multi-tick step's state reading "Stalled" forever.
            program.State = GolemState.Running;
            program.StepProgressTicks++;
            // _stepDuration, not step.durationTicks: see the field comment. It is always set by
            // the BeginStep above (which runs on the StepProgressTicks == 0 tick), and clamped
            // here rather than there so a hand-authored 0 can never make a step never finish.
            int duration = Mathf.Max(1, _stepDuration);
            if (program.StepProgressTicks < duration)
            {
                return;
            }

            CompleteStep(step);
            program.AdvanceStep();
            if (program.CurrentStepIndex == 0)
            {
                program.State = GolemState.Idle;
                EventBus.Publish(new GolemCompletedEvent(golemId));
            }
        }

        private bool ShouldTrigger(long tick)
        {
            LogicCoreDefinition logicCore = program.logicCore;
            if (logicCore == null)
            {
                return false;
            }

            switch (logicCore.triggerType)
            {
                case TriggerType.AlwaysOn:
                    return true;
                case TriggerType.Interval:
                    return logicCore.intervalTicks > 0 && tick % logicCore.intervalTicks == 0;
                case TriggerType.Threshold:
                    return ShouldTriggerThreshold(logicCore);
                case TriggerType.Signal:
                    if (!program.PendingSignal)
                    {
                        return false;
                    }
                    program.PendingSignal = false;
                    return true;
                default:
                    return false;
            }
        }

        // Edge-triggered, not level-triggered: fires once when the watched quantity
        // reaches/crosses thresholdQuantity, then stays disarmed (won't refire every tick
        // just because the level is still at/above threshold) until it dips back below and
        // crosses again. Directly polls the already-held bufferRegistryHolder rather than
        // going through a separate trigger-watching system -- no event subscription needed
        // since the state to check is already available every tick.
        private bool ShouldTriggerThreshold(LogicCoreDefinition logicCore)
        {
            int quantity = 0;
            if (bufferRegistryHolder != null &&
                bufferRegistryHolder.Registry.TryGetBuffer(logicCore.thresholdBufferId, out StorageBuffer buffer))
            {
                quantity = buffer.GetQuantity(logicCore.thresholdItemType);
            }

            bool atOrAboveThreshold = quantity >= logicCore.thresholdQuantity;
            if (!atOrAboveThreshold)
            {
                program.ThresholdArmed = true;
                return false;
            }

            if (!program.ThresholdArmed)
            {
                return false;
            }

            program.ThresholdArmed = false;
            EventBus.Publish(new ThresholdCrossedEvent(logicCore.thresholdBufferId, quantity));
            return true;
        }

        // Returns StallReason.None on success, otherwise why the step is blocked and (via
        // blockedResourceId) which belt/node/buffer id blocked it, so the badge and alerts
        // strip can name the actual culprit instead of only reporting that something failed.
        private StallReason BeginStep(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;

            // Cleared every Begin attempt so a stale amount from a previous step can never be
            // reported against this one; only BeginAssemble ever sets it.
            _pendingShortfall = 0;

            // The authored duration is the default; the three quantity-derived verbs overwrite
            // it inside their own Begin, once they know how much they actually moved, and
            // Assemble replaces it with its recipe's.
            _stepDuration = Mathf.Max(1, step.durationTicks);

            // --- Steam (docs/progression-design.md §3.1) --------------------------------------
            // FIRST, ahead of every verb, and therefore ahead of every side effect. This is the
            // whole reason it lives in BeginStep rather than in Tick: BeginStep is where a
            // step's precondition is checked and its withdraw/enqueue/dequeue happens, and an
            // unpowered golem must stall BEFORE any of that -- never half-execute a step, never
            // silently skip the tick, and never consume from a node it cannot then push from.
            //
            // §3.1: "a golem halting on an unmet power precondition is the existing rigidity
            // rule applied to a new precondition, not a departure from it". So it returns a
            // StallReason like any other failure and rides the identical publishing, retry and
            // resume machinery in Tick -- no new state, no new event, no special case.
            //
            // Checked at Begin rather than every tick, which means a golem mid-step finishes
            // that step and stalls at the start of the next. That is the same contract every
            // other precondition has (a node emptying mid-Extract does not abort it either),
            // and it keeps a multi-tick step atomic.
            StallReason steamStall = CheckSteamPower(out blockedResourceId);
            if (steamStall != StallReason.None)
            {
                return steamStall;
            }

            switch (step.actionType)
            {
                case AppendageActionType.ExtractFromNode:
                    return BeginExtractFromNode(step, out blockedResourceId);
                case AppendageActionType.LoadIntoBuffer:
                    return BeginLoadIntoBuffer(step, out blockedResourceId);
                case AppendageActionType.Refine:
                    return BeginRefine(step, out blockedResourceId);
                case AppendageActionType.Haul:
                    return BeginHaul(step, out blockedResourceId);
                case AppendageActionType.Push:
                    return BeginPush(out blockedResourceId);
                case AppendageActionType.Assemble:
                    return BeginAssemble(step, out blockedResourceId);
                default:
                    return StallReason.None;
            }
        }

        /// <summary>Player-set batch size for the step currently executing.</summary>
        private int CurrentStepQuantity() => program.GetQuantityAt(program.CurrentStepIndex);

        /// <summary>
        /// Whether steam reaches this golem, or <see cref="StallReason.NoSteam"/> naming its
        /// tile if not.
        ///
        /// THE EXEMPTION IS THE FIRST LINE, and it is the whole compatibility story: a golem
        /// with no steam network configured is not "powered by a network with no boilers", it
        /// is outside the mechanic entirely. Main.unity's seven demos, every pre-existing test,
        /// and Sandbox.unity until requireSteamPower is turned on all take this branch.
        /// </summary>
        private StallReason CheckSteamPower(out string blockedResourceId)
        {
            blockedResourceId = null;

            if (steamNetworkHolder == null)
            {
                return StallReason.None;
            }

            if (steamNetworkHolder.Network.IsPowered(golemId, _currentTick))
            {
                return StallReason.None;
            }

            // The golem's OWN cell, matching NoSourceAtTile/NoTargetAtTile's convention of
            // naming the tile rather than a resource id -- every fix for this stall is spatial
            // (lay a pipe to here, move the golem to a pipe, build another boiler).
            blockedResourceId = cell.ToString();
            return StallReason.NoSteam;
        }

        // --- Spatial resolution -----------------------------------------------------------
        // The fallback keys on whether this golem is spatially placed AT ALL -- not on whether
        // an individual tile lookup happened to succeed. That distinction is the whole point.
        //
        // The first version of this decided per-endpoint: an empty tile silently reverted that
        // half of the step to the authored sourceId/destinationId. Which meant rotating a
        // player's golem away from its node did not stall it -- it quietly kept working by id,
        // making facing advisory for exactly the two actions players use most
        // (ExtractFromNode/LoadIntoBuffer) and defeating the entire purpose of the feature.
        //
        // So it is now a hard branch on the golem, decided once:
        //   * spatialEndpointHolder wired  -> STRICT spatial. Tiles are the only routing truth.
        //     An empty source tile stalls NoSourceAtTile, an empty/full target tile stalls
        //     NoTargetAtTile. The authored ids are never consulted.
        //   * spatialEndpointHolder null   -> pure id routing, byte for byte as it always was.
        //
        // Main.unity's seven hand-wired demo golems and the entire pre-existing test suite
        // never call ConfigureSpatial, so they all take the second branch and cannot be
        // affected by anything on the first one.
        //
        // THE MACHINE MODEL RIDES ON THE SAME FORK (progression-design §2). A spatially placed
        // golem no longer moves an item straight through from the tile behind to the tile in
        // front: Haul/ExtractFromNode deposit into its own typed input stock, Assemble converts
        // input stock to output stock, and Push empties stock onto the tile in front. Durations
        // become derived (max(2,qty) / 6+qty / 2+units) instead of the authored durationTicks.
        //
        // An id-routed golem keeps every one of the old semantics byte for byte, INCLUDING
        // step.durationTicks as its duration. Putting the new model on both paths was the
        // obvious alternative and was rejected: Main.unity is a diorama of seven hand-wired
        // demos whose whole job is to show M2-M7 still working, and a "Haul now fills an
        // invisible internal stock" change would silently turn all of them into golems that
        // consume forever and emit nothing. The fork already existed and already had exactly
        // the right shape, so the new model is layered onto its spatial half rather than
        // inventing a second axis of configuration to say which era a golem belongs to.
        //
        // The one exception is Assemble, which never touches a tile at all and therefore
        // behaves identically on both paths -- see BeginAssemble.
        private bool IsSpatiallyPlaced => spatialEndpointHolder != null;

        // --- The pure-logistics rule (progression-design §2) --------------------------------
        // "A program containing no Assemble step treats its input stock as its output stock."
        //
        // This is required, not an optimisation. Without it every logistics golem -- the
        // Scavenger (the first and most common unit), every node extractor, every belt-to-
        // buffer golem, roughly a quarter of the endgame factory -- hauls into input stock,
        // pushes an empty output stock, fills input to the 12-per-type cap and stalls forever.
        //
        // It is written as an explicit named special case rather than by merging the two
        // dictionaries when no Assemble is present, because keeping them separate is precisely
        // what lets Push empty everything at once (mixed types and all) and what makes
        // byproducts free: a smelter emitting Iron Plate *and* Slag still costs one Push.
        // Merging would make that a property of one code path instead of a property of Push.
        private GolemInventory.Stock PushStock =>
            program.HasAssembleStep ? _inventory.Output : _inventory.Input;

        private IItemEndpoint ResolveSpatialSource()
        {
            IItemEndpoint endpoint;
            if (spatialEndpointHolder == null ||
                !spatialEndpointHolder.Registry.TryGetEndpoint(SourceCell, out endpoint))
            {
                return null;
            }

            return endpoint;
        }

        private IItemEndpoint ResolveSpatialTarget()
        {
            IItemEndpoint endpoint;
            if (spatialEndpointHolder == null ||
                !spatialEndpointHolder.Registry.TryGetEndpoint(TargetCell, out endpoint))
            {
                return null;
            }

            return endpoint;
        }

        // Which "it's empty" reason best describes a source endpoint that had nothing to give,
        // so the existing stall phrasing stays accurate on the spatial path too.
        private static StallReason EmptyReasonFor(IItemEndpoint endpoint)
        {
            if (endpoint is ResourceNodeEndpoint)
            {
                return StallReason.NodeEmpty;
            }

            if (endpoint is StorageBufferEndpoint)
            {
                return StallReason.BufferEmpty;
            }

            return StallReason.BeltEmpty;
        }

        // Only the two processing verbs need a completion-time side effect: their output must
        // appear once the duration has elapsed, not when processing began (see BeginRefine /
        // BeginAssemble). Haul/Extract/Push do their entire side effect in Begin, so this is a
        // no-op for them.
        private void CompleteStep(AppendageActionDefinition step)
        {
            if (step.actionType == AppendageActionType.Refine && bufferRegistryHolder != null)
            {
                bufferRegistryHolder.Registry.Deposit(step.destinationId, step.outputItemType);
                return;
            }

            if (step.actionType == AppendageActionType.Assemble)
            {
                RecipeDefinition recipe = step.recipe;
                if (recipe == null)
                {
                    // Unreachable: BeginAssemble stalls Unconfigured on a null recipe, so the
                    // step never starts counting ticks and never completes. Guarded anyway
                    // because CompleteStep runs inside Tick, where a throw is not an option.
                    return;
                }

                // Unconditional Add, no room re-check: BeginAssemble already confirmed room for
                // the full output quantity AND for the byproduct, and a golem's output stock has
                // exactly one writer -- itself, one step at a time. Nothing can have consumed
                // the room in between.
                //
                // Both land in OUTPUT stock, at completion rather than at begin, so the
                // processing time is real: nothing appears until the recipe has actually run.
                // The byproduct costs no extra slot because Push empties the whole output stock
                // in one step (progression-design §2, Consequence 3).
                _inventory.AddOutput(recipe.outputItemType, recipe.outputQuantity);

                // FRESH PRODUCTION (progression-design §7). This is the exact moment a good comes
                // into existence, and it is the only such moment in the game: Haul and Push move
                // goods that already existed, which is precisely what the Clock Tower's
                // hoard-blitz rule has to be able to tell apart from making them. Published here
                // rather than at Begin because nothing exists until the recipe has actually run
                // its duration -- crediting at withdrawal would let a stalled 120-tick
                // Chronometer Core count as production it never completed.
                //
                // The tick rides along so the tower's 60 s windows stay keyed to simulation time
                // rather than to whenever a listener happened to hear about it.
                EventBus.Publish(new ItemAssembledEvent(
                    golemId, recipe.outputItemType, recipe.outputQuantity, _currentTick));

                if (recipe.HasByproduct)
                {
                    _inventory.AddOutput(recipe.byproductItemType, recipe.byproductQuantity);

                    // A byproduct is fresh production of its own type. R4's Slag is genuinely
                    // new Slag, and §5.3(c)'s whole disposal economy depends on it being counted
                    // as such rather than treated as a lesser output.
                    EventBus.Publish(new ItemAssembledEvent(
                        golemId, recipe.byproductItemType, recipe.byproductQuantity,
                        _currentTick, true));
                }
            }
        }

        // --- Note on the retired one-step spatial transfer ---------------------------------
        // Until the machine model landed, every spatially routed action reduced to one physical
        // verb (BeginSpatialTransfer): take one item off the tile behind, put it on the tile in
        // front. progression-design §2 "Consequence 2" is a direct answer to that collapse --
        // if Haul, ExtractFromNode and LoadIntoBuffer are the same code, then the Scavenger's
        // "2 slots = Extract then Load" is really "do the same transfer twice" and the logistics
        // cards have no identity. With an internal inventory they sit on two different sides of
        // the golem: Haul/Extract fill from behind (BeginFillInputStock), Push empties in front
        // (BeginPush), and no step spans both.
        //
        // The ordering rule that method existed to enforce did NOT go away, it split in two and
        // is restated at each half: never consume from an irreversible source before confirming
        // the destination has room. See BeginFillInputStock (room in input stock, clamped) and
        // BeginPush (consume from stock only after the target accepts).

        // --- Filling the golem: Haul and ExtractFromNode ------------------------------------
        // The shared body of the two "pull from the tile behind into my own input stock" verbs.
        // They differ only in how the item type is chosen and how long they take, so the
        // ordering discipline that matters lives here, once.
        //
        // Ordering, same reasoning as BeginSpatialTransfer's CanGive-before-TryTake: nothing is
        // consumed from the source until there is somewhere confirmed to put it. Here the
        // destination is the golem's own input stock, so the check is InputRoomFor -- and the
        // take is CLAMPED to that room rather than merely gated by it, because a partial take
        // that overshot the cap would have to drop the overflow on the floor. Taking from a
        // finite ResourceNode is irreversible; there is no putting it back.
        private StallReason BeginFillInputStock(
            string requestedType, int quantity, out string blockedResourceId)
        {
            blockedResourceId = null;

            IItemEndpoint source = ResolveSpatialSource();
            if (source == null)
            {
                blockedResourceId = SourceCell.ToString();
                return StallReason.NoSourceAtTile;
            }

            // An authored inputItemType makes the step strictly typed -- a Haul(Iron Plate)
            // against a tile holding Scrap stalls rather than hauling the wrong good, which is
            // the entire point of typing Haul. Blank means "whatever this tile offers", which
            // is what the pre-typed cards (HaulScrap.asset, and every ExtractFromNode, whose
            // node has exactly one type anyway) mean and must keep meaning.
            string itemType = string.IsNullOrEmpty(requestedType) ? source.PeekAvailableType() : requestedType;
            if (string.IsNullOrEmpty(itemType))
            {
                blockedResourceId = source.DisplayName;
                return EmptyReasonFor(source);
            }

            int room = _inventory.InputRoomFor(itemType);
            if (room <= 0)
            {
                // Names the item type, not the endpoint: the tile behind is fine, the golem is
                // the thing that is full, and the fix is downstream of it.
                blockedResourceId = itemType;
                return StallReason.InputFull;
            }

            int wanted = Mathf.Min(quantity, room);
            int taken;
            if (!source.TryTake(itemType, wanted, out taken) || taken <= 0)
            {
                // Two very different failures land here, and the player's fix differs, so they
                // must not share a message. PeekAvailableType is the discriminator:
                //   * non-null -> the tile HAS goods, just not this type. Naming the endpoint
                //     ("no input in ScrapBuffer") would point at a buffer the player can see is
                //     full; the actionable fact is the missing type, so look upstream of it.
                //   * null -> the source really is empty (including a belt item still in
                //     transit, which TryPeekHead correctly refuses). Naming the endpoint is
                //     right there: wait for it, or rotate the golem.
                if (!string.IsNullOrEmpty(source.PeekAvailableType()))
                {
                    blockedResourceId = itemType;
                    return StallReason.MissingItem;
                }

                blockedResourceId = source.DisplayName;
                return EmptyReasonFor(source);
            }

            // Cannot lose anything: taken <= wanted <= room, so AddInput accepts all of it.
            _inventory.AddInput(itemType, taken);
            return StallReason.None;
        }

        private StallReason BeginExtractFromNode(AppendageActionDefinition step, out string blockedResourceId)
        {
            // Nothing spatial configured at all -> the original id-routed implementation,
            // untouched. This is the branch every Main.unity demo golem and every pre-existing
            // test takes.
            if (!IsSpatiallyPlaced)
            {
                return BeginExtractFromNodeById(step, out blockedResourceId);
            }

            // 6 + qty (progression-design §2): a fixed setup cost plus one tick per unit, which
            // is what makes a 4-unit extractor 10 ticks and worth batching. Set before the
            // precondition checks so a stalled step doesn't carry a stale duration into its
            // eventual successful retry.
            int quantity = CurrentStepQuantity();
            _stepDuration = 6 + quantity;

            // §3.2's crew cap, checked BEFORE BeginFillInputStock -- i.e. before anything is
            // taken out of the ground. A refused golem must not extract this tick and then be
            // told it was over quota, for exactly the reason the steam check sits at the top of
            // BeginStep: a precondition that runs after the side effect is not a precondition.
            StallReason crowded = CheckNodeExtractorCap(out blockedResourceId);
            if (crowded != StallReason.None)
            {
                return crowded;
            }

            // A node holds exactly one type, so there is no ambiguity to resolve and no reason
            // to require the card to name it -- PeekAvailableType is the node's own answer.
            return BeginFillInputStock(null, quantity, out blockedResourceId);
        }

        /// <summary>
        /// Whether this golem is one of the (at most two) golems allowed to work the node
        /// behind it, or <see cref="StallReason.NodeCrowded"/> naming that node if not
        /// (docs/progression-design.md §3.2).
        ///
        /// <para>
        /// THE EXEMPTION IS THE FIRST LINE, exactly as in CheckSteamPower: a golem with no cap
        /// registry configured is outside the mechanic, not "capped by an empty registry".
        /// </para>
        ///
        /// <para>
        /// A golem facing something that is not a resource node RELEASES its claim rather than
        /// keeping one. That is what stops a rotated-away extractor from holding a slot at a
        /// seam it has stopped working -- and it is why the claim is filed here, per attempt,
        /// rather than once at configuration time: the node a golem works is a fact about the
        /// tile behind it, which the player can change with R.
        /// </para>
        /// </summary>
        private StallReason CheckNodeExtractorCap(out string blockedResourceId)
        {
            blockedResourceId = null;

            if (nodeExtractorHolder == null || string.IsNullOrEmpty(golemId))
            {
                return StallReason.None;
            }

            var nodeEndpoint = ResolveSpatialSource() as ResourceNodeEndpoint;
            if (nodeEndpoint == null || nodeEndpoint.Node == null)
            {
                // Not facing a node at all. BeginFillInputStock reports what IS wrong (nothing
                // behind me / an empty belt / a buffer), which is a better sentence than a crew
                // rule the player is not currently breaking.
                ReleaseNodeClaim();
                return StallReason.None;
            }

            string nodeId = nodeEndpoint.Node.NodeId;
            NodeExtractorRegistry registry = nodeExtractorHolder.Registry;

            // Filing the claim is how a golem applies. Idempotent, so the repeated attempts a
            // stalled golem makes every tick cost a dictionary probe and no re-sort.
            registry.RegisterExtractor(golemId, cell, nodeId);

            if (registry.IsWorking(nodeId, golemId))
            {
                return StallReason.None;
            }

            // Names the node, like NodeEmpty does -- the player has to know WHICH seam is
            // over-subscribed to work out where to put this golem instead.
            blockedResourceId = nodeId;
            return StallReason.NodeCrowded;
        }

        private StallReason BeginExtractFromNodeById(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;
            if (conveyorHolder == null || nodeRegistryHolder == null)
            {
                return StallReason.Unconfigured;
            }

            // Check for belt room *before* extracting. TryExtract decrements a finite
            // ResourceNode irreversibly, so extracting first and enqueuing second silently
            // destroyed one unit every time the destination belt was full -- a real leak out
            // of a finite node, not just a stall. CanEnqueue is the side-effect-free half of
            // TryEnqueue's guard, added for exactly this ordering.
            if (!conveyorHolder.System.CanEnqueue(step.destinationId))
            {
                blockedResourceId = step.destinationId;
                return StallReason.BeltFull;
            }

            // M5: sourceId is a real ResourceNode id; the node supplies the item's actual
            // ItemType (replaces M4's "every node is an infinite placeholder keyed by its
            // own sourceId" hack) and enforces finite depletion.
            if (!nodeRegistryHolder.Registry.TryExtract(step.sourceId, out ItemStack item))
            {
                blockedResourceId = step.sourceId;
                return StallReason.NodeEmpty;
            }

            // Guarded by CanEnqueue above, so this cannot drop the item we just extracted.
            conveyorHolder.System.TryEnqueue(step.destinationId, item);
            return StallReason.None;
        }

        private StallReason BeginLoadIntoBuffer(AppendageActionDefinition step, out string blockedResourceId)
        {
            if (!IsSpatiallyPlaced)
            {
                return BeginLoadIntoBufferById(step, out blockedResourceId);
            }

            // progression-design §2 "Consequence 2" renames this verb: on a spatially placed
            // golem LoadIntoBuffer IS Push. The enum name is kept rather than migrated because
            // the value is serialized by index into LoadIntoScrapBuffer.asset, which the
            // Sandbox scene's Workbench roster hands the player -- renaming the enum member
            // would be free, renumbering it would silently repoint that card at Refine.
            return BeginPush(out blockedResourceId);
        }

        private StallReason BeginLoadIntoBufferById(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;
            if (conveyorHolder == null || bufferRegistryHolder == null)
            {
                return StallReason.Unconfigured;
            }

            if (!conveyorHolder.System.TryDequeueHead(step.sourceId, out ItemStack item))
            {
                blockedResourceId = step.sourceId;
                return StallReason.BeltEmpty;
            }

            bufferRegistryHolder.Registry.Deposit(step.destinationId, item.ItemType);
            return StallReason.None;
        }

        // --- Haul -------------------------------------------------------------------------
        // Haul used to be a no-op success stub: locomotion was never built, so a player who
        // slotted the HaulScrap card got a golem that ran happily and moved nothing, which is
        // actively misleading. Facing-based routing gave it a meaning that needs no locomotion
        // at all -- take from the tile behind -- and the machine model finishes the job: it
        // takes a NAMED type in a PLAYER-SET QUANTITY into the golem's own input stock, which
        // is what separates it from Push and gives the Workbench its one remaining decision
        // (progression-design §2, Consequences 2 and 4).
        //
        // Haul carries no meaningful sourceId/destinationId (it never routed by id), so there
        // is nothing to fall back to: when no spatial endpoints exist it keeps the historical
        // no-op success, leaving every existing Haul demo and test unaffected.
        private StallReason BeginHaul(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;

            // The fallback keys off "was this golem ever placed spatially", NOT "are both its
            // tiles empty". Those differ in exactly the case that matters: a spatially placed
            // golem rotated to face empty ground has two empty tiles, and reporting no-op
            // success there would silently restore the old lie that facing does nothing.
            if (!IsSpatiallyPlaced)
            {
                return StallReason.None;
            }

            // max(2, qty): a floor of 2 ticks of fixed overhead, then one tick per unit, so a
            // big batch amortises the fixed cost of the Push at the end of the cycle. That
            // trade -- throughput against holding N units hostage inside one golem and pulling
            // N at a time out of a shared buffer -- is the decision §2 hands the player.
            int quantity = CurrentStepQuantity();
            _stepDuration = Mathf.Max(2, quantity);

            return BeginFillInputStock(step.inputItemType, quantity, out blockedResourceId);
        }

        // --- Push ---------------------------------------------------------------------------
        // Empties the golem's ENTIRE resolved stock onto the tile in front -- mixed types and
        // all, in one step. That is what makes byproducts free (progression-design §2,
        // Consequence 3): a smelter emitting Iron Plate and Slag pushes both in a single slot
        // and stays a 4-slot recipe. Which stock it drains is the pure-logistics rule; see
        // PushStock.
        //
        // Push is a brand-new verb with no legacy call sites, so an unplaced golem gets an
        // honest Unconfigured stall rather than the no-op success Haul had to keep for
        // compatibility. There is no id-routed meaning of "the tile in front" to fall back to,
        // and inventing one would be a worse lie than the one Haul's stub used to tell.
        private StallReason BeginPush(out string blockedResourceId)
        {
            blockedResourceId = null;

            if (!IsSpatiallyPlaced)
            {
                return StallReason.Unconfigured;
            }

            IItemEndpoint target = ResolveSpatialTarget();
            if (target == null)
            {
                blockedResourceId = TargetCell.ToString();
                return StallReason.NoTargetAtTile;
            }

            GolemInventory.Stock stock = PushStock;
            if (stock.TotalUnits <= 0)
            {
                // MissingItem with NO item type, rather than a fourth stall reason for "my own
                // hold is empty". Push is the one step that names no type -- it needs anything
                // at all -- so the null-resourceId fallback ("no goods available") is exactly
                // the sentence. The two rejected alternatives both misdirect: BufferEmpty
                // against golemId says the golem's name twice, and BufferEmpty against null
                // says "its source has no input", pointing at a source that is not the problem.
                // In practice this is nearly unreachable -- the Haul or Assemble ahead of the
                // Push stalls first -- which is another reason not to spend a reason on it.
                return StallReason.MissingItem;
            }

            if (!target.CanGive())
            {
                blockedResourceId = target.DisplayName;
                return StallReason.BeltFull;
            }

            // Unit at a time, and each unit is removed from stock only AFTER the destination
            // has accepted it. That ordering is the same invariant CanGive-before-TryTake
            // protects, applied to the other end of the golem: a destination that fills partway
            // through a mixed push leaves the remainder sitting in stock for the next cycle
            // instead of on the floor. Expressing it as consume-after-give rather than
            // drain-then-return-the-remainder means there is no window in which the items exist
            // in neither place, so no ordering bug can lose them.
            int pushed = 0;

            // Snapshot the type list: the loop mutates the stock, and a type emptying removes
            // it from the live ordering. Order is deterministic (see GolemInventory.Stock) so
            // two identically-programmed golems drain a mixed hold the same way when the
            // destination only has room for part of it.
            var types = new System.Collections.Generic.List<string>(stock.TypesInOrder);
            for (int i = 0; i < types.Count; i++)
            {
                string itemType = types[i];
                while (stock.Get(itemType) > 0)
                {
                    if (!target.TryGive(new ItemStack { ItemType = itemType }))
                    {
                        // A REFUSED TYPE IS SKIPPED, NOT THE WHOLE PUSH ABANDONED. This loop
                        // used to break out of the outer loop here, which was fine while
                        // buffers had no capacity and only a belt could ever say no. With
                        // per-item-type capacity it is precisely the permanent deadlock
                        // progression-design §10 warns about: a full Slag slot in the
                        // destination would stop Iron Plate being pushed as well, and no rigid
                        // golem can ever be programmed to clear it. The entire reason capacity
                        // is per type is that A FULL SLAG SLOT MUST NEVER BLOCK IRON PLATE.
                        // The refused units simply stay in stock for the next cycle.
                        break;
                    }

                    stock.TryConsume(itemType, 1);
                    pushed++;
                }

                // Early-out for destinations whose capacity is NOT per type -- a belt with no
                // free slot refuses every type once it refuses one, so walking the remaining
                // types is pure waste (each TryGive would just fail). Gated on the untyped
                // CanGive -- "can you accept anything at all" -- and deliberately NOT on the
                // TryGive failure above: that failure is the per-type refusal this fix exists
                // to skip past, and keying the early-out on it would reinstate the deadlock
                // under a different name. A per-type-capped buffer answers CanGive() true while
                // any type could still fit, so this never fires for the Slag case.
                if (!target.CanGive())
                {
                    break;
                }
            }

            if (pushed <= 0)
            {
                blockedResourceId = target.DisplayName;
                return StallReason.BeltFull;
            }

            // 2 + unitCount, on what actually left the golem rather than on what it hoped to
            // push -- a partial push must not also be charged for the units still held.
            _stepDuration = 2 + pushed;
            return StallReason.None;
        }

        // --- Assemble (docs/progression-design.md §5.2, §11 item 2) ---------------------------
        // Runs one RecipeDefinition: consumes 1-4 typed inputs from input stock at Begin and
        // deposits the output (+ optional byproduct) into output stock at Complete.
        //
        // NEVER TOUCHES A TILE, which is the whole reason the machine model was worth adopting:
        // a recipe reads a typed dictionary the golem owns, so it cannot grab the wrong good off
        // a mixed tile and silently transmute it. That also makes it the one verb with no
        // spatial/id fork -- an unplaced golem assembles exactly like a placed one, and it needs
        // no ConfigureSpatial to do it.
        //
        // IT READS ONLY step.recipe. The step's own inputItemType/outputItemType were an
        // explicit §1.1 placeholder for a single-input Assemble and are no longer consulted here
        // at all; they stay on the asset because Refine and Haul still mean something by them.
        private StallReason BeginAssemble(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;

            RecipeDefinition recipe = step.recipe;

            // A missing recipe and a malformed one are the same failure to the player -- this
            // card was never finished being authored -- and both must be an honest stall rather
            // than a throw: BeginStep runs inside Tick, where an exception takes the whole
            // simulation clock down with it. Validation itself lives on RecipeDefinition, at the
            // authoring edge; this is just the tick loop refusing to half-execute the result.
            if (recipe == null || !recipe.IsWellFormed())
            {
                return StallReason.Unconfigured;
            }

            // Authored, not derived (progression-design §2's cycle-time table), and taken from
            // the recipe rather than the card -- one Assemble card pointed at R1 (12t) and R15
            // (90t) must not run both at the same speed. Set before the precondition checks so a
            // stalled step cannot carry a stale duration into its eventual successful retry.
            _stepDuration = Mathf.Max(1, recipe.durationTicks);

            // ROOM FOR EVERYTHING THIS WILL PRODUCE, BEFORE CONSUMING ANYTHING. Consuming the
            // ingredients and only then discovering the output stock is full destroys material
            // every tick. Both the output and the byproduct are checked, because a recipe that
            // could deposit its Iron Plate but not its Slag must stall with its inputs intact --
            // that backed-up-byproduct stall is the mechanism behind §5.3(c)'s whole Slag
            // economy, not an edge case. Output and byproduct are guaranteed different types
            // (RecipeDefinition.IsWellFormed), so the two checks cannot overlap.
            if (_inventory.OutputRoomFor(recipe.outputItemType) < recipe.outputQuantity)
            {
                blockedResourceId = recipe.outputItemType;
                return StallReason.OutputFull;
            }

            if (recipe.HasByproduct &&
                _inventory.OutputRoomFor(recipe.byproductItemType) < recipe.byproductQuantity)
            {
                blockedResourceId = recipe.byproductItemType;
                return StallReason.OutputFull;
            }

            // ATOMIC: every input is checked before any input is withdrawn. A partial withdrawal
            // on a recipe that then stalls would strand goods inside the golem forever -- a rigid
            // program has no step that could ever put them back, and nothing outside the golem
            // can reach its input stock. This is the single most important line in the verb.
            System.Collections.Generic.List<RecipeIngredient> ingredients = recipe.inputs;
            for (int i = 0; i < ingredients.Count; i++)
            {
                RecipeIngredient ingredient = ingredients[i];
                int held = _inventory.GetInput(ingredient.itemType);
                if (held >= ingredient.quantity)
                {
                    continue;
                }

                // FIRST short input in the recipe's AUTHORED order, deliberately -- not the
                // largest shortfall, not the scarcest good. Two identically-programmed golems
                // must always name the same ingredient, or a player comparing two stalled
                // smelters gets two different diagnoses of one problem. Authored order is the
                // only ordering both golems provably share.
                //
                // Names the missing ingredient, not the golem: "GolemA stalled: GolemA has no
                // input" is unusable on a line where three assemblers wait on three different
                // precursors. §8 additionally requires the AMOUNT, which rides alongside in
                // _pendingShortfall rather than being encoded into the resource id.
                blockedResourceId = ingredient.itemType;
                _pendingShortfall = ingredient.quantity - held;
                return StallReason.MissingItem;
            }

            // Withdrawn up front, so the processing time is committed work -- exactly the
            // reasoning BeginRefine records. Every one of these is guaranteed to succeed by the
            // loop above; the products appear in CompleteStep.
            for (int i = 0; i < ingredients.Count; i++)
            {
                _inventory.TryConsumeInput(ingredients[i].itemType, ingredients[i].quantity);
            }

            return StallReason.None;
        }

        // Withdraws the recipe input up front so processing time is real "committed" work
        // (matches a physical refinery: once started, it can't be interrupted by the source
        // buffer running dry mid-cycle since nothing else can drain it back out). The
        // output is deposited later, in CompleteStep, once durationTicks have elapsed.
        //
        // DELIBERATELY EXEMPT FROM SPATIAL ROUTING, even for a spatially placed golem.
        // A recipe is defined by its item *types* (inputItemType -> outputItemType), but the
        // untyped IItemEndpoint.TryTake hands over "whatever this endpoint had", with no way to
        // ask for a specific type. Routing Refine spatially would therefore let it grab the
        // wrong input off a mixed buffer and silently transmute it, which is worse than an
        // honest stall.
        //
        // IItemEndpoint has since grown the typed take this comment used to wait for -- but the
        // exemption STAYS, because §1.1 answered the problem from the other end instead:
        // Assemble reads a typed dictionary the golem itself owns, so it needs no spatial take
        // at all. Refine is the pre-machine-model verb, superseded by Assemble in §1.3 and left
        // keyed to the buffer ids its recipe names until then. Nothing new should be built on it.
        private StallReason BeginRefine(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;
            if (bufferRegistryHolder == null)
            {
                return StallReason.Unconfigured;
            }

            if (!bufferRegistryHolder.Registry.TryWithdraw(step.sourceId, step.inputItemType))
            {
                blockedResourceId = step.sourceId;
                return StallReason.BufferEmpty;
            }

            return StallReason.None;
        }
    }
}
