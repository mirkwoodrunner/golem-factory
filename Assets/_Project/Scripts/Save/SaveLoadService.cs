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
            StorageBufferRegistry buffers,
            PatentRegistry patents, IEnumerable<GolemEntity> golems,
            IEnumerable<GolemFactory.Buildings.PlaceableBuilding> buildings = null)
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
                    facing = (int)golem.Facing,
                    wasRuntimeSpawned = golem.IsRuntimeSpawned
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

            CaptureBuildings(data, buildings);
            return data;
        }

        // Only what the PLAYER placed. A scene-authored building comes back with the scene, so
        // capturing one would rebuild it into a cell its original already occupies -- the same
        // distinction GolemEntry.wasRuntimeSpawned draws for golems, and the reason both flags
        // default to false.
        private static void CaptureBuildings(
            SaveData data, IEnumerable<GolemFactory.Buildings.PlaceableBuilding> buildings)
        {
            if (buildings == null)
            {
                return;
            }

            foreach (GolemFactory.Buildings.PlaceableBuilding building in buildings)
            {
                if (building == null || !building.IsRuntimePlaced)
                {
                    continue;
                }

                var entry = new BuildingEntry
                {
                    prefabKey = building.PrefabKey,
                    cellX = building.Cell.x,
                    cellY = building.Cell.y,
                    facing = (int)building.Facing
                };

                // Per-type extras, read off whichever sibling component happens to be there.
                // A boiler's fuel and the tower's progress are both things the player spent real
                // time on; everything else about a building is recreated by placing it.
                var boiler = building.GetComponent<GolemFactory.Buildings.PlaceableBoiler>();
                if (boiler != null && boiler.Boiler != null)
                {
                    entry.cokeStock = boiler.Boiler.CokeStock;
                }

                var depot = building.GetComponent<GolemFactory.Buildings.PlaceableDepot>();
                if (depot != null)
                {
                    entry.depotFilterItemType = depot.FilterItemType;
                }

                var recycler = building.GetComponent<GolemFactory.Buildings.PlaceableScrapRecycler>();
                if (recycler != null && recycler.Recycler != null)
                {
                    entry.recyclerCokeStock = recycler.Recycler.CokeStock;
                    entry.recyclerScrapStock = recycler.Recycler.ScrapStock;
                    entry.recyclerPendingPoints = recycler.Recycler.PendingPoints;
                }

                var tower = building.GetComponent<GolemFactory.Buildings.PlaceableClockTower>();
                if (tower != null && tower.SiteHolder != null && tower.SiteHolder.Site != null)
                {
                    entry.clockTowerStageIndex = tower.SiteHolder.Site.StageIndex;
                    entry.clockTowerProgressUnits = tower.SiteHolder.Site.ProgressUnits;
                    entry.clockTowerComplete = tower.SiteHolder.Site.IsComplete;
                }

                data.buildings.Add(entry);
            }
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

        /// <summary>
        /// What a load actually did to the golems, so the UI can say so instead of counting the
        /// save file's entries and calling that a result.
        /// </summary>
        public readonly struct RestoreReport
        {
            /// <summary>Golems already in the scene that had their program restored.</summary>
            public int Restored { get; }

            /// <summary>Golems rebuilt from the save because the scene no longer had them.</summary>
            public int Respawned { get; }

            /// <summary>
            /// Entries neither matched nor rebuilt -- a scene golem that is genuinely gone, or a
            /// player golem with no respawner wired. Reported rather than swallowed: this used
            /// to be every player-built golem in the file, silently.
            /// </summary>
            public int Skipped { get; }

            /// <summary>Player-placed buildings rebuilt from the save.</summary>
            public int BuildingsRebuilt { get; }

            /// <summary>
            /// Building entries that could not be rebuilt -- an unresolvable prefab, an occupied
            /// cell, or no rebuilder wired.
            /// </summary>
            public int BuildingsSkipped { get; }

            public RestoreReport(
                int restored, int respawned, int skipped,
                int buildingsRebuilt = 0, int buildingsSkipped = 0)
            {
                Restored = restored;
                Respawned = respawned;
                Skipped = skipped;
                BuildingsRebuilt = buildingsRebuilt;
                BuildingsSkipped = buildingsSkipped;
            }
        }

        // An entry with no live golem is REBUILT if the save says it was built during play and a
        // respawner is available, and skipped otherwise -- which is what every entry used to do,
        // because there was no way to make a golem a save file had merely described.
        //
        // The respawner only ever produces a bare-chassis golem standing in the right place. The
        // program, the stock, the step index and the placement are then applied by the SAME loop
        // that handles a golem which was already alive; there is deliberately no second restore
        // path for a respawned golem to drift away from.
        public static RestoreReport RestoreState(
            SaveData data, StorageBufferRegistry buffers,
            PatentRegistry patents, IEnumerable<GolemEntity> golems, DefinitionCatalog catalog,
            IGolemRespawner respawner = null, IBuildingRebuilder buildingRebuilder = null)
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

            foreach (BlueprintEntry entry in data.blueprints)
            {
                var blueprint = new Blueprint(
                    entry.blueprintId, entry.ownerId,
                    catalog.FindChassis(entry.chassisName), catalog.FindLogicCore(entry.logicCoreName),
                    entry.appendageNames.Select(catalog.FindAppendage).Where(a => a != null).ToList());
                patents.TryPatent(blueprint);
            }

            // THE WORLD BEFORE ITS INHABITANTS. Golems route by what is on the tile in front of
            // them, so restoring a factory's golems into a floor with no belts and no depots
            // brings them back already stalled -- which is exactly what happened between the
            // golem-respawn pass and this one.
            int buildingsRebuilt = 0;
            int buildingsSkipped = 0;
            RestoreBuildings(data, buildingRebuilder, ref buildingsRebuilt, ref buildingsSkipped);

            Dictionary<string, GolemEntity> golemsById = new Dictionary<string, GolemEntity>();
            foreach (GolemEntity golem in golems)
            {
                if (!string.IsNullOrEmpty(golem.GolemId))
                {
                    golemsById[golem.GolemId] = golem;
                }
            }

            int restored = 0;
            int respawned = 0;
            int skipped = 0;

            foreach (GolemEntry entry in data.golems)
            {
                bool wasRespawned = false;
                if (!golemsById.TryGetValue(entry.golemId, out GolemEntity golem))
                {
                    if (!TryRespawnGolem(entry, catalog, respawner, out golem))
                    {
                        skipped++;
                        continue;
                    }

                    wasRespawned = true;
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

                if (wasRespawned)
                {
                    respawned++;
                }
                else
                {
                    restored++;
                }
            }

            return new RestoreReport(restored, respawned, skipped, buildingsRebuilt, buildingsSkipped);
        }

        // Clears first, for the same reason buffers.Clear() runs before buffers are replayed: a
        // load must REPLACE the built world, not merge into it. Loading twice would otherwise
        // stack two factories, and the second copy would fail cell by cell against the first.
        private static void RestoreBuildings(
            SaveData data, IBuildingRebuilder rebuilder, ref int rebuilt, ref int skipped)
        {
            if (rebuilder == null)
            {
                // No rebuilder wired: every entry is skipped, and SAID to be skipped. Silence
                // here is how the whole built world went missing without anyone noticing.
                skipped = data.buildings != null ? data.buildings.Count : 0;
                return;
            }

            rebuilder.ClearPlacedBuildings();
            if (data.buildings == null)
            {
                return;
            }

            foreach (BuildingEntry entry in data.buildings)
            {
                if (!rebuilder.TryRebuild(
                        entry.prefabKey,
                        new UnityEngine.Vector2Int(entry.cellX, entry.cellY),
                        (GolemFactory.World.Facing)entry.facing,
                        out GolemFactory.Buildings.PlaceableBuilding building) || building == null)
                {
                    skipped++;
                    continue;
                }

                RestoreBuildingState(entry, building);
                rebuilt++;
            }
        }

        // Applied after the building exists, because both of these live on components that only
        // register themselves during the placement the rebuilder just performed.
        private static void RestoreBuildingState(
            BuildingEntry entry, GolemFactory.Buildings.PlaceableBuilding building)
        {
            var boiler = building.GetComponent<GolemFactory.Buildings.PlaceableBoiler>();
            if (boiler != null && boiler.Boiler != null)
            {
                // SetCoke, not AddCoke: the rebuilt boiler was placed with whatever the prefab
                // says it starts with, and adding to that would hand the player free fuel on
                // every load.
                boiler.Boiler.SetCoke(entry.cokeStock);
            }

            // SetFilter, not a bare field write: the rebuilder has already placed this depot and
            // published an UNFILTERED endpoint on its cell, so the label has to re-register the
            // tile to mean anything. That is exactly why PlaceableDepot.SetFilter re-publishes.
            var depot = building.GetComponent<GolemFactory.Buildings.PlaceableDepot>();
            if (depot != null)
            {
                depot.SetFilter(entry.depotFilterItemType);
            }

            // Restore, not Add: the rebuilt hopper was placed with whatever the prefab starts
            // with, and adding to that would mint fuel and goods on every load -- the same
            // reasoning SteamBoiler.SetCoke records above.
            var recycler = building.GetComponent<GolemFactory.Buildings.PlaceableScrapRecycler>();
            if (recycler != null && recycler.Recycler != null)
            {
                recycler.Recycler.Restore(
                    entry.recyclerCokeStock, entry.recyclerScrapStock, entry.recyclerPendingPoints);
            }

            var tower = building.GetComponent<GolemFactory.Buildings.PlaceableClockTower>();
            if (tower != null && tower.SiteHolder != null && tower.SiteHolder.Site != null)
            {
                tower.SiteHolder.Site.RestoreProgress(
                    entry.clockTowerStageIndex, entry.clockTowerProgressUnits, entry.clockTowerComplete);
            }
        }

        // Three conditions, and the first two are refusals rather than failures:
        //
        // No respawner means the caller did not ask for this (every pre-existing call site, and
        // every test written before it existed), so the entry is skipped exactly as it was.
        //
        // `wasRuntimeSpawned == false` means the save is describing a golem that was authored
        // into a scene. Rebuilding one from GolemPrefab would produce a different object wearing
        // its name -- no hand-wired references, no deliberately-absent spatial routing -- so a
        // missing scene golem stays missing, which is the honest answer.
        //
        // A chassis the catalog cannot resolve is the genuine failure, and it also has to be
        // caught HERE rather than after spawning: a golem is placed on a cell and registered
        // with the clock at birth, so spawning first and discovering the chassis is unresolvable
        // afterwards would leave an empty golem standing in the factory occupying a tile.
        private static bool TryRespawnGolem(
            GolemEntry entry, DefinitionCatalog catalog, IGolemRespawner respawner,
            out GolemEntity golem)
        {
            golem = null;
            if (respawner == null || !entry.wasRuntimeSpawned)
            {
                return false;
            }

            ChassisDefinition chassis = catalog.FindChassis(entry.chassisName);
            if (chassis == null)
            {
                return false;
            }

            return respawner.TryRespawn(
                entry.golemId, chassis,
                new UnityEngine.Vector2Int(entry.cellX, entry.cellY),
                (GolemFactory.World.Facing)entry.facing,
                out golem) && golem != null;
        }
    }
}
