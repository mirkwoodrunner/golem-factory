using UnityEngine;
using GolemFactory.Simulation;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Belts;

namespace GolemFactory.Golems
{
    public sealed class GolemEntity : MonoBehaviour, ITickable
    {
        [SerializeField] private string golemId;
        [SerializeField] private GolemProgram program = new GolemProgram();
        [SerializeField] private ConveyorSystemHolder conveyorHolder;

        public string GolemId => golemId;
        public GolemProgram Program => program;

        // Programmatic setup used by tests (and available for runtime bootstrapping), mirroring
        // BuildModeController.Configure -- avoids requiring Inspector-assigned references.
        public void Configure(string id, ConveyorSystemHolder holder)
        {
            golemId = id;
            conveyorHolder = holder;
        }

        public void Tick(long tick)
        {
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

            if (TryExecute(step))
            {
                program.AdvanceStep();
                if (program.CurrentStepIndex == 0)
                {
                    program.State = GolemState.Idle;
                    EventBus.Publish(new GolemCompletedEvent(golemId));
                }
            }
            else
            {
                program.State = GolemState.Stalled;
                EventBus.Publish(new GolemStalledEvent(golemId));
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
                default:
                    // Threshold / Signal evaluation moves into a standalone GolemTriggerSystem at M7.
                    return false;
            }
        }

        private bool TryExecute(AppendageActionDefinition step)
        {
            switch (step.actionType)
            {
                case AppendageActionType.ExtractFromNode:
                    return TryExtractFromNode(step);
                case AppendageActionType.LoadIntoBuffer:
                    return TryLoadIntoBuffer(step);
                default:
                    // Haul (golem locomotion) and Refine (M5's recipe-over-N-ticks appendage)
                    // are out of scope for the M4 belts slice; stay a no-op success stub.
                    return true;
            }
        }

        private bool TryExtractFromNode(AppendageActionDefinition step)
        {
            if (conveyorHolder == null)
            {
                return false;
            }

            // M4 placeholder: every node is treated as an infinite source -- no
            // ResourceNode/Economy system exists yet (that's M5).
            var item = new ItemStack { ItemType = step.sourceId };
            return conveyorHolder.System.TryEnqueue(step.destinationId, item);
        }

        private bool TryLoadIntoBuffer(AppendageActionDefinition step)
        {
            if (conveyorHolder == null)
            {
                return false;
            }

            if (!conveyorHolder.System.TryDequeueHead(step.sourceId, out ItemStack item))
            {
                return false;
            }

            DemoBuffer.Deposit(step.destinationId, item.ItemType);
            return true;
        }
    }
}
