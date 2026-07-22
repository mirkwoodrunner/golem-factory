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

        public AppendageActionDefinition CurrentStep =>
            CurrentStepIndex >= 0 && CurrentStepIndex < appendages.Count ? appendages[CurrentStepIndex] : null;

        public void AdvanceStep()
        {
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
