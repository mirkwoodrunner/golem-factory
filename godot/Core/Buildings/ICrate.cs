using GolemFactory.Economy;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A building with a crate on its tile (docs/crates-design.md §4.1): a depot, or a Freight
    /// Mast. The interactor, demolition and the save ask this, never the concrete part, so a mast
    /// and a depot are collected, refunded and saved by one path.
    /// </summary>
    public interface ICrate
    {
        /// <summary>The buffer id: <c>Crate(x,y)</c>, <c>Mast(x,y)</c>, or a shared buffer's. Null before placement.</summary>
        string CrateId { get; }

        /// <summary>The buffer the tile publishes. Null before placement.</summary>
        StorageBuffer Buffer { get; }

        /// <summary>
        /// Whether the crate is its own. False for one opening onto a shared store (a Post crate,
        /// §10): demolishing that refunds nothing of what it shows, and the save keeps it by id.
        /// </summary>
        bool OwnsBuffer { get; }

        /// <summary>The label, or null for "any goods".</summary>
        string FilterItemType { get; }

        /// <summary>False for a mast: what a Zeppelin lands is whatever it carried.</summary>
        bool CanLabel { get; }

        /// <summary>Labels the crate (null clears it) and re-publishes its tile.</summary>
        void SetFilter(string itemType);
    }
}
