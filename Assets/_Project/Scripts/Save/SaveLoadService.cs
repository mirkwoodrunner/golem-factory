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
                var entry = new GolemEntry
                {
                    golemId = golem.GolemId,
                    chassisName = program.chassis != null ? program.chassis.name : null,
                    logicCoreName = program.logicCore != null ? program.logicCore.name : null,
                    appendageNames = program.appendages.Select(a => a.name).ToList(),
                    currentStepIndex = program.CurrentStepIndex,
                    state = (int)program.State,
                    cellX = golem.Cell.x,
                    cellY = golem.Cell.y,
                    facing = (int)golem.Facing
                };

                // Read through GetQuantityAt rather than copying appendageQuantities directly,
                // so a desynced parallel list is normalised on the way out instead of being
                // persisted and reloaded still broken.
                for (int i = 0; i < program.appendages.Count; i++)
                {
                    entry.appendageQuantities.Add(program.GetQuantityAt(i));
                }

                CaptureStock(golem.Inventory.Input, entry.inputStockTypes, entry.inputStockQuantities);
                CaptureStock(golem.Inventory.Output, entry.outputStockTypes, entry.outputStockQuantities);

                data.golems.Add(entry);
            }

            return data;
        }

        private static void CaptureStock(
            GolemInventory.Stock stock, List<string> types, List<int> quantities)
        {
            // Iterates TypesInOrder, not the raw dictionary, so a save round-trip preserves the
            // order Push drains a mixed hold in.
            foreach (string itemType in stock.TypesInOrder)
            {
                types.Add(itemType);
                quantities.Add(stock.Get(itemType));
            }
        }

        // Tolerates lists of mismatched length, and an entirely absent pair (a save written
        // before golems had internal stock), by taking only the prefix both lists cover.
        private static void RestoreStock(
            GolemInventory.Stock stock, List<string> types, List<int> quantities)
        {
            if (types == null || quantities == null)
            {
                return;
            }

            int count = types.Count < quantities.Count ? types.Count : quantities.Count;
            for (int i = 0; i < count; i++)
            {
                stock.Add(types[i], quantities[i]);
            }
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

                // After TryAddAppendage, which has already seeded each slot with its card's
                // authored default -- this overwrites those with the player's chosen batch
                // sizes. Bounded by both lists: an appendage the catalog could not resolve was
                // skipped above, so the program can legitimately be shorter than the save's
                // quantity list, and an older save has no list at all.
                if (entry.appendageQuantities != null)
                {
                    int quantityCount = entry.appendageQuantities.Count < program.appendages.Count
                        ? entry.appendageQuantities.Count
                        : program.appendages.Count;
                    for (int i = 0; i < quantityCount; i++)
                    {
                        program.SetQuantityAt(i, entry.appendageQuantities[i]);
                    }
                }

                // Clear before restoring, for the same reason buffers.Clear() exists above:
                // Stock.Add is additive, so replaying a save onto a golem that is already
                // holding goods would merge rather than replace.
                golem.Inventory.Clear();
                RestoreStock(golem.Inventory.Input, entry.inputStockTypes, entry.inputStockQuantities);
                RestoreStock(golem.Inventory.Output, entry.outputStockTypes, entry.outputStockQuantities);

                program.logicCore = catalog.FindLogicCore(entry.logicCoreName);
                program.CurrentStepIndex = entry.currentStepIndex;
                program.State = (GolemState)entry.state;

                // SetPlacement, not ConfigureSpatial: the registry reference is scene wiring
                // the live GolemEntity already holds (or deliberately does not), and a save
                // file has no business granting one. This only restores where it stood.
                golem.SetPlacement(
                    new UnityEngine.Vector2Int(entry.cellX, entry.cellY),
                    (GolemFactory.World.Facing)entry.facing);
            }
        }
    }
}
