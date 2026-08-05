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
        // Copies a built demo program onto a live golem, fitting a chassis first.
        //
        // Every demo bootstrap used to do this inline as
        //   golem.Program.logicCore = source.logicCore;
        //   golem.Program.appendages.AddRange(source.appendages);
        // which appends straight past GolemProgram.TryAddAppendage's "no chassis means no
        // capacity" guard. That left every demo golem in a state the game's own rules say
        // is impossible -- steps with no chassis to hold them -- and the Workbench,
        // pointed at one of them, opened reading "CHASSIS -- none --  SLOTS 1/0" over a
        // viewport that (correctly, since no chassis means no sockets) drew nothing at
        // all. That was the first thing a player ever saw on that screen.
        public static void ApplyTo(GolemEntity golem, ChassisDefinition chassis, GolemProgram source)
        {
            if (golem == null || source == null)
            {
                return;
            }

            golem.Program.logicCore = source.logicCore;

            if (chassis == null)
            {
                // Unwired scene reference: keep the pre-existing behaviour rather than
                // silently dropping every step, but make the authoring mistake visible.
                Debug.LogWarning($"{golem.name}: no demo chassis assigned; its program will have no chassis fitted.");
                golem.Program.appendages.AddRange(source.appendages);
                return;
            }

            golem.Program.TryAssignChassis(chassis);
            foreach (AppendageActionDefinition appendage in source.appendages)
            {
                if (!golem.Program.TryAddAppendage(appendage))
                {
                    Debug.LogWarning(
                        $"{golem.name}: {chassis.name} has only {chassis.maxAppendageSlots} slots; a demo step was dropped.");
                }
            }
        }

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
