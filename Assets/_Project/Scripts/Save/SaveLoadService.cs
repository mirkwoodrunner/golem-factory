using System.Collections.Generic;
using System.Linq;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;

namespace GolemFactory.Save
{
    // Pure state capture/restore logic -- no file I/O (see SaveFileIO), no MonoBehaviour
    // lifecycle dependency, so it's fully EditMode-testable.
    public static class SaveLoadService
    {
        public static SaveData CaptureState(
            StorageBufferRegistry buffers, ArtificerFocusMeter focus,
            PatentRegistry patents, IEnumerable<GolemEntity> golems)
        {
            var data = new SaveData();

            foreach (StorageBuffer buffer in buffers.Buffers.Values)
            {
                var entry = new BufferEntry { bufferId = buffer.BufferId };
                foreach (KeyValuePair<string, int> quantity in buffer.Quantities)
                {
                    entry.itemTypes.Add(quantity.Key);
                    entry.quantities.Add(quantity.Value);
                }
                data.buffers.Add(entry);
            }

            data.focusCurrent = focus.CurrentFocus;

            foreach (Blueprint blueprint in patents.Blueprints.Values)
            {
                data.blueprints.Add(new BlueprintEntry
                {
                    blueprintId = blueprint.BlueprintId,
                    ownerId = blueprint.OwnerId,
                    chassisName = blueprint.Chassis != null ? blueprint.Chassis.name : null,
                    logicCoreName = blueprint.LogicCore != null ? blueprint.LogicCore.name : null,
                    appendageNames = blueprint.Appendages.Select(a => a.name).ToList()
                });
            }

            foreach (GolemEntity golem in golems)
            {
                GolemProgram program = golem.Program;
                data.golems.Add(new GolemEntry
                {
                    golemId = golem.GolemId,
                    chassisName = program.chassis != null ? program.chassis.name : null,
                    logicCoreName = program.logicCore != null ? program.logicCore.name : null,
                    appendageNames = program.appendages.Select(a => a.name).ToList(),
                    currentStepIndex = program.CurrentStepIndex,
                    state = (int)program.State
                });
            }

            return data;
        }

        // Golems not present in `golems` (e.g. removed since the save was made) are
        // silently skipped -- restoring a golem program requires the real GolemEntity to
        // apply it to, and there's no "spawn a new one" concept for a save file to invent.
        public static void RestoreState(
            SaveData data, StorageBufferRegistry buffers, ArtificerFocusMeter focus,
            PatentRegistry patents, IEnumerable<GolemEntity> golems, DefinitionCatalog catalog)
        {
            // Deposit is additive -- clear first so a load *replaces* buffer state
            // instead of merging into whatever's currently there.
            buffers.Clear();

            foreach (BufferEntry entry in data.buffers)
            {
                for (int i = 0; i < entry.itemTypes.Count; i++)
                {
                    buffers.Deposit(entry.bufferId, entry.itemTypes[i], entry.quantities[i]);
                }
            }

            focus.SetCurrent(data.focusCurrent);

            foreach (BlueprintEntry entry in data.blueprints)
            {
                var blueprint = new Blueprint(
                    entry.blueprintId, entry.ownerId,
                    catalog.FindChassis(entry.chassisName), catalog.FindLogicCore(entry.logicCoreName),
                    entry.appendageNames.Select(catalog.FindAppendage).Where(a => a != null).ToList());
                patents.TryPatent(blueprint);
            }

            Dictionary<string, GolemEntity> golemsById = new Dictionary<string, GolemEntity>();
            foreach (GolemEntity golem in golems)
            {
                if (!string.IsNullOrEmpty(golem.GolemId))
                {
                    golemsById[golem.GolemId] = golem;
                }
            }

            foreach (GolemEntry entry in data.golems)
            {
                if (!golemsById.TryGetValue(entry.golemId, out GolemEntity golem))
                {
                    continue;
                }

                GolemProgram program = golem.Program;
                while (program.appendages.Count > 0)
                {
                    program.RemoveAppendageAt(0);
                }

                ChassisDefinition chassis = catalog.FindChassis(entry.chassisName);
                if (chassis != null)
                {
                    program.TryAssignChassis(chassis);
                }

                foreach (string appendageName in entry.appendageNames)
                {
                    AppendageActionDefinition appendage = catalog.FindAppendage(appendageName);
                    if (appendage != null)
                    {
                        program.TryAddAppendage(appendage);
                    }
                }

                program.logicCore = catalog.FindLogicCore(entry.logicCoreName);
                program.CurrentStepIndex = entry.currentStepIndex;
                program.State = (GolemState)entry.state;
            }
        }
    }
}
