using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    // The endgame profile's load (docs/performance-report-template.md §2, §5.1): what the `perf`
    // scenario builds, checked here so the scenario's numbers are about the factory they claim.
    public class EndgameLoadTests
    {
        private const float Frame = 1f / 60f;

        /// <summary><paramref name="ticks"/> ticks of 0.1 s, the load maintained after each, as a frame would.</summary>
        private static void Advance(SandboxWorld world, EndgameLoadResult load, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Advance(0.1f);
                load.Maintain();
            }
        }

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

            Advance(world, load, 600);
            Assert.GreaterOrEqual(load.BeltItems, 500, "the loops keep every item");
            Assert.Less(load.Golems.Count(g => g.Program.State == GolemState.Stalled && g.Mood == GolemMood.Stalled), 10, "flowing, red: " + Stalls(load));
            TestContext.WriteLine($"flowing: {load.Stalled} stalled");

            load.Starve();
            Advance(world, load, 300);
            Assert.GreaterOrEqual(load.Stalled, 60, Stalls(load));
            Assert.Greater(load.Golems.Count(g => g.Mood == GolemMood.Waiting), 30, "a starved line waits grey where its feeder pushes its good");
            Assert.AreEqual(96, load.Powered, "starved, not for steam");
            TestContext.WriteLine($"starved: {load.Stalled} stalled, {load.Golems.Count(g => g.Mood == GolemMood.Waiting)} grey, " +
                                  $"{load.Golems.Count(g => g.Mood == GolemMood.Stalled)} red");

            load.Refill();
            Advance(world, load, 300);
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
            Advance(world, load, 200);
            var bare = new StallFactsProbe(() => world.Golems, world.Endpoints, world.Belts, id => world.Supply.Refills(id));
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

        /// <summary>
        /// The indices last a frame, but a change between two reads in one frame is seen at once:
        /// the moods scenario writes a program and reads the crate's pusher in the same step.
        /// </summary>
        [Test]
        public void TheFramedProbe_SeesAChangeWithinAFrame()
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world);
            load.Starve();
            Advance(world, load, 200);
            GolemEntity waiting = load.Golems.First(g => g.Mood == GolemMood.Waiting);
            var feeders = world.StallFacts.FeedersOf(waiting, 10).ToList();
            foreach (GolemEntity other in feeders)
            {
                other.SetHeld(true);
            }
            Assert.AreEqual(GolemMood.Stalled, waiting.Mood, "a carried feeder feeds nothing, in the same frame");
            foreach (GolemEntity other in feeders)
            {
                other.SetHeld(false);
            }
            Assert.AreEqual(GolemMood.Waiting, waiting.Mood, "set down, it feeds again");

            GolemEntity feeder = feeders[0];
            List<AppendageActionDefinition> program = feeder.Program.appendages.ToList();
            feeder.Program.appendages.Clear();
            Assert.AreEqual(GolemMood.Stalled, waiting.Mood, "a feeder with no program feeds nothing");
            feeder.Program.appendages.AddRange(program);
            Assert.AreEqual(GolemMood.Waiting, waiting.Mood);
        }

        /// <summary>
        /// The other half of the probe's index key (Phase 3 critique): a feeder turned away, or
        /// moved off its tile, stops feeding in the same frame, and is seen feeding again once
        /// it is put back. The test above covers carry and program.
        /// </summary>
        [Test]
        public void TheFramedProbe_SeesAFeederTurnOrMove_WithinAFrame()
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world);
            load.Starve();
            Advance(world, load, 200);
            GolemEntity waiting = load.Golems.First(g => g.Mood == GolemMood.Waiting && world.StallFacts.FeedersOf(g, 10).Count == 1);
            GolemEntity feeder = world.StallFacts.FeedersOf(waiting, 10).Single();
            Vector2Int cell = feeder.Cell;
            Facing facing = feeder.Facing;

            feeder.SetFacing(FacingUtility.Opposite(facing));
            Assert.AreEqual(GolemMood.Stalled, waiting.Mood, "a feeder turned away feeds nothing, in the same frame");
            feeder.SetFacing(facing);
            Assert.AreEqual(GolemMood.Waiting, waiting.Mood, "turned back, it feeds again");

            feeder.SetPlacement(cell + new Vector2Int(0, 40), facing);
            Assert.AreEqual(GolemMood.Stalled, waiting.Mood, "moved away, it feeds nothing");
            feeder.SetPlacement(cell, facing);
            Assert.AreEqual(GolemMood.Waiting, waiting.Mood, "put back, it feeds again");
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
                    world.Advance(Frame);
                    load.Maintain();
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
            Advance(world, load, 300);
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
        private static Dictionary<string, double> Lines(List<RecipeDefinition> recipes, (string good, double rate)[] demands)
        {
            var byOutput = new Dictionary<string, RecipeDefinition>();
            foreach (var r in recipes)
            {
                byOutput[r.outputItemType] = r;
            }
            Dictionary<string, double> result = null;
            double steam = 0;
            for (int round = 0; round < 30; round++)
            {
                var need = demands.ToDictionary(d => d.good, d => d.rate);
                need[ItemType.Coke] = (need.TryGetValue(ItemType.Coke, out double c) ? c : 0) + steam;
                result = new Dictionary<string, double>();
                // Highest tier first, so every consumer has added its demand before a good is made.
                foreach (string good in ItemTiers.CanonicalOrder.Reverse())
                {
                    if (!need.TryGetValue(good, out double rate) || rate <= 0)
                    {
                        continue;
                    }
                    if (good == ItemType.Slag || good == ItemType.IronPlate)
                    {
                        continue; // R4, below, once both are known
                    }
                    if (!byOutput.TryGetValue(good, out var recipe))
                    {
                        result["raw " + good] = rate / 24.0;
                        continue;
                    }
                    Run(recipe, rate / recipe.outputQuantity, need, result);
                    if (good == ItemType.Glass)
                    {
                        // Glass's Slag is now known: smelt for the Plate and the Slag together.
                        var r4 = recipes.First(r => r.name == "R4_IronSmelting");
                        double plate = need.TryGetValue(ItemType.IronPlate, out double p) ? p : 0;
                        double slag = need.TryGetValue(ItemType.Slag, out double s) ? s : 0;
                        Run(r4, Math.Max(plate / r4.outputQuantity, slag / r4.byproductQuantity), need, result);
                        need[ItemType.IronPlate] = 0;
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

        private static void Run(RecipeDefinition recipe, double runsPerMinute,
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
    }
}
