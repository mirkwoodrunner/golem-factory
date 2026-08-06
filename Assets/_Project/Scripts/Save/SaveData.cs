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
