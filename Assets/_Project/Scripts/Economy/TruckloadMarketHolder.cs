using System.Collections.Generic;
using UnityEngine;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.World;

namespace GolemFactory.Economy
{
    /// <summary>
    /// Scene presence for <see cref="TruckloadMarket"/> (Holder pattern), plus the authored
    /// offer table. Registered with the clock by <c>SandboxBootstrap</c>, so delivery time
    /// follows Play/Pause and the speed multiplier like every other duration in the game.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TruckloadMarketHolder : MonoBehaviour, ITickable
    {
        /// <summary>
        /// One authored stall. A serializable mirror of <see cref="MarketOffer"/> -- the runtime
        /// type is immutable and engine-free, and Unity cannot serialize either of those.
        /// </summary>
        [System.Serializable]
        public struct OfferEntry
        {
            public string nodeId;
            public List<RecipeIngredient> price;
            public int truckloadSize;
            public int deliveryTicks;
        }

        // EVERY NUMBER HERE IS TUNING, NOT DERIVED. §5.1 prices no raw good, so these are a
        // first pass for the Game Director to balance in play -- the mechanism is what this
        // pass owes, and the balance is explicitly theirs.
        //
        // THE SCRAP STALL IS FREE, and that one is structural rather than tuning: §10 forbids a
        // soft-lock, and if every stall cost Scrap a player at zero Scrap could buy nothing,
        // make nothing and never recover. See the note on TruckloadMarket.
        [SerializeField]
        private List<OfferEntry> offers = new List<OfferEntry>();

        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private GameModeHolder gameModeHolder;

        private TruckloadMarket _market;
        private long _currentTick;

        public long CurrentTick => _currentTick;

        public TruckloadMarket Market
        {
            get
            {
                if (_market == null)
                {
                    Rebuild();
                }

                return _market;
            }
        }

        public void Configure(ResourceNodeRegistryHolder nodes, GameModeHolder mode)
        {
            nodeRegistryHolder = nodes;
            gameModeHolder = mode;
            Rebuild();
        }

        /// <summary>
        /// Rebuilds the market from the authored table. "Always re-render from data", the same
        /// idiom <c>WorkbenchController.RebuildUI</c> and <c>BeltNetwork.Relink</c> follow --
        /// re-running this after an offer is retuned replaces the table rather than merging into
        /// it, so a removed stall actually goes.
        /// </summary>
        public void Rebuild()
        {
            ResourceNodeRegistry nodes = nodeRegistryHolder != null ? nodeRegistryHolder.Registry : null;
            GameMode mode = gameModeHolder != null ? gameModeHolder.Mode : new GameMode();
            _market = new TruckloadMarket(nodes, mode);

            for (int i = 0; i < offers.Count; i++)
            {
                OfferEntry entry = offers[i];
                _market.AddOffer(new MarketOffer(
                    entry.nodeId, entry.price, entry.truckloadSize, entry.deliveryTicks));
            }
        }

        public void Tick(long currentTick)
        {
            _currentTick = currentTick;
            Market.Tick(currentTick);
        }
    }
}
