using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    /// <summary>
    /// Writes a whole program onto a golem: the one commit both the Workbench's ENGAGE lever
    /// and a build from a patent at the construction station make. One writer, so a patent
    /// stamped at the station and the same patent loaded and engaged at the Workbench can
    /// never leave two different golems.
    /// </summary>
    public static class GolemProgramWriter
    {
        /// <summary>
        /// Replaces <paramref name="program"/>'s chassis (when one is given), logic core and
        /// steps, and rewinds it to Idle on step 0. A null card is an empty socket and is
        /// skipped; each committed step takes the good and batch size from its own slot.
        /// False when the chassis refuses the program, in which case the steps are already
        /// cleared and nothing else was written -- the lever's long-standing behaviour.
        /// </summary>
        public static bool TryWrite(
            GolemProgram program, ChassisDefinition chassis, LogicCoreDefinition logicCore,
            IReadOnlyList<AppendageActionDefinition> cards,
            IReadOnlyList<string> itemTypes, IReadOnlyList<int> quantities)
        {
            while (program.appendages.Count > 0)
            {
                program.RemoveAppendageAt(0);
            }
            if (chassis != null && !program.TryAssignChassis(chassis))
            {
                return false;
            }

            int committed = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && program.TryAddAppendage(cards[i]))
                {
                    program.SetQuantityAt(committed, quantities[i]);
                    program.SetItemTypeAt(committed, itemTypes[i]);
                    committed++;
                }
            }
            program.logicCore = logicCore;
            program.CurrentStepIndex = 0;
            program.StepProgressTicks = 0;
            program.State = GolemState.Idle;
            return true;
        }
    }
}
