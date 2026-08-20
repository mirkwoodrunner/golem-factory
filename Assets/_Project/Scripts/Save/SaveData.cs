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
        public List<BufferEntry> buffers = new List<BufferEntry>();
        public float focusCurrent;
        public List<BlueprintEntry> blueprints = new List<BlueprintEntry>();
        public List<GolemEntry> golems = new List<GolemEntry>();

        // Everything the player BUILT: belts, depots, boilers, steam pipes, the Clock Tower.
        // Absent until now, which made the rest of this file a save of a factory's contents with
        // no factory around them -- golems came back (see GolemEntry.wasRuntimeSpawned) and
        // stalled immediately, because the belts they push into and the depots they fill did not.
        public List<BuildingEntry> buildings = new List<BuildingEntry>();
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
