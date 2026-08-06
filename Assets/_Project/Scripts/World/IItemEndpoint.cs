using GolemFactory.Belts;

namespace GolemFactory.World
{
    // Anything that sits on a cell and can hand an item over and/or accept one. This is the
    // seam that lets a golem route by *position* instead of by the bare-string sourceId/
    // destinationId baked into its appendage asset: the golem asks "what is on the tile
    // behind me" rather than "what is the id my card names".
    //
    // Was kept deliberately narrow (one item at a time, no ids, no type, no capacity
    // introspection) -- ResourceNode, BeltSegment and StorageBuffer have nothing else in
    // common, and a wider interface would just push their differences into every adapter.
    //
    // It widened by exactly one concept: a TYPED, QUANTIFIED take. That is not a drift back
    // toward a fat interface, it is the specific ask docs/progression-design.md §2 makes, and
    // the reason it is worth it is written down in two places already:
    //
    //   * §2 ("A golem is a machine with an internal inventory"): Haul must pull one named
    //     item type into the golem's own typed input stock so Assemble reads a dictionary the
    //     golem owns and can never transmute the wrong thing.
    //   * GolemEntity.BeginRefine's exemption comment, which says Refine stays keyed to buffer
    //     ids "until IItemEndpoint grows a typed take". This is that growth. §1.3 replaces
    //     Refine with a recipe-driven Assemble that reads internal stock, so the exemption gets
    //     to stay exactly as written rather than needing a spatial rewrite.
    //
    // Everything else about the contract is unchanged, including the untyped TryTake below:
    // the id-routed path (Main.unity's demo golems) and the legacy one-item spatial transfer
    // still use it.
    public interface IItemEndpoint
    {
        /// <summary>Human-readable label for stall text and Inspector debugging.</summary>
        string DisplayName { get; }

        /// <summary>Removes one item from this endpoint. False if it has nothing to give.</summary>
        bool TryTake(out ItemStack item);

        /// <summary>
        /// Item type this endpoint would dispense right now, or null if it has nothing.
        /// Side-effect-free. Lets ExtractFromNode name the node's single type without having
        /// to consume a unit to find out what it is.
        /// </summary>
        string PeekAvailableType();

        /// <summary>
        /// Removes up to <paramref name="quantity"/> of exactly <paramref name="itemType"/>.
        /// Returns true if <paramref name="taken"/> ended up greater than zero.
        ///
        /// Partial takes are deliberate and normal -- a Haul(Scrap, 8) against a tile holding
        /// 3 Scrap takes 3 and succeeds, because stalling a golem that CAN make progress would
        /// deadlock every under-supplied line in the factory. Callers must therefore read
        /// <paramref name="taken"/> rather than assuming they got what they asked for.
        /// </summary>
        bool TryTake(string itemType, int quantity, out int taken);

        /// <summary>
        /// "Could this endpoint accept ANYTHING AT ALL right now?" -- deliberately untyped.
        ///
        /// Side-effect-free half of <see cref="TryGive"/>'s guard. Exists for exactly the
        /// ordering hazard that caused the extract-onto-a-full-belt item-loss bug: a producer
        /// pulling from an irreversible source (a finite ResourceNode) must confirm the
        /// destination has room *before* it consumes, not after. Mirrors
        /// BeltSegment.CanEnqueue, which was added for the id-routed version of this bug.
        ///
        /// THIS IS NOT A DUPLICATE OF <see cref="CanGive(string)"/> AND MUST NOT BE DELETED
        /// AS ONE. The two answer different questions and diverge on exactly the endpoint the
        /// Slag economy runs on: a per-item-type-capped StorageBuffer whose Slag slot is full
        /// still accepts Iron Plate, so the typed answer is false and the untyped one true.
        /// The untyped question is still the right one in two places -- the legacy id-routed
        /// path, which has no item type to ask about, and Push's early-out, which needs to
        /// know whether continuing to the next type could possibly help. A belt's capacity is
        /// genuinely not per-type, so its adapter answers both identically; that is a property
        /// of belts, not evidence the two overloads are the same question.
        /// </summary>
        bool CanGive();

        /// <summary>
        /// "Could this endpoint accept a unit of <paramref name="itemType"/> right now?"
        ///
        /// Added because capacity became per item type (docs/progression-design.md §10): with
        /// a per-type cap, "do you have room?" is unanswerable without naming the type. See
        /// <see cref="CanGive()"/> for why both overloads exist.
        /// </summary>
        bool CanGive(string itemType);

        /// <summary>Hands one item to this endpoint. False if it had no room.</summary>
        bool TryGive(ItemStack item);
    }
}
