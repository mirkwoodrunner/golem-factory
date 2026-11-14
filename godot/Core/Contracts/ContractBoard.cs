using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.Supply;

namespace GolemFactory.Contracts
{
    /// <summary>Where the office's one contract is (docs/contracts-design.md §2). Saved by value.</summary>
    public enum ContractState
    {
        /// <summary>No contract posted: the office waits for the next one's condition, or has none left.</summary>
        Waiting = 0,

        /// <summary>Posted, not yet taken: the office's crate takes nothing.</summary>
        Posted = 1,

        /// <summary>Taken: the crate takes what it still asks for, and its par runs.</summary>
        Accepted = 2,
    }

    /// <summary>A cart rung waiting on the office's shelf for the player to pick its cart.</summary>
    public readonly struct ShelfRung
    {
        public readonly string ItemType;
        public readonly ContractRungKind Kind;

        public ShelfRung(string itemType, ContractRungKind kind)
        {
            ItemType = itemType;
            Kind = kind;
        }
    }

    /// <summary>
    /// The City Office's contracts (docs/contracts-design.md): one sequence from
    /// <c>data/contracts.json</c>, one contract at a time, each posted once its condition holds,
    /// taken with a click, filled from any source, and rewarded on the spot. Plain rules on
    /// <c>SandboxWorld.Contracts</c>; the office and its panel only draw it and forward clicks.
    ///
    /// <para>
    /// The invariants, §0: a contract never fails (no deadline, nothing taken away); the bonus only
    /// adds; any source counts, once; the crate takes nothing it wasn't asked for and nothing past
    /// the amount, and what it takes is spent; a reward is never lost (the pack, else the shelf);
    /// the order is fixed; none in Creative.
    /// </para>
    /// </summary>
    public sealed class ContractBoard : ITickable
    {
        /// <summary>The office crate's buffer. Not in the registry: it is drained as it fills, and a save keeps the counts.</summary>
        public const string OfficeBufferId = "CityOffice";

        private readonly List<ContractDefinition> _sequence;
        private readonly Func<string, bool> _seen;
        private readonly Func<int> _towerStageStarted;
        private readonly Func<PlayerWallet> _wallet;
        private readonly Func<SupplyStreet> _street;
        private readonly int _ticksPerSecond;

        private readonly List<string> _done = new List<string>();
        private readonly HashSet<string> _bonusEarned = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _delivered = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _shelfGoods = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<ShelfRung> _shelfRungs = new List<ShelfRung>();

        /// <param name="sequence">The contracts, any order: they run by <see cref="ContractDefinition.order"/>.</param>
        /// <param name="seen">Whether the Ledger has seen a good.</param>
        /// <param name="towerStageStarted">The highest Clock Tower stage started, 1-based; 0 while the site is roped off.</param>
        /// <param name="enabled">False in Creative (invariant 12): the office never opens.</param>
        public ContractBoard(
            IEnumerable<ContractDefinition> sequence, Func<string, bool> seen, Func<int> towerStageStarted,
            Func<PlayerWallet> wallet, Func<SupplyStreet> street, int ticksPerSecond, bool enabled = true)
        {
            _sequence = (sequence ?? Enumerable.Empty<ContractDefinition>())
                .Where(c => c != null).OrderBy(c => c.order).ThenBy(c => c.name, StringComparer.Ordinal).ToList();
            _seen = seen ?? (_ => false);
            _towerStageStarted = towerStageStarted ?? (() => 0);
            _wallet = wallet;
            _street = street;
            _ticksPerSecond = ticksPerSecond < 1 ? 1 : ticksPerSecond;
            Enabled = enabled;
            OfficeBuffer = new StorageBuffer(OfficeBufferId) { RoomLimit = RoomFor };
        }

        public bool Enabled { get; }

        /// <summary>
        /// Endless play's contracts after the authored sequence (docs/endless-design.md §1.1); null
        /// ends the sequence with its last authored contract.
        /// </summary>
        public ContractGenerator Generator { get; set; }

        /// <summary>Whether the Clock Tower stands, for a generated contract's condition.</summary>
        public Func<bool> TowerComplete { get; set; }

        /// <summary>Generated contracts done: the next one's index.</summary>
        public int GeneratedDone => _done.Count(n => ContractGenerator.TryIndexOf(n, out _));

        public IReadOnlyList<ContractDefinition> Sequence => _sequence;

        /// <summary>The office's crate: takes only what the taken contract still needs (§1).</summary>
        public StorageBuffer OfficeBuffer { get; }

        /// <summary>The contract on the notice, posted or taken; null while waiting.</summary>
        public ContractDefinition Current { get; private set; }

        public ContractState State { get; private set; }

        /// <summary>Ticks since it was taken, on the simulation clock: Pause and the speed setting apply.</summary>
        public long ElapsedTicks { get; private set; }

        /// <summary>Bumped on every change a reader might draw, so a panel can redraw only then.</summary>
        public int Version { get; private set; }

        /// <summary>
        /// Whether the office has opened: once its first contract has posted, it stays open. Until
        /// then the office is roped off (§1).
        /// </summary>
        public bool IsOpen => Enabled && (Current != null || _done.Count > 0);

        public IReadOnlyList<string> Done => _done;

        public bool EarnedBonus(string contractName) => _bonusEarned.Contains(contractName ?? "");

        public int Delivered(string itemType) =>
            itemType != null && _delivered.TryGetValue(itemType, out int n) ? n : 0;

        /// <summary>How many more of a good the taken contract needs; 0 for any other good, or before it is taken.</summary>
        public int Remaining(string itemType)
        {
            if (State != ContractState.Accepted || Current == null)
            {
                return 0;
            }
            int left = Current.Asked(itemType) - Delivered(itemType);
            return left > 0 ? left : 0;
        }

        /// <summary>The crate's room for a good: what is still needed, less what already sits in it.</summary>
        private int RoomFor(string itemType) => Math.Max(0, Remaining(itemType) - OfficeBuffer.GetQuantity(itemType));

        public long ParTicks => Current == null ? 0L : (long)Current.parSeconds * _ticksPerSecond;

        /// <summary>Whether the bonus can still be earned: taken, and inside its par (or not yet taken).</summary>
        public bool WithinPar => Current != null && (State != ContractState.Accepted || ElapsedTicks <= ParTicks);

        /// <summary>Seconds left in the par, rounded up; 0 once it has passed.</summary>
        public int ParSecondsLeft
        {
            get
            {
                if (Current == null)
                {
                    return 0;
                }
                long left = ParTicks - (State == ContractState.Accepted ? ElapsedTicks : 0L);
                return left <= 0 ? 0 : (int)((left + _ticksPerSecond - 1) / _ticksPerSecond);
            }
        }

        /// <summary>The next contract in the sequence while none is posted, or null when the sequence is done.</summary>
        public ContractDefinition Next => Current ?? NextInSequence() ?? Generator?.Generate(GeneratedDone);

        // A loop, not a query: Settle asks every tick and every frame while nothing is posted.
        private ContractDefinition NextInSequence()
        {
            for (int i = 0; i < _sequence.Count; i++)
            {
                if (!_done.Contains(_sequence[i].name))
                {
                    return _sequence[i];
                }
            }
            return null;
        }

        /// <summary>
        /// The first thing the next contract is waiting for: a good the Ledger hasn't seen
        /// ("Brass"), or a tower stage. Null when it could post now, or when there is none.
        /// </summary>
        public string WaitingForGood => Current != null ? null : Next?.postWhenSeen.FirstOrDefault(g => !_seen(g));

        /// <summary>Whether the next contract waits for the Clock Tower to stand (a generated one).</summary>
        public bool WaitingForTowerComplete
        {
            get
            {
                ContractDefinition next = Current != null ? null : Next;
                return next != null && next.postWhenTowerComplete && TowerComplete?.Invoke() != true;
            }
        }

        public int WaitingForTowerStage
        {
            get
            {
                ContractDefinition next = Current != null ? null : Next;
                return next != null && next.postWhenTowerStage > _towerStageStarted() ? next.postWhenTowerStage : 0;
            }
        }

        // --- Goods and rungs waiting at the office -----------------------------------------------

        /// <summary>Reward goods that didn't fit the pack, in the game's order (invariant 5).</summary>
        public IReadOnlyList<RecipeIngredient> ShelfGoods =>
            _shelfGoods.Where(p => p.Value > 0)
                .OrderBy(p => ItemTiers.CanonicalPosition(p.Key)).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new RecipeIngredient(p.Key, p.Value)).ToList();

        public IReadOnlyList<ShelfRung> ShelfRungs => _shelfRungs;

        public bool ShelfEmpty => _shelfRungs.Count == 0 && _shelfGoods.Values.All(n => n <= 0);

        // --- The clock ---------------------------------------------------------------------------

        public void Tick(long tick)
        {
            if (!Enabled)
            {
                return;
            }
            if (State == ContractState.Accepted)
            {
                ElapsedTicks++;
            }
            Settle();
        }

        /// <summary>
        /// Counts what has reached the office's crate and posts the next contract when its
        /// condition holds. Run every tick and every frame (a hand delivery while paused counts at
        /// once), and before a save, so nothing ever waits uncounted in the crate.
        /// </summary>
        public void Settle()
        {
            if (!Enabled)
            {
                return;
            }
            if (State == ContractState.Accepted && Current != null)
            {
                Take();
                if (Current.asks.All(a => Delivered(a.itemType) >= a.quantity))
                {
                    Complete();
                }
            }
            if (Current == null)
            {
                TryPost();
            }
        }

        // What reached the crate is the contract's, up to what it asks: spent, as the tower's
        // deliveries are (invariant 4). The crate's room never lets more in.
        private void Take()
        {
            foreach (RecipeIngredient line in Current.asks)
            {
                int held = OfficeBuffer.GetQuantity(line.itemType);
                int take = Math.Min(held, Current.Asked(line.itemType) - Delivered(line.itemType));
                if (take > 0 && OfficeBuffer.TryWithdraw(line.itemType, take))
                {
                    _delivered[line.itemType] = Delivered(line.itemType) + take;
                    EventBus.Consumed(line.itemType, take);
                    Version++;
                }
            }
        }

        private void TryPost()
        {
            ContractDefinition next = Next;
            if (next == null || !CanPost(next))
            {
                return;
            }
            Current = next;
            State = ContractState.Posted;
            ElapsedTicks = 0L;
            _delivered.Clear();
            Version++;
            EventBus.Publish(new ContractEvent(ContractEventKind.Posted, next.name, next.requester));
        }

        /// <summary>Whether a contract's condition holds: every named good seen, and its tower stage started.</summary>
        public bool CanPost(ContractDefinition contract)
        {
            if (contract == null)
            {
                return false;
            }

            // Plain loops: this is asked every tick and every frame while a contract waits to
            // be posted, which is most of the game, and a lambda here was a closure each time.
            for (int i = 0; i < contract.postWhenSeen.Count; i++)
            {
                if (!_seen(contract.postWhenSeen[i]))
                {
                    return false;
                }
            }
            for (int i = 0; i < contract.asks.Count; i++)
            {
                if (!_seen(contract.asks[i].itemType))
                {
                    return false;
                }
            }

            return (contract.postWhenTowerStage <= 0 || _towerStageStarted() >= contract.postWhenTowerStage)
                && (!contract.postWhenTowerComplete || TowerComplete?.Invoke() == true);
        }

        /// <summary>Takes the posted contract: from now its crate takes goods and its par runs.</summary>
        public bool Accept()
        {
            if (!Enabled || State != ContractState.Posted || Current == null)
            {
                return false;
            }
            State = ContractState.Accepted;
            ElapsedTicks = 0L;
            Version++;
            EventBus.Publish(new ContractEvent(ContractEventKind.Accepted, Current.name, Current.requester));
            return true;
        }

        private void Complete()
        {
            ContractDefinition done = Current;
            bool bonus = ElapsedTicks <= ParTicks;
            Give(done.reward);
            if (bonus)
            {
                Give(done.bonus);
                _bonusEarned.Add(done.name);
            }
            _done.Add(done.name);
            Current = null;
            State = ContractState.Waiting;
            ElapsedTicks = 0L;
            _delivered.Clear();
            Version++;
            EventBus.Publish(new ContractEvent(ContractEventKind.Completed, done.name, done.requester, bonus));
        }

        // Goods into the pack as far as they fit, the rest on the shelf; a rung always waits on the
        // shelf, because the player picks its cart (§4).
        private void Give(ContractReward reward)
        {
            if (reward == null)
            {
                return;
            }
            StorageBuffer pack = _wallet?.Invoke()?.Pack;
            foreach (RecipeIngredient line in reward.goods.Where(g => g.quantity > 0))
            {
                int put = pack?.Deposit(line.itemType, line.quantity) ?? 0;
                if (put < line.quantity)
                {
                    _shelfGoods.TryGetValue(line.itemType, out int shelved);
                    _shelfGoods[line.itemType] = shelved + line.quantity - put;
                }
            }
            if (reward.HasRung)
            {
                _shelfRungs.Add(new ShelfRung(reward.rungGood, reward.rungKind));
            }
        }

        /// <summary>
        /// Goods the office pays for something other than a contract (a golem order,
        /// docs/golem-sales-design.md §3): into the pack as far as they fit, the rest on this shelf.
        /// </summary>
        public void Pay(IReadOnlyList<RecipeIngredient> goods)
        {
            if (goods == null || goods.Count == 0)
            {
                return;
            }
            Give(new ContractReward { goods = goods.ToList() });
            Version++;
        }

        /// <summary>The shelf's goods into the pack, as far as they fit; what doesn't stays. What moved, for a status line.</summary>
        public List<RecipeIngredient> CollectGoods()
        {
            var moved = new List<RecipeIngredient>();
            StorageBuffer pack = _wallet?.Invoke()?.Pack;
            if (pack == null)
            {
                return moved;
            }
            foreach (RecipeIngredient line in ShelfGoods)
            {
                int put = pack.Deposit(line.itemType, line.quantity);
                if (put > 0)
                {
                    _shelfGoods[line.itemType] = line.quantity - put;
                    moved.Add(new RecipeIngredient(line.itemType, put));
                }
            }
            if (moved.Count > 0)
            {
                Version++;
            }
            return moved;
        }

        /// <summary>The carts a shelved rung can go on: its good's, below the top of its ladder, west to east.</summary>
        public IReadOnlyList<CartSpot> CartsFor(ShelfRung rung)
        {
            SupplyStreet street = _street?.Invoke();
            if (street == null)
            {
                return new List<CartSpot>();
            }
            return street.Spots
                .Where(s => s.Cart != null && s.Cart.ItemType == rung.ItemType && NextCost(s.Cart, rung.Kind) != null)
                .OrderBy(s => s.Cell.x).ToList();
        }

        private static IReadOnlyList<RecipeIngredient> NextCost(SupplyCart cart, ContractRungKind kind) =>
            kind == ContractRungKind.Rate ? SupplyStreet.NextRateCost(cart) : SupplyStreet.NextCapacityCost(cart);

        /// <summary>
        /// Puts the shelved rung at <paramref name="index"/> on the cart at <paramref name="spot"/>, for
        /// nothing. Refused, and kept on the shelf, for a cart of another good or one at the top.
        /// </summary>
        public SupplyResult ApplyRung(int index, CartSpot spot)
        {
            if (index < 0 || index >= _shelfRungs.Count)
            {
                return SupplyResult.Refused("No rung waiting.");
            }
            ShelfRung rung = _shelfRungs[index];
            if (!CartsFor(rung).Contains(spot))
            {
                return SupplyResult.Refused(spot?.Cart == null
                    ? "No cart there."
                    : $"That isn't a {ItemTiers.DisplayName(rung.ItemType)} cart below the top rung.");
            }
            SupplyResult result = _street().GiveRung(spot, rung.Kind == ContractRungKind.Rate);
            if (result.Done)
            {
                _shelfRungs.RemoveAt(index);
                Version++;
            }
            return result;
        }

        // --- Saves (§7) --------------------------------------------------------------------------

        public Save.ContractsEntry Capture()
        {
            Settle();
            var entry = new Save.ContractsEntry
            {
                current = Current?.name,
                state = (int)State,
                elapsedTicks = ElapsedTicks,
            };
            entry.done.AddRange(_done);
            entry.bonusEarned.AddRange(_done.Where(_bonusEarned.Contains));
            foreach (KeyValuePair<string, int> pair in _delivered.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                entry.deliveredItems.Add(pair.Key);
                entry.deliveredCounts.Add(pair.Value);
            }
            foreach (RecipeIngredient line in ShelfGoods)
            {
                entry.shelfItems.Add(line.itemType);
                entry.shelfCounts.Add(line.quantity);
            }
            foreach (ShelfRung rung in _shelfRungs)
            {
                entry.shelfRungItems.Add(rung.ItemType);
                entry.shelfRungKinds.Add((int)rung.Kind);
            }
            return entry;
        }

        /// <summary>
        /// The board as a save left it (invariant 11). A save without contracts (null) starts the
        /// sequence from its first contract, nothing delivered; it posts at once if its condition
        /// holds. A contract the data no longer has is dropped; a load never clamps a count.
        /// </summary>
        public void Restore(Save.ContractsEntry entry)
        {
            _done.Clear();
            _bonusEarned.Clear();
            _delivered.Clear();
            _shelfGoods.Clear();
            _shelfRungs.Clear();
            OfficeBuffer.ClearContents();
            Current = null;
            State = ContractState.Waiting;
            ElapsedTicks = 0L;
            if (entry != null)
            {
                var known = new HashSet<string>(_sequence.Select(c => c.name), StringComparer.Ordinal);
                _done.AddRange(entry.done.Where(n => known.Contains(n) || (Generator != null && ContractGenerator.TryIndexOf(n, out _))).Distinct());
                foreach (string name in entry.bonusEarned.Where(_done.Contains))
                {
                    _bonusEarned.Add(name);
                }
                ContractDefinition current = _sequence.FirstOrDefault(c => c.name == entry.current)
                    ?? (Generator != null && ContractGenerator.TryIndexOf(entry.current, out int k) ? Generator.Generate(k) : null);
                if (current != null && !_done.Contains(current.name) && entry.state != (int)ContractState.Waiting)
                {
                    Current = current;
                    State = entry.state == (int)ContractState.Accepted ? ContractState.Accepted : ContractState.Posted;
                    ElapsedTicks = State == ContractState.Accepted ? Math.Max(0L, entry.elapsedTicks) : 0L;
                    for (int i = 0; i < entry.deliveredItems.Count && i < entry.deliveredCounts.Count; i++)
                    {
                        if (entry.deliveredCounts[i] > 0)
                        {
                            _delivered[entry.deliveredItems[i]] = entry.deliveredCounts[i];
                        }
                    }
                }
                for (int i = 0; i < entry.shelfItems.Count && i < entry.shelfCounts.Count; i++)
                {
                    if (entry.shelfCounts[i] > 0)
                    {
                        _shelfGoods.TryGetValue(entry.shelfItems[i], out int n);
                        _shelfGoods[entry.shelfItems[i]] = n + entry.shelfCounts[i];
                    }
                }
                for (int i = 0; i < entry.shelfRungItems.Count && i < entry.shelfRungKinds.Count; i++)
                {
                    var kind = (ContractRungKind)entry.shelfRungKinds[i];
                    if (kind == ContractRungKind.Rate || kind == ContractRungKind.Capacity)
                    {
                        _shelfRungs.Add(new ShelfRung(entry.shelfRungItems[i], kind));
                    }
                }
            }
            Version++;
            // Quietly: a load isn't the city posting work, so the alerts don't log it again.
            if (Current == null && Enabled)
            {
                ContractDefinition next = Next;
                if (next != null && CanPost(next))
                {
                    Current = next;
                    State = ContractState.Posted;
                }
            }
        }
    }
}
