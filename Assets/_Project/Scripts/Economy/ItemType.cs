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
    }
}
