using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using GolemFactory.Diagnostics;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Simulation;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The endgame profile (docs/performance-report-template.md §4): Core's
    /// <see cref="EndgameLoad"/> builds about 96 golems, 500 belt items and 12 boilers into the
    /// Sandbox, and the scenario times six phases of 1,200 frames, each after a 120-frame
    /// warm-up: <c>flowing-1x</c>, <c>flowing-4x</c>, <c>starved</c> (every crate emptied),
    /// <c>hover</c> (starved, the pointer on a waiting golem), <c>unlit</c> (flowing, the room's
    /// tint and every light off) and <c>no-draw</c> (flowing, the world's drawing hidden). Frame time is wall time between this node's frames; Core's
    /// sections come from <see cref="SandboxWorld.Perf"/>; the renderer's from Godot's monitors.
    ///
    /// <para>
    /// Windowed, every phase but <c>no-draw</c> must hold p95 ≤ 16.6 ms and max ≤ 33.3 ms, and
    /// the lighting (<c>flowing-1x</c>'s render CPU p95 less <c>unlit</c>'s) at most 1.0 ms
    /// (art-pipeline-design.md §6.3).
    /// Headless there is no renderer, so it judges only Core's share: p95 ≤ 6 ms. One line per
    /// phase is printed, and a filled copy of the report's tables goes to
    /// <c>user://perf-report.md</c>. Nothing touches the player's save.
    /// </para>
    /// </summary>
    public sealed class PerfScenario : IScenario
    {
        public const int WarmupFrames = 120;
        public const int MeasureFrames = 1200;
        public const double LightingRenderBudget = 1.0;
        private const int StarveFramesMax = 1200;

        private const double FrameP95Budget = 16.6, FrameMaxBudget = 33.3, CoreP95Budget = 6.0;

        private sealed class Phase
        {
            public string Name;
            public Action Enter;
            public int Frames;
            public readonly FrameTimeStats FrameMs = new FrameTimeStats(MeasureFrames);
            public readonly FrameTimeStats CoreMs = new FrameTimeStats(MeasureFrames);
            public readonly FrameTimeStats RenderCpu = new FrameTimeStats(MeasureFrames);
            public readonly FrameTimeStats RenderGpu = new FrameTimeStats(MeasureFrames);
            public readonly FrameTimeStats ProcessMs = new FrameTimeStats(MeasureFrames);
            public double Draws, Objects, Nodes;
            public int Golems, BeltItems, Stalled, Waiting;
            public readonly Dictionary<string, (long calls, double p50, double p95, double max, double callsP95, double framesP95)> Sections =
                new Dictionary<string, (long, double, double, double, double, double)>();
            public readonly Dictionary<string, (int count, double p50, double p95, double max)> PerCall =
                new Dictionary<string, (int, double, double, double)>();
        }

        private SceneTree _tree;
        private Viewport _viewport;
        private SandboxWorld _world;
        private PlayerNode _player;
        private EndgameLoadResult _load;
        private PerfSampler _perf;
        private readonly List<Phase> _phases = new List<Phase>();
        private int _phase = -1;
        private int _frame;
        private ulong _lastUsec;
        private bool _headless;
        private string _failure;
        private GolemEntity _hovered;
        private Func<Vector2> _pointer;
        private int _starveWait;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _viewport = runner.GetViewport();
            _world = runner.World.Sandbox;
            _player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            _headless = DisplayServer.GetName() == "headless";

            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
            RenderingServer.ViewportSetMeasureRenderTime(_viewport.GetViewportRid(), true);

            try
            {
                _load = EndgameLoad.Build(_world);
            }
            catch (Exception e)
            {
                _failure = "load: " + e.Message;
                return;
            }
            _perf = new PerfSampler();
            _world.Perf = _perf;
            NodePerf.Sampler = _perf;
            _player.TeleportTo(Standing);

            _phases.Add(new Phase { Name = "flowing-1x", Enter = () => Flowing(1f) });
            _phases.Add(new Phase { Name = "flowing-4x", Enter = () => Flowing(ClockReadout.SpeedPresets.Max()) });
            _phases.Add(new Phase { Name = "starved", Enter = Starve });
            _phases.Add(new Phase { Name = "hover", Enter = HoverAWaitingGolem });
            _phases.Add(new Phase { Name = "unlit", Enter = () => { Flowing(1f); Light(false); } });
            _phases.Add(new Phase { Name = "no-draw", Enter = () => { Flowing(1f); Light(true); DrawWorld(false); } });
        }

        // --- Phases -------------------------------------------------------------------------

        /// <summary>Where the player stands for every phase but hover: the middle of the chains, the factory filling the view.</summary>
        private static readonly CoreVector3 Standing = new CoreVector3(0f, 11.5f, 0f);

        private void Flowing(float speed)
        {
            _pointer = null;
            _player.TeleportTo(Standing);
            _world.Clock.Speed = speed;
            if (_load.Starved)
            {
                _load.Refill();
            }
        }

        private void Starve()
        {
            _player.TeleportTo(Standing);
            _world.Clock.Speed = 1f;
            _load.Starve();
            _starveWait = StarveFramesMax;
        }

        private void HoverAWaitingGolem()
        {
            // The waiting golem nearest where the player stands, so the view is the starved
            // phase's and only the hover differs.
            _player.TeleportTo(Standing);
            _hovered = _load.Golems
                .Where(g => g.Mood == GolemMood.Waiting)
                .OrderBy(g => Math.Abs(g.Cell.x - Standing.x) + Math.Abs(g.Cell.y - Standing.y))
                .FirstOrDefault();
            if (_hovered == null)
            {
                _failure = "hover: no waiting golem to point at";
                return;
            }
            CoreVector2Int cell = _hovered.Cell;
            _pointer = () => _viewport.GetCanvasTransform() * GridConversions.CellToWorld(cell);
        }

        /// <summary>
        /// The room's tint and every light, on or off. Hidden rather than disabled: a boiler's
        /// glow sets its own Enabled each frame from its fuel.
        /// </summary>
        private void Light(bool on)
        {
            foreach (Node node in _tree.Root.FindChildren("*", nameof(CanvasModulate), true, false)
                         .Concat(_tree.Root.FindChildren("*", nameof(PointLight2D), true, false)))
            {
                switch (node)
                {
                    case CanvasModulate tint:
                        tint.Visible = on;
                        break;
                    case CanvasItem light:
                        light.Visible = on;
                        break;
                }
            }
        }

        private void DrawWorld(bool visible)
        {
            // The golems' badges share one layer outside the golem nodes, so they are hidden with them.
            if (_tree.Root.FindChild(GolemNode.BadgeLayerName, true, false) is CanvasLayer badges)
            {
                badges.Visible = visible;
            }
            foreach (string group in new[] { GolemNodeGroup.Name, BuildingsLayer.GroupName, BeltRun.GroupName })
            {
                foreach (CanvasItem item in _tree.GetNodesInGroup(group).OfType<CanvasItem>())
                {
                    item.Visible = visible;
                }
            }
        }

        // --- Per frame ----------------------------------------------------------------------

        public ScenarioResult? Step(double delta)
        {
            if (_failure != null)
            {
                return new ScenarioResult(false, _failure);
            }

            ulong now = Time.GetTicksUsec();
            double frameMs = _lastUsec == 0 ? 0 : (now - _lastUsec) / 1000.0;
            _lastUsec = now;
            _perf.EndFrame();

            if (_pointer != null)
            {
                Vector2 at = _pointer();
                _viewport.WarpMouse(at);
                using var motion = new InputEventMouseMotion { Position = at, GlobalPosition = at };
                Input.ParseInputEvent(motion);
                Input.FlushBufferedEvents();
            }

            if (_phase < 0 || _frame >= WarmupFrames + MeasureFrames)
            {
                if (_phase >= 0)
                {
                    Close(_phases[_phase]);
                }
                if (++_phase >= _phases.Count)
                {
                    DrawWorld(true);
                    NodePerf.Sampler = null;
                    _world.Perf = null;
                    return Verdict();
                }
                _frame = 0;
                _phases[_phase].Enter();
                if (_failure != null)
                {
                    return new ScenarioResult(false, _failure);
                }
                _perf.BeginFrame();
                return null;
            }

            Phase phase = _phases[_phase];
            _load.Maintain();

            if (_phase == 2 && _starveWait > 0)
            {
                // Starving takes the lines a few cycles; measuring starts once ≥ 60 have stalled.
                _starveWait = _load.Stalled >= 60 ? 0 : _starveWait - 1;
                if (_starveWait == 0 && _load.Stalled < 60)
                {
                    return new ScenarioResult(false, $"starved: only {_load.Stalled} golems stalled");
                }
                _perf.BeginFrame();
                return null;
            }

            _frame++;
            if (_frame == WarmupFrames)
            {
                if (!CheckBeforeMeasuring(phase))
                {
                    return new ScenarioResult(false, _failure);
                }
                _perf.Reset();
            }
            else if (_frame > WarmupFrames)
            {
                phase.Frames++;
                phase.FrameMs.Add(frameMs);
                phase.CoreMs.Add(Last(PerfSampler.Tick) + Last(PerfSampler.Mood) + Last(PerfSampler.Strip) + Last(PerfSampler.Hover));
                phase.ProcessMs.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);
                Rid viewport = _viewport.GetViewportRid();
                phase.RenderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport));
                phase.RenderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport));
                phase.Draws = Math.Max(phase.Draws, Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
                phase.Objects = Math.Max(phase.Objects, Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
                phase.Nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            }
            _perf.BeginFrame();
            return null;
        }

        private double Last(string section) => _perf.Has(section) ? _perf.Get(section).LastFrameMs : 0.0;

        private bool CheckBeforeMeasuring(Phase phase)
        {
            if (_load.Golems.Count < 96 || _world.Golems.Count(g => !g.IsRemoved) < 96)
            {
                _failure = $"{phase.Name}: {_load.Golems.Count} golems stand";
                return false;
            }
            GolemEntity unpowered = _load.Golems.FirstOrDefault(g => g.StallReason == StallReason.NoSteam);
            if (unpowered != null || _load.Powered < _load.Golems.Count)
            {
                _failure = $"{phase.Name}: {_load.Golems.Count - _load.Powered} golems unpowered";
                return false;
            }
            if (_load.BeltItems < 500)
            {
                _failure = $"{phase.Name}: {_load.BeltItems} belt items";
                return false;
            }
            if (phase.Name == "starved" && _load.Stalled < 60)
            {
                _failure = $"starved: {_load.Stalled} stalled";
                return false;
            }
            if (phase.Name == "hover" && _world.Inspector.Hovered != _hovered)
            {
                _failure = $"hover: the pointer is not on {_hovered?.GolemId} (hovered {_world.Inspector.Hovered?.GolemId ?? "nothing"})";
                return false;
            }
            return true;
        }

        private void Close(Phase phase)
        {
            phase.Golems = _load.Golems.Count;
            phase.BeltItems = _load.BeltItems;
            phase.Stalled = _load.Stalled;
            phase.Waiting = _load.Golems.Count(g => g.Mood == GolemMood.Waiting);
            foreach (PerfSampler.Section s in _perf.Sections)
            {
                phase.Sections[s.Name] = (s.TotalCalls, s.FrameMs.P50, s.FrameMs.P95, s.FrameMs.Max, s.FrameCalls.Mean, s.FrameCalls.P95);
                if (s.CallMs != null)
                {
                    phase.PerCall[s.Name] = (s.CallMs.Count, s.CallMs.P50, s.CallMs.P95, s.CallMs.Max);
                }
            }
            GD.Print(Line(phase));
            GD.Print("[perf]   sections p95 ms/frame: " + Sections(phase));
        }

        private (long calls, double p50, double p95, double max, double callsMean, double callsP95) S(Phase p, string name) =>
            p.Sections.TryGetValue(name, out var v) ? v : default;

        private string Line(Phase p)
        {
            var tick = p.PerCall.TryGetValue(PerfSampler.Tick, out var t) ? t : default;
            // One invariant string: concatenated interpolations bind to the handler, not to FormattableString.
            return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[perf] phase={p.Name} frames={p.Frames} p50={p.FrameMs.P50:F1} p95={p.FrameMs.P95:F1} max={p.FrameMs.Max:F1} " +
                $"tick_p95={tick.p95:F2} core_p95={p.CoreMs.P95:F2} probe_calls={S(p, PerfSampler.ProbeRead).calls} " +
                $"probe_ms={S(p, PerfSampler.ProbeRead).p95:F2} mood_ms={S(p, PerfSampler.Mood).p95:F2} " +
                $"render_cpu_p95={p.RenderCpu.P95:F1} render_gpu_p95={p.RenderGpu.P95:F1} draws={p.Draws:F0} nodes={p.Nodes:F0} " +
                $"golems={p.Golems} belt_items={p.BeltItems} stalled={p.Stalled} waiting={p.Waiting}");
        }

        /// <summary>Every section's per-frame p95, node sections included, heaviest first.</summary>
        private static string Sections(Phase p) => string.Join(" ", p.Sections
            .Where(s => s.Key != PerfSampler.LoopWalk)
            .OrderByDescending(s => s.Value.p95)
            .Select(s => FormattableString.Invariant($"{s.Key}={s.Value.p95:F2}")));

        // --- Verdict and report ----------------------------------------------------------------

        private Phase P(string name) => _phases.First(p => p.Name == name);

        private ScenarioResult Verdict()
        {
            var failures = new List<string>();
            foreach (Phase p in _phases)
            {
                if (_headless)
                {
                    if (p.CoreMs.P95 > CoreP95Budget)
                    {
                        failures.Add(FormattableString.Invariant($"{p.Name} core_p95={p.CoreMs.P95:F2} > {CoreP95Budget}"));
                    }
                }
                else if (p.Name != "no-draw")
                {
                    if (p.FrameMs.P95 > FrameP95Budget)
                    {
                        failures.Add(FormattableString.Invariant($"{p.Name} p95={p.FrameMs.P95:F1} > {FrameP95Budget}"));
                    }
                    if (p.FrameMs.Max > FrameMaxBudget)
                    {
                        failures.Add(FormattableString.Invariant($"{p.Name} max={p.FrameMs.Max:F1} > {FrameMaxBudget}"));
                    }
                }
            }
            if (!_headless)
            {
                double lighting = P("flowing-1x").RenderCpu.P95 - P("unlit").RenderCpu.P95;
                if (lighting > LightingRenderBudget)
                {
                    failures.Add(FormattableString.Invariant($"lighting render CPU p95 +{lighting:F2} ms > {LightingRenderBudget}"));
                }
            }
            WriteReport(failures);
            string summary = string.Join("; ", _phases.Select(p => FormattableString.Invariant(
                $"{p.Name} p95 {p.FrameMs.P95:F1} max {p.FrameMs.Max:F1} core {p.CoreMs.P95:F2}")));
            return failures.Count == 0
                ? new ScenarioResult(true, (_headless ? "headless (Core budget): " : "windowed: ") + summary)
                : new ScenarioResult(false, string.Join("; ", failures));
        }

        private void WriteReport(List<string> failures)
        {
            var md = new StringBuilder();
            string F(double v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            md.AppendLine("# Perf run").AppendLine();
            md.AppendLine("```");
            md.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}   Machine: {OS.GetProcessorName()} · {RenderingServer.GetVideoAdapterName()} · {OS.GetMemoryInfo()["physical"].AsInt64() / (1024 * 1024 * 1024)} GB");
            md.AppendLine($"Godot: {Engine.GetVersionInfo()["string"]}   Run: {(_headless ? "headless" : "windowed")}   Load: golems {_load.Golems.Count}  belt items {_load.BeltItems}  boilers {_load.Boilers.Count}");
            md.AppendLine("```").AppendLine();

            md.AppendLine("### 6.1 Frame time (ms)").AppendLine();
            md.AppendLine("| Phase | Frames | p50 | p95 | max | Pass (p95 ≤ 16.6, max ≤ 33.3) |").AppendLine("|---|---|---|---|---|---|");
            foreach (Phase p in _phases)
            {
                string pass = p.Name == "no-draw" ? "n/a" : _headless ? "headless: Core only" :
                    p.FrameMs.P95 <= FrameP95Budget && p.FrameMs.Max <= FrameMaxBudget ? "yes" : "no";
                md.AppendLine($"| `{p.Name}` | {p.Frames} | {F(p.FrameMs.P50)} | {F(p.FrameMs.P95)} | {F(p.FrameMs.Max)} | {pass} |");
            }

            md.AppendLine().AppendLine("### 6.2 Simulation tick (ms)").AppendLine();
            md.AppendLine("| Phase | Ticks | p50 | p95 | max | `tick.golems` p95 | `tick.belts` p95 | `tick.steam` p95 |").AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (string name in new[] { "flowing-1x", "flowing-4x", "starved" })
            {
                Phase p = P(name);
                var t = p.PerCall.TryGetValue(PerfSampler.Tick, out var tt) ? tt : default;
                md.AppendLine($"| `{name}` | {t.count} | {F(t.p50)} | {F(t.p95)} | {F(t.max)} | {F(S(p, PerfSampler.TickGolems).p95)} | {F(S(p, PerfSampler.TickBelts).p95)} | {F(S(p, PerfSampler.TickSteam).p95)} |");
            }
            md.AppendLine().AppendLine("(the `tick.*` columns are per frame; a frame runs at most one tick at 4×)");

            md.AppendLine().AppendLine("### 6.3 Core hot spots, per frame").AppendLine();
            md.AppendLine("| Phase | `mood` calls | `mood` ms p95 | `probe.read` calls | `probe.read` ms p95 | `loop.walk` golems visited p95 | `strip` ms p95 | `hover` ms p95 |").AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (string name in new[] { "flowing-1x", "starved", "hover" })
            {
                Phase p = P(name);
                md.AppendLine($"| `{name}` | {S(p, PerfSampler.Mood).callsP95:F0} | {F(S(p, PerfSampler.Mood).p95)} | {S(p, PerfSampler.ProbeRead).callsP95:F0} | {F(S(p, PerfSampler.ProbeRead).p95)} | {S(p, PerfSampler.LoopWalk).callsP95:F0} | {F(S(p, PerfSampler.Strip).p95)} | {F(S(p, PerfSampler.Hover).p95)} |");
            }

            md.AppendLine().AppendLine("### 6.4 Renderer").AppendLine();
            md.AppendLine("| Phase | Render CPU p95 | Render GPU p95 | Draw calls | Objects | Nodes |").AppendLine("|---|---|---|---|---|---|");
            Phase a = P("flowing-1x"), b = P("no-draw");
            md.AppendLine($"| `flowing-1x` | {F(a.RenderCpu.P95)} | {F(a.RenderGpu.P95)} | {a.Draws:F0} | {a.Objects:F0} | {a.Nodes:F0} |");
            md.AppendLine($"| `no-draw` | {F(b.RenderCpu.P95)} | {F(b.RenderGpu.P95)} | {b.Draws:F0} | {b.Objects:F0} | {b.Nodes:F0} |");
            md.AppendLine($"| Difference (world drawing) | {F(a.RenderCpu.P95 - b.RenderCpu.P95)} | {F(a.RenderGpu.P95 - b.RenderGpu.P95)} | {a.Draws - b.Draws:F0} | {a.Objects - b.Objects:F0} | |");
            Phase u = P("unlit");
            md.AppendLine($"| `unlit` | {F(u.RenderCpu.P95)} | {F(u.RenderGpu.P95)} | {u.Draws:F0} | {u.Objects:F0} | {u.Nodes:F0} |");
            md.AppendLine($"| Difference (lighting, ≤ {LightingRenderBudget:F1} CPU) | {F(a.RenderCpu.P95 - u.RenderCpu.P95)} | {F(a.RenderGpu.P95 - u.RenderGpu.P95)} | {a.Draws - u.Draws:F0} | {a.Objects - u.Objects:F0} | |");

            md.AppendLine().AppendLine("### Lines").AppendLine().AppendLine("```");
            foreach (Phase p in _phases)
            {
                md.AppendLine(Line(p));
                md.AppendLine("  sections p95 ms/frame: " + Sections(p));
            }
            md.AppendLine("```").AppendLine();
            md.AppendLine(failures.Count == 0 ? "Verdict: PASS" : "Verdict: FAIL · " + string.Join("; ", failures));

            using FileAccess file = FileAccess.Open("user://perf-report.md", FileAccess.ModeFlags.Write);
            file?.StoreString(md.ToString());
        }
    }
}
