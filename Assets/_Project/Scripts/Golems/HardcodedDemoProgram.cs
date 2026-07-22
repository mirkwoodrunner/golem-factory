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

        // M4 demo: a golem that extracts from an (infinite, placeholder) node and pushes
        // the item onto a named belt segment instead of depositing directly.
        public static GolemProgram ExtractOntoBelt(string beltSegmentId)
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var extract = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            extract.actionType = AppendageActionType.ExtractFromNode;
            extract.sourceId = "ScrapNode";
            extract.destinationId = beltSegmentId;

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(extract);

            return program;
        }

        // M4 demo: a golem that pulls the head item off a named belt segment once it
        // arrives, and deposits it into the M4 placeholder DemoBuffer (not the real M5
        // StorageBuffer).
        public static GolemProgram LoadFromBelt(string beltSegmentId, string bufferId)
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var load = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            load.actionType = AppendageActionType.LoadIntoBuffer;
            load.sourceId = beltSegmentId;
            load.destinationId = bufferId;

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(load);

            return program;
        }
    }
}
