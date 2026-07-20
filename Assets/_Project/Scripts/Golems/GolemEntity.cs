using UnityEngine;
using GolemFactory.Simulation;
using GolemFactory.Events;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    public sealed class GolemEntity : MonoBehaviour, ITickable
    {
        [SerializeField] private string golemId;
        [SerializeField] private GolemProgram program = new GolemProgram();

        public string GolemId => golemId;
        public GolemProgram Program => program;

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
            // Appendage execution (Haul/ExtractFromNode/Refine/LoadIntoBuffer) lands in M3-M5.
            return true;
        }
    }
}
