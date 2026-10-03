using GolemFactory.Compat;

namespace GolemFactory.PunchCards
{
    // APPEND ONLY. Unity serializes an enum field by its integer value, so every authored
    // .asset under ScriptableObjects/Appendages/ stores its actionType as an index into this
    // list (ExtractScrap is `actionType: 1`, RefineBrass is `2`, LoadIntoScrapBuffer is `3`).
    // Reordering or inserting would silently repoint every one of them at a different verb,
    // with no compile error and no import warning.
    public enum AppendageActionType
    {
        Haul,
        ExtractFromNode,
        Refine,
        LoadIntoBuffer,

        // docs/progression-design.md §2. Push empties the golem's whole output stock onto the
        // tile in front; Assemble consumes from its input stock and deposits into its output
        // stock without ever touching a tile. Both are new verbs with no legacy call sites.
        Push,
        Assemble,

        // docs/progression-design.md §6, "The Overclocker's verb". Re-runs the immediately
        // preceding Assemble n more times from the same input stock, amortising the fixed
        // Haul/Push overhead across a batch. APPENDED, like every member before it: this enum
        // is serialized by integer index into authored .asset files, so inserting anywhere but
        // the end would silently re-point every card in the project at a different verb.
        Repeat,

        // docs/progression-design.md §6, "The Zeppelin's verb". Empties the golem's output
        // stock onto its bound Freight Mast's tile, regardless of distance -- one-way, fixed
        // pair, 24 ticks, no pathfinding and no adaptivity. APPENDED, like every member before
        // it: this enum is serialized by integer index into authored .asset files.
        FreightLaunch
    }

    public sealed class AppendageActionDefinition
    {
        // ScriptableObject.name in Unity: the asset's file name, which the catalog,
        // the save file and the Ledger all key on. Plain data now, so it is a field.
        public string name;

        public AppendageActionType actionType;

        // Authored duration. Only Refine uses it -- Haul, ExtractFromNode and Push derive theirs
        // from quantity, and Assemble takes its recipe's durationTicks (progression-design section
        // 2).
        public int durationTicks = 1;

        public string sourceId;
        public string destinationId;

        // AUTHORED DEFAULT ONLY. The live, player-set quantity for a particular golem slot lives
        // on GolemProgram.appendageQuantities -- this asset is shared by every golem using the
        // card, so writing a player's batch size here would change all of them at once.
        public int haulQuantity = 1;

        // --- Assemble's recipe (docs/progression-design.md §5.2, §11 item 2) ------------------
        // Assemble reads THIS AND NOTHING ELSE. The inputItemType/outputItemType pair below was
        // an explicit placeholder while §1.1 built the machine model against a single-input
        // Assemble; it is no longer consulted by Assemble at all. The two fields stay because
        // Refine (and, for inputItemType, Haul) still mean something by them.
        // Assemble ONLY. The recipe this card runs: 1-4 typed inputs consumed atomically from the
        // golem's input stock, an output (quantity may exceed 1) and one optional byproduct
        // deposited into its output stock. An Assemble card with no recipe stalls 'not wired up'.
        public RecipeDefinition recipe;

        // NOT READ BY ASSEMBLE -- Assemble's inputs come from its recipe. Refine: item type
        // withdrawn from the sourceId buffer. Haul: the type to pull off the tile behind; leave
        // blank to take whatever that tile offers.
        public string inputItemType;

        // NOT READ BY ASSEMBLE -- Assemble's output comes from its recipe. Refine: item type
        // deposited into the destinationId buffer.
        public string outputItemType;
    }
}
