using UnityEngine;

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
        Assemble
    }

    [CreateAssetMenu(fileName = "NewAppendageAction", menuName = "Golem Factory/Punch Cards/Appendage Action")]
    public sealed class AppendageActionDefinition : ScriptableObject
    {
        public AppendageActionType actionType;

        [Tooltip("Authored duration. Only Refine and Assemble use it -- Haul, ExtractFromNode " +
                 "and Push derive their duration from quantity (progression-design section 2).")]
        public int durationTicks = 1;

        public string sourceId;
        public string destinationId;

        [Tooltip("AUTHORED DEFAULT ONLY. The live, player-set quantity for a particular golem " +
                 "slot lives on GolemProgram.appendageQuantities -- this asset is shared by " +
                 "every golem using the card, so writing a player's batch size here would " +
                 "change all of them at once.")]
        public int haulQuantity = 1;

        [Tooltip("Refine: item type withdrawn from the sourceId buffer. Assemble: the single " +
                 "precursor consumed from the golem's input stock (section 1.3 replaces this " +
                 "with a RecipeDefinition carrying 1-4 typed inputs). Haul: the type to pull " +
                 "off the tile behind; leave blank to take whatever that tile offers.")]
        public string inputItemType;

        [Tooltip("Refine: item type deposited into the destinationId buffer. Assemble: the " +
                 "type deposited into the golem's own output stock.")]
        public string outputItemType;
    }
}
