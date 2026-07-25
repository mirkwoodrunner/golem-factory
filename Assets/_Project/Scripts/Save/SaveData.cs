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
    }
}
