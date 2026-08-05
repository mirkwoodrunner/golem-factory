using GolemFactory.Belts;

namespace GolemFactory.World
{
    // Anything that sits on a cell and can hand an item over and/or accept one. This is the
    // seam that lets a golem route by *position* instead of by the bare-string sourceId/
    // destinationId baked into its appendage asset: the golem asks "what is on the tile
    // behind me" rather than "what is the id my card names".
    //
    // Kept deliberately narrow (one item at a time, no ids, no capacity introspection) --
    // ResourceNode, BeltSegment and StorageBuffer have nothing else in common, and a wider
    // interface would just push their differences into every adapter.
    public interface IItemEndpoint
    {
        /// <summary>Human-readable label for stall text and Inspector debugging.</summary>
        string DisplayName { get; }

        /// <summary>Removes one item from this endpoint. False if it has nothing to give.</summary>
        bool TryTake(out ItemStack item);

        /// <summary>
        /// Side-effect-free half of <see cref="TryGive"/>'s guard. Exists for exactly the
        /// ordering hazard that caused the extract-onto-a-full-belt item-loss bug: a producer
        /// pulling from an irreversible source (a finite ResourceNode) must confirm the
        /// destination has room *before* it consumes, not after. Mirrors
        /// BeltSegment.CanEnqueue, which was added for the id-routed version of this bug.
        /// </summary>
        bool CanGive();

        /// <summary>Hands one item to this endpoint. False if it had no room.</summary>
        bool TryGive(ItemStack item);
    }
}
