using System.Collections.Generic;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.World;

namespace GolemFactory.Economy
{
    /// <summary>
    /// One stall's terms of trade: what a truckload costs, how much arrives, and how long the
    /// cart takes. Authored per stall rather than derived, because §5.1 gives raw goods no
    /// prices at all and inventing a formula would hide five tuning decisions inside one.
    /// </summary>
    public sealed class MarketOffer
    {
        public string NodeId { get; }
        public IReadOnlyList<RecipeIngredient> Price { get; }
        public int TruckloadSize { get; }
        public int DeliveryTicks { get; }

        public MarketOffer(
            string nodeId, IReadOnlyList<RecipeIngredient> price, int truckloadSize, int deliveryTicks)
        {
            NodeId = nodeId;
            Price = price ?? new List<RecipeIngredient>();
            TruckloadSize = truckloadSize < 1 ? 1 : truckloadSize;
            DeliveryTicks = deliveryTicks < 0 ? 0 : deliveryTicks;
        }

        /// <summary>A stall that asks for nothing. The Scrap stall is one -- see the note on
        /// <see cref="TruckloadMarket"/> about why at least one has to be.</summary>
        public bool IsFree => Price.Count == 0;
    }

    /// <summary>
    /// Why an order was refused, so the caller can say something specific rather than "no".
    /// </summary>
    public enum MarketOrderResult
    {
        Ordered = 0,
        NoSuchStall = 1,
        AlreadyInTransit = 2,
        CannotAfford = 3,

        /// <summary>Creative Mode: there is nothing to order, because nothing runs out.</summary>
        NotNeeded = 4,
    }

    /// <summary>
    /// The market street's economy (docs/progression-design.md §13.2): a raw good is BOUGHT, and
    /// it arrives as a **truckload** -- one batch, after a delay -- rather than trickling out of
    /// an infinite hole.
    ///
    /// <para>
    /// <b>It reuses the node, it does not replace it.</b> A truckload is
    /// <c>ResourceNode.Deliver</c>, and everything downstream -- player harvesting, a golem's
    /// <c>ExtractFromNode</c>, §3.2's two-extractor cap, the marker's depletion tint -- is
    /// untouched and simply sees a stall that now has stock and later does not. That last one is
    /// a small revival: <c>ResourceNodeVisualState</c> has been pinned to full-white since §5.1
    /// made every node infinite, and a finite stall gives the depletion readout its job back.
    /// </para>
    ///
    /// <para>
    /// <b>Bursts are the point, not a side effect.</b> A trickle needs no storage; a lump does.
    /// Buffer chests and accumulator lines exist to smooth a bursty supply into a steady one,
    /// and until the market delivered in lumps the factory was never asked to do it.
    /// </para>
    ///
    /// <para>
    /// <b>Creative Mode bypasses the whole thing</b> (<see cref="GameMode.IsCreativeMode"/>):
    /// stalls stay <c>ResourceNode.Infinite</c>, orders are free and instant, and the behaviour
    /// is byte-for-byte the boulder-era one. The bypass is a state rather than a compile switch
    /// so `Main.unity`, every pre-existing test and every old save stay valid by simply running
    /// in it.
    /// </para>
    ///
    /// <para>
    /// <b>One stall must always be free, and it is Scrap.</b> §10 forbids a soft-lock, and if
    /// every stall cost Scrap then a player at zero Scrap could buy nothing, make nothing and
    /// never recover -- the Hand-Crank Bench does not help, because it needs inputs too. A free
    /// Scrap stall is also the flavour the game already has: the Artificer scavenges. Every
    /// other good is a purchase, which is where the sink lives.
    /// </para>
    ///
    /// <para>
    /// Plain C# behind <c>TruckloadMarketHolder</c>, ticking on the simulation clock, so
    /// delivery time follows Play/Pause and the speed multiplier exactly as every other duration
    /// in the game does.
    /// </para>
    /// </summary>
    public sealed class TruckloadMarket : ITickable
    {
        private sealed class PendingDelivery
        {
            public string NodeId;
            public int Quantity;
            public long ArrivesOnTick;
        }

        private readonly Dictionary<string, MarketOffer> _offers = new Dictionary<string, MarketOffer>();
        private readonly List<PendingDelivery> _inTransit = new List<PendingDelivery>();
        private readonly ResourceNodeRegistry _nodes;
        private readonly GameMode _mode;

        public TruckloadMarket(ResourceNodeRegistry nodes, GameMode mode)
        {
            _nodes = nodes;
            _mode = mode ?? new GameMode();
        }

        public IReadOnlyCollection<string> StallIds => _offers.Keys;

        /// <summary>How many carts are on the road. Exposed for the HUD and for tests.</summary>
        public int InTransitCount => _inTransit.Count;

        public void AddOffer(MarketOffer offer)
        {
            if (offer != null && !string.IsNullOrEmpty(offer.NodeId))
            {
                _offers[offer.NodeId] = offer;
            }
        }

        public bool TryGetOffer(string nodeId, out MarketOffer offer)
        {
            offer = null;
            return nodeId != null && _offers.TryGetValue(nodeId, out offer);
        }

        /// <summary>Whether this stall already has a cart on the way. One at a time, per stall:
        /// queueing orders would let a player convert a wallet into a pipeline with no storage,
        /// which is the pressure the burst is meant to create.</summary>
        public bool IsInTransit(string nodeId)
        {
            for (int i = 0; i < _inTransit.Count; i++)
            {
                if (_inTransit[i].NodeId == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Buys one truckload. Charges atomically through
        /// <see cref="StorageBufferRegistry.TryWithdrawBundle"/> -- the same full-refund
        /// guarantee a chassis and a building placement get, because a two-good price that
        /// took the first good and refused would be theft at the stall.
        /// </summary>
        public MarketOrderResult TryOrder(
            string nodeId, StorageBufferRegistry buffers, string bufferId, long currentTick)
        {
            MarketOffer offer;
            if (!TryGetOffer(nodeId, out offer))
            {
                return MarketOrderResult.NoSuchStall;
            }

            // Creative Mode: the stall never empties, so an order would deliver into an infinite
            // node and change nothing. Reported as NotNeeded rather than silently succeeding, so
            // the UI can say why the prompt is absent.
            if (_mode.IsCreativeMode)
            {
                return MarketOrderResult.NotNeeded;
            }

            if (IsInTransit(nodeId))
            {
                return MarketOrderResult.AlreadyInTransit;
            }

            if (!offer.IsFree)
            {
                if (buffers == null || !buffers.TryWithdrawBundle(bufferId, offer.Price))
                {
                    return MarketOrderResult.CannotAfford;
                }
            }

            _inTransit.Add(new PendingDelivery
            {
                NodeId = nodeId,
                Quantity = offer.TruckloadSize,
                ArrivesOnTick = currentTick + offer.DeliveryTicks,
            });

            return MarketOrderResult.Ordered;
        }

        /// <summary>
        /// Ticks in the delivery clock. Walked back to front so an arrival can be removed
        /// without disturbing the indices of the carts still on the road.
        /// </summary>
        public void Tick(long currentTick)
        {
            for (int i = _inTransit.Count - 1; i >= 0; i--)
            {
                PendingDelivery delivery = _inTransit[i];
                if (delivery.ArrivesOnTick > currentTick)
                {
                    continue;
                }

                ResourceNode node;
                if (_nodes != null && _nodes.TryGetNode(delivery.NodeId, out node))
                {
                    node.Deliver(delivery.Quantity);
                }

                _inTransit.RemoveAt(i);
            }
        }

        /// <summary>
        /// Ticks remaining on this stall's cart, or -1 when nothing is on the way. For the
        /// stall's own prompt: "arriving in 40 ticks" is the only part of a burst economy the
        /// player can plan around.
        /// </summary>
        public long TicksUntilDelivery(string nodeId, long currentTick)
        {
            for (int i = 0; i < _inTransit.Count; i++)
            {
                if (_inTransit[i].NodeId == nodeId)
                {
                    long remaining = _inTransit[i].ArrivesOnTick - currentTick;
                    return remaining < 0 ? 0 : remaining;
                }
            }

            return -1;
        }

        /// <summary>Forgets every cart on the road. The load path: a save restores stall stock,
        /// and replaying a delivery that was in flight when the player quit would mint goods.</summary>
        public void Clear() => _inTransit.Clear();
    }
}
