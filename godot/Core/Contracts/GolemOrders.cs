using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;

namespace GolemFactory.Contracts
{
    /// <summary>The golem orders' settings (<c>sandbox.json</c>'s <c>golemOrders</c>, docs/golem-sales-design.md §1).</summary>
    public sealed class GolemOrdersSetup
    {
        /// <summary>The raw goods an order is sized to once it levels off.</summary>
        public int plateauRaw = 640;

        /// <summary>Orders over which the budget grows to its plateau.</summary>
        public int growthOrders = 8;

        /// <summary>The most golems one order asks for.</summary>
        public int maxCount = 8;

        /// <summary>
        /// What an order pays done (2026-11-19, the user's call): vouchers, not goods, and nothing
        /// more for a golem with cards, which only rewarded pulling a working golem off its line.
        /// </summary>
        public int vouchers = 1;

        /// <summary>A Golem Foundry's build time by chassis tier, 1-based; the last covers any higher tier.</summary>
        public List<int> foundrySecondsByTier = new List<int>();

        /// <summary>Who orders, with their lines; <c>{golems}</c> is the order ("3 Brass Pressers").</summary>
        public List<EndlessContractsSetup.RequesterLines> requesters = new List<EndlessContractsSetup.RequesterLines>();
    }

    /// <summary>What a sale at the office came to: refused with a reason, or sold.</summary>
    public readonly struct GolemSaleResult
    {
        public readonly bool Done;
        public readonly string Refusal;

        /// <summary>The order it went to.</summary>
        public readonly GolemOrder Order;

        /// <summary>Golems that order has had, this one included.</summary>
        public readonly int Delivered;

        /// <summary>The golem's cargo, back in the pack.</summary>
        public readonly IReadOnlyList<RecipeIngredient> Cargo;

        private GolemSaleResult(bool done, string refusal, GolemOrder order, int delivered, IReadOnlyList<RecipeIngredient> cargo)
        {
            Done = done;
            Refusal = refusal;
            Order = order;
            Delivered = delivered;
            Cargo = cargo ?? Array.Empty<RecipeIngredient>();
        }

        public static GolemSaleResult Refused(string why) => new GolemSaleResult(false, why, null, 0, null);

        public static GolemSaleResult Sold(GolemOrder order, int delivered, IReadOnlyList<RecipeIngredient> cargo) =>
            new GolemSaleResult(true, null, order, delivered, cargo);

        /// <summary>Whether this sale was the order's last golem.</summary>
        public bool CompletedOrder => Done && Order != null && Delivered >= Order.Count;
    }

    /// <summary>One golem order: the city wants <see cref="Count"/> of a chassis.</summary>
    public sealed class GolemOrder
    {
        /// <summary>0 for the first order ever posted: what a save keeps, and what the rotation turns on.</summary>
        public int Index;
        public string Chassis;
        public int Count;
        public string Requester;
        public string Words;
    }

    /// <summary>
    /// The City Office's golem orders (docs/golem-sales-design.md): one at a time, beside the
    /// contract, from the office's opening in chapter 5. An order names a chassis and a count; a
    /// golem of that chassis counts whether the player hands it over (<see cref="SandboxWorld"/>'s
    /// sale, through the station's removal) or a Golem Foundry ships it. Done, it pays vouchers
    /// (<see cref="CityVouchers"/>).
    ///
    /// <para>
    /// The invariants, §0: from a formula, never chance; nothing fails or expires; never paid in
    /// goods; none in Creative; a save restores it exactly.
    /// </para>
    /// </summary>
    public sealed class GolemOrderBoard : ITickable
    {
        private readonly GolemOrdersSetup _setup;
        private readonly IReadOnlyDictionary<string, ChassisDefinition> _chassis;
        private readonly IReadOnlyDictionary<string, double> _raw;
        private readonly Func<bool> _officeOpen;
        private readonly Func<string, bool> _chassisBuilt;
        private readonly Action<int> _pay;

        public GolemOrderBoard(
            GolemOrdersSetup setup, IReadOnlyDictionary<string, ChassisDefinition> chassis,
            IReadOnlyDictionary<string, double> rawCosts, Func<bool> officeOpen, Func<string, bool> chassisBuilt,
            Action<int> payVouchers, bool enabled = true)
        {
            _setup = setup ?? new GolemOrdersSetup();
            _chassis = chassis ?? new Dictionary<string, ChassisDefinition>();
            _raw = rawCosts ?? new Dictionary<string, double>();
            _officeOpen = officeOpen ?? (() => false);
            _chassisBuilt = chassisBuilt ?? (_ => false);
            _pay = payVouchers;
            Enabled = enabled && _setup.requesters.Count > 0;
        }

        public bool Enabled { get; }

        public GolemOrdersSetup Setup => _setup;

        /// <summary>The order on the notice, or null while the office is shut or no chassis has been built.</summary>
        public GolemOrder Current { get; private set; }

        /// <summary>Golems the current order has had.</summary>
        public int Delivered { get; private set; }

        /// <summary>Orders done: the next one's index.</summary>
        public int Done { get; private set; }

        /// <summary>Every golem the city has had, by hand or from a Foundry: the overview's total.</summary>
        public int GolemsSold { get; private set; }

        /// <summary>Bumped on every change a reader might draw.</summary>
        public int Version { get; private set; }

        /// <summary>How many more golems the current order needs; 0 with none.</summary>
        public int Remaining => Current == null ? 0 : Math.Max(0, Current.Count - Delivered);

        // --- Sizing (§1) -----------------------------------------------------------------------

        /// <summary>A good's cost in raw goods, as contracts count it.</summary>
        public double Raw(string good) => _raw.TryGetValue(good ?? "", out double r) && r > 0 ? r : 1.0;

        /// <summary>A bare chassis's raw cost: what its price costs in raw goods.</summary>
        public double ChassisRaw(string chassis) =>
            _chassis.TryGetValue(chassis ?? "", out ChassisDefinition c) && c?.cost != null
                ? c.cost.Sum(line => Raw(line.itemType) * line.quantity)
                : 0.0;

        /// <summary>What the current order pays done: the notice's figure.</summary>
        public int RewardVouchers => Current == null ? 0 : Math.Max(0, _setup.vouchers);

        /// <summary>The raw goods order <paramref name="k"/> is sized to: growing, then level.</summary>
        public int BudgetRaw(int k)
        {
            int growth = Math.Max(1, _setup.growthOrders);
            return (int)Math.Round(_setup.plateauRaw * Math.Min(1.0, (k + 1) / (double)growth));
        }

        /// <summary>A Foundry's build time for a chassis, in seconds: by its tier.</summary>
        public int FoundrySeconds(string chassis)
        {
            List<int> byTier = _setup.foundrySecondsByTier;
            if (byTier.Count == 0)
            {
                return 30;
            }
            int tier = _chassis.TryGetValue(chassis ?? "", out ChassisDefinition c) && c != null ? c.tier : 1;
            return byTier[Math.Min(byTier.Count, Math.Max(1, tier)) - 1];
        }

        // --- Posting ---------------------------------------------------------------------------

        /// <summary>The chassis an order can name now: those the factory has built, cheapest first.</summary>
        public List<string> Rotation() =>
            _chassis.Keys.Where(_chassisBuilt)
                .OrderBy(ChassisRaw).ThenBy(n => n, StringComparer.Ordinal).ToList();

        /// <summary>
        /// Order <paramref name="k"/> as it would post now: the rotation's <c>k</c>th chassis, as
        /// many as the budget buys (at least one, at most <see cref="GolemOrdersSetup.maxCount"/>).
        /// Null when nothing can be named.
        /// </summary>
        public GolemOrder Generate(int k)
        {
            List<string> rotation = Rotation();
            if (k < 0 || rotation.Count == 0)
            {
                return null;
            }
            string chassis = rotation[k % rotation.Count];
            double each = Math.Max(1.0, ChassisRaw(chassis));
            int count = Math.Max(1, Math.Min(Math.Max(1, _setup.maxCount), (int)Math.Floor(BudgetRaw(k) / each)));
            return Make(k, chassis, count);
        }

        private GolemOrder Make(int k, string chassis, int count)
        {
            EndlessContractsSetup.RequesterLines who = _setup.requesters[k % _setup.requesters.Count];
            string line = who.lines.Count == 0 ? "The city wants {golems}." : who.lines[k / _setup.requesters.Count % who.lines.Count];
            return new GolemOrder
            {
                Index = k,
                Chassis = chassis,
                Count = count,
                Requester = who.name,
                Words = line.Replace("{golems}", GolemOrderText.Golems(chassis, count)),
            };
        }

        public void Tick(long tick) => Settle();

        /// <summary>Posts the next order once the office is open and a chassis can be named.</summary>
        public void Settle()
        {
            if (!Enabled || Current != null || !_officeOpen())
            {
                return;
            }
            GolemOrder next = Generate(Done);
            if (next == null)
            {
                return;
            }
            Current = next;
            Delivered = 0;
            Version++;
            EventBus.Publish(new ContractEvent(ContractEventKind.OrderPosted, next.Chassis, next.Requester));
        }

        // --- Filling ---------------------------------------------------------------------------

        /// <summary>Whether the current order still wants a golem of <paramref name="chassis"/>.</summary>
        public bool Wants(string chassis) => Current != null && Remaining > 0 && Current.Chassis == chassis;

        /// <summary>Why a golem of <paramref name="chassis"/> isn't wanted, for the player; null when it is.</summary>
        public string Refusal(string chassis)
        {
            if (!Enabled || Current == null)
            {
                return "The city has no order for golems";
            }
            return Wants(chassis) ? null : "The order is for " + GolemOrderText.Plural(Current.Chassis);
        }

        /// <summary>
        /// One golem of the ordered chassis reaches the city, programmed or bare: it counts the
        /// same. Refused (false) for any other chassis. The last one completes the order and pays it.
        /// </summary>
        public bool Deliver(string chassis)
        {
            if (!Enabled || !Wants(chassis))
            {
                return false;
            }
            Delivered++;
            GolemsSold++;
            Version++;
            if (Delivered >= Current.Count)
            {
                Complete();
            }
            return true;
        }

        private void Complete()
        {
            GolemOrder done = Current;
            _pay?.Invoke(RewardVouchers);
            Done++;
            Current = null;
            Delivered = 0;
            Version++;
            EventBus.Publish(new ContractEvent(ContractEventKind.OrderCompleted, done.Chassis, done.Requester));
            Settle();
        }

        // --- Saves -----------------------------------------------------------------------------

        public Save.GolemOrdersEntry Capture() => new Save.GolemOrdersEntry
        {
            done = Done,
            sold = GolemsSold,
            chassis = Current?.Chassis,
            count = Current?.Count ?? 0,
            delivered = Delivered,
        };

        /// <summary>
        /// The orders as a save left them. A save without them (null) starts at the first order,
        /// posted quietly if the office is open. An order whose chassis the data no longer has is
        /// dropped and the next one posts; a load never clamps a count.
        /// </summary>
        public void Restore(Save.GolemOrdersEntry entry)
        {
            Current = null;
            Delivered = 0;
            Done = Math.Max(0, entry?.done ?? 0);
            GolemsSold = Math.Max(0, entry?.sold ?? 0);
            if (entry != null && entry.chassis != null && _chassis.ContainsKey(entry.chassis) && entry.count > 0)
            {
                Current = Make(Done, entry.chassis, entry.count);
                Delivered = Math.Max(0, entry.delivered);
            }
            Version++;
            // Quietly: a load isn't the city posting work, so the alerts don't log it again.
            if (Enabled && Current == null && _officeOpen())
            {
                Current = Generate(Done);
            }
        }
    }
}
