using System;
using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    public enum GolemState
    {
        Idle,
        Running,
        Stalled
    }

    [Serializable]
    public sealed class GolemProgram
    {
        public ChassisDefinition chassis;
        public LogicCoreDefinition logicCore;
        public List<AppendageActionDefinition> appendages = new List<AppendageActionDefinition>();

        public int CurrentStepIndex { get; set; }
        public GolemState State { get; set; } = GolemState.Idle;

        // Ticks the current step has been processing for (M5: recipe-over-N-ticks
        // support, e.g. Refine). Zero means "not yet begun" -- GolemEntity.Tick uses that
        // to gate the once-only TryBeginStep call. Reset whenever the step changes.
        public int StepProgressTicks { get; set; }

        // M7 Threshold trigger: true means "ready to fire on the next at-or-above-
        // threshold check." Starts armed so an already-above-threshold buffer can still
        // fire once. Disarmed immediately after firing; re-armed only once the watched
        // quantity dips back below the threshold -- edge-triggered, not level-triggered,
        // so a continuously-full buffer doesn't refire every tick.
        public bool ThresholdArmed { get; set; } = true;

        // M7 Signal trigger: latched true by GolemEntity's GolemCompleted subscription
        // when the watched golem finishes a cycle, consumed (and reset) the next time
        // this golem is Idle and checks its trigger. Queues a signal that arrives while
        // this golem is busy rather than dropping it; multiple signals while busy coalesce
        // into one pending fire (not queued individually).
        public bool PendingSignal { get; set; }

        public AppendageActionDefinition CurrentStep =>
            CurrentStepIndex >= 0 && CurrentStepIndex < appendages.Count ? appendages[CurrentStepIndex] : null;

        public void AdvanceStep()
        {
            StepProgressTicks = 0;
            CurrentStepIndex++;
            if (CurrentStepIndex >= appendages.Count)
            {
                CurrentStepIndex = 0;
            }
        }

        // Chassis capacity is enforced only here, at assembly time (per the M3 design
        // note); execution never re-checks slot counts.
        public bool TryAssignChassis(ChassisDefinition newChassis)
        {
            if (newChassis == null || appendages.Count > newChassis.maxAppendageSlots)
            {
                return false;
            }

            chassis = newChassis;
            return true;
        }

        public bool TryAddAppendage(AppendageActionDefinition appendage)
        {
            if (appendage == null || chassis == null || appendages.Count >= chassis.maxAppendageSlots)
            {
                return false;
            }

            appendages.Add(appendage);
            return true;
        }

        public void RemoveAppendageAt(int index)
        {
            if (index < 0 || index >= appendages.Count)
            {
                return;
            }

            appendages.RemoveAt(index);
        }
    }
}
