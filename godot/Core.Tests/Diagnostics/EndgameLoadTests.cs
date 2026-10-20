using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Diagnostics;
using GolemFactory.Events;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The endgame profile's load (docs/performance-report-template.md §2, §5.1): what the `perf`
    // scenario builds, checked here so the scenario's numbers are about the factory they claim.
    public class EndgameLoadTests
    {
        internal static SandboxWorld Compose() =>
            SandboxWorld.Compose(AuthoredData.Load(), SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(AuthoredData.Load()));

        private const float Frame = 1f / 60f;

        [Test]
        public void TheLoad_IsWhatItSays()
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world);

            Assert.AreEqual(96, load.Golems.Count);
            Assert.AreEqual(12, load.Boilers.Count);
            Assert.AreEqual(500, load.BeltItems);
            // §2's mix, role by role.
            var mix = new (string role, int count)[]
            {
                (EndgameLoad.Extractor, 8), (EndgameLoad.Coker, 23), (EndgameLoad.Loader, 11),
                (EndgameLoad.Smelter, 12), (EndgameLoad.BrassAndWire, 4), (EndgameLoad.Component, 12),
                (EndgameLoad.TierFourFive, 8), (EndgameLoad.Carrier, 15), (EndgameLoad.Zeppelin, 3),
            };
            foreach ((string role, int count) in mix)
            {
                Assert.AreEqual(count, load.ByRole[role].Count, role);
            }

            world.Advance(0.1f);
            Assert.AreEqual(96, load.Powered, "every golem powered after one tick");
            Assert.IsFalse(load.Golems.Any(g => g.StallReason == StallReason.NoSteam));

            for (int i = 0; i < 600; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
            Assert.GreaterOrEqual(load.BeltItems, 500, "the loops keep every item");
            Assert.Less(load.Golems.Count(g => g.Program.State == GolemState.Stalled && g.Mood == GolemMood.Stalled), 10, "flowing, red: " + Stalls(load));
            TestContext.WriteLine($"flowing: {load.Stalled} stalled");

            load.Starve();
            for (int i = 0; i < 300; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
            Assert.GreaterOrEqual(load.Stalled, 60, Stalls(load));
            Assert.Greater(load.Golems.Count(g => g.Mood == GolemMood.Waiting), 30, "a starved line waits grey where its feeder pushes its good");
            Assert.AreEqual(96, load.Powered, "starved, not for steam");
            TestContext.WriteLine($"starved: {load.Stalled} stalled, {load.Golems.Count(g => g.Mood == GolemMood.Waiting)} grey, " +
                                  $"{load.Golems.Count(g => g.Mood == GolemMood.Stalled)} red");

            load.Refill();
            for (int i = 0; i < 300; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
            Assert.Less(load.Golems.Count(g => g.Program.State == GolemState.Stalled && g.Mood == GolemMood.Stalled), 10,
                "refilled after starving, the lines run again: " + Stalls(load));
        }

        /// <summary>
        /// §5.2 step 2 changes no verdict: on the starved endgame, the world's probe (indexed, one
        /// read per golem per frame) answers every golem exactly as a bare probe does, and the
        /// hover's feeders agree too.
        /// </summary>
        [Test]
        public void TheFramedProbe_AnswersAsABareOne()
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world);
            load.Starve();
            for (int i = 0; i < 200; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
            var bare = new StallFactsProbe(() => world.Golems, world.Endpoints, world.Belts, () => world.Market);
            int fed = 0;
            foreach (GolemEntity golem in world.Golems)
            {
                StallFacts framed = world.StallFacts.Read(golem);
                StallFacts fresh = bare.Read(golem);
                Assert.AreEqual(fresh.SourceGives, framed.SourceGives, golem.GolemId);
                Assert.AreEqual(fresh.SourceFed, framed.SourceFed, golem.GolemId);
                Assert.AreEqual(fresh.TargetDrained, framed.TargetDrained, golem.GolemId);
                Assert.AreEqual(fresh.WantedGood, framed.WantedGood, golem.GolemId);
                CollectionAssert.AreEqual(bare.FeedersOf(golem), world.StallFacts.FeedersOf(golem), golem.GolemId);
                fed += fresh.SourceFed ? 1 : 0;
            }
            Assert.Greater(fed, 20, "the starved load has fed golems to compare");
        }

        [Test]
        public void TheFramedProbe_KeepsAFramesFacts_UntilTheWorldMovesOn()
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world);
            load.Starve();
            for (int i = 0; i < 200; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
            GolemEntity waiting = load.Golems.First(g => g.Mood == GolemMood.Waiting);
            GolemEntity feeder = world.StallFacts.FeedersOf(waiting).First();
            foreach (GolemEntity other in world.StallFacts.FeedersOf(waiting, 10))
            {
                other.SetHeld(true);
            }
            Assert.AreEqual(GolemMood.Waiting, waiting.Mood, "within a frame the facts are kept");
            world.InvalidateStallFacts();
            Assert.AreEqual(GolemMood.Stalled, waiting.Mood, "a carried feeder feeds nothing, from the next frame");
            Assert.IsTrue(feeder.IsHeld);
        }

        private static string Stalls(EndgameLoadResult load) => string.Join("; ", load.Golems
            .Where(g => g.Program.State == GolemState.Stalled)
            .Select(g => $"{g.GolemId}@{g.Cell} {g.StallReason} {g.StallResourceId}"));

        /// <summary>
        /// §5.1's coarse alarm: 1,200 frames at 1/60 s, each reading every golem's mood once and
        /// reconciling the alerts strip once (what the nodes do), flowing and starved. Prints the
        /// sections' p50, p95 and max.
        /// </summary>
        [Test, Explicit, Category("Perf")]
        public void TheLoad_TicksAndReadsWithinBudget()
        {
            foreach (bool starved in new[] { false, true })
            {
                SandboxWorld world = Compose();
                EndgameLoadResult load = EndgameLoad.Build(world);
                var perf = new PerfSampler();
                world.Perf = perf;
                if (starved)
                {
                    load.Starve();
                }
                for (int i = 0; i < 300; i++)
                {
                    Step(world, load);
                }
                perf.Reset();
                var frames = new FrameTimeStats();
                for (int i = 0; i < 1200; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    perf.BeginFrame();
                    world.Advance(Frame);
                    foreach (GolemEntity golem in load.Golems)
                    {
                        _ = golem.Mood;
                    }
                    world.Alerts.Reconcile();
                    perf.EndFrame();
                    frames.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                    load.Maintain();
                }
                string phase = starved ? "starved" : "flowing";
                TestContext.WriteLine($"[{phase}] tick+presentation p50={frames.P50:F3} p95={frames.P95:F3} max={frames.Max:F3} ms; stalled={load.Stalled}");
                foreach (PerfSampler.Section s in perf.Sections)
                {
                    TestContext.WriteLine($"  {s.Name,-12} calls/frame p95={s.FrameCalls.P95,6:F0}  ms/frame p50={s.FrameMs.P50:F3} p95={s.FrameMs.P95:F3} max={s.FrameMs.Max:F3}" +
                                          (s.CallMs != null ? $"  per call p50={s.CallMs.P50:F3} p95={s.CallMs.P95:F3} max={s.CallMs.Max:F3}" : ""));
                }
                Assert.LessOrEqual(frames.P95, 6.0, phase);
            }
        }

        /// <summary>
        /// tuning-data-spec.md §18 M2, the two numbers left for the endgame load: the coal line's
        /// share of a stage 4 factory, and the golems stage 4's Aether Conduit line costs.
        /// Measured twice: the load's own Coke burn while flowing, and a line count from the
        /// recipes at the guide's program shape (Haul each input, Assemble, Push; §2's step
        /// times), at 3 Coke a minute a working golem.
        /// </summary>
        [Test, Explicit, Category("Perf")]
        public void StageFour_CoalShare_AndTheConduitLine()
        {
            // The load, flowing: Coke burned a minute, from the boilers' stock between refills.
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world);
            for (int i = 0; i < 300; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
            long burned = 0;
            int working = 0;
            const int ticks = 1200;
            for (int i = 0; i < ticks; i++)
            {
                int before = load.Boilers.Sum(b => b.Boiler.CokeStock);
                world.Advance(0.1f);
                burned += Math.Max(0, before - load.Boilers.Sum(b => b.Boiler.CokeStock));
                working += world.Steam.LastEvaluatedWorkingCount;
                load.Maintain();
            }
            double minutes = ticks / 600.0;
            double burnPerMinute = burned / minutes;
            TestContext.WriteLine($"load, flowing: {burnPerMinute:F0} Coke/min burned by {working / (double)ticks:F1} working golems of 96 " +
                                  $"({burnPerMinute / 27.3:F1} guide cokers' output)");

            // The line count.
            var recipes = AuthoredData.Load().Recipes.Values.ToList();
            var stage4 = new (string, double)[] { ("ChronometerCore", 2), ("GreatCog", 2), ("AetherConduit", 1) };
            var full = Lines(recipes, stage4);
            var trimmed = Lines(recipes, stage4.Where(d => d.Item1 != "AetherConduit").ToArray());
            double total = full.Values.Sum();
            double coal = full.Where(l => l.Key == "R1_Coking" || l.Key == "raw Coal").Sum(l => l.Value);
            TestContext.WriteLine($"stage 4 at full demand: {total:F1} golem-equivalents; coal line (cokers + Coal loaders) {coal:F1} = {coal / total:P0}");
            foreach (var line in full.OrderByDescending(l => l.Value))
            {
                TestContext.WriteLine($"  {line.Key,-28} {line.Value,6:F2}" + (trimmed.TryGetValue(line.Key, out double t) ? $"  trimmed {t,6:F2}" : "  trimmed   0.00"));
            }
            TestContext.WriteLine($"without the Conduit line: {trimmed.Values.Sum():F1}; the Conduit line costs {total - trimmed.Values.Sum():F1} golem-equivalents " +
                                  $"({full.Count(l => l.Value > 0.01) - trimmed.Count(l => l.Value > 0.01)} lines fewer)");
        }

        /// <summary>
        /// Golem-equivalents per line to meet <paramref name="demands"/> (a minute), raw goods
        /// hauled by loaders (Haul 4, Push: 24 a minute), R4 run for the larger of its Plate and
        /// its Slag, and every golem's 3 Coke a minute added until it settles.
        /// </summary>
        private static Dictionary<string, double> Lines(List<GolemFactory.PunchCards.RecipeDefinition> recipes, (string good, double rate)[] demands)
        {
            var byOutput = new Dictionary<string, GolemFactory.PunchCards.RecipeDefinition>();
            foreach (var r in recipes)
            {
                byOutput[r.outputItemType] = r;
            }
            Dictionary<string, double> result = null;
            double steam = 0;
            for (int round = 0; round < 30; round++)
            {
                var need = demands.ToDictionary(d => d.good, d => d.rate);
                need[GolemFactory.Economy.ItemType.Coke] = (need.TryGetValue(GolemFactory.Economy.ItemType.Coke, out double c) ? c : 0) + steam;
                result = new Dictionary<string, double>();
                // Highest tier first, so every consumer has added its demand before a good is made.
                foreach (string good in GolemFactory.Economy.ItemTiers.CanonicalOrder.Reverse())
                {
                    if (!need.TryGetValue(good, out double rate) || rate <= 0)
                    {
                        continue;
                    }
                    if (good == GolemFactory.Economy.ItemType.Slag || good == GolemFactory.Economy.ItemType.IronPlate)
                    {
                        continue; // R4, below, once both are known
                    }
                    if (!byOutput.TryGetValue(good, out var recipe))
                    {
                        result["raw " + good] = rate / 24.0;
                        continue;
                    }
                    Run(recipe, rate / recipe.outputQuantity, need, result);
                    if (good == GolemFactory.Economy.ItemType.Glass)
                    {
                        // Glass's Slag is now known: smelt for the Plate and the Slag together.
                        var r4 = recipes.First(r => r.name == "R4_IronSmelting");
                        double plate = need.TryGetValue(GolemFactory.Economy.ItemType.IronPlate, out double p) ? p : 0;
                        double slag = need.TryGetValue(GolemFactory.Economy.ItemType.Slag, out double s) ? s : 0;
                        Run(r4, Math.Max(plate / r4.outputQuantity, slag / r4.byproductQuantity), need, result);
                        need[GolemFactory.Economy.ItemType.IronPlate] = 0;
                    }
                }
                double next = result.Values.Sum() * 3.0;
                if (Math.Abs(next - steam) < 0.01)
                {
                    break;
                }
                steam = next;
            }
            return result;
        }

        private static void Run(GolemFactory.PunchCards.RecipeDefinition recipe, double runsPerMinute,
            Dictionary<string, double> need, Dictionary<string, double> lines)
        {
            int cycle = recipe.inputs.Sum(i => StepDurationRules.Haul(i.quantity)) + recipe.durationTicks +
                        2 + recipe.outputQuantity + (string.IsNullOrEmpty(recipe.byproductItemType) ? 0 : recipe.byproductQuantity);
            lines[recipe.name] = (lines.TryGetValue(recipe.name, out double had) ? had : 0) + runsPerMinute * cycle / 600.0;
            foreach (var input in recipe.inputs)
            {
                need[input.itemType] = (need.TryGetValue(input.itemType, out double n) ? n : 0) + runsPerMinute * input.quantity;
            }
        }

        private static void Step(SandboxWorld world, EndgameLoadResult load)
        {
            world.Advance(Frame);
            load.Maintain();
        }
    }
}
