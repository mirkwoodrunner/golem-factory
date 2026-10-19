using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
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
    /// Phase 2's "Done when" (docs/roadmap.md): a new player finishes chapters 1-4 without the
    /// kit in under 15 minutes. Only a person can measure that. This measures the part no text
    /// can shorten: the GAME TIME chapters 1-4 take when a player does everything instantly
    /// (no walking, no reading, no hesitation) but must still earn every good with the player's
    /// own verbs, at their real rates, on the world's own clock:
    ///
    /// - Scrap and Coal are harvested at the stalls at [E]'s hold rate (one, then after 0.35 s
    ///   one every 0.15 s), taken from crates the golems fill (free), and an empty stall waits
    ///   for its truckload (Scrap's free, Coal's for 10 Scrap);
    /// - Coke, Iron Plate and Gears are cranked at the bench (HandCrankRules.CrankTicks), Iron
    ///   Plate taken first from any crate a Presser has filled;
    /// - placing, building, claiming, programming and turning are the kit's own actions
    ///   (TutorialGuide.Kit.cs), instant, but paid from goods earned this way, never granted;
    /// - work steps wait for the golems' real cycles, and a boiler that runs dry is refuelled
    ///   with cranked Coke.
    ///
    /// The result is a floor, not a forecast: a real player adds reading, walking and thinking.
    /// The guide-chapters spec estimated about 5 1/4 minutes of cranking in its 15-minute budget;
    /// this measures 7:02, nearly all of it Iron Plate and Gears for the two Pressers.
    /// </summary>
    public class ChaptersOneToFourFloorTests
    {
        private const float Dt = 1f / 30f;
        private const float StepTimeout = 600f;

        private SandboxWorld _w;
        private Dictionary<string, Action> _kit;
        private readonly Dictionary<string, float> _cranked = new Dictionary<string, float>();

        private static readonly string[] Goods =
            { ItemType.Scrap, ItemType.Coal, ItemType.Coke, ItemType.IronPlate, ItemType.Gear };

        [Test]
        public void ChaptersOneToFour_PlayedInstantly_FitWellInsideFifteenMinutesOfGameTime()
        {
            DefinitionSet definitions = AuthoredData.Load();
            _w = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            _w.Clock.Play();
            _kit = (Dictionary<string, Action>)typeof(TutorialGuide)
                .GetProperty("Performs", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_w.Tutorial);

            var chapterStart = new SortedDictionary<int, float>();
            var stepTimes = new List<string>();
            while (_w.Tutorial.Current.Id != PlaytestSession.ChapterFiveStepId)
            {
                TutorialStep step = _w.Tutorial.Current;
                int chapter = _w.Tutorial.ChapterOf(_w.Tutorial.Index);
                Assert.LessOrEqual(chapter, 4, "the guide left chapter 4 without passing r4-card");
                if (!chapterStart.ContainsKey(chapter))
                {
                    chapterStart[chapter] = _w.Tutorial.Now;
                }
                float entered = _w.Tutorial.Now;
                Perform(step.Id);
                WaitToLeave(step.Id);
                stepTimes.Add($"{step.Id} {_w.Tutorial.Now - entered:0.0}s");
            }
            float end = _w.Tutorial.Now;

            for (int c = 1; c <= 4; c++)
            {
                float to = c < 4 ? chapterStart[c + 1] : end;
                TestContext.Out.WriteLine($"chapter {c}: {Clock(to - chapterStart[c])}");
            }
            TestContext.Out.WriteLine($"chapters 1-4: {Clock(end)} of game time; cranking {Clock(_cranked.Values.Sum())} "
                + $"({string.Join(", ", _cranked.Select(kv => $"{kv.Key} {kv.Value:0}s"))})");
            TestContext.Out.WriteLine(string.Join(" · ", stepTimes));

            string line = _w.Tutorial.Playtest.ChaptersOneToFour();
            TestContext.Out.WriteLine(line);
            StringAssert.EndsWith("kit used: no", line, "nothing here is the kit's fast-forward");

            // Measured 2026-10-19: 7:58 (chapters 1:20, 4:15, 2:16, 0:06), 7:02 of it at the
            // bench. A generous ceiling over that, so a retune that adds minutes of unavoidable
            // grind to chapters 1-4 fails here before a playtest finds it.
            Assert.Less(end, 10 * 60f, "the unavoidable game time of chapters 1-4 crowds out the 15-minute target");
        }

        // --- The player's verbs, instantly -------------------------------------------------

        private void Perform(string stepId)
        {
            switch (stepId)
            {
                case "scrap": Acquire(ItemType.Scrap, 20); return;
                case "coal": Acquire(ItemType.Coal, 5); return;
                case "coke": Acquire(ItemType.Coke, 5); return;
                case "iron": Acquire(ItemType.IronPlate, 10); return;
                case "gears": Acquire(ItemType.Gear, 10); return;
                case "fuel":
                    Acquire(ItemType.Coke, 1);
                    Assert.IsTrue(_w.Interactor.TryRefuelBoiler(Boilers().First()), _w.Interactor.LastStatusMessage);
                    return;
                case "coal-order":
                    Acquire(ItemType.Scrap, 10);
                    Assert.IsTrue(_w.Interactor.TryOrderTruckload(Marker("CoalNode")), _w.Interactor.LastStatusMessage);
                    return;
                case "collect":
                    ICrate depot = _w.Build.Buildings.First(b => !b.IsRemoved && b.Cell == _w.Tutorial.DepotSpot).GetPart<ICrate>();
                    Until(() => depot.Buffer.GetQuantity(ItemType.Scrap) > 0, "the first crate fills");
                    Assert.IsTrue(_w.Interactor.TryCollect(depot), _w.Interactor.LastStatusMessage);
                    return;
            }

            // Everything else is the kit's own action, paid for with goods earned above.
            foreach (RecipeIngredient need in Needs(stepId).OrderBy(n => Array.IndexOf(AcquireOrder, n.itemType)))
            {
                Acquire(need.itemType, need.quantity);
            }
            Dictionary<string, int> before = Goods.ToDictionary(g => g, g => Pack.TotalDeposited(g));
            _kit[stepId]();
            foreach (string good in Goods)
            {
                int granted = Pack.TotalDeposited(good) - before[good];
                if (granted > 0)
                {
                    // The kit granted what it spent; the player pays it back from their own goods.
                    Acquire(good, granted);
                    Assert.IsTrue(Pack.TryWithdraw(good, granted), $"{stepId}: repay {granted} {good}");
                }
            }
        }

        private static readonly string[] AcquireOrder =
            { ItemType.Gear, ItemType.Coke, ItemType.IronPlate, ItemType.Coal, ItemType.Scrap };

        /// <summary>What a step's kit action spends, so the player earns it before, not after.</summary>
        private IEnumerable<RecipeIngredient> Needs(string stepId)
        {
            switch (stepId)
            {
                case "boiler":
                case "boiler2": return Cost("BoilerPrefab", 1);
                case "depot":
                case "depot2":
                case "depot3": return Cost("DepotPrefab", 1);
                case "pipe": return Cost("SteamPipePrefab", _w.Tutorial.PipeSpots.Length);
                case "pipes2": return Cost("SteamPipePrefab", _w.Tutorial.Pipe2Spots.Length);
                case "golem":
                case "scav2": return _w.Definitions.Chassis["ClockworkScavenger"].cost;
                case "presser":
                case "presser2": return _w.Definitions.Chassis["BrassPresser"].cost;
                case "claim": return CardCost("AssembleScrapReclamation");
                case "coking-card": return CardCost("AssembleCoking");
                default: return Enumerable.Empty<RecipeIngredient>();
            }
        }

        private IEnumerable<RecipeIngredient> Cost(string key, int count) =>
            _w.Placeables.First(p => p.Key == key).Prefab.Cost
                .Select(c => new RecipeIngredient(c.itemType, c.quantity * count));

        private IEnumerable<RecipeIngredient> CardCost(string appendage)
        {
            var line = _w.AssemblyLine;
            for (int i = 0; i < line.SlotCount; i++)
            {
                if (line.GetCard(i)?.appendage?.name == appendage)
                {
                    return line.GetCurrentCostBundle(i).ToList();
                }
            }
            return Enumerable.Empty<RecipeIngredient>();
        }

        /// <summary>Until the pack holds <paramref name="quantity"/> of <paramref name="good"/>.</summary>
        private void Acquire(string good, int quantity)
        {
            if (Pack.GetQuantity(good) >= quantity)
            {
                return;
            }
            TakeFromCrates(good, quantity);
            if (good == ItemType.Scrap || good == ItemType.Coal)
            {
                Harvest(good, quantity);
            }
            else
            {
                Crank(good, quantity);
            }
        }

        /// <summary>
        /// Takes from crates golems have filled, but never from a crate a golem works from: an
        /// instant player keeps the Presser fed and harvests the stall instead.
        /// </summary>
        private void TakeFromCrates(string good, int quantity)
        {
            var sources = new HashSet<Compat.Vector2Int>(_w.Golems.Where(g => !g.IsRemoved).Select(g => g.SourceCell));
            foreach (ICrate crate in _w.Build.Buildings
                .Where(b => !b.IsRemoved && !sources.Contains(b.Cell))
                .Select(b => b.GetPart<ICrate>())
                .Where(c => c?.Buffer != null && c.OwnsBuffer))
            {
                int want = quantity - Pack.GetQuantity(good);
                if (want <= 0)
                {
                    return;
                }
                if (crate.Buffer.GetQuantity(good) > 0)
                {
                    CrateTransfer.Take(crate, Pack, good, want);
                }
            }
        }

        private void Harvest(string good, int quantity)
        {
            float held = 0f, next = 0f;
            ResourceNodeMarker at = null;
            float started = _w.Tutorial.Now;
            while (Pack.GetQuantity(good) < quantity)
            {
                Assert.Less(_w.Tutorial.Now - started, StepTimeout, $"harvesting {quantity} {good}");
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
                        // One paid cart at a time, 10 Scrap each.
                        Acquire(ItemType.Scrap, 10);
                        Assert.IsTrue(_w.Interactor.TryOrderTruckload(Marker("CoalNode")), _w.Interactor.LastStatusMessage);
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
            HandCrankBench bench = _w.StarterBench;
            RecipeDefinition recipe = bench.CrankableRecipes.First(r => r.outputItemType == good);
            int crafts = (quantity - Pack.GetQuantity(good) + recipe.outputQuantity - 1) / recipe.outputQuantity;
            foreach (RecipeIngredient input in recipe.inputs)
            {
                Acquire(input.itemType, input.quantity * crafts);
            }
            // Collecting what a Presser makes while cranking: the bench and the line together.
            // The dial is re-set each frame, since a chore (Coke for a boiler) may turn it.
            Until(() =>
            {
                TakeFromCrates(good, quantity);
                if (Pack.GetQuantity(good) >= quantity)
                {
                    return true;
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

        private void WaitToLeave(string stepId)
        {
            float started = _w.Tutorial.Now;
            Step();
            while (_w.Tutorial.Current.Id == stepId)
            {
                Assert.Less(_w.Tutorial.Now - started, StepTimeout, $"stuck on '{stepId}': " + string.Join("; ",
                    _w.Golems.Where(g => !g.IsRemoved).Select(g => $"{g.GolemId} {g.Program.State} {g.StallReason} {g.StallResourceId}")));
                Step();
            }
        }

        private void Until(Func<bool> done, string what)
        {
            float started = _w.Tutorial.Now;
            while (!done())
            {
                Assert.Less(_w.Tutorial.Now - started, StepTimeout, what);
                Step();
            }
        }

        private bool _inChore;

        /// <summary>
        /// One frame. Before it, the chores any attentive player does whatever the step: top up a
        /// boiler about to run dry while golems stand on the floor (with Coke they crank), and
        /// reorder a free Scrap stall that has emptied.
        /// </summary>
        private void Step()
        {
            if (!_inChore)
            {
                _inChore = true;
                if (_w.Golems.Any(g => !g.IsRemoved && g.HasRunnableProgram))
                {
                    foreach (PlaceableBoiler boiler in Boilers().Where(b => b.Boiler.CokeStock <= 1).ToList())
                    {
                        HandCrankBench bench = _w.StarterBench;
                        bool wasCranking = bench.IsCranking;
                        bench.IsCranking = false;
                        Acquire(ItemType.Coke, 5);
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
            HandCrankBench b = _w.StarterBench;
            if (b.IsCranking && b.CanAffordSelected())
            {
                string good = b.SelectedRecipe.outputItemType;
                _cranked[good] = (_cranked.TryGetValue(good, out float s) ? s : 0f) + Dt;
            }
            _w.Advance(Dt);
        }

        private StorageBuffer Pack => _w.Wallet.Pack;

        private IEnumerable<PlaceableBoiler> Boilers() => _w.Build.Buildings
            .Where(b => !b.IsRemoved)
            .Select(b => b.GetPart<PlaceableBoiler>())
            .Where(b => b?.Boiler != null);

        private ResourceNodeMarker Marker(string nodeId) => _w.Markers.First(m => m.NodeId == nodeId);

        private static string Clock(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    }
}
