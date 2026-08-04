using UnityEngine;
using GolemFactory.Simulation;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Belts;
using GolemFactory.Economy;
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

        // Programmatic setup used by tests (and available for runtime bootstrapping), mirroring
        // BuildModeController.Configure -- avoids requiring Inspector-assigned references.
        public void Configure(string id, ConveyorSystemHolder holder)
        {
            golemId = id;
            conveyorHolder = holder;
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
        /// Moves/rotates the golem without re-supplying the registry. "Golems cannot pivot" is a
        /// rule about *runtime execution* -- nothing in a program can turn the golem -- not about
        /// the player repositioning one between runs, which is the core spatial puzzle.
        /// </summary>
        public void SetPlacement(Vector2Int placedCell, Facing placedFacing)
        {
            cell = placedCell;
            facing = placedFacing;
        }

        // M7: Signal trigger is inherently event-driven (there's no already-held state to
        // poll, unlike Threshold's buffer query), so subscribe/unsubscribe on the
        // MonoBehaviour lifecycle -- same idiom M6's UI listeners established.
        private void OnEnable()
        {
            EventBus.GolemCompleted += OnGolemCompletedForSignal;
        }

        private void OnDisable()
        {
            EventBus.GolemCompleted -= OnGolemCompletedForSignal;
        }

        private void OnGolemCompletedForSignal(GolemCompletedEvent e)
        {
            LogicCoreDefinition logicCore = program.logicCore;
            if (logicCore != null && logicCore.triggerType == TriggerType.Signal && e.GolemId == logicCore.signalGolemId)
            {
                program.PendingSignal = true;
            }
        }

        public void Tick(long tick)
        {
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
                    bool isNewIncident = !wasStalled ||
                        reason != _stallReason ||
                        blockedResourceId != _stallResourceId;

                    program.State = GolemState.Stalled;
                    _stallReason = reason;
                    _stallResourceId = blockedResourceId;

                    if (isNewIncident)
                    {
                        EventBus.Publish(new GolemStalledEvent(
                            golemId, reason, blockedResourceId, program.CurrentStepIndex));
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
            int duration = Mathf.Max(1, step.durationTicks);
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
            switch (step.actionType)
            {
                case AppendageActionType.ExtractFromNode:
                    return BeginExtractFromNode(step, out blockedResourceId);
                case AppendageActionType.LoadIntoBuffer:
                    return BeginLoadIntoBuffer(step, out blockedResourceId);
                case AppendageActionType.Refine:
                    return BeginRefine(step, out blockedResourceId);
                case AppendageActionType.Haul:
                    return BeginHaul(out blockedResourceId);
                default:
                    return StallReason.None;
            }
        }

        // --- Spatial resolution -----------------------------------------------------------
        // Routing is resolved spatially *first*, then falls back to the authored id. The
        // fallback is load-bearing, not a nicety: Main.unity's seven hand-wired demo golems
        // and the entire existing test suite never call ConfigureSpatial, so
        // spatialEndpointHolder is null for them and every path below collapses to exactly
        // the id-routed code that shipped before.

        private bool IsSpatiallyPlaced => spatialEndpointHolder != null;

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

        // Only Refine needs a completion-time side effect: its output must appear once
        // durationTicks have elapsed, not when processing began (see TryBeginRefine).
        // Extract/Load do their entire side effect in Begin, so this is a no-op for them.
        private void CompleteStep(AppendageActionDefinition step)
        {
            if (step.actionType == AppendageActionType.Refine && bufferRegistryHolder != null)
            {
                bufferRegistryHolder.Registry.Deposit(step.destinationId, step.outputItemType);
            }
        }

        private StallReason BeginExtractFromNode(AppendageActionDefinition step, out string blockedResourceId)
        {
            blockedResourceId = null;

            // Nothing spatial configured at all -> the original id-routed implementation,
            // untouched. This is the branch every Main.unity demo golem and every pre-existing
            // test takes.
            if (!IsSpatiallyPlaced)
            {
                return BeginExtractFromNodeById(step, out blockedResourceId);
            }

            IItemEndpoint spatialSource = ResolveSpatialSource();
            IItemEndpoint spatialTarget = ResolveSpatialTarget();

            // --- destination half: resolve spatially, else fall back to the authored id ---
            BeltSegment idSegment = null;
            bool hasIdTarget = spatialTarget == null &&
                conveyorHolder != null &&
                conveyorHolder.System.TryGetSegment(step.destinationId, out idSegment);

            if (spatialTarget == null && !hasIdTarget)
            {
                blockedResourceId = TargetCell.ToString();
                return StallReason.NoTargetAtTile;
            }

            // Room check BEFORE consuming, for the same reason spelled out in
            // BeginExtractFromNodeById: taking from a finite node is irreversible, so a full
            // destination discovered afterwards silently destroys the unit. CanGive is the
            // spatial-path equivalent of BeltSegment.CanEnqueue.
            bool hasRoom = spatialTarget != null ? spatialTarget.CanGive() : idSegment.CanEnqueue();
            if (!hasRoom)
            {
                blockedResourceId = spatialTarget != null ? spatialTarget.DisplayName : step.destinationId;
                return StallReason.BeltFull;
            }

            // --- source half ---
            ItemStack item;
            if (spatialSource != null)
            {
                if (!spatialSource.TryTake(out item))
                {
                    blockedResourceId = spatialSource.DisplayName;
                    return EmptyReasonFor(spatialSource);
                }
            }
            else if (nodeRegistryHolder != null && nodeRegistryHolder.Registry.TryGetNode(step.sourceId, out _))
            {
                if (!nodeRegistryHolder.Registry.TryExtract(step.sourceId, out item))
                {
                    blockedResourceId = step.sourceId;
                    return StallReason.NodeEmpty;
                }
            }
            else
            {
                // Neither the tile behind nor the authored id yields anything. For a spatially
                // placed golem the tile is the actionable fact -- "rotate me", not "wait".
                blockedResourceId = SourceCell.ToString();
                return StallReason.NoSourceAtTile;
            }

            if (spatialTarget != null)
            {
                spatialTarget.TryGive(item);
            }
            else
            {
                idSegment.TryEnqueue(item);
            }

            return StallReason.None;
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
            blockedResourceId = null;

            if (!IsSpatiallyPlaced)
            {
                return BeginLoadIntoBufferById(step, out blockedResourceId);
            }

            IItemEndpoint spatialSource = ResolveSpatialSource();
            IItemEndpoint spatialTarget = ResolveSpatialTarget();

            // --- destination half ---
            bool hasIdBuffer = spatialTarget == null && bufferRegistryHolder != null && step.destinationId != null;
            if (spatialTarget == null && !hasIdBuffer)
            {
                blockedResourceId = TargetCell.ToString();
                return StallReason.NoTargetAtTile;
            }

            if (spatialTarget != null && !spatialTarget.CanGive())
            {
                blockedResourceId = spatialTarget.DisplayName;
                return StallReason.BeltFull;
            }

            // --- source half ---
            ItemStack item;
            if (spatialSource != null)
            {
                if (!spatialSource.TryTake(out item))
                {
                    blockedResourceId = spatialSource.DisplayName;
                    return EmptyReasonFor(spatialSource);
                }
            }
            else if (conveyorHolder != null && conveyorHolder.System.TryGetSegment(step.sourceId, out BeltSegment idSegment))
            {
                if (!idSegment.TryRemoveHead(out item))
                {
                    blockedResourceId = step.sourceId;
                    return StallReason.BeltEmpty;
                }
            }
            else
            {
                blockedResourceId = SourceCell.ToString();
                return StallReason.NoSourceAtTile;
            }

            if (spatialTarget != null)
            {
                spatialTarget.TryGive(item);
            }
            else
            {
                bufferRegistryHolder.Registry.Deposit(step.destinationId, item.ItemType);
            }

            return StallReason.None;
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
        // actively misleading. Facing-based routing gives Haul an obvious correct meaning that
        // needs no locomotion at all -- take one item from the tile behind, give it to the tile
        // in front. That is precisely the "Source tile / Target tile" verb the design doc
        // describes, and it makes Haul the one action that is purely spatial.
        //
        // Haul carries no meaningful sourceId/destinationId (it never routed by id), so there
        // is nothing to fall back to: when no spatial endpoints exist it keeps the historical
        // no-op success, leaving every existing Haul demo and test unaffected.
        private StallReason BeginHaul(out string blockedResourceId)
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

            IItemEndpoint source = ResolveSpatialSource();
            IItemEndpoint target = ResolveSpatialTarget();

            // Missing-endpoint checks run source-first because that is the order the player
            // reads the chain in, and neither check has a side effect. The consume-safety
            // ordering that actually matters -- CanGive before TryTake -- is preserved below.
            if (source == null)
            {
                blockedResourceId = SourceCell.ToString();
                return StallReason.NoSourceAtTile;
            }

            if (target == null)
            {
                blockedResourceId = TargetCell.ToString();
                return StallReason.NoTargetAtTile;
            }

            if (!target.CanGive())
            {
                blockedResourceId = target.DisplayName;
                return StallReason.BeltFull;
            }

            ItemStack item;
            if (!source.TryTake(out item))
            {
                blockedResourceId = source.DisplayName;
                return EmptyReasonFor(source);
            }

            target.TryGive(item);
            return StallReason.None;
        }

        // Withdraws the recipe input up front so processing time is real "committed" work
        // (matches a physical refinery: once started, it can't be interrupted by the source
        // buffer running dry mid-cycle since nothing else can drain it back out). The
        // output is deposited later, in CompleteStep, once durationTicks have elapsed.
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
