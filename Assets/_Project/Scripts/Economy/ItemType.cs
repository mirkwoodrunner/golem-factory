namespace GolemFactory.Economy
{
    // Canonical item type ids, matching the bare-string-id convention used for
    // node/buffer/belt-segment ids elsewhere (see Belts/ItemStack.cs). Centralized here
    // so ResourceNode/StorageBuffer/Refine recipes don't restate raw literals.
    public static class ItemType
    {
        public const string Scrap = "Scrap";
        public const string Brass = "Brass";
        public const string Aether = "Aether";

        // Boiler fuel (docs/progression-design.md §3.1). Added ahead of the rest of §1.5's
        // 24-item roster because steam power is meaningless without the good it burns: the
        // Steam/ system, the fuel gauge and every burn test name this constant. The other
        // twenty items land with §1.5's authoring pass, together with the coal node and the
        // coking recipe that are the only things able to *produce* this -- which is exactly
        // why SandboxBootstrap.requireSteamPower is still off (see the §1.4 notes in
        // docs/open-items.md).
        public const string Coke = "Coke";
    }
}
