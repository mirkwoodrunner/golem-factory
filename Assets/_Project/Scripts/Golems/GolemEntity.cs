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

        public string GolemId => golemId;
        public GolemProgram Program => program;

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
            if (program.StepProgressTicks == 0 && !TryBeginStep(step))
            {
                program.State = GolemState.Stalled;
                EventBus.Publish(new GolemStalledEvent(golemId));
                return;
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

        private bool TryBeginStep(AppendageActionDefinition step)
        {
            switch (step.actionType)
            {
                case AppendageActionType.ExtractFromNode:
                    return TryExtractFromNode(step);
                case AppendageActionType.LoadIntoBuffer:
                    return TryLoadIntoBuffer(step);
                case AppendageActionType.Refine:
                    return TryBeginRefine(step);
                default:
                    // Haul (golem locomotion) needs a locomotion system that doesn't exist
                    // yet; stays a no-op success stub.
                    return true;
            }
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

        private bool TryExtractFromNode(AppendageActionDefinition step)
        {
            if (conveyorHolder == null || nodeRegistryHolder == null)
            {
                return false;
            }

            // M5: sourceId is a real ResourceNode id; the node supplies the item's actual
            // ItemType (replaces M4's "every node is an infinite placeholder keyed by its
            // own sourceId" hack) and enforces finite depletion.
            if (!nodeRegistryHolder.Registry.TryExtract(step.sourceId, out ItemStack item))
            {
                return false;
            }

            return conveyorHolder.System.TryEnqueue(step.destinationId, item);
        }

        private bool TryLoadIntoBuffer(AppendageActionDefinition step)
        {
            if (conveyorHolder == null || bufferRegistryHolder == null)
            {
                return false;
            }

            if (!conveyorHolder.System.TryDequeueHead(step.sourceId, out ItemStack item))
            {
                return false;
            }

            bufferRegistryHolder.Registry.Deposit(step.destinationId, item.ItemType);
            return true;
        }

        // Withdraws the recipe input up front so processing time is real "committed" work
        // (matches a physical refinery: once started, it can't be interrupted by the source
        // buffer running dry mid-cycle since nothing else can drain it back out). The
        // output is deposited later, in CompleteStep, once durationTicks have elapsed.
        private bool TryBeginRefine(AppendageActionDefinition step)
        {
            if (bufferRegistryHolder == null)
            {
                return false;
            }

            return bufferRegistryHolder.Registry.TryWithdraw(step.sourceId, step.inputItemType);
        }
    }
}
