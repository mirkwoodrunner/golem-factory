namespace GolemFactory.Belts
{
    // Mutable on purpose (Progress changes every tick). Held in BeltSegment's List<ItemStack>
    // and mutated via read-copy -> mutate -> write-back through the list indexer, since
    // List<T>'s indexer is not addressable (list[i].Progress = x is CS1612) and foreach
    // yields readonly copies. ItemType is a bare string id for now, matching the
    // sourceId/destinationId/golemId convention elsewhere -- Economy/ItemType is M5+.
    public struct ItemStack
    {
        public string ItemType;
        public float Progress;
    }
}
