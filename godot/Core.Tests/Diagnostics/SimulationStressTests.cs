using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GolemFactory.Diagnostics;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Simulation;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    // The tick, on its own and far past the size the game allows (Diagnostics/StressLoad): that
    // it produces, that it allocates nothing, and that it is the same simulation however it is
    // built, sliced into frames or run again. The small loads run with the suite; the
    // thousand-golem benchmark is explicit, like the endgame profile's.
    public class SimulationStressTests
    {
        private const float Tick = 0.1f;

        /// <summary>Records the load's checksum after every tick, from inside the clock.</summary>
        private sealed class Recorder : ITickable
        {
            private readonly StressLoad _load;
            public readonly List<ulong> Checksums = new List<ulong>();

            public Recorder(StressLoad load)
            {
                _load = load;
                load.Clock.Register(this); // last, so it reads a finished tick
            }

            public void Tick(long tick) => Checksums.Add(_load.Checksum());
        }

        private static List<ulong> Run(int lines, int ticks, bool reverseBuildOrder = false, float frame = Tick)
        {
            using (StressLoad load = StressLoad.Build(AuthoredData.Load(), lines, reverseBuildOrder))
            {
                var recorder = new Recorder(load);
                while (load.Clock.CurrentTick < ticks)
                {
                    load.Clock.Advance(frame);
                }
                return recorder.Checksums.Take(ticks).ToList();
            }
        }

        private static void AssertSameRun(List<ulong> expected, List<ulong> actual, string what)
        {
            Assert.AreEqual(expected.Count, actual.Count, what + ": ticks recorded");
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i] != actual[i])
                {
                    Assert.Fail($"{what}: the runs part at tick {i + 1} of {expected.Count}");
                }
            }
        }

        [Test]
        public void TheLoad_IsWhatItSays_AndProduces()
        {
            int listenersBefore = EventBus.ListenerCount;
            using (StressLoad load = StressLoad.Build(AuthoredData.Load(), 16))
            {
                Assert.AreEqual(16 * StressLoad.GolemsPerLine, load.Golems.Count);
                Assert.AreEqual(16, load.Steam.BoilerCount);
                Assert.Greater(load.GridEntities, 16 * 40, "seams, crates, belts, boilers, sinks, pipes and golems");

                load.Clock.Advance(Tick);
                Assert.AreEqual(load.Golems.Count, load.Steam.PoweredGolemCount(load.Clock.CurrentTick),
                    "every golem stands beside its line's pipes");

                for (int i = 0; i < 3000; i++)
                {
                    load.Clock.Advance(Tick);
                }
                Assert.Greater(load.Delivered, 16 * 20, "every line delivers gears");
                Assert.Greater(load.BeltItems, 0, "gears ride the belts");
                Assert.IsFalse(load.Golems.Any(g => g.StallReason == StallReason.NoSteam), "each line keeps its own boiler in Coke");
                Assert.IsFalse(load.Golems.Any(g => g.StallReason == StallReason.Unconfigured
                                                    || g.StallReason == StallReason.NoSourceAtTile
                                                    || g.StallReason == StallReason.NoTargetAtTile),
                    "every golem has a tile behind it and a tile in front");
                Assert.Greater(load.Stalled, 0, "lines back up behind their slowest machine: the stall path is under load too");
            }
            Assert.AreEqual(listenersBefore, EventBus.ListenerCount, "a disposed load leaves nothing on the bus");
        }

        [Test]
        public void TwoRuns_AgreeOnEveryTick()
        {
            AssertSameRun(Run(12, 1500), Run(12, 1500), "the same load, run twice");
        }

        /// <summary>
        /// The same factory built last line first: every registry is filled in the opposite
        /// order, and every golem ticks in the opposite order between lines. Nothing joins one
        /// line to another, so nothing may change.
        /// </summary>
        [Test]
        public void TheOrderLinesAreBuiltIn_ChangesNothing()
        {
            AssertSameRun(Run(12, 1500), Run(12, 1500, reverseBuildOrder: true), "built last line first");
        }

        /// <summary>
        /// A tick is a tick however many frames it took to arrive: one tick a call, a 60 fps
        /// frame a call, and a slow frame that carries several ticks all run the same ticks.
        /// </summary>
        [Test]
        public void HowFramesSliceTheTicks_ChangesNothing()
        {
            List<ulong> tickAtATime = Run(8, 1200);
            AssertSameRun(tickAtATime, Run(8, 1200, frame: 1f / 60f), "at 60 frames a second");
            AssertSameRun(tickAtATime, Run(8, 1200, frame: 0.37f), "at slow frames of several ticks");
        }

        /// <summary>
        /// The tick allocates nothing. Warmed until every list and dictionary has reached its
        /// size (a line's first backlog, a golem's first stall on each tile), then measured for
        /// a thousand ticks of golems extracting, hauling, assembling, pushing and stalling.
        /// </summary>
        [Test]
        public void TheTick_AllocatesNothing()
        {
            using (StressLoad load = StressLoad.Build(AuthoredData.Load(), 16))
            {
                for (int i = 0; i < 4000; i++)
                {
                    load.Clock.Advance(Tick);
                }
                long deliveredBefore = load.Delivered;
                long ticksBefore = load.Clock.CurrentTick;

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++)
                {
                    load.Clock.Advance(Tick);
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.AreEqual(1000, load.Clock.CurrentTick - ticksBefore);
                Assert.Greater(load.Delivered, deliveredBefore, "the measured ticks did work");
                Assert.AreEqual(0, allocated, "bytes allocated by 1,000 ticks of 128 golems");
            }
        }

        /// <summary>
        /// The same, for the whole composed world at the size the game caps (160 golems, the
        /// guide, the contracts, the alerts strip and every other listener on the bus): its
        /// ticks allocate nothing either, flowing or starved.
        /// </summary>
        [Test]
        public void TheComposedWorldsTick_AllocatesNothing([Values(false, true)] bool starved)
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world, EndgameLoadSpec.Endless);
            if (starved)
            {
                load.Starve();
            }
            for (int i = 0; i < 1200; i++)
            {
                world.Advance(Tick);
                load.Maintain();
            }

            long allocated = 0;
            for (int i = 0; i < 600; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                world.Clock.Advance(Tick);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                load.Maintain(); // the load's own refill, outside what is measured
            }

            Assert.AreEqual(0, allocated, "bytes allocated by 600 ticks of the 160-golem world");
            world.Dispose();
        }

        /// <summary>
        /// Reading every golem's mood on a fresh presentation frame, which rebuilds the
        /// stall-facts index and reads it for every stalled golem, allocates nothing.
        /// </summary>
        [Test]
        public void AStarvedFramesMoods_AllocateNothing()
        {
            SandboxWorld world = Compose();
            EndgameLoadResult load = EndgameLoad.Build(world, EndgameLoadSpec.Endless);
            load.Starve();
            for (int i = 0; i < 600; i++)
            {
                world.Advance(Tick);
                load.Maintain();
            }
            IReadOnlyList<GolemEntity> golems = world.Golems;
            Assert.Greater(load.Stalled, 100, "starved");
            ReadMoods(world, golems);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int stopped = 0;
            for (int frame = 0; frame < 50; frame++)
            {
                stopped += ReadMoods(world, golems);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Greater(stopped, 0);
            Assert.AreEqual(0, allocated, "bytes allocated by 50 frames of 160 mood reads");
            world.Dispose();
        }

        private static int ReadMoods(SandboxWorld world, IReadOnlyList<GolemEntity> golems)
        {
            world.InvalidateStallFacts(); // a new frame: the index is rebuilt
            int stopped = 0;
            for (int i = 0; i < golems.Count; i++)
            {
                stopped += GolemMoodRules.IsStopped(golems[i].Mood) ? 1 : 0;
            }
            return stopped;
        }

        // --- The benchmark ---------------------------------------------------------------------

        /// <summary>
        /// 1,024 golems on 128 lines for 10,000 ticks, twice. Prints the tick's time and its
        /// share by registrant, and asserts what the run is for: zero bytes allocated, and the
        /// two runs' checksums equal at every hundredth tick and at the end.
        /// <c>dotnet test -c Release --filter Category=Perf</c>.
        /// </summary>
        [Test, Explicit, Category("Perf")]
        public void AThousandGolems_TenThousandTicks()
        {
            const int lines = 128, warmUp = 2000, ticks = 10000;
            var checksums = new List<ulong>[2];
            for (int run = 0; run < 2; run++)
            {
                using (StressLoad load = StressLoad.Build(AuthoredData.Load(), lines, reverseBuildOrder: run == 1))
                {
                    for (int i = 0; i < warmUp; i++)
                    {
                        load.Clock.Advance(Tick);
                    }
                    long deliveredBefore = load.Delivered;
                    checksums[run] = new List<ulong>();
                    var perTick = new FrameTimeStats(ticks);
                    long allocated = 0;
                    long started = Stopwatch.GetTimestamp();
                    for (int i = 0; i < ticks; i++)
                    {
                        long bytes = GC.GetAllocatedBytesForCurrentThread();
                        long t0 = Stopwatch.GetTimestamp();
                        load.Clock.Advance(Tick);
                        long t1 = Stopwatch.GetTimestamp();
                        allocated += GC.GetAllocatedBytesForCurrentThread() - bytes;
                        perTick.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
                        if (i % 100 == 99)
                        {
                            checksums[run].Add(load.Checksum());
                        }
                    }
                    double seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;

                    TestContext.WriteLine(
                        $"[run {run + 1}{(run == 1 ? ", built last line first" : "")}] {load.Golems.Count} golems, {load.GridEntities} grid entities, " +
                        $"{load.Steam.BoilerCount} boilers, {load.Steam.PipeCount} pipes, {load.BeltItems} belt items, {load.Stalled} stalled");
                    TestContext.WriteLine(
                        $"  {ticks} ticks in {seconds:F2} s: per tick mean={perTick.Mean:F3} p50={perTick.P50:F3} p95={perTick.P95:F3} max={perTick.Max:F3} ms; " +
                        $"{allocated} bytes allocated; {load.Delivered - deliveredBefore} gears delivered; checksum {load.Checksum():X16}");

                    Assert.AreEqual(warmUp + ticks, load.Clock.CurrentTick);
                    Assert.Greater(load.Delivered, deliveredBefore);
                    Assert.AreEqual(0, allocated, "bytes allocated by the measured ticks");
                    // A tick has 100 ms at ten a second; a 60 fps frame has 16.7. A tenth of the frame.
                    Assert.LessOrEqual(perTick.P95, 1.67, "p95 tick time, ms");
                    if (run == 0)
                    {
                        PrintShares(load);
                    }
                }
            }
            CollectionAssert.AreEqual(checksums[0], checksums[1], "the two runs' checksums, every hundredth tick");
        }

        /// <summary>Where a tick's time goes, by registrant, over 500 more ticks.</summary>
        private static void PrintShares(StressLoad load)
        {
            var perf = new PerfSampler();
            load.Clock.Perf = perf;
            load.Clock.PerfGroup = t => t is GolemEntity ? PerfSampler.TickGolems
                : t is GolemFactory.Belts.ConveyorSystem ? PerfSampler.TickBelts : PerfSampler.TickSteam;
            for (int i = 0; i < 500; i++)
            {
                perf.BeginFrame();
                load.Clock.Advance(Tick);
                perf.EndFrame();
            }
            foreach (PerfSampler.Section section in perf.Sections)
            {
                TestContext.WriteLine($"    {section.Name,-12} ms/tick p50={section.FrameMs.P50:F3} p95={section.FrameMs.P95:F3} max={section.FrameMs.Max:F3}");
            }
            load.Clock.Perf = null;
            load.Clock.PerfGroup = null;
        }

        /// <summary>
        /// The spatial hash against the dictionary it replaced, on a factory-sized block of
        /// cells: a million lookups each, half of them misses.
        /// </summary>
        [Test, Explicit, Category("Perf")]
        public void TheSpatialHash_AgainstADictionary()
        {
            const int side = 64, rounds = 250;
            var grid = new SpatialHashGrid<object>(side * side);
            var dictionary = new Dictionary<Vector2Int, object>();
            var entity = new object();
            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y += 2)
                {
                    grid.Add(new Vector2Int(x - 32, y - 32), entity);
                    dictionary[new Vector2Int(x - 32, y - 32)] = entity;
                }
            }

            int gridHits = 0, dictionaryHits = 0;
            double gridMs = double.MaxValue, dictionaryMs = double.MaxValue;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int r = 0; r < rounds; r++)
                {
                    for (int x = 0; x < side; x++)
                    {
                        for (int y = 0; y < side; y++)
                        {
                            gridHits += grid.TryGet(new Vector2Int(x - 32, y - 32), out _) ? 1 : 0;
                        }
                    }
                }
                long t1 = Stopwatch.GetTimestamp();
                for (int r = 0; r < rounds; r++)
                {
                    for (int x = 0; x < side; x++)
                    {
                        for (int y = 0; y < side; y++)
                        {
                            dictionaryHits += dictionary.TryGetValue(new Vector2Int(x - 32, y - 32), out _) ? 1 : 0;
                        }
                    }
                }
                long t2 = Stopwatch.GetTimestamp();
                gridMs = Math.Min(gridMs, (t1 - t0) * 1000.0 / Stopwatch.Frequency);
                dictionaryMs = Math.Min(dictionaryMs, (t2 - t1) * 1000.0 / Stopwatch.Frequency);
            }

            int lookups = side * side * rounds;
            TestContext.WriteLine($"{lookups} lookups: spatial hash {gridMs * 1e6 / lookups:F1} ns each, dictionary {dictionaryMs * 1e6 / lookups:F1} ns each");
            Assert.AreEqual(dictionaryHits, gridHits);
        }
    }
}
