namespace GolemFactory.Economy
{
    // Canonical item type ids, matching the bare-string-id convention used for
    // node/buffer/belt-segment ids elsewhere (see Belts/ItemStack.cs). Centralized here
    // so ResourceNode/StorageBuffer/recipes don't restate raw literals.
    //
    // THE FULL 24-ITEM ROSTER of docs/progression-design.md §5.1, grouped by its tiers. The ids
    // are taken verbatim from §5.1's `id` column -- they are serialized into save files, authored
    // into .asset recipes, and matched by ordinal string comparison in GolemInventory and
    // StorageBuffer, so a "tidier" spelling here is a silent data migration, not a rename.
    //
    // Dead-end audit (§5.1, and pinned by RecipeCatalogTests.NoDeadEndItems): every Tier 0-4
    // item below has at least one consumer among the authored recipes or the five chassis cost
    // bundles. The four Tier-5 goods are terminal by design -- the Clock Tower (§1.6, unbuilt)
    // is their sink and the win condition.
    public static class ItemType
    {
        // --- Tier 0: raw, node-extracted, hand-harvestable (§5.1) --------------------------
        public const string Scrap = "Scrap";
        public const string Coal = "Coal";
        public const string CopperOre = "CopperOre";
        public const string ZincOre = "ZincOre";

        // Aether Crystal. The id predates the design and stays as-is: it is written into
        // Sandbox.unity's AetherNode marker and into every existing save.
        public const string Aether = "Aether";

        // --- Tier 1: basic processing, 1 input (Brass Presser) -----------------------------

        // Boiler fuel (§3.1) and the throat of the whole economy: R4/R5/R6 consume it, every
        // powered golem burns 6/min of it via its Boiler, and the Slag Heap charges it to void
        // Slag. Added ahead of the rest of the roster during §1.4 because steam power is
        // meaningless without the good it burns.
        public const string Coke = "Coke";
        public const string IronPlate = "IronPlate";
        public const string Slag = "Slag";
        public const string Glass = "Glass";

        // --- Tier 2: metals (Aether-Hauler, 2 inputs; Copper Wire is 1) ---------------------
        public const string CopperIngot = "CopperIngot";
        public const string ZincIngot = "ZincIngot";

        // §5.1: Brass is MANUFACTURED (R7) from Phase 4 on and never dug up -- BrassNode is
        // deleted from SandboxBootstrap. The id is unchanged because RefineBrass.asset, every
        // existing save and the pre-existing tests all name it.
        public const string Brass = "Brass";
        public const string CopperWire = "CopperWire";

        // --- Tier 3: components ------------------------------------------------------------
        public const string Gear = "Gear";
        public const string Casing = "Casing";
        public const string Lens = "Lens";
        public const string Mainspring = "Mainspring";
        public const string AetherCell = "AetherCell";

        // --- Tier 4: mechanisms -------------------------------------------------------------
        public const string Mechanism = "Mechanism";
        public const string Regulator = "Regulator";

        // --- Tier 5: megaproject goods, terminal in the Clock Tower -------------------------
        public const string FrameSection = "FrameSection";
        public const string GreatCog = "GreatCog";
        public const string AetherConduit = "AetherConduit";
        public const string ChronometerCore = "ChronometerCore";
    }
}
