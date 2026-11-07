using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Supply
{
    /// <summary>
    /// One good's ladder (docs/supply-design.md §3): what a cart of it costs to add, and its
    /// Rate and Capacity rungs. Rung 0 is what a cart has when added; each cost is the price of
    /// the rung after it.
    /// </summary>
    public sealed class CartLadder
    {
        public string ItemType { get; }
        public IReadOnlyList<RecipeIngredient> AddPrice { get; }
        public IReadOnlyList<int> Rates { get; }
        public IReadOnlyList<int> Capacities { get; }
        public IReadOnlyList<IReadOnlyList<RecipeIngredient>> RateCosts { get; }
        public IReadOnlyList<IReadOnlyList<RecipeIngredient>> CapacityCosts { get; }

        public CartLadder(
            string itemType, IReadOnlyList<RecipeIngredient> addPrice,
            IReadOnlyList<int> rates, IReadOnlyList<int> capacities,
            IReadOnlyList<IReadOnlyList<RecipeIngredient>> rateCosts,
            IReadOnlyList<IReadOnlyList<RecipeIngredient>> capacityCosts)
        {
            ItemType = itemType;
            AddPrice = addPrice ?? new List<RecipeIngredient>();
            Rates = rates != null && rates.Count > 0 ? rates : new[] { 0 };
            Capacities = capacities != null && capacities.Count > 0 ? capacities : new[] { 0 };
            RateCosts = rateCosts ?? new List<IReadOnlyList<RecipeIngredient>>();
            CapacityCosts = capacityCosts ?? new List<IReadOnlyList<RecipeIngredient>>();
        }

        /// <summary>How many rungs there are past rung 0: as many as both the rates and their
        /// prices name, so a ladder with a missing price simply ends early.</summary>
        public int TopRateRung => Math.Min(Rates.Count - 1, RateCosts.Count);
        public int TopCapacityRung => Math.Min(Capacities.Count - 1, CapacityCosts.Count);
    }

    /// <summary>A place on the street where a cart can stand (§1).</summary>
    public sealed class CartSpot
    {
        public CartSpot(Vector2Int cell) => Cell = cell;

        public Vector2Int Cell { get; }

        /// <summary>The cart standing here, or null: the spot is roped off.</summary>
        public SupplyCart Cart { get; internal set; }

        public bool IsEmpty => Cart == null;

        /// <summary>The id the cart's node and marker carry. Never shown to the player.</summary>
        public string NodeId => $"Cart({Cell.x},{Cell.y})";
    }

    /// <summary>A bought cart: its good, its two rungs, and the trickle that refills it (§0.2).</summary>
    public sealed class SupplyCart
    {
        private readonly List<RecipeIngredient> _addPaid = new List<RecipeIngredient>();
        private readonly List<RecipeIngredient> _rungsPaid = new List<RecipeIngredient>();

        internal SupplyCart(CartLadder ladder, ResourceNode node, ResourceNodeMarker marker, int ticksPerSecond)
        {
            Ladder = ladder;
            Node = node;
            Marker = marker;
            Flow = new CartFlow(ticksPerSecond);
        }

        /// <summary>Its goods in and out over the last minute, for the market view. A new cart starts a new count.</summary>
        public CartFlow Flow { get; }

        public CartLadder Ladder { get; internal set; }
        public string ItemType => Ladder.ItemType;
        public int RateRung { get; internal set; }
        public int CapacityRung { get; internal set; }
        public int RatePerMinute => Ladder.Rates[Math.Min(RateRung, Ladder.Rates.Count - 1)];
        public int Capacity => Ladder.Capacities[Math.Min(CapacityRung, Ladder.Capacities.Count - 1)];

        /// <summary>
        /// The trickle's remainder, in goods × ticks: each tick adds the rate, and a whole good
        /// comes in for every tick-per-minute it holds. Whole-number arithmetic, so a run is the
        /// same every time (invariant 2).
        /// </summary>
        public long Accumulator { get; internal set; }

        public ResourceNode Node { get; internal set; }
        public ResourceNodeMarker Marker { get; internal set; }
        public int Stock => Node.RemainingQuantity;
        public bool IsInfinite => Node.RemainingQuantity == ResourceNode.Infinite;

        /// <summary>What was paid to add this cart (nothing for a starting cart).</summary>
        public IReadOnlyList<RecipeIngredient> AddPaid => _addPaid;

        /// <summary>What was paid for its rungs.</summary>
        public IReadOnlyList<RecipeIngredient> RungsPaid => _rungsPaid;

        /// <summary>Every good paid for this cart and its rungs: half of it is the removal's refund.</summary>
        public IEnumerable<RecipeIngredient> Paid => _addPaid.Concat(_rungsPaid);

        internal void RecordAdd(IEnumerable<RecipeIngredient> bundle) => Record(_addPaid, bundle);

        internal void RecordRung(IEnumerable<RecipeIngredient> bundle) => Record(_rungsPaid, bundle);

        private static void Record(List<RecipeIngredient> into, IEnumerable<RecipeIngredient> bundle)
        {
            if (bundle != null)
            {
                into.AddRange(bundle.Where(b => b.quantity > 0 && !string.IsNullOrEmpty(b.itemType)));
            }
        }
    }

    /// <summary>What an action at a spot did, and the line that says so.</summary>
    public readonly struct SupplyResult
    {
        public readonly bool Done;
        public readonly string Message;

        private SupplyResult(bool done, string message)
        {
            Done = done;
            Message = message;
        }

        public static SupplyResult Ok(string message) => new SupplyResult(true, message);
        public static SupplyResult Refused(string message) => new SupplyResult(false, message);
    }

    /// <summary>
    /// The market street's carts (docs/supply-design.md): spots where the player adds, upgrades,
    /// changes and removes carts, each a <see cref="ResourceNode"/> that refills itself at its
    /// rate up to its capacity. Every price goes through the wallet; a removal refunds half.
    /// </summary>
    public sealed class SupplyStreet : ITickable
    {
        private readonly List<CartSpot> _spots = new List<CartSpot>();
        private readonly Dictionary<string, CartLadder> _ladders = new Dictionary<string, CartLadder>();
        private readonly ResourceNodeRegistry _nodes;
        private readonly SpatialEndpointRegistry _endpoints;
        private readonly GridCoordinateConverter _cells;
        private readonly List<ResourceNodeMarker> _markers;
        private readonly Func<PlayerWallet> _wallet;
        private readonly Func<string, bool> _seen;
        private readonly Func<Vector2Int, bool> _occupied;
        private readonly HashSet<string> _keptGoods = new HashSet<string>();

        public SupplyStreet(
            ResourceNodeRegistry nodes, SpatialEndpointRegistry endpoints, GridCoordinateConverter cells,
            List<ResourceNodeMarker> markers, Func<PlayerWallet> wallet, Func<string, bool> seen,
            bool creative, int ticksPerMinute, Func<Vector2Int, bool> occupied = null)
        {
            _nodes = nodes;
            _endpoints = endpoints;
            _cells = cells;
            _markers = markers;
            _wallet = wallet;
            _seen = seen ?? (_ => true);
            Creative = creative;
            TicksPerMinute = ticksPerMinute < 1 ? 1 : ticksPerMinute;
            _occupied = occupied ?? (_ => false);
        }

        public bool Creative { get; }
        public int TicksPerMinute { get; }

        /// <summary>Bumped whenever a cart comes, goes or changes, so a reader can cache around it.</summary>
        public int Version { get; private set; }
        public IReadOnlyList<CartSpot> Spots => _spots;
        public IEnumerable<SupplyCart> Carts => _spots.Where(s => s.Cart != null).Select(s => s.Cart);

        /// <summary>The goods the street keeps at least one cart of (§0.9): the starting carts'.</summary>
        public IReadOnlyCollection<string> KeptGoods => _keptGoods;

        public void AddSpot(Vector2Int cell)
        {
            if (_spots.All(s => s.Cell != cell))
            {
                _spots.Add(new CartSpot(cell));
            }
        }

        public void AddLadder(CartLadder ladder)
        {
            if (ladder != null && !string.IsNullOrEmpty(ladder.ItemType))
            {
                _ladders[ladder.ItemType] = ladder;
            }
        }

        public bool TryGetLadder(string itemType, out CartLadder ladder)
        {
            ladder = null;
            return itemType != null && _ladders.TryGetValue(itemType, out ladder);
        }

        public CartSpot SpotAt(Vector2Int cell) => _spots.FirstOrDefault(s => s.Cell == cell);

        public CartSpot SpotOf(string nodeId) => _spots.FirstOrDefault(s => s.NodeId == nodeId);

        /// <summary>Whether this node id is one of the street's carts.</summary>
        public bool IsCart(string nodeId) => _spots.Any(s => s.Cart != null && s.NodeId == nodeId);

        /// <summary>Whether this node id is a cart that refills itself: an empty one is waited
        /// on, grey, not a stall the player must fix (§4).</summary>
        public bool Refills(string nodeId) =>
            SpotOf(nodeId)?.Cart is SupplyCart cart && !cart.IsInfinite && cart.RatePerMinute > 0;

        /// <summary>
        /// A cart the world starts with (§1), free, at rung 0 and full. Its good is kept: the
        /// last cart of it can't be removed or changed, so the street can't be emptied of it.
        /// </summary>
        public void PlaceStartingCart(Vector2Int cell, string itemType)
        {
            CartSpot spot = SpotAt(cell);
            if (spot == null || !spot.IsEmpty || !TryGetLadder(itemType, out CartLadder ladder))
            {
                return;
            }
            _keptGoods.Add(itemType);
            SupplyCart cart = Place(spot, ladder);
            cart.Node.Deliver(cart.Capacity);
            cart.Marker.RefreshVisualState();
            RaiseChanged();
        }

        // --- What a spot offers ------------------------------------------------------------------

        /// <summary>The carts an empty spot offers (invariant 8, the user's call of 2026-11-06):
        /// every ladder whose add price names only goods the Ledger has seen, in the game's
        /// canonical order. A raw good is never seen before its first cart, so the price opens it:
        /// Coal's Scrap from the start, Zinc Ore's Copper Ingot in chapter 8.</summary>
        public IReadOnlyList<CartLadder> Offers() =>
            ItemTiers.CanonicalOrder.Where(i => _ladders.ContainsKey(i) && Unseen(_ladders[i].AddPrice) == null)
                .Select(i => _ladders[i]).ToList();

        /// <summary>The first good in a price the Ledger hasn't seen, or null.</summary>
        private string Unseen(IEnumerable<RecipeIngredient> price) =>
            price.Where(p => p.quantity > 0 && !_seen(p.itemType)).Select(p => p.itemType).FirstOrDefault();

        /// <summary>The next Rate rung's price, or null at the top.</summary>
        public static IReadOnlyList<RecipeIngredient> NextRateCost(SupplyCart cart) =>
            cart.RateRung < cart.Ladder.TopRateRung ? cart.Ladder.RateCosts[cart.RateRung] : null;

        public static IReadOnlyList<RecipeIngredient> NextCapacityCost(SupplyCart cart) =>
            cart.CapacityRung < cart.Ladder.TopCapacityRung ? cart.Ladder.CapacityCosts[cart.CapacityRung] : null;

        /// <summary>Half of every good paid, each good's total rounded down (§0.6).</summary>
        public static List<RecipeIngredient> HalfRefund(SupplyCart cart) =>
            cart.Paid.GroupBy(p => p.itemType)
                .Select(g => new RecipeIngredient { itemType = g.Key, quantity = g.Sum(p => p.quantity) / 2 })
                .Where(r => r.quantity > 0)
                .ToList();

        /// <summary>What a removal puts in the pack: the half refund and the cart's stock.</summary>
        public static List<RecipeIngredient> RemovalReturns(SupplyCart cart)
        {
            List<RecipeIngredient> back = HalfRefund(cart);
            if (!cart.IsInfinite && cart.Stock > 0)
            {
                back.Add(new RecipeIngredient { itemType = cart.ItemType, quantity = cart.Stock });
            }
            return back;
        }

        // --- Actions at a spot --------------------------------------------------------------------

        public SupplyResult Add(CartSpot spot, string itemType)
        {
            if (spot == null || !spot.IsEmpty)
            {
                return SupplyResult.Refused("A cart already stands here.");
            }
            if (!TryGetLadder(itemType, out CartLadder ladder))
            {
                return SupplyResult.Refused("No cart sells that.");
            }
            if (Unseen(ladder.AddPrice) is string unseen)
            {
                return SupplyResult.Refused("Requires: " + ItemTiers.DisplayName(unseen));
            }
            if (_occupied(spot.Cell))
            {
                return SupplyResult.Refused("Clear the spot first.");
            }
            PlayerWallet wallet = _wallet();
            if (!wallet.TryCharge(ladder.AddPrice))
            {
                return SupplyResult.Refused(ConstructionCostPolicy.FormatShortfall(wallet, ladder.AddPrice));
            }
            SupplyCart cart = Place(spot, ladder);
            cart.RecordAdd(ladder.AddPrice);
            RaiseChanged();
            return SupplyResult.Ok($"Added a {ItemTiers.DisplayName(itemType)} cart.");
        }

        public SupplyResult UpgradeRate(CartSpot spot) => Upgrade(spot, rate: true, given: false);

        public SupplyResult UpgradeCapacity(CartSpot spot) => Upgrade(spot, rate: false, given: false);

        /// <summary>
        /// The next Rate or Capacity rung for nothing: a contract's reward (docs/contracts-design.md
        /// §4). Nothing is recorded as paid, so a removal refunds nothing of it, and a given rung
        /// can never be turned into goods.
        /// </summary>
        public SupplyResult GiveRung(CartSpot spot, bool rate) => Upgrade(spot, rate, given: true);

        private SupplyResult Upgrade(CartSpot spot, bool rate, bool given)
        {
            SupplyCart cart = spot?.Cart;
            if (cart == null)
            {
                return SupplyResult.Refused("No cart here.");
            }
            IReadOnlyList<RecipeIngredient> cost = rate ? NextRateCost(cart) : NextCapacityCost(cart);
            if (cost == null)
            {
                return SupplyResult.Refused("At the top rung.");
            }
            if (!given)
            {
                PlayerWallet wallet = _wallet();
                if (!wallet.TryCharge(cost))
                {
                    return SupplyResult.Refused(ConstructionCostPolicy.FormatShortfall(wallet, cost));
                }
                cart.RecordRung(cost);
            }
            string good = ItemTiers.DisplayName(cart.ItemType);
            if (rate)
            {
                cart.RateRung++;
                RaiseChanged();
                return SupplyResult.Ok($"{good} cart: {cart.RatePerMinute} a minute.");
            }
            cart.CapacityRung++;
            RaiseChanged();
            return SupplyResult.Ok($"{good} cart: holds {cart.Capacity}.");
        }

        public SupplyResult Change(CartSpot spot, string itemType)
        {
            SupplyCart cart = spot?.Cart;
            if (cart == null)
            {
                return SupplyResult.Refused("No cart here.");
            }
            if (itemType == cart.ItemType)
            {
                return SupplyResult.Refused("It already sells that.");
            }
            if (!TryGetLadder(itemType, out CartLadder ladder))
            {
                return SupplyResult.Refused("No cart sells that.");
            }
            if (Unseen(ladder.AddPrice) is string unseen)
            {
                return SupplyResult.Refused("Requires: " + ItemTiers.DisplayName(unseen));
            }
            if (IsLastKept(cart))
            {
                return SupplyResult.Refused($"The street needs one {ItemTiers.DisplayName(cart.ItemType)} cart.");
            }
            PlayerWallet wallet = _wallet();
            var stock = new List<RecipeIngredient>();
            if (!cart.IsInfinite && cart.Stock > 0)
            {
                stock.Add(new RecipeIngredient { itemType = cart.ItemType, quantity = cart.Stock });
            }
            string misfit = wallet.FirstMisfit(stock);
            if (misfit != null)
            {
                return SupplyResult.Refused("Not changed: pack full of " + ItemTiers.DisplayName(misfit) + ".");
            }
            if (!wallet.TryCharge(ladder.AddPrice))
            {
                return SupplyResult.Refused(ConstructionCostPolicy.FormatShortfall(wallet, ladder.AddPrice));
            }
            if (!wallet.Refund(stock))
            {
                wallet.Uncharge(ladder.AddPrice); // can't happen after FirstMisfit; never lose the price
                return SupplyResult.Refused("Not changed: pack full of " + ItemTiers.DisplayName(cart.ItemType) + ".");
            }
            // The rungs stay; the old good's price is spent, the new one is what a removal halves.
            Unplace(spot);
            SupplyCart changed = Place(spot, ladder);
            changed.RateRung = Math.Min(cart.RateRung, ladder.TopRateRung);
            changed.CapacityRung = Math.Min(cart.CapacityRung, ladder.TopCapacityRung);
            changed.RecordAdd(ladder.AddPrice);
            changed.RecordRung(cart.RungsPaid);
            RaiseChanged();
            return SupplyResult.Ok($"Now a {ItemTiers.DisplayName(itemType)} cart.");
        }

        public SupplyResult Remove(CartSpot spot)
        {
            SupplyCart cart = spot?.Cart;
            if (cart == null)
            {
                return SupplyResult.Refused("No cart here.");
            }
            if (IsLastKept(cart))
            {
                return SupplyResult.Refused($"The street needs one {ItemTiers.DisplayName(cart.ItemType)} cart.");
            }
            PlayerWallet wallet = _wallet();
            List<RecipeIngredient> back = RemovalReturns(cart);
            string misfit = wallet.FirstMisfit(back);
            if (misfit != null)
            {
                return SupplyResult.Refused("Not removed: pack full of " + ItemTiers.DisplayName(misfit) + ".");
            }
            if (!wallet.Refund(back))
            {
                return SupplyResult.Refused("Not removed: the pack can't take it.");
            }
            string good = ItemTiers.DisplayName(cart.ItemType);
            Unplace(spot);
            RaiseChanged();
            return SupplyResult.Ok($"Removed the {good} cart.");
        }

        /// <summary>Whether this is the street's last cart of a kept good (§0.9): it can't be
        /// removed or changed.</summary>
        public bool IsLastKept(SupplyCart cart) =>
            cart != null && _keptGoods.Contains(cart.ItemType) && Carts.Count(c => c.ItemType == cart.ItemType) <= 1;

        // --- The trickle ------------------------------------------------------------------------

        public void Tick(long tick)
        {
            foreach (CartSpot spot in _spots)
            {
                SupplyCart cart = spot.Cart;
                if (cart == null || cart.IsInfinite)
                {
                    continue;
                }
                int before = cart.Stock;
                cart.Flow.Tick(before, Trickle(cart));
            }
        }

        /// <summary>One tick of a cart's trickle; what it brought.</summary>
        private int Trickle(SupplyCart cart)
        {
            if (cart.Stock >= cart.Capacity)
            {
                return 0; // full: the accumulator holds (invariant 2)
            }
            cart.Accumulator += cart.RatePerMinute;
            int whole = (int)(cart.Accumulator / TicksPerMinute);
            if (whole <= 0)
            {
                return 0;
            }
            cart.Accumulator -= (long)whole * TicksPerMinute;
            int brought = Math.Min(whole, cart.Capacity - cart.Stock);
            cart.Node.Deliver(brought);
            if (cart.Stock >= cart.Capacity)
            {
                cart.Accumulator = 0;
            }
            cart.Marker.RefreshVisualState();
            return brought;
        }

        // --- Saves ------------------------------------------------------------------------------

        /// <summary>Every cart as a save records it.</summary>
        public List<Save.SupplyCartEntry> Capture() =>
            _spots.Where(s => s.Cart != null).Select(s => new Save.SupplyCartEntry
            {
                x = s.Cell.x,
                y = s.Cell.y,
                itemType = s.Cart.ItemType,
                rateRung = s.Cart.RateRung,
                capacityRung = s.Cart.CapacityRung,
                stock = s.Cart.IsInfinite ? 0 : s.Cart.Stock,
                accumulator = s.Cart.Accumulator,
                addPaid = s.Cart.AddPaid.ToList(),
                rungsPaid = s.Cart.RungsPaid.ToList(),
            }).ToList();

        /// <summary>
        /// A load: the saved carts replace every cart now on the street, exactly as saved and
        /// never clamped (invariant 7). A cart whose spot or good the data no longer has is
        /// skipped, and counted.
        /// </summary>
        public int Restore(IReadOnlyList<Save.SupplyCartEntry> carts)
        {
            foreach (CartSpot spot in _spots.Where(s => s.Cart != null).ToList())
            {
                Unplace(spot);
            }
            int skipped = 0;
            foreach (Save.SupplyCartEntry entry in carts ?? new List<Save.SupplyCartEntry>())
            {
                CartSpot spot = SpotAt(new Vector2Int(entry.x, entry.y));
                if (spot == null || !spot.IsEmpty || !TryGetLadder(entry.itemType, out CartLadder ladder))
                {
                    skipped++;
                    continue;
                }
                SupplyCart cart = Place(spot, ladder);
                cart.RateRung = Math.Max(0, Math.Min(entry.rateRung, ladder.TopRateRung));
                cart.CapacityRung = Math.Max(0, Math.Min(entry.capacityRung, ladder.TopCapacityRung));
                cart.Node.RestoreQuantity(entry.stock); // an infinite (Creative) node stays infinite
                cart.Accumulator = entry.accumulator < 0 ? 0 : entry.accumulator;
                cart.RecordAdd(entry.addPaid);
                cart.RecordRung(entry.rungsPaid);
                cart.Marker.RefreshVisualState();
            }
            RaiseChanged();
            return skipped;
        }

        // --- Placing a cart's node ----------------------------------------------------------------

        private SupplyCart Place(CartSpot spot, CartLadder ladder)
        {
            var node = new ResourceNode(spot.NodeId, ladder.ItemType, Creative ? ResourceNode.Infinite : 0);
            _nodes.Register(node);
            var marker = new ResourceNodeMarker { Position = new Vector3(spot.Cell.x, spot.Cell.y, 0f) };
            marker.Configure(_nodes, spot.NodeId);
            marker.RegisterAsSpatialEndpoint(_endpoints, _cells);
            _markers?.Add(marker);
            var cart = new SupplyCart(ladder, node, marker, Math.Max(1, TicksPerMinute / 60));
            spot.Cart = cart;
            return cart;
        }

        private void Unplace(CartSpot spot)
        {
            SupplyCart cart = spot.Cart;
            if (cart == null)
            {
                return;
            }
            // Only the cart's own endpoint: a building placed on its cell since owns the cell now.
            if (_endpoints != null && _endpoints.TryGetEndpoint(spot.Cell, out IItemEndpoint endpoint)
                && endpoint is ResourceNodeEndpoint source && source.Node == cart.Node)
            {
                _endpoints.Unregister(spot.Cell);
            }
            _nodes.Remove(spot.NodeId);
            _markers?.Remove(cart.Marker);
            spot.Cart = null;
        }

        private void RaiseChanged()
        {
            Version++;
            EventBus.Publish(new WorldInteractablesChangedEvent("supply cart"));
        }
    }
}
