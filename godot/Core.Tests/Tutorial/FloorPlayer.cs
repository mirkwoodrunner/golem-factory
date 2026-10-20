using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GolemFactory.AssemblyLine;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// A player who does everything instantly -- no walking, reading or hesitation -- but earns
    /// every good with the player's own verbs, at their real rates, on the world's own clock
    /// (docs/tuning-data-spec.md §15.4). Shared by <c>ChaptersOneToFourFloorTests</c> and
    /// <c>FullRunFloorTests</c>, so both measure the same player.
    ///
    /// <list type="bullet">
    /// <item>Raw goods (the stalls' goods) are harvested at [E]'s hold rate, taken from crates the
    ///   golems fill, and an empty stall waits for its truckload: Scrap's free, the others paid
    ///   with earned Scrap, one in transit per stall.</item>
    /// <item>Chapters 1-7: one-input goods (Coke, Iron Plate, Glass, Gear, Copper Wire) are taken
    ///   from crates golems have filled, else cranked at the bench; Iron Plate also from the
    ///   guide's smelter, kept fed. Slag comes from the smelter's crate, its carrier turned away
    ///   (R) while it is taken, as the guide's glass step says.</item>
    /// <item>From chapter 8, where the guide's goods first need two inputs: reference lines
    ///   (<see cref="RunLine"/>), the cheapest chassis with room for the recipe, between two crates,
    ///   steamed, its card claimed, all paid for, the player putting inputs in and taking the
    ///   output. The first is an Iron Plate line on Scrap, which pays for the rest. One-input goods
    ///   get up to <see cref="MaxParallelLines"/> lines, two-input goods up to
    ///   <see cref="MaxParallelMadeLines"/>, a line more whenever the player has waited
    ///   <see cref="WaitBeforeAnotherLine"/> seconds; lines on a raw good are kept full; the bench
    ///   cranks alongside. Slag comes from a smelting line of the player's own.</item>
    /// <item>Everything else a kit action does is the kit's own action, instant, but whatever the
    ///   kit grants is earned: what it spent, paid from the pack (earned first); what it left in
    ///   the pack, made.</item>
    /// </list>
    /// </summary>
    internal sealed class FloorPlayer
    {
        /// <summary>Game seconds a frame: 1/30, as chapters 1-4 were first measured; 1/10 (one tick) for the full run.</summary>
        public float Dt { get; }

        /// <summary>Chapters 1-7: Iron Plate this short feeds the guide's smelter as well as the bench. Also a one-input line's lead.</summary>
        public const int Bulk = 20;

        private readonly SandboxWorld _w;
        private readonly float _timeout;
        private readonly Dictionary<string, Action> _kit;

        /// <summary>Seconds spent cranking, by good.</summary>
        public Dictionary<string, float> Cranked { get; } = new Dictionary<string, float>();

        /// <summary>The reference lines built, by recipe name: what the run cost in golems.</summary>
        public List<string> LinesBuilt { get; } = new List<string>();

        public FloorPlayer(float stepTimeout, float dt = 1f / 30f)
        {
            Dt = dt;
            DefinitionSet definitions = AuthoredData.Load();
            _w = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            _w.Clock.Play();
            _timeout = stepTimeout;
            _kit = (Dictionary<string, Action>)typeof(TutorialGuide)
                .GetProperty("Performs", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_w.Tutorial);
        }

        public SandboxWorld World => _w;

        public TutorialGuide Guide => _w.Tutorial;

        /// <summary>Game time so far, in seconds: the world's own clock.</summary>
        public float Now => _w.Tutorial.Now;

        private StorageBuffer Pack => _w.Wallet.Pack;

        private DefinitionSet Defs => _w.Definitions;

        // --- A step ----------------------------------------------------------------------------

        /// <summary>Does what <paramref name="stepId"/> asks, as the player would, then waits for the guide to move on.</summary>
        public void Play(string stepId)
        {
            _linesAllowed = _w.Tutorial.ChapterOf(_w.Tutorial.Index) >= LinesFromChapter;
            Perform(stepId);
            WaitToLeave(stepId);
        }

        private void Perform(string stepId)
        {
            switch (stepId)
            {
                case "scrap": Acquire(ItemType.Scrap, 20); return;
                case "coal": Acquire(ItemType.Coal, 5); return;
                case "coke": Acquire(ItemType.Coke, 5); return;
                case "iron": Acquire(ItemType.IronPlate, 10); return;
                case "gears": Acquire(ItemType.Gear, Line(Defs.Chassis["BrassPresser"].cost, ItemType.Gear)); return;
                case "fuel":
                    Acquire(ItemType.Coke, 1);
                    Assert.IsTrue(_w.Interactor.TryRefuelBoiler(Boilers().First()), _w.Interactor.LastStatusMessage);
                    return;
                case "coal-order":
                    Acquire(ItemType.Scrap, OfferPrice("CoalNode"));
                    Assert.IsTrue(_w.Interactor.TryOrderTruckload(Marker("CoalNode")), _w.Interactor.LastStatusMessage);
                    return;
                case "copper": Acquire(ItemType.CopperOre, 1); return;
                case "collect":
                    CollectWhenFilled(_w.Build.Buildings.First(b => !b.IsRemoved && b.Cell == _w.Tutorial.DepotSpot), ItemType.Scrap);
                    return;
                case "mast-collect":
                    CollectWhenFilled(_w.Build.Buildings.First(b => !b.IsRemoved && b.GetPart<PlaceableFreightMast>() != null), ItemType.CopperOre);
                    return;
                case "tower-feed":
                    // A golem's push, as the kit's: one Frame Section the line made, into a footprint cell.
                    Acquire(ItemType.FrameSection, 1);
                    Assert.IsTrue(Pack.TryWithdraw(ItemType.FrameSection, 1));
                    Assert.IsTrue(_w.Endpoints.TryGetEndpoint(TownSquare.TowerOrigin, out IItemEndpoint tower));
                    Assert.IsTrue(tower.TryGive(new ItemStack { ItemType = ItemType.FrameSection }), "the tower takes it");
                    return;
                case "stage1":
                    // tuning-data-spec §15.4: the one exception. Stage 1's floor is its nominal
                    // time: the tower is fed 6 a minute, each one fresh production, as a line
                    // built for that rate would. Building it is the player's design, timed by the
                    // human run.
                    FeedTheFoundation();
                    return;
            }

            // Everything else is the kit's own action, paid for with goods earned first.
            for (int round = 0; round < 5; round++)
            {
                AcquireAll(Needs(stepId).ToList());
                if (!BuildsAGolem.Contains(stepId) || _w.AssemblyBay.HasFreeSlot)
                {
                    break;
                }
                // The floor player's own lines fill bays the guide's golems need: one more upgrade.
                AcquireAll(_w.AssemblyBay.UpgradeCost);
                Assert.IsTrue(_w.AssemblyLineBoard.UpgradeBays(), _w.AssemblyLineBoard.Status);
            }
            // Taken just before the kit acts, which is instant: nothing else moves in between.
            IReadOnlyList<string> goods = ItemTiers.CanonicalOrder;
            Dictionary<string, int> before = goods.ToDictionary(g => g, g => Pack.TotalDeposited(g));
            Dictionary<string, int> inPack = goods.ToDictionary(g => g, g => Pack.GetQuantity(g));
            Dictionary<(Vector2Int, string), int> inCrates = CrateContents();
            _kit[stepId]();
            // A crate the kit filled by hand (a smelter's inputs, the carrier's Coke) is a golem's
            // to work from: what the player put there is never taken back to repay the kit.
            foreach (KeyValuePair<(Vector2Int, string), int> held in CrateContents())
            {
                if (held.Value > (inCrates.TryGetValue(held.Key, out int was) ? was : 0))
                {
                    _handFed.Add(held.Key);
                }
            }
            foreach (string good in goods)
            {
                int granted = Pack.TotalDeposited(good) - before[good];
                if (granted > 0)
                {
                    Repay(stepId, good, granted, Pack.GetQuantity(good) - inPack[good]);
                }
            }
        }

        /// <summary>
        /// The kit granted <paramref name="granted"/> of a good. What it spent, the player pays from
        /// their own goods, earned first (<see cref="Needs"/>); what it left in the pack (a goal
        /// step's goods), the player earns. <paramref name="kept"/> is how much more of the good the
        /// pack holds than before the kit acted.
        /// </summary>
        private void Repay(string stepId, string good, int granted, int kept)
        {
            kept = Math.Max(0, Math.Min(granted, kept));
            Assert.IsTrue(Pack.TryWithdraw(good, kept), $"{stepId}: take back the {kept} {good} granted");
            int spent = granted - kept;
            while (spent > 0)
            {
                // A pack at a time: a step can spend more of a good than one pack holds.
                int chunk = Math.Min(spent, Pack.CapacityPerType);
                Acquire(good, chunk);
                Assert.IsTrue(Pack.TryWithdraw(good, chunk), $"{stepId}: repay {chunk} {good}");
                spent -= chunk;
            }
            if (kept > 0)
            {
                // Up to a full pack: anything the player made past it is already in hand.
                Acquire(good, Math.Min(Pack.CapacityPerType, Pack.GetQuantity(good) + kept));
            }
        }

        private readonly HashSet<(Vector2Int, string)> _handFed = new HashSet<(Vector2Int, string)>();

        private Dictionary<(Vector2Int, string), int> CrateContents()
        {
            var contents = new Dictionary<(Vector2Int, string), int>();
            foreach (PlaceableBuilding building in _w.Build.Buildings.Where(b => !b.IsRemoved))
            {
                ICrate crate = building.GetPart<ICrate>();
                if (crate?.Buffer == null || !crate.OwnsBuffer)
                {
                    continue;
                }
                foreach (KeyValuePair<string, int> good in crate.Buffer.Quantities)
                {
                    contents[(building.Cell, good.Key)] = good.Value;
                }
            }
            return contents;
        }

        private static readonly HashSet<string> BuildsAGolem = new HashSet<string>
        {
            "golem", "scav2", "scav3", "unloader", "presser", "presser2", "carrier", "hauler", "zeppelin",
        };

        private static readonly string[] AcquireOrder =
        {
            ItemType.FrameSection, ItemType.AetherCell, ItemType.Mainspring, ItemType.Lens, ItemType.Casing, ItemType.Brass,
            ItemType.ZincIngot, ItemType.CopperIngot, ItemType.Glass, ItemType.Gear, ItemType.Coke, ItemType.IronPlate,
            ItemType.Aether, ItemType.ZincOre, ItemType.CopperOre, ItemType.Coal, ItemType.Scrap,
        };

        /// <summary>What a step's kit action spends, so the player earns it before, not after.</summary>
        private IEnumerable<RecipeIngredient> Needs(string stepId)
        {
            switch (stepId)
            {
                case "boiler":
                case "boiler2": return Cost("BoilerPrefab", 1);
                case "depot":
                case "depot2":
                case "depot3":
                case "copper-depot": return Cost("DepotPrefab", 1);
                case "smelt-depots": return Cost("DepotPrefab", 2);
                case "slag-heap": return Cost("SlagHeapPrefab", 1);
                case "pipe": return Cost("SteamPipePrefab", _w.Tutorial.PipeSpots.Length);
                case "pipes2": return Cost("SteamPipePrefab", _w.Tutorial.Pipe2Spots.Length);
                case "pipes3": return Cost("SteamPipePrefab", _w.Tutorial.Pipe3Spots.Length);
                case "pipes4": return Cost("SteamPipePrefab", _w.Tutorial.Pipe4Spots.Length);
                case "zeppelin-pipe": return Cost("SteamPipePrefab", _w.Tutorial.ZeppelinPipeSpots.Length);
                case "belts": return Cost("BeltPrefab", _w.Tutorial.BeltSpots.Length);
                case "post": return Cost("PneumaticPostPrefab", _w.Tutorial.PostSpots.Length);
                case "mast": return Cost("FreightMastPrefab", 1);
                case "golem":
                case "scav2":
                case "scav3":
                case "unloader": return Defs.Chassis["ClockworkScavenger"].cost;
                case "presser":
                case "presser2":
                case "carrier": return Defs.Chassis["BrassPresser"].cost;
                case "hauler": return Defs.Chassis["AetherHauler"].cost;
                case "zeppelin": return Defs.Chassis["ZeppelinFreightLoader"].cost;
                case "expand": return _w.FloorExpansion.NextCost();
                case "bays": return _w.AssemblyBay.UpgradeCost;
                case "pack": return _w.PackUpgrade.NextCost();
                case "claim": return CardCost("AssembleScrapReclamation");
                case "coking-card": return CardCost("AssembleCoking");
                case "r4-card": return CardCost("AssembleIronSmelting");
                case "zeppelin-card": return CardCost("ZeppelinFreightLoader");
                default: return Enumerable.Empty<RecipeIngredient>();
            }
        }

        private IEnumerable<RecipeIngredient> Cost(string key, int count) =>
            _w.Placeables.First(p => p.Key == key).Prefab.Cost
                .Select(c => new RecipeIngredient(c.itemType, c.quantity * count));

        private IEnumerable<RecipeIngredient> CardCost(string card)
        {
            AssemblyLineState line = _w.AssemblyLine;
            for (int i = 0; i < line.SlotCount; i++)
            {
                if (TutorialGuide.CardKey(line.GetCard(i)) == card)
                {
                    return line.GetCurrentCostBundle(i).ToList();
                }
            }
            return Enumerable.Empty<RecipeIngredient>();
        }

        private static int Line(IEnumerable<RecipeIngredient> cost, string good) => cost.Where(c => c.itemType == good).Sum(c => c.quantity);

        private int OfferPrice(string stall) => _w.Market.TryGetOffer(stall, out MarketOffer offer) ? offer.Price.Sum(c => c.quantity) : 0;

        private void CollectWhenFilled(PlaceableBuilding building, string good)
        {
            ICrate crate = building.GetPart<ICrate>();
            Until(() => crate.Buffer.GetQuantity(good) > 0, $"the crate on {building.Cell} fills with {good}");
            Assert.IsTrue(_w.Interactor.TryCollect(crate), _w.Interactor.LastStatusMessage);
        }

        /// <summary>
        /// Stage 1 at its demand: every 60/rate seconds one Frame Section assembled and pushed into
        /// the tower, until the stage is laid.
        /// </summary>
        private void FeedTheFoundation()
        {
            ClockTower.ClockTowerSite site = _w.ClockTower.Site;
            ClockTower.StageDemand demand = site.ActiveStage.demands.Single();
            Assert.IsTrue(_w.Endpoints.TryGetEndpoint(TownSquare.TowerOrigin, out IItemEndpoint tower));
            float interval = 60f / demand.ratePerMinute;
            float next = 0f;
            float started = Now;
            while (site.StageIndex < 1 && !site.IsComplete)
            {
                Assert.Less(Now - started, _timeout, "stage 1 never finished at its demand rate");
                if (Now - started >= next)
                {
                    next += interval;
                    EventBus.Publish(new ItemAssembledEvent("FloorFrameLine", demand.itemType, 1, _w.Clock.CurrentTick));
                    Assert.IsTrue(tower.TryGive(new ItemStack { ItemType = demand.itemType }), "the tower takes it");
                }
                Step();
            }
        }

        // --- Goods ----------------------------------------------------------------------------

        private HashSet<string> _raw;

        private HashSet<string> Raw => _raw ??= new HashSet<string>(_w.Setup.nodes.Select(n => n.itemType));

        private readonly HashSet<string> _acquiring = new HashSet<string>();

        /// <summary>Until the pack holds <paramref name="quantity"/> of <paramref name="good"/>.</summary>
        public void Acquire(string good, int quantity)
        {
            if (Pack.GetQuantity(good) >= quantity)
            {
                return;
            }
            Assert.LessOrEqual(quantity, Pack.CapacityPerType, $"{quantity} {good} can't fit a {Pack.CapacityPerType} pack");
            TakeFromCrates(good, quantity);
            if (Pack.GetQuantity(good) >= quantity)
            {
                return;
            }
            if (Raw.Contains(good))
            {
                Harvest(good, quantity);
                return;
            }
            if (good == ItemType.Slag)
            {
                // Before lines, the guide's smelter's Slag; after, a smelting line of the player's
                // own, whose Slag nobody carries off.
                if (LinesAllowed && !_acquiring.Contains(good))
                {
                    WithCarriersTurned(() => Making(good, () => RunLine(Defs.Recipes["R4_IronSmelting"], quantity, ItemType.Slag)));
                }
                else
                {
                    AcquireSlag(quantity);
                }
                return;
            }
            RecipeDefinition recipe = RecipeFor(good);
            int shortfall = quantity - Pack.GetQuantity(good);
            bool crankable = HandCrankRules.IsCrankable(recipe);
            bool hasLine = _lines.TryGetValue(recipe.name, out List<RefLine> made) && made.Count > 0;
            if (!LinesAllowed)
            {
                // Chapters 1-7: the bench, and the guide's own lines.
                if (good == ItemType.IronPlate && shortfall >= Bulk)
                {
                    FeedSmelters(shortfall);
                }
                Crank(good, quantity);
                return;
            }
            // Iron Plate first: every line is paid for in it, and a Plate line on Scrap pays for
            // itself in a minute.
            EnsurePlateLine();
            hasLine = _lines.TryGetValue(recipe.name, out made) && made.Count > 0;
            // A good whose first line is being paid for (a line's own price needs it), or a
            // chore's Coke before any Coke line stands, is cranked: a chore never builds a line.
            if (crankable && ((!hasLine && (_acquiring.Contains(good) || _inChore)) || _bootstrapping))
            {
                Crank(good, quantity);
                return;
            }
            Making(good, () => RunLine(recipe, quantity));
        }

        private void Making(string good, Action make)
        {
            _acquiring.Add(good);
            try
            {
                make();
            }
            finally
            {
                _acquiring.Remove(good);
            }
        }

        /// <summary>The recipe the floor player makes a good by: the bench's for one-input goods, else the one that makes it.</summary>
        private RecipeDefinition RecipeFor(string good)
        {
            RecipeDefinition crankable = _w.StarterBench.CrankableRecipes.FirstOrDefault(r => r.outputItemType == good);
            if (crankable != null)
            {
                return crankable;
            }
            return Defs.Recipes.Values.Where(r => r.outputItemType == good).OrderBy(r => r.inputs.Count).First();
        }

        /// <summary>
        /// Whether a golem works from <paramref name="crate"/> for <paramref name="good"/>: its
        /// Hauls take it (or anything). An instant player keeps such a golem fed and takes only
        /// what nobody hauls on, as they take Iron Plate from the smelter's crate and leave its Slag.
        /// </summary>
        private bool Feeds(PlaceableBuilding crate, string good) =>
            _w.Golems.Any(g => !g.IsRemoved && g.SourceCell == crate.Cell && g.Program.appendages.Select((a, i) => (a, i)).Any(p =>
                p.a != null && (p.a.actionType == AppendageActionType.ExtractFromNode
                    || (p.a.actionType == AppendageActionType.Haul && (string.IsNullOrEmpty(g.Program.GetItemTypeAt(p.i)) || g.Program.GetItemTypeAt(p.i) == good)))));

        /// <summary>Takes from crates golems have filled, but never what a golem hauls from that crate.</summary>
        public void TakeFromCrates(string good, int quantity, bool evenFromFeeders = false)
        {
            foreach (PlaceableBuilding building in _w.Build.Buildings.Where(b => !b.IsRemoved).ToList())
            {
                ICrate crate = building.GetPart<ICrate>();
                if (crate?.Buffer == null || !crate.OwnsBuffer || crate.Buffer.GetQuantity(good) <= 0
                    || _lineInputs.Contains(building.Cell)
                    || (!evenFromFeeders && (Feeds(building, good) || _handFed.Contains((building.Cell, good)))))
                {
                    continue;
                }
                int want = quantity - Pack.GetQuantity(good);
                if (want <= 0)
                {
                    return;
                }
                CrateTransfer.Take(crate, Pack, good, want);
            }
        }

        private void Harvest(string good, int quantity)
        {
            using IDisposable _ = Doing("harvest " + good);
            _w.StarterBench.IsCranking = false; // the player is at the stall, not the bench
            float held = 0f, next = 0f;
            ResourceNodeMarker at = null;
            float started = Now;
            while (Pack.GetQuantity(good) < quantity)
            {
                Assert.Less(Now - started, _timeout, $"harvesting {quantity} {good}");
                TakeFromCrates(good, quantity);
                if (Pack.GetQuantity(good) >= quantity)
                {
                    return;
                }
                ResourceNodeMarker stocked = _w.Markers.FirstOrDefault(m => m.ItemType == good && !m.IsDepleted);
                if (stocked == null)
                {
                    if (good == ItemType.Scrap)
                    {
                        // Free carts: both Scrap stalls, whenever they're empty.
                        foreach (ResourceNodeMarker empty in _w.Markers.Where(m => m.ItemType == good && !_w.Market.IsInTransit(m.NodeId)))
                        {
                            _w.Interactor.TryOrderTruckload(empty);
                        }
                    }
                    else if (!_w.Markers.Any(m => m.ItemType == good && _w.Market.IsInTransit(m.NodeId)))
                    {
                        // One paid cart at a time, at the offer's price.
                        // The guide's own stall for the good (CoalNode, not CoalNodeWest).
                        ResourceNodeMarker stall = _w.Markers.Where(m => m.ItemType == good)
                            .OrderBy(m => m.NodeId.EndsWith("West") || m.NodeId.EndsWith("East") ? 1 : 0).First();
                        Acquire(ItemType.Scrap, OfferPrice(stall.NodeId));
                        Assert.IsTrue(_w.Interactor.TryOrderTruckload(stall), _w.Interactor.LastStatusMessage);
                    }
                    at = null;
                    Step();
                    continue;
                }
                if (stocked != at)
                {
                    at = stocked;
                    held = 0f;
                    next = 0f;
                }
                while (held >= next && Pack.GetQuantity(good) < quantity && !at.IsDepleted)
                {
                    Assert.IsTrue(_w.Interactor.TryHarvest(at), _w.Interactor.LastStatusMessage);
                    next = next == 0f ? PlayerInteractor.HarvestRepeatDelay : next + PlayerInteractor.HarvestRepeatInterval;
                }
                Step();
                held += Dt;
            }
        }

        private void Crank(string good, int quantity)
        {
            using IDisposable _ = Doing("crank " + good);
            HandCrankBench bench = _w.StarterBench;
            RecipeDefinition recipe = bench.CrankableRecipes.First(r => r.outputItemType == good);
            int crafts = (quantity - Pack.GetQuantity(good) + recipe.outputQuantity - 1) / recipe.outputQuantity;
            foreach (RecipeIngredient input in recipe.inputs)
            {
                Acquire(input.itemType, Math.Min(Pack.CapacityPerType, input.quantity * crafts));
            }
            // Collecting what the golems make while cranking: the bench and the lines together.
            // The dial is re-set each frame, since a chore (Coke for a boiler) may turn it.
            Until(() =>
            {
                TakeFromCrates(good, quantity);
                if (Pack.GetQuantity(good) >= quantity)
                {
                    return true;
                }
                foreach (RecipeIngredient input in recipe.inputs.Where(i => Pack.GetQuantity(i.itemType) < i.quantity))
                {
                    bench.IsCranking = false;
                    Acquire(input.itemType, Math.Min(Pack.CapacityPerType, input.quantity * (quantity - Pack.GetQuantity(good))));
                }
                while (bench.SelectedRecipe != recipe)
                {
                    bench.CycleRecipe();
                }
                bench.IsCranking = true;
                return false;
            }, $"cranking {quantity} {good}");
            bench.IsCranking = false;
        }

        /// <summary>
        /// Slag, as the guide's glass step says: from the smelters' crates, with whoever hauls it
        /// to the heap turned away (R) while it is taken, and back after.
        /// </summary>
        private void AcquireSlag(int quantity)
        {
            using IDisposable _ = Doing("Slag from the smelters");
            WithCarriersTurned(() =>
                Until(() =>
                {
                    TakeFromCrates(ItemType.Slag, quantity, evenFromFeeders: true);
                    if (Pack.GetQuantity(ItemType.Slag) >= quantity)
                    {
                        return true;
                    }
                    FeedSmelters(2 * (quantity - Pack.GetQuantity(ItemType.Slag)));
                    return false;
                }, $"taking {quantity} Slag from the smelters (pack {Pack.GetQuantity(ItemType.Slag)}; crates "
                    + string.Join(", ", CrateContents().Where(c => c.Key.Item2 == ItemType.Slag).Select(c => $"{c.Key.Item1} {c.Value}")) + ")"));
        }

        /// <summary>The guide's carriers, which take Slag to the heap, turned away (R) while <paramref name="act"/> runs.</summary>
        private void WithCarriersTurned(Action act)
        {
            var turned = new List<(GolemEntity golem, Facing facing)>();
            foreach (GolemEntity carrier in _w.Golems.Where(g => !g.IsRemoved && HaulsOf(g, ItemType.Slag)
                && !_lines.Values.SelectMany(l => l).Any(l => l.Golem == g.Cell)).ToList())
            {
                turned.Add((carrier, carrier.Facing));
                carrier.SetPlacement(carrier.Cell, FacingUtility.RotateClockwise(carrier.Facing));
            }
            try
            {
                act();
            }
            finally
            {
                foreach ((GolemEntity golem, Facing facing) in turned)
                {
                    golem.SetPlacement(golem.Cell, facing);
                }
            }
        }

        private static bool HaulsOf(GolemEntity golem, string good) =>
            golem.Program.appendages.Select((a, i) => (a, i)).Any(p =>
                p.a?.actionType == AppendageActionType.Haul && golem.Program.GetItemTypeAt(p.i) == good);

        private bool _feeding;

        /// <summary>
        /// Keeps every golem smelting Iron Plate from a crate fed for about <paramref name="plates"/>
        /// more: what it hauls, put into its crate from the pack (earned first).
        /// </summary>
        private void FeedSmelters(int plates)
        {
            if (_feeding)
            {
                return;
            }
            _feeding = true;
            try
            {
                RecipeDefinition r4 = Defs.Recipes["R4_IronSmelting"];
                // The guide's carrier hauls Slag with the Coke that burns it: its crate needs Coke too.
                foreach (GolemEntity carrier in _w.Golems.Where(g => !g.IsRemoved && HaulsOf(g, ItemType.Slag) && HaulsOf(g, ItemType.Coke)
                    && !_lines.Values.SelectMany(l => l).Any(l => l.Golem == g.Cell)).ToList())
                {
                    ICrate crate = CrateOn(carrier.SourceCell);
                    if (crate?.Buffer != null && crate.Buffer.GetQuantity(ItemType.Coke) < 2)
                    {
                        Acquire(ItemType.Coke, 10);
                        CrateTransfer.Put(Pack, crate, ItemType.Coke, 10);
                        _handFed.Add((carrier.SourceCell, ItemType.Coke));
                    }
                }
                foreach (GolemEntity smelter in _w.Golems.Where(g => !g.IsRemoved && g.HasRunnableProgram
                    && !_lines.Values.SelectMany(l => l).Any(l => l.Golem == g.Cell)
                    && g.Program.appendages.Any(a => a?.recipe == r4)).ToList())
                {
                    ICrate crate = CrateOn(smelter.SourceCell);
                    if (crate?.Buffer == null)
                    {
                        continue;
                    }
                    int runs = (plates + r4.outputQuantity - 1) / r4.outputQuantity;
                    foreach (RecipeIngredient input in r4.inputs)
                    {
                        int want = Math.Min(crate.Buffer.CapacityPerType, input.quantity * runs) - crate.Buffer.GetQuantity(input.itemType);
                        // Topped up a batch at a time, so the smelter runs while the next is earned.
                        if (want < input.quantity * 4)
                        {
                            continue;
                        }
                        want = Math.Min(want, Pack.CapacityPerType);
                        Acquire(input.itemType, want);
                        CrateTransfer.Put(Pack, crate, input.itemType, want);
                        _handFed.Add((smelter.SourceCell, input.itemType));
                    }
                }
            }
            finally
            {
                _feeding = false;
            }
        }

        private ICrate CrateOn(Vector2Int cell) =>
            _w.Build.Buildings.FirstOrDefault(b => !b.IsRemoved && b.Cell == cell)?.GetPart<ICrate>();

        // --- Reference lines --------------------------------------------------------------------
        //
        // tuning-data-spec §15.4: a good the bench can't make (or not fast enough) is made the way
        // a player would make it: the cheapest chassis with room for a Haul per input, the
        // Assemble and a Push, between an input crate and an output crate, steamed by a boiler
        // beside it, its recipe's card claimed, all paid for from earned goods. The player puts
        // the inputs into its crate by hand and collects the output. A line stays and is reused.

        /// <summary>
        /// Lines from chapter 8, where the guide's goods first need two inputs and so a line of
        /// their own. Not in chapter 5 in any case: the guide would adopt a line as its own (a
        /// line's two crates, two apart with a golem between, are the shape chapter 5 takes as the
        /// smelter's pair of depots, and a line's Brass Presser counts as "another Brass Presser"
        /// for the carrier, which the next step reprograms). And not in chapters 6-7: their few
        /// dozen Plate and Gears come faster from the bench and the guide's own lines than from
        /// a line that must first be paid for.
        /// </summary>
        private const int LinesFromChapter = 8;

        private bool LinesAllowed => _linesAllowed;

        // Set as each step begins, so a step chapter 5 finishes part-way keeps its rule.
        private bool _linesAllowed;

        private sealed class RefLine
        {
            public RecipeDefinition Recipe;
            public Vector2Int In, Golem, Out;
        }

        private readonly Dictionary<string, List<RefLine>> _lines = new Dictionary<string, List<RefLine>>();
        private readonly HashSet<Vector2Int> _lineInputs = new HashSet<Vector2Int>();

        /// <summary>
        /// Until the pack holds <paramref name="quantity"/> of <paramref name="good"/> (the recipe's
        /// output, or its byproduct), made by the recipe's line: built once, its input crate
        /// topped up from the pack (earned first), its output collected.
        /// </summary>
        public void RunLine(RecipeDefinition recipe, int quantity, string good = null)
        {
            using IDisposable _ = Doing("line " + recipe.name);
            good ??= recipe.outputItemType;
            int perRun = good == recipe.outputItemType ? recipe.outputQuantity : recipe.byproductQuantity;
            if (!_lines.TryGetValue(recipe.name, out List<RefLine> lines))
            {
                lines = _lines[recipe.name] = new List<RefLine>();
            }
            if (lines.Count == 0)
            {
                AddLine(recipe, lines);
            }
            HandCrankBench bench = _w.StarterBench;
            bool crankable = bench.CrankableRecipes.Contains(recipe);
            float started = Now;
            float lastAdded = Now;
            while (Pack.GetQuantity(good) < quantity)
            {
                Assert.Less(Now - started, _timeout, $"{lines.Count} {recipe.name} line(s) making {quantity} {good} (pack {Pack.GetQuantity(good)}; their crates hold "
                    + string.Join("; ", lines.Select(l => string.Join(", ", CrateOn(l.In).Buffer.Quantities.Select(q => $"{q.Value} {q.Key}"))))
                    + "; their golems " + string.Join("; ", lines.Select(l => GolemOn(l.Golem)).Select(g => g == null ? "gone" : $"{g.Program.State} {g.StallReason} {g.StallResourceId}"))
                    + "; outputs " + string.Join("; ", lines.Select(l => { PlaceableBuilding ob = _w.Build.Buildings.First(b => !b.IsRemoved && b.Cell == l.Out); return $"{l.Out} {ob.PrefabKey} holds {ob.GetPart<ICrate>()?.Buffer?.GetQuantity(good)} owns {ob.GetPart<ICrate>()?.OwnsBuffer} feeds {Feeds(ob, good)} fed {_handFed.Contains((l.Out, good))} input {_lineInputs.Contains(l.Out)} by {string.Join(",", _w.Golems.Where(x => !x.IsRemoved && x.SourceCell == l.Out).Select(x => x.GolemId))}"; }))
                    + "): " + GolemStates());
                if (good == ItemType.Slag)
                {
                    // The guide's smelter too, its carrier turned away while the Slag is taken.
                    TakeFromCrates(good, quantity, evenFromFeeders: true);
                }
                TakeFromCrates(good, quantity);
                int owed = quantity - Pack.GetQuantity(good);
                if (owed <= 0)
                {
                    break;
                }
                // Where the factory keeps the player waiting, a player adds a line.
                if (!_inChore && lines.Count < (crankable ? MaxParallelLines : MaxParallelMadeLines)
                    && ((crankable && owed >= ParallelAt * (lines.Count + 1)) || Now - lastAdded > WaitBeforeAnotherLine))
                {
                    AddLine(recipe, lines);
                    lastAdded = Now;
                }
                int inFlight = lines.Sum(l => (GolemOn(l.Golem)?.Inventory.GetOutput(good) ?? 0) + (CrateOn(l.Out)?.Buffer.GetQuantity(good) ?? 0));
                int runs = Math.Max(0, (owed - inFlight + perRun - 1) / perRun);
                int runsEach = (runs + lines.Count - 1) / lines.Count;
                bench.IsCranking = false;
                foreach (RefLine line in lines.ToList())
                {
                    // A line on a raw good is kept full: a few seconds at a stall keep it running
                    // for minutes, and what it makes is always wanted again. Others get what
                    // this need asks, and a little more if they are one-input.
                    bool onRaw = recipe.inputs.All(i => Raw.Contains(i.itemType));
                    TopUp(line, recipe, onRaw ? int.MaxValue / 64 : runsEach + (crankable ? Bulk / lines.Count : 0));
                    if (good != recipe.outputItemType)
                    {
                        // Making the byproduct: the main output is collected too, or it fills the crate.
                        ICrate output = CrateOn(line.Out);
                        if (output.Buffer.GetQuantity(recipe.outputItemType) > 0)
                        {
                            CrateTransfer.Take(output, Pack, recipe.outputItemType, int.MaxValue);
                        }
                    }
                }
                ServiceRawLines();
                if (good == ItemType.IronPlate || good == ItemType.Slag)
                {
                    FeedSmelters(2 * owed);
                }
                // The bench and the line together: cranking the same good while the line runs,
                // from inputs already in the pack (fetching more would leave the bench).
                if (!_inChore && crankable && good == recipe.outputItemType && recipe.inputs.All(i => Pack.GetQuantity(i.itemType) >= i.quantity))
                {
                    while (bench.SelectedRecipe != recipe)
                    {
                        bench.CycleRecipe();
                    }
                    bench.IsCranking = true;
                }
                Step();
            }
            bench.IsCranking = false;
        }

        private bool _servicing;

        /// <summary>
        /// The first line a player builds once lines pay: Iron Plate from Scrap. Every later line
        /// is paid for in Plate; it costs a Presser, pays for itself in a minute, and its own
        /// price is cranked. (A Coke line built straight after it measured slower: 37:24 against
        /// 35:39 for chapter 8, its price competing with the Hauler's.)
        /// </summary>
        private void EnsurePlateLine()
        {
            foreach ((string recipe, string good) in new[] { ("R2_ScrapReclamation", ItemType.IronPlate) })
            {
                if (_inChore || _bootstrapping || _acquiring.Contains(good)
                    || (_lines.TryGetValue(recipe, out List<RefLine> made) && made.Count > 0))
                {
                    continue;
                }
                if (!_lines.ContainsKey(recipe))
                {
                    _lines[recipe] = new List<RefLine>();
                }
                _bootstrapping = true;
                try
                {
                    Making(good, () => AddLine(Defs.Recipes[recipe], _lines[recipe]));
                }
                finally
                {
                    _bootstrapping = false;
                }
            }
        }

        private bool _bootstrapping;

        /// <summary>
        /// Every line that runs on a raw good (Iron Plate from Scrap, Coke from Coal) kept fed
        /// whenever the player is waiting anyway: a few seconds at a stall keep it running for minutes.
        /// </summary>
        private void ServiceRawLines()
        {
            if (_servicing || _inChore)
            {
                return;
            }
            _servicing = true;
            try
            {
                foreach (List<RefLine> lines in _lines.Values.ToList())
                {
                    foreach (RefLine line in lines.ToList())
                    {
                        if (line.Recipe.inputs.All(i => Raw.Contains(i.itemType))
                            && line.Recipe.inputs.Any(i => CrateOn(line.In).Buffer.GetQuantity(i.itemType) < 10))
                        {
                            TopUp(line, line.Recipe, int.MaxValue / 64);
                        }
                    }
                }
            }
            finally
            {
                _servicing = false;
            }
        }

        /// <summary>A one-input good this many short per line gets another line, up to <see cref="MaxParallelLines"/>.</summary>
        private const int ParallelAt = 40;

        private const int MaxParallelLines = 4;

        /// <summary>Lines of a good of two or more inputs: a Hauler or an Overclocker each, so fewer.</summary>
        private const int MaxParallelMadeLines = 2;

        /// <summary>Game seconds a player waits on a good's lines before adding another.</summary>
        private const float WaitBeforeAnotherLine = 45f;

        /// <summary>Tops a line's input crate up from the pack for about <paramref name="runs"/> more runs.</summary>
        private void TopUp(RefLine line, RecipeDefinition recipe, int runs)
        {
            ICrate input = CrateOn(line.In);
            GolemEntity golem = GolemOn(line.Golem);
            foreach (RecipeIngredient need in recipe.inputs)
            {
                int held = input.Buffer.GetQuantity(need.itemType) + (golem?.Inventory.GetInput(need.itemType) ?? 0);
                int want = (int)Math.Min(input.Buffer.CapacityPerType, (long)need.quantity * runs) - held;
                // A batch at a time, a few runs ahead, so the line runs while the next is earned.
                if (want <= 0 || (held >= need.quantity * 2 && want < Math.Max(need.quantity * 4, 10)))
                {
                    continue;
                }
                want = Math.Min(Math.Min(want, Pack.CapacityPerType), input.Buffer.CapacityPerType - input.Buffer.GetQuantity(need.itemType));
                if (want <= 0)
                {
                    continue;
                }
                Acquire(need.itemType, want);
                CrateTransfer.Put(Pack, input, need.itemType, want);
            }
        }

        private void AddLine(RecipeDefinition recipe, List<RefLine> lines)
        {
            ClaimCardFor(recipe);
            ChassisDefinition chassis = Defs.Chassis.Values
                .Where(c => c.maxAppendageSlots >= recipe.inputs.Count + 2)
                .OrderBy(c => c.maxAppendageSlots).First();
            (Vector2Int a, Vector2Int g, Vector2Int b, List<(string key, Vector2Int cell)> steam) = FindStrip();
            // Counted as the recipe's line only once its golem stands: until then, earning its
            // price must not wait on it.
            var line = new RefLine { Recipe = recipe, In = a, Golem = g, Out = b };
            _building.Add(line);
            _lineInputs.Add(a);

            PlacePaid("DepotPrefab", a);
            PlacePaid("DepotPrefab", b);
            foreach ((string key, Vector2Int cell) in steam)
            {
                PlacePaid(key, cell);
            }
            GolemEntity golem = BuildPaid(chassis);
            AppendageActionDefinition haul = Defs.Appendages["HaulScrap"];
            var cards = new List<AppendageActionDefinition>();
            var goods = new List<string>();
            var quantities = new List<int>();
            foreach (RecipeIngredient input in recipe.inputs)
            {
                cards.Add(haul);
                goods.Add(input.itemType);
                quantities.Add(input.quantity);
            }
            cards.Add(Defs.Appendages.Values.First(c => c.actionType == AppendageActionType.Assemble && c.recipe == recipe));
            cards.Add(Defs.Appendages["PushOutput"]);
            goods.Add("");
            goods.Add("");
            quantities.Add(1);
            quantities.Add(1);
            Assert.IsTrue(GolemProgramWriter.TryWrite(golem.Program, null, Defs.LogicCores["AlwaysOnCore"], cards, goods, quantities, out string refusal),
                $"{recipe.name} line: {refusal}");
            golem.SetPlacement(g, Facing.East);
            _building.Remove(line);
            lines.Add(line);
            LinesBuilt.Add($"{recipe.name} on a {chassis.name} ({_w.Tutorial.Current?.Id})");
        }

        private readonly List<RefLine> _building = new List<RefLine>();

        private void ClaimCardFor(RecipeDefinition recipe)
        {
            string user = _w.Setup.assemblyLine.claimUserId;
            AssemblyLineState line = _w.AssemblyLine;
            if (line.GetClaimedCards(user).Any(c => c.appendage?.recipe == recipe))
            {
                return;
            }
            for (int i = 0; i < line.SlotCount; i++)
            {
                if (line.GetCard(i)?.appendage?.recipe == recipe)
                {
                    AcquireAll(line.GetCurrentCostBundle(i).ToList());
                    Assert.IsTrue(_w.AssemblyLineBoard.Claim(i), _w.AssemblyLineBoard.Status);
                    return;
                }
            }
            // Not on show: paid at its full price, as the line would ask once it came round.
            DraftableCardDefinition card = _w.Definitions.Decks[_w.Setup.assemblyLine.deck].Cards.First(c => c.appendage?.recipe == recipe);
            AcquireAll(card.claimCost);
            foreach (RecipeIngredient c in card.claimCost)
            {
                Assert.IsTrue(Pack.TryWithdraw(c.itemType, c.quantity));
            }
            line.GrantClaim(user, card);
        }

        /// <summary>
        /// Until the pack holds every line of <paramref name="cost"/> at once: earning one good
        /// can spend another (a line built for Coke costs Iron Plate), so it goes round again.
        /// </summary>
        public void AcquireAll(IEnumerable<RecipeIngredient> cost)
        {
            List<(string good, int sum)> lines = cost.GroupBy(c => c.itemType)
                .Select(g => (g.Key, g.Sum(c => c.quantity)))
                .OrderBy(l => Array.IndexOf(AcquireOrder, l.Key)).ToList();
            for (int round = 0; round < 10; round++)
            {
                if (lines.All(l => Pack.GetQuantity(l.good) >= l.sum))
                {
                    return;
                }
                foreach ((string good, int sum) in lines)
                {
                    Acquire(good, sum);
                }
            }
            Assert.Fail("never held " + string.Join(" + ", lines.Select(l => $"{l.sum} {l.good}")) + " at once");
        }

        private void PlacePaid(string key, Vector2Int cell)
        {
            bool Standing() => _w.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == cell && b.PrefabKey == key);
            PlaceableBuilding prefab = _w.Placeables.First(p => p.Key == key).Prefab;
            if (!Standing())
            {
                AcquireAll(prefab.Cost);
            }
            // Checked again: a line built while this one's price was earned may share its steam
            // run, and a click on a standing building takes it up.
            if (Standing())
            {
                return;
            }
            Assert.IsFalse(_w.Grid.IsOccupied(cell), $"{key} on {cell}: something else stands there");
            _w.Build.SetActivePrefab(prefab);
            _w.Build.PlaceOrRemove(cell);
            string status = _w.Build.LastStatusMessage;
            _w.Build.CancelPlacement();
            Assert.IsTrue(Standing(), $"{key} on {cell}: {status}");
        }

        private GolemEntity BuildPaid(ChassisDefinition chassis)
        {
            // Earning the price can build lines that take the free bays: round again until both hold.
            for (int round = 0; round < 5; round++)
            {
                AcquireAll(chassis.cost);
                if (_w.AssemblyBay.HasFreeSlot)
                {
                    break;
                }
                AcquireAll(_w.AssemblyBay.UpgradeCost);
                Assert.IsTrue(_w.AssemblyLineBoard.UpgradeBays(), _w.AssemblyLineBoard.Status);
            }
            Assert.IsTrue(_w.StarterStation.TryConstructGolem(chassis, out GolemEntity golem),
                $"built a {chassis.name}: {_w.StarterStation.LastRefusalReason}");
            return golem;
        }

        private GolemEntity GolemOn(Vector2Int cell) => _w.Golems.FirstOrDefault(g => !g.IsRemoved && g.Cell == cell);

        /// <summary>
        /// Where the next line goes, and the steam it needs. Lines stand in a block under the
        /// workshop's north wall, away from the street the guide builds along: two rows of lines,
        /// golems facing east between their crates, with a run of Steam Pipe between the rows that
        /// steams every golem beside it, and a boiler for every four golems on the run (a boiler
        /// powers eight). A slot something else stands on is skipped; with the block full, any
        /// free row of three tiles with a boiler beside its golem.
        /// </summary>
        private (Vector2Int a, Vector2Int g, Vector2Int b, List<(string key, Vector2Int cell)> steam) FindStrip()
        {
            var reserved = new HashSet<Vector2Int>();
            foreach (TutorialStep step in _w.Tutorial.Steps)
            {
                reserved.UnionWith(step.Spots);
            }
            foreach (GolemEntity golem in _w.Golems.Where(g => !g.IsRemoved))
            {
                reserved.Add(golem.Cell);
                reserved.Add(golem.SourceCell);
                reserved.Add(golem.TargetCell);
            }
            foreach (RefLine line in _lines.Values.SelectMany(l => l).Concat(_building))
            {
                reserved.Add(line.In);
                reserved.Add(line.Golem);
                reserved.Add(line.Out);
            }
            int half = FloorLayout.HalfExtent;
            bool Free(Vector2Int c) =>
                FloorLayout.IsInsideWorkshop(c, half, _w.Bounds.NorthExtent) && c.y < _w.Bounds.NorthExtent
                && !reserved.Contains(c) && !_w.Grid.IsOccupied(c) && _w.Build.IsCellBuildable(c) && !_w.Build.TryFindGolemAt(c, out _);
            bool Mine(Vector2Int c, string key) =>
                _w.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == c && b.PrefabKey == key);

            int pipeRow = _w.Bounds.NorthExtent - 2;
            for (int column = 0; -half + 1 + 3 * column + 2 < half; column++)
            {
                int x0 = -half + 1 + 3 * column;
                var run = new List<(string key, Vector2Int cell)>
                {
                    (column % 2 == 0 ? "BoilerPrefab" : "SteamPipePrefab", new Vector2Int(x0, pipeRow)),
                    ("SteamPipePrefab", new Vector2Int(x0 + 1, pipeRow)),
                    ("SteamPipePrefab", new Vector2Int(x0 + 2, pipeRow)),
                };
                if (!run.All(r => Mine(r.cell, r.key) || Free(r.cell)))
                {
                    continue;
                }
                foreach (int row in new[] { pipeRow + 1, pipeRow - 1 })
                {
                    var a = new Vector2Int(x0, row);
                    var g = new Vector2Int(x0 + 1, row);
                    var b = new Vector2Int(x0 + 2, row);
                    if (Free(a) && Free(g) && Free(b))
                    {
                        return (a, g, b, run.Where(r => !Mine(r.cell, r.key)).ToList());
                    }
                }
            }

            long tick = _w.Clock.CurrentTick;
            for (int y = _w.Bounds.NorthExtent - 1; y > -half; y--)
            {
                for (int x = -half + 1; x + 2 < half; x++)
                {
                    var a = new Vector2Int(x, y);
                    var g = new Vector2Int(x + 1, y);
                    var b = new Vector2Int(x + 2, y);
                    if (!Free(a) || !Free(g) || !Free(b))
                    {
                        continue;
                    }
                    // Its own boiler, even beside someone's pipe: that network's boilers may be full.
                    foreach (Vector2Int boiler in new[] { new Vector2Int(x + 1, y - 1), new Vector2Int(x + 1, y + 1) })
                    {
                        if (Free(boiler))
                        {
                            return (a, g, b, new List<(string, Vector2Int)> { ("BoilerPrefab", boiler) });
                        }
                    }
                }
            }
            Assert.Fail("no room on the floor for a reference line");
            return default;
        }

        // --- Time -------------------------------------------------------------------------------

        public void WaitToLeave(string stepId)
        {
            float started = Now;
            Step();
            while (_w.Tutorial.Current.Id == stepId)
            {
                Assert.Less(Now - started, _timeout, $"stuck on '{stepId}': " + GolemStates());
                Step();
            }
        }

        private string GolemStates() => string.Join("; ",
            _w.Golems.Where(g => !g.IsRemoved).Select(g => $"{g.GolemId} {g.Program.chassis?.name} {g.Program.State} {g.StallReason} {g.StallResourceId}"));

        public void Until(Func<bool> done, string what)
        {
            float started = Now;
            while (!done())
            {
                Assert.Less(Now - started, _timeout, what + ": " + GolemStates());
                Step();
            }
        }

        private bool _inChore;

        /// <summary>
        /// A boiler is topped up at this much Coke, with <see cref="RefuelWith"/>: chapters 1-4
        /// as measured before Phase 3 (1 and 5); later, with lines on the floor, sooner and more,
        /// so a Coke line steamed by that boiler never waits on its own fuel.
        /// </summary>
        private int RefuelBelow => LinesAllowed ? 5 : 1;

        private int RefuelWith => _lines.TryGetValue("R1_Coking", out List<RefLine> coke) && coke.Count > 0 ? 20 : 5;

        private readonly Stack<string> _doing = new Stack<string>();

        /// <summary>Game seconds by what the player was doing (the innermost activity): where a floor goes.</summary>
        public Dictionary<string, float> Spent { get; } = new Dictionary<string, float>();

        private IDisposable Doing(string what)
        {
            _doing.Push(what);
            return new Popper(_doing);
        }

        private sealed class Popper : IDisposable
        {
            private readonly Stack<string> _stack;
            public Popper(Stack<string> stack) => _stack = stack;
            public void Dispose() => _stack.Pop();
        }

        /// <summary>
        /// One frame. Before it, the chores any attentive player does whatever the step: top up a
        /// boiler about to run dry while golems stand on the floor (with Coke they earn), and
        /// reorder a free Scrap stall that has emptied.
        /// </summary>
        public void Step()
        {
            if (!_inChore)
            {
                _inChore = true;
                if (_w.Golems.Any(g => !g.IsRemoved && g.HasRunnableProgram))
                {
                    foreach (PlaceableBoiler boiler in Boilers().Where(b => b.Boiler.CokeStock <= RefuelBelow).ToList())
                    {
                        HandCrankBench bench = _w.StarterBench;
                        bool wasCranking = bench.IsCranking;
                        bench.IsCranking = false;
                        Acquire(ItemType.Coke, RefuelWith);
                        _w.Interactor.TryRefuelBoiler(boiler);
                        bench.IsCranking = wasCranking;
                    }
                }
                foreach (ResourceNodeMarker empty in _w.Markers.Where(m => m.ItemType == ItemType.Scrap && m.IsDepleted && !_w.Market.IsInTransit(m.NodeId)))
                {
                    _w.Interactor.TryOrderTruckload(empty);
                }
                _inChore = false;
            }
            string doing = _doing.Count > 0 ? _doing.Peek() : "waiting for the step";
            Spent[doing] = (Spent.TryGetValue(doing, out float t) ? t : 0f) + Dt;
            HandCrankBench b = _w.StarterBench;
            if (b.IsCranking && b.CanAffordSelected())
            {
                string good = b.SelectedRecipe.outputItemType;
                Cranked[good] = (Cranked.TryGetValue(good, out float s) ? s : 0f) + Dt;
            }
            _w.Advance(Dt);
        }

        private IEnumerable<PlaceableBoiler> Boilers() => _w.Build.Buildings
            .Where(b => !b.IsRemoved)
            .Select(b => b.GetPart<PlaceableBoiler>())
            .Where(b => b?.Boiler != null);

        private ResourceNodeMarker Marker(string nodeId) => _w.Markers.First(m => m.NodeId == nodeId);

        public static string Clock(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    }
}
