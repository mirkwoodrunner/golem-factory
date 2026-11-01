using NUnit.Framework;
using GolemFactory.Diagnostics;
using GolemFactory.Simulation;

namespace GolemFactory.Tests.EditMode
{
    // The endgame profile's sampler (docs/performance-report-template.md §3, §7): off costs a null
    // check, on counts and times sections per frame, and percentiles read one way everywhere.
    public class PerfSamplerTests
    {
        [Test]
        public void ANullSampler_MeasuresNothing_AndNeverThrows()
        {
            PerfSampler none = null;
            using (none.Measure(PerfSampler.Tick))
            {
            }
            none.Count(PerfSampler.LoopWalk, 3);
            Assert.Pass();
        }

        [Test]
        public void Sections_CountAndTime_PerFrame()
        {
            var perf = new PerfSampler();
            perf.BeginFrame();
            for (int i = 0; i < 3; i++)
            {
                using (perf.Measure(PerfSampler.Mood))
                {
                    System.Threading.Thread.SpinWait(1000);
                }
            }
            perf.Count(PerfSampler.LoopWalk, 7);
            perf.EndFrame();

            perf.BeginFrame();
            using (perf.Measure(PerfSampler.Mood))
            {
            }
            perf.EndFrame();

            PerfSampler.Section mood = perf.Get(PerfSampler.Mood);
            Assert.AreEqual(2, perf.Frames);
            Assert.AreEqual(4, mood.TotalCalls);
            Assert.AreEqual(3.0, mood.FrameCalls.Max);
            Assert.AreEqual(1.0, mood.FrameCalls.Percentile(50));
            Assert.Greater(mood.FrameMs.Max, 0.0);
            Assert.IsNull(mood.CallMs, "only the tick sections keep every call");
            Assert.AreEqual(7.0, perf.Get(PerfSampler.LoopWalk).FrameCalls.Max);
            Assert.AreEqual(0.0, perf.Get(PerfSampler.LoopWalk).FrameCalls.Percentile(50), "a frame with no walk counts 0");

            perf.Reset();
            Assert.AreEqual(0, perf.Frames);
            Assert.AreEqual(0, mood.FrameCalls.Count);
        }

        [Test]
        public void Percentiles_OfAKnownSeries()
        {
            var stats = new FrameTimeStats();
            for (int i = 100; i >= 1; i--)
            {
                stats.Add(i);
            }
            Assert.AreEqual(50.0, stats.P50);
            Assert.AreEqual(95.0, stats.P95);
            Assert.AreEqual(100.0, stats.Max);
            Assert.AreEqual(1.0, stats.Percentile(0));
            Assert.AreEqual(50.5, stats.Mean, 1e-9);

            var short3 = new FrameTimeStats();
            short3.Add(2);
            short3.Add(9);
            short3.Add(4);
            Assert.AreEqual(4.0, short3.P50);
            Assert.AreEqual(9.0, short3.P95);
            Assert.AreEqual(0.0, new FrameTimeStats().P95, "empty reads 0");
        }

        private sealed class Counter : ITickable
        {
            public int Ticks;
            public void Tick(long currentTick) => Ticks++;
        }

        [Test]
        public void TheClock_TimesEachTick_ByGroup_AndTicksTheSame()
        {
            var plain = new SimulationClock();
            var measured = new SimulationClock { Perf = new PerfSampler(), PerfGroup = _ => PerfSampler.TickGolems };
            var a = new Counter();
            var b = new Counter();
            plain.Register(a);
            measured.Register(b);
            plain.Play();
            measured.Play();
            measured.Perf.BeginFrame();
            plain.Advance(1f);
            measured.Advance(1f);
            measured.Perf.EndFrame();

            Assert.AreEqual(a.Ticks, b.Ticks, "measuring never changes what a tick does");
            Assert.AreEqual(10, b.Ticks);
            Assert.AreEqual(10, measured.Perf.Get(PerfSampler.Tick).TotalCalls);
            Assert.AreEqual(10, measured.Perf.Get(PerfSampler.Tick).CallMs.Count);
            Assert.AreEqual(10, measured.Perf.Get(PerfSampler.TickGolems).TotalCalls);
        }
    }
}
