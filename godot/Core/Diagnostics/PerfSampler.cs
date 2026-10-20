using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GolemFactory.Diagnostics
{
    /// <summary>
    /// Named timing sections for the endgame profile (docs/performance-report-template.md §3).
    /// Off unless a perf run turns it on: <c>SandboxWorld.Perf</c> is null by default, and
    /// <see cref="PerfSamplerExtensions.Measure"/> on a null sampler is a null check returning
    /// an empty struct, so the game pays nothing. On, a call allocates nothing: a section is made
    /// once, the scope is a struct, and the per-frame series grow only once a frame.
    ///
    /// <para>
    /// Measuring reads <see cref="Stopwatch.GetTimestamp"/> and never changes what is measured
    /// (invariant 3). A section nested in another (the probe inside a mood) counts in both.
    /// </para>
    /// </summary>
    public sealed class PerfSampler
    {
        public const string Tick = "tick";
        public const string TickGolems = "tick.golems";
        public const string TickBelts = "tick.belts";
        public const string TickSteam = "tick.steam";
        public const string TickOther = "tick.other";
        public const string ProbeRead = "probe.read";
        public const string Mood = "mood";
        public const string Strip = "strip";
        public const string Hover = "hover";

        /// <summary>A counter, not a timer: golems the probe's feeder walk visited.</summary>
        public const string LoopWalk = "loop.walk";

        private static readonly double MsPerTimestamp = 1000.0 / Stopwatch.Frequency;

        private readonly Dictionary<string, Section> _sections = new Dictionary<string, Section>();
        private readonly List<Section> _order = new List<Section>();

        /// <summary>Frames closed since the last <see cref="Reset"/>.</summary>
        public int Frames { get; private set; }

        public sealed class Section
        {
            internal Section(string name, bool keepCalls)
            {
                Name = name;
                if (keepCalls)
                {
                    CallMs = new FrameTimeStats(4096);
                }
            }

            public string Name { get; }

            /// <summary>Calls (or, for a counter, the count) in each closed frame.</summary>
            public FrameTimeStats FrameCalls { get; } = new FrameTimeStats();

            /// <summary>Milliseconds spent in each closed frame.</summary>
            public FrameTimeStats FrameMs { get; } = new FrameTimeStats();

            /// <summary>Each call's milliseconds, kept for the tick sections only; else null.</summary>
            public FrameTimeStats CallMs { get; }

            public long TotalCalls { get; private set; }

            internal long FrameTimestamps;
            internal long FrameCount;

            internal void Stop(long start)
            {
                long elapsed = Stopwatch.GetTimestamp() - start;
                FrameTimestamps += elapsed;
                FrameCount++;
                TotalCalls++;
                CallMs?.Add(elapsed * MsPerTimestamp);
            }

            internal void Add(int count)
            {
                FrameCount += count;
                TotalCalls += count;
            }

            internal void CloseFrame()
            {
                FrameCalls.Add(FrameCount);
                FrameMs.Add(FrameTimestamps * MsPerTimestamp);
                FrameCount = 0;
                FrameTimestamps = 0;
            }

            internal void Clear()
            {
                FrameCalls.Clear();
                FrameMs.Clear();
                CallMs?.Clear();
                FrameCount = 0;
                FrameTimestamps = 0;
                TotalCalls = 0;
            }
        }

        /// <summary>A running measurement; disposing it adds its time to the section.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly Section _section;
            private readonly long _start;

            internal Scope(Section section)
            {
                _section = section;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose() => _section?.Stop(_start);
        }

        /// <summary>The section named <paramref name="name"/>, made on first use.</summary>
        public Section Get(string name)
        {
            if (!_sections.TryGetValue(name, out Section section))
            {
                section = new Section(name, name.StartsWith(Tick, StringComparison.Ordinal));
                _sections[name] = section;
                _order.Add(section);
            }
            return section;
        }

        /// <summary>Whether a section has been used since it was made.</summary>
        public bool Has(string name) => _sections.ContainsKey(name);

        public IReadOnlyList<Section> Sections => _order;

        public Scope Start(string name) => new Scope(Get(name));

        public void Tally(string name, int count) => Get(name).Add(count);

        /// <summary>Opens a frame: anything measured before it belongs to no frame and is dropped.</summary>
        public void BeginFrame()
        {
            foreach (Section section in _order)
            {
                section.FrameCount = 0;
                section.FrameTimestamps = 0;
            }
        }

        /// <summary>Closes a frame: every section's frame totals join its series.</summary>
        public void EndFrame()
        {
            foreach (Section section in _order)
            {
                section.CloseFrame();
            }
            Frames++;
        }

        /// <summary>Starts a new phase: every series empties.</summary>
        public void Reset()
        {
            foreach (Section section in _order)
            {
                section.Clear();
            }
            Frames = 0;
        }
    }

    /// <summary>Null-safe calls, so a site writes <c>using (perf.Measure(...))</c> whether or not a sampler is on.</summary>
    public static class PerfSamplerExtensions
    {
        public static PerfSampler.Scope Measure(this PerfSampler sampler, string section) =>
            sampler == null ? default : sampler.Start(section);

        public static void Count(this PerfSampler sampler, string section, int count)
        {
            sampler?.Tally(section, count);
        }
    }
}
