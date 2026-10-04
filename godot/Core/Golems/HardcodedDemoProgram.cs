using GolemFactory.Compat;
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
                Debug.LogWarning($"{golem.GolemId}: no demo chassis assigned; its program will have no chassis fitted.");
                golem.Program.appendages.AddRange(source.appendages);
                return;
            }

            golem.Program.TryAssignChassis(chassis);
            foreach (AppendageActionDefinition appendage in source.appendages)
            {
                if (!golem.Program.TryAddAppendage(appendage))
                {
                    Debug.LogWarning(
                        $"{golem.GolemId}: {chassis.name} has only {chassis.maxAppendageSlots} slots; a demo step was dropped.");
                }
            }
        }

        /// <summary>
        /// The canonical id-routed chain: pull from "ScrapNode" onto
        /// <paramref name="beltSegmentId"/>, then carry that belt's head into "ScrapBuffer".
        ///
        /// <para>
        /// <b>THE BELT ARGUMENT IS NEW, AND IT IS WHY THIS PROGRAM NOW WORKS.</b> Written in M2
        /// with no belt at all, it could never run: the id-routed <c>ExtractFromNode</c> extracts
        /// ONTO a belt named by the card's <c>destinationId</c>, so an extract card naming none
        /// refused at <c>CanEnqueue(null)</c> before it ever touched the node -- every tick,
        /// forever. <c>GolemDemoBootstrap</c> applied exactly this to a golem standing in
        /// <c>Main.unity</c>, which is why that golem visibly did nothing for several milestones.
        /// </para>
        ///
        /// <para>
        /// It was left broken deliberately while the scene existed, because giving it a belt
        /// would have changed what the diorama demonstrated -- a content decision rather than a
        /// test's to make (docs/open-items.md §3z B). The scene is retired, so that constraint is
        /// gone and only the question about the PROGRAM remains. A file whose whole job is to be
        /// the definition of the reference programs cannot afford a reference that does not run:
        /// the next person to copy it would inherit the jam along with the name.
        /// </para>
        ///
        /// <para>
        /// The belt is a required parameter rather than a defaulted magic string, so the
        /// requirement is structural. A caller cannot rebuild the original fiction by accident.
        /// </para>
        /// </summary>
        public static GolemProgram ExtractAndDeposit(string beltSegmentId)
        {
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var extract = new AppendageActionDefinition();
            extract.actionType = AppendageActionType.ExtractFromNode;
            extract.sourceId = "ScrapNode";
            extract.destinationId = beltSegmentId;

            var deposit = new AppendageActionDefinition();
            deposit.actionType = AppendageActionType.LoadIntoBuffer;
            deposit.sourceId = beltSegmentId;
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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var extract = new AppendageActionDefinition();
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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var load = new AppendageActionDefinition();
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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var refine = new AppendageActionDefinition();
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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;

            var extract = new AppendageActionDefinition();
            extract.actionType = AppendageActionType.ExtractFromNode;
            extract.sourceId = nodeId;
            extract.destinationId = beltSegmentId;

            var load = new AppendageActionDefinition();
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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.Threshold;
            logicCore.thresholdBufferId = sourceBufferId;
            logicCore.thresholdItemType = inputItemType;
            logicCore.thresholdQuantity = thresholdQuantity;

            var refine = new AppendageActionDefinition();
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
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.Signal;
            logicCore.signalGolemId = signalGolemId;

            var ship = new AppendageActionDefinition();
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
