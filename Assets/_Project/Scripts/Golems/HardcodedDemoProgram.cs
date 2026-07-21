using UnityEngine;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    // Builds the M2 "Extract Scrap -> deposit" demo program from runtime-created
    // ScriptableObject instances, so a GolemEntity can run a working program
    // without pre-authored .asset files. Real data-driven authoring (dragging
    // authored .asset files into the Inspector) is the M3 concern.
    public static class HardcodedDemoProgram
    {
        public static GolemProgram ExtractAndDeposit()
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var extract = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            extract.actionType = AppendageActionType.ExtractFromNode;
            extract.sourceId = "ScrapNode";

            var deposit = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            deposit.actionType = AppendageActionType.LoadIntoBuffer;
            deposit.destinationId = "ScrapBuffer";

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(extract);
            program.appendages.Add(deposit);

            return program;
        }
    }
}
