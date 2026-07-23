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

        // A golem that extracts from the "ScrapNode" ResourceNode (registered as infinite
        // by the bootstrap, since M5) and pushes the item onto a named belt segment
        // instead of depositing directly.
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

        // A golem that pulls the head item off a named belt segment once it arrives, and
        // deposits it (by its real ItemType, supplied by the ResourceNode it came from)
        // into the named StorageBuffer.
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

        // M5 demo: a golem that runs the Refine appendage alone -- withdraws
        // inputItemType from the sourceId buffer, waits durationTicks while "processing",
        // then deposits outputItemType into the destinationId buffer.
        public static GolemProgram Refine(
            string sourceBufferId, string destinationBufferId,
            string inputItemType, string outputItemType, int durationTicks)
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var refine = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            refine.actionType = AppendageActionType.Refine;
            refine.sourceId = sourceBufferId;
            refine.destinationId = destinationBufferId;
            refine.inputItemType = inputItemType;
            refine.outputItemType = outputItemType;
            refine.durationTicks = durationTicks;

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(refine);

            return program;
        }

        // M5 demo: a single golem chaining ExtractFromNode -> LoadIntoBuffer itself
        // (rather than two golems handing off across a belt, as the Scrap chain does) --
        // demonstrates that a multi-step program self-stalls on step 2 until the belt
        // carries the item from step 1 all the way to the far end.
        public static GolemProgram ExtractThenLoad(string nodeId, string beltSegmentId, string bufferId)
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var extract = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            extract.actionType = AppendageActionType.ExtractFromNode;
            extract.sourceId = nodeId;
            extract.destinationId = beltSegmentId;

            var load = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            load.actionType = AppendageActionType.LoadIntoBuffer;
            load.sourceId = beltSegmentId;
            load.destinationId = bufferId;

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(extract);
            program.appendages.Add(load);

            return program;
        }

        // M7 demo: a single-step Refine golem gated by a Threshold trigger instead of
        // AlwaysOn -- fires once sourceBufferId's inputItemType quantity reaches
        // thresholdQuantity (edge-triggered; see GolemEntity.ShouldTriggerThreshold).
        public static GolemProgram ThresholdRefine(
            string sourceBufferId, string destinationBufferId,
            string inputItemType, string outputItemType, int durationTicks, int thresholdQuantity)
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.Threshold;
            logicCore.thresholdBufferId = sourceBufferId;
            logicCore.thresholdItemType = inputItemType;
            logicCore.thresholdQuantity = thresholdQuantity;

            var refine = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            refine.actionType = AppendageActionType.Refine;
            refine.sourceId = sourceBufferId;
            refine.destinationId = destinationBufferId;
            refine.inputItemType = inputItemType;
            refine.outputItemType = outputItemType;
            refine.durationTicks = durationTicks;

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(refine);

            return program;
        }

        // M7 demo: a single-step "ship into storage" golem gated by a Signal trigger --
        // fires once when the named golem completes its cycle. The step itself is a
        // same-item-type Refine (a plain buffer-to-buffer move): there's no dedicated
        // buffer-to-buffer appendage type, and a 1:1 recipe is a legitimate degenerate
        // case of Refine rather than a new action type just for this.
        public static GolemProgram SignalShip(
            string signalGolemId, string sourceBufferId, string destinationBufferId, string itemType)
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.Signal;
            logicCore.signalGolemId = signalGolemId;

            var ship = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            ship.actionType = AppendageActionType.Refine;
            ship.sourceId = sourceBufferId;
            ship.destinationId = destinationBufferId;
            ship.inputItemType = itemType;
            ship.outputItemType = itemType;
            ship.durationTicks = 1;

            var program = new GolemProgram
            {
                logicCore = logicCore
            };
            program.appendages.Add(ship);

            return program;
        }
    }
}
