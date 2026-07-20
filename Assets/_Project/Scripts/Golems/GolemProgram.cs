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
    }
}
