using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Contracts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>What a Golem Foundry is doing, for its text and its picture.</summary>
    public enum FoundryState
    {
        /// <summary>No order for golems is open: it waits, grey.</summary>
        NoOrder = 0,

        /// <summary>Short of the ordered chassis's goods: it waits for them, grey.</summary>
        Short = 1,

        /// <summary>Building a chassis.</summary>
        Building = 2,

        /// <summary>A golem built and waiting for an order of its chassis.</summary>
        Holding = 3,
    }

    /// <summary>
    /// The Golem Foundry (docs/golem-sales-design.md §2.2): a crate-like intake that takes only the
    /// open golem order's chassis goods, up to one chassis's cost of each; once it holds a whole
    /// cost it spends it and builds that chassis over its tier's time, then ships it to the city,
    /// where the order counts it. Its golem never enters the world (invariant 3): no entity, no
    /// slot, no frame. Nothing it does is ever red: with no order, or short of goods, it waits.
    ///
    /// <para>
    /// It is a crate the rules already know (an <see cref="IIntakeCrate"/> that owns its buffer),
    /// so golems push into it, belts feed it, the Crate screen fills it, a save keeps what it holds
    /// on its building, and demolition hands that back. Its buffer is not in the registry: the
    /// intake's room is the order's, as the City Office's crate is the contract's.
    /// </para>
    /// </summary>
    public sealed class PlaceableGolemFoundry : IBuildingPart, IIntakeCrate, Simulation.ITickable
    {
        public const string Name = "Golem Foundry";

        private GolemOrderBoard _orders;
        private IReadOnlyDictionary<string, ChassisDefinition> _chassis;
        private int _ticksPerSecond = 1;
        private Vector2Int _cell;
        private bool _placed;
        private StorageBuffer _buffer;

        public IBuildingPart CloneForInstance() => new PlaceableGolemFoundry();

        /// <summary>The chassis being built, or held once built; null when idle.</summary>
        public string Chassis { get; private set; }

        /// <summary>Ticks into the build; meaningful while <see cref="FoundryState.Building"/>.</summary>
        public long ProgressTicks { get; private set; }

        /// <summary>Whether the chassis is built and waiting for an order of its kind.</summary>
        public bool Holding { get; private set; }

        /// <summary>Golems it has shipped since it was placed or loaded: what its view glints on.</summary>
        public int Shipped { get; private set; }

        /// <summary>The cell it stands on, once placed.</summary>
        public Vector2Int Cell => _cell;

        /// <summary>Wires a placed foundry to the orders and publishes its tile.</summary>
        public void Attach(
            SpatialEndpointRegistry endpoints, Vector2Int cell, GolemOrderBoard orders,
            IReadOnlyDictionary<string, ChassisDefinition> chassis, int ticksPerSecond)
        {
            _orders = orders;
            _chassis = chassis ?? new Dictionary<string, ChassisDefinition>();
            _ticksPerSecond = Math.Max(1, ticksPerSecond);
            _cell = cell;
            _placed = true;
            _buffer = new StorageBuffer(CrateId) { RoomLimit = RoomFor };
            endpoints?.Register(cell, new StorageBufferEndpoint(_buffer));
        }

        // --- The crate ---------------------------------------------------------------------------

        public string CrateId => _placed ? "Foundry(" + _cell.x + "," + _cell.y + ")" : null;

        public StorageBuffer Buffer => _buffer;

        public bool OwnsBuffer => true;

        public string FilterItemType => null;

        public bool CanLabel => false;

        public void SetFilter(string itemType)
        {
        }

        public string Title => Name;

        public string CapLine => FoundryText.CapLine(this);

        public string RefusalFor(string itemType) => RoomFor(itemType) > 0 ? null : FoundryText.Refusal(this, itemType);

        /// <summary>The chassis it gathers goods for: the open order's, or null with none.</summary>
        public string OrderedChassis => _orders?.Current?.Chassis;

        /// <summary>The ordered chassis's cost: what one golem takes.</summary>
        public IReadOnlyList<RecipeIngredient> Cost =>
            OrderedChassis != null && _chassis != null && _chassis.TryGetValue(OrderedChassis, out ChassisDefinition c) && c?.cost != null
                ? c.cost
                : (IReadOnlyList<RecipeIngredient>)Array.Empty<RecipeIngredient>();

        /// <summary>How many of a good one chassis takes; 0 for any good the order's chassis doesn't.</summary>
        public int Needs(string itemType) => Cost.Where(c => c.itemType == itemType).Sum(c => c.quantity);

        // Up to one chassis's cost of each of its goods, less what is already in.
        private int RoomFor(string itemType) =>
            _buffer == null ? 0 : Math.Max(0, Needs(itemType) - _buffer.GetQuantity(itemType));

        /// <summary>The goods still missing from one chassis's cost, in its order.</summary>
        public List<RecipeIngredient> Missing() =>
            Cost.Select(c => new RecipeIngredient(c.itemType, Math.Max(0, c.quantity - (_buffer?.GetQuantity(c.itemType) ?? 0))))
                .Where(c => c.quantity > 0).ToList();

        // --- The work ----------------------------------------------------------------------------

        public FoundryState State =>
            Holding ? FoundryState.Holding
            : Chassis != null ? FoundryState.Building
            : OrderedChassis == null ? FoundryState.NoOrder
            : FoundryState.Short;

        /// <summary>The ticks the current build takes.</summary>
        public long BuildTicks => Chassis == null || _orders == null ? 0L : (long)_orders.FoundrySeconds(Chassis) * _ticksPerSecond;

        /// <summary>One tick of the simulation clock: build, ship, or start the next.</summary>
        public void Tick(long currentTick)
        {
            if (!_placed || _orders == null)
            {
                return;
            }
            if (Chassis != null && !Holding)
            {
                ProgressTicks++;
                if (ProgressTicks >= BuildTicks)
                {
                    Holding = true;
                    ProgressTicks = 0L;
                }
            }
            if (Holding)
            {
                if (_orders.Deliver(Chassis, 0))
                {
                    Shipped++;
                    EventBus.Publish(new FoundryShippedEvent(_cell, Chassis));
                    Chassis = null;
                    Holding = false;
                }
                else
                {
                    return;
                }
            }
            if (Chassis == null)
            {
                TryStart();
            }
        }

        // A whole cost in: spend it, as any recipe's inputs are spent, and begin.
        private void TryStart()
        {
            string chassis = OrderedChassis;
            IReadOnlyList<RecipeIngredient> cost = Cost;
            if (chassis == null || _orders.Remaining <= 0 || cost.Count == 0 || Missing().Count > 0)
            {
                return;
            }
            foreach (RecipeIngredient line in cost)
            {
                _buffer.TryWithdraw(line.itemType, line.quantity);
                EventBus.Consumed(line.itemType, line.quantity);
            }
            Chassis = chassis;
            ProgressTicks = 0L;
            Holding = false;
        }

        /// <summary>
        /// What demolishing it hands back beyond its price and its intake: the chassis under way or
        /// held, at its cost, because the goods it was made of were the player's.
        /// </summary>
        public IReadOnlyList<RecipeIngredient> GolemUnderWayRefund =>
            Chassis != null && _chassis != null && _chassis.TryGetValue(Chassis, out ChassisDefinition c) && c?.cost != null
                ? c.cost
                : (IReadOnlyList<RecipeIngredient>)Array.Empty<RecipeIngredient>();

        /// <summary>A save's word on the build (invariant 9): never clamped, a chassis the data lacks dropped.</summary>
        public void Restore(string chassis, long progressTicks, bool holding)
        {
            bool known = chassis != null && _chassis != null && _chassis.ContainsKey(chassis);
            Chassis = known ? chassis : null;
            Holding = known && holding;
            ProgressTicks = known && !holding ? Math.Max(0L, progressTicks) : 0L;
        }
    }
}
