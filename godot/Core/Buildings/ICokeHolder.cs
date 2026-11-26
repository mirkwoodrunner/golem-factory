namespace GolemFactory.Buildings
{
    /// <summary>
    /// A machine that burns Coke it holds: a boiler, a Slag Heap, a Scrap Recycler. The player's
    /// hands treat them alike (2026-11-26): [E] loads Coke from the pack, [I] takes it back, so
    /// any of them can be emptied and then demolished, whatever it holds. Golems feed them through
    /// each machine's own tile.
    /// </summary>
    public interface ICokeHolder
    {
        /// <summary>"Boiler", "Slag Heap", "Scrap Recycler": the prompt's name.</summary>
        string CokeHolderName { get; }

        /// <summary>The name inside a sentence: "the boiler", "the Slag Heap".</summary>
        string CokeHolderInSentence { get; }

        /// <summary>Whether the machine is live (a boiler before it joins the network is not).</summary>
        bool HasCokeStore { get; }

        int CokeStock { get; }

        /// <summary>Its cap: what [E] fills it to. An old save can hold more than this.</summary>
        int CokeCapacity { get; }

        /// <summary>How much more it takes; 0 at or over its cap.</summary>
        int CokeRoom { get; }

        /// <summary>Adds up to <paramref name="quantity"/>, never past the cap. Returns how much went in.</summary>
        int AddCoke(int quantity);

        /// <summary>Takes up to <paramref name="quantity"/> out. Returns how much came out.</summary>
        int TakeCoke(int quantity);
    }
}
