using System;
using System.Collections.Generic;

namespace GolemFactory.Save
{
    // JsonUtility can't serialize Dictionary or polymorphic ScriptableObject references,
    // so buffer contents are parallel lists and every asset reference is stored as its
    // name (resolved back via DefinitionCatalog on load). Deliberately excludes belt
    // contents/positions and tick count -- a "continue where you left off" save of the
    // economy/golem-programs, not a byte-for-byte simulation snapshot.
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>The format this build writes: 1 since crates (docs/crates-design.md §7).</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// The format a save was written in. Absent reads 0 -- the initializer must stay 0 for
        /// that -- which is a save from before crates: one stockpile, depots that held nothing.
        /// </summary>
        public int version;

        // The pack (and, once it exists, the Post store) by id. A crate that owns its buffer is
        // NOT here: buffers restore before buildings, and the load's sweep of the old buildings
        // removes their crates, so its contents ride on its BuildingEntry instead.
        public List<BufferEntry> buffers = new List<BufferEntry>();
        public List<BlueprintEntry> blueprints = new List<BlueprintEntry>();
        public List<GolemEntry> golems = new List<GolemEntry>();

        // Everything the player BUILT: belts, depots, boilers, steam pipes, the Clock Tower.
        // Absent until now, which made the rest of this file a save of a factory's contents with
        // no factory around them -- golems came back (see GolemEntry.wasRuntimeSpawned) and
        // stalled immediately, because the belts they push into and the depots they fill did not.
        public List<BuildingEntry> buildings = new List<BuildingEntry>();

        // The factory's progress (G10): what Unity's save never held. Null in a save written
        // before it existed, and a load then leaves progress exactly as it is.
        public ProgressEntry progress;

        /// <summary>
        /// The market street: what each stall holds and the carts on the road (Phase 3
        /// critique). Null in a save written before it, and a load then leaves the street as it is.
        /// </summary>
        public MarketEntry market;

        /// <summary>
        /// What a save slot shows about this save (docs/menus-design.md §7): when it was written,
        /// where the guide was, how big the factory is. Null in a save from before slots; a slot
        /// then falls back to the file's own time. Nothing here is read back into the world.
        /// </summary>
        public MetaEntry meta;

        /// <summary>
        /// A Creative world's save (docs/menus-design.md §9). Absent, false, in every save before
        /// Creative, which were all campaign games. A load refuses a save of the other kind.
        /// </summary>
        public bool creative;
    }

    /// <summary>A save's summary for its slot: display only, never restored.</summary>
    [Serializable]
    public sealed class MetaEntry
    {
        /// <summary>
        /// When it was written, in UTC and the round-trip format ("o"): the file's own time
        /// changes when the file is copied, this doesn't.
        /// </summary>
        public string savedAtUtc;

        /// <summary>Written by the game, not the player: on quit, or when a tower stage completes.</summary>
        public bool autosave;

        /// <summary>Why an autosave was made ("on quit", "Tower stage 2 complete"); empty for the player's own.</summary>
        public string reason;

        /// <summary>The guide's chapter and its title, or 0 when the guide is finished or put away.</summary>
        public int chapter;
        public string chapterTitle;

        public int golems;
        public int buildings;

        /// <summary>Clock Tower stages completed, and whether the tower stands.</summary>
        public int towerStagesDone;
        public bool towerComplete;
    }

    /// <summary>
    /// The stalls' stock and the truckloads in flight, as parallel lists like
    /// <see cref="BufferEntry"/>. A truckload keeps the ticks it still has to travel, since a
    /// save keeps no tick count.
    /// </summary>
    [Serializable]
    public sealed class MarketEntry
    {
        public List<string> stallIds = new List<string>();
        public List<int> stallStocks = new List<int>();
        public List<string> cartStallIds = new List<string>();
        public List<int> cartQuantities = new List<int>();
        public List<long> cartTicksLeft = new List<long>();
    }

    /// <summary>
    /// Progress a session earns beyond its goods and buildings: Assembly Line claims, the tech
    /// tree's ledger, the Floor Expansion and assembly-bay purchases, and the clock's setting.
    /// Cards, chassis and buildings are by NAME, like the rest of this file.
    /// </summary>
    [Serializable]
    public sealed class ProgressEntry
    {
        public List<string> claimedCards = new List<string>();
        public List<string> ledgerItems = new List<string>();
        public List<string> ledgerChassis = new List<string>();
        public List<string> ledgerBuildings = new List<string>();
        public List<string> ledgerCards = new List<string>();
        public int ledgerTowerStages;
        public int floorNorthExtent;
        public int assemblyBayTier = 1;

        /// <summary>Pack upgrades bought (docs/pack-upgrades-design.md): absent in an older save, so 0, the base cap.</summary>
        public int packUpgradeTier;
        public float clockSpeed = 1f;
        public bool clockPaused;
        public int tutorialStep;

        /// <summary>
        /// The guide's step by id, which survives steps being inserted; <see cref="tutorialStep"/>
        /// stays for saves written before it.
        /// </summary>
        public string tutorialStepId;

        /// <summary>
        /// Which golem each guide role names ("scav2=PlayerGolem-004"), so a load keeps talking
        /// about the golem the player built for it rather than guessing by build order.
        /// </summary>
        public List<string> tutorialRoles = new List<string>();
        public bool tutorialDismissed;

        /// <summary>
        /// The Clock Tower on its town-square site (G10). A fixture is never in the building
        /// list -- only what the player placed is -- so its progress rides here instead of on a
        /// BuildingEntry, which is where a placed tower's always went.
        /// </summary>
        public int clockTowerStageIndex;
        public long clockTowerProgressUnits;
        public bool clockTowerComplete;
    }

    /// <summary>
    /// One player-placed building. Buildings authored into the scene are never captured -- see
    /// <c>PlaceableBuilding.IsRuntimePlaced</c> -- because the scene brings those back by itself
    /// and rebuilding them would double them on the floor.
    /// </summary>
    [Serializable]
    public sealed class BuildingEntry
    {
        /// <summary>The prefab's name, resolved back through BuildModeController's own roster.</summary>
        public string prefabKey;

        public int cellX;
        public int cellY;

        /// <summary>
        /// Load-bearing, not decoration: a belt's facing IS its routing, and a construction
        /// station's decides which tile its golems step out onto. A factory restored with every
        /// belt pointing North is a different factory.
        /// </summary>
        public int facing;

        // --- Per-type state, each meaningless on the buildings that do not have it ------------
        // Kept as flat fields rather than a polymorphic hierarchy because JsonUtility cannot
        // serialize one, which is the same constraint that made buffer contents parallel lists.

        /// <summary>A boiler's remaining fuel. Dropping it would burn the player's Coke on load.</summary>
        public int cokeStock;

        /// <summary>
        /// A depot's label (docs/cozy-automation-design.md §1), empty for "any goods".
        ///
        /// <para>
        /// Saved for the same reason a belt's facing is: it is routing, not decoration. A
        /// factory restored with every crate unlabelled is a factory whose sorters have all been
        /// wiped, and the player would find out one stalled golem at a time.
        /// </para>
        /// </summary>
        public string depotFilterItemType;

        /// <summary>
        /// A crate's own goods (a depot's or a mast's), parallel lists as <see cref="BufferEntry"/>
        /// keeps. Empty in a save from before crates, whose depots held nothing of their own.
        /// </summary>
        public List<string> crateItemTypes = new List<string>();
        public List<int> crateQuantities = new List<int>();

        /// <summary>
        /// A Scrap Recycler's fuel, its uncollected Scrap, and its banked remainder
        /// (docs/cozy-automation-design.md §4b). All three, because dropping the remainder would
        /// silently round every hopper's ratio down on every load -- the exact thing the integer
        /// accumulator exists to prevent.
        /// </summary>
        public int recyclerCokeStock;

        public int recyclerScrapStock;

        public int recyclerPendingPoints;

        /// <summary>
        /// Clock Tower progress: which stage is running and how far into it, in the same
        /// millionths-of-a-tick units the site accrues. Saved because a stage is measured in
        /// tens of minutes of a whole factory's output -- by far the most expensive single
        /// number in a save file.
        /// </summary>
        public int clockTowerStageIndex;
        public long clockTowerProgressUnits;
        public bool clockTowerComplete;
    }

    [Serializable]
    public sealed class BufferEntry
    {
        public string bufferId;
        public List<string> itemTypes = new List<string>();
        public List<int> quantities = new List<int>();
    }

    [Serializable]
    public sealed class BlueprintEntry
    {
        public string blueprintId;
        public string ownerId;
        public string chassisName;
        public string logicCoreName;
        public List<string> appendageNames = new List<string>();

        // G10: each slot's hauled good and batch. Absent in an older save: the cards' own.
        public List<string> appendageItemTypes = new List<string>();
        public List<int> appendageQuantities = new List<int>();
    }

    [Serializable]
    public sealed class GolemEntry
    {
        public string golemId;
        public string chassisName;
        public string logicCoreName;
        public List<string> appendageNames = new List<string>();
        public int currentStepIndex;
        public int state;

        // Where the golem stands and which way it points. Saved because facing is now
        // load-bearing routing, not decoration: a spatially placed golem restored with its
        // program but not its facing would come back pointing North at empty ground and stall
        // for reasons the player has no way to connect to loading. Defaults (0,0)/North are
        // harmless for a golem that was never placed spatially, since such a golem ignores
        // cell and facing entirely.
        public int cellX;
        public int cellY;
        public int facing;

        // Whether this golem was BUILT DURING PLAY, and can therefore be rebuilt on load rather
        // than only reprogrammed. Captured from GolemEntity.IsRuntimeSpawned.
        //
        // The whole point of the flag is what happens when it is FALSE: a scene golem is
        // hand-wired and cannot be reconstructed from GolemPrefab, so it is left to be matched
        // by id against the live scene exactly as before. An older save has no field at all and
        // deserializes to false, which restores it byte for byte as it always did -- the same
        // opt-in fork the machine model, spatial routing and steam all ride.
        public bool wasRuntimeSpawned;

        // Per-slot Haul batch sizes, parallel to appendageNames. Absent in a pre-machine-model
        // save, which restores fine -- GolemProgram self-heals a short list back to the
        // authored per-card defaults.
        public List<int> appendageQuantities = new List<int>();

        /// <summary>G10: each slot's hauled good. Absent in an older save: the cards' own.</summary>
        public List<string> appendageItemTypes = new List<string>();

        // The golem's internal input/output stock. Parallel string/int lists per stock rather
        // than a dictionary, following BufferEntry exactly, because JsonUtility serializes
        // neither Dictionary nor a List of KeyValuePair. Saved because a golem mid-cycle can be
        // holding up to 12 of several types: dropping that on load would quietly delete goods
        // the player watched it collect, and would also reset a nearly-finished batch.
        public List<string> inputStockTypes = new List<string>();
        public List<int> inputStockQuantities = new List<int>();
        public List<string> outputStockTypes = new List<string>();
        public List<int> outputStockQuantities = new List<int>();
    }
}
