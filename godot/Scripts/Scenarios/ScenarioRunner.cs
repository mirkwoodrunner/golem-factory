using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>A scenario's verdict: whether it passed, and one line saying why.</summary>
    public readonly struct ScenarioResult
    {
        public readonly bool Passed;
        public readonly string Report;

        public ScenarioResult(bool passed, string report)
        {
            Passed = passed;
            Report = report;
        }
    }

    /// <summary>
    /// One end-to-end check, driven against the real scene. <see cref="Step"/> runs every frame
    /// until it returns a result.
    /// </summary>
    public interface IScenario
    {
        void Begin(ScenarioRunner runner);

        /// <summary>Null to keep running; a result to finish.</summary>
        ScenarioResult? Step(double delta);
    }

    /// <summary>
    /// A scenario that goes on across a scene reload (the title's New game and Continue reload
    /// the scene, docs/menus-design.md §6). The runner of the new scene hands it back here; its
    /// old nodes are gone, so it finds them again.
    /// </summary>
    public interface IReloadingScenario : IScenario
    {
        void Resumed(ScenarioRunner runner);
    }

    /// <summary>
    /// The headless end-to-end checks (docs/godot-conversion-plan.md, "Verification"):
    ///
    /// <code>
    /// Godot_console.exe --headless --path godot --fixed-fps 60 -- --scenario &lt;name&gt;
    /// </code>
    ///
    /// Inert unless a scenario is named after the <c>--</c>. It runs the scenario to a verdict,
    /// prints <c>[scenario &lt;name&gt;] PASS|FAIL ...</c>, and quits with exit code 0 or 1 --
    /// which is what lets a milestone prove itself without anyone watching. <c>--fixed-fps 60</c>
    /// makes every frame 1/60 s of game time, so runs are deterministic and as fast as the
    /// machine allows.
    ///
    /// <para>
    /// <c>--demo</c> (or the spike's <c>--spike-demo</c>) only performs the scripted setup -- the
    /// station builds its golem -- without checking or quitting, for rendering frames with
    /// <c>--write-movie</c>. The spike's <c>--spike-check</c> is an alias for
    /// <c>--scenario loop</c>.
    /// </para>
    /// </summary>
    public partial class ScenarioRunner : Node
    {
        private static readonly Dictionary<string, Func<IScenario>> Scenarios =
            new Dictionary<string, Func<IScenario>>(StringComparer.Ordinal)
            {
                ["loop"] = () => new LoopScenario(),
                ["font-glyphs"] = () => new FontGlyphsScenario(),
                ["world"] = () => new WorldScenario(),
                ["build"] = () => new BuildScenario(),
                ["interact"] = () => new InteractScenario(),
                ["golems"] = () => new GolemsScenario(),
                ["golem-motion"] = () => new GolemMotionScenario(),
                ["workbench"] = () => new WorkbenchScenario(),
                ["management"] = () => new ManagementScenario(),
                ["save"] = () => new SaveScenario(),
                ["tutorial"] = () => new TutorialScenario(),
                ["playtest-kit"] = () => new PlaytestKitScenario(),
                ["inventory"] = () => new InventoryScenario(),
                ["moods"] = () => new MoodsScenario(),
                ["hover"] = () => new HoverScenario(),
                ["perf"] = () => new PerfScenario(),
                ["art"] = () => new ArtScenario(),
                ["audio"] = () => new AudioScenario(),
                ["feel"] = () => new FeelScenario(),
                ["settings"] = () => new SettingsScenario(),
                ["accessibility"] = () => new AccessibilityScenario(),
                ["title"] = () => new TitleScenario(),
                ["slots"] = () => new SlotsScenario(),
                ["overview"] = () => new OverviewScenario(),
                ["creative"] = () => new CreativeScenario(),
                ["supply"] = () => new SupplyScenario(),
                ["contracts"] = () => new ContractsScenario(),
                ["endless"] = () => new EndlessScenario(),
                ["docks"] = () => new DocksScenario(),
                ["golem-sales"] = () => new GolemSalesScenario(),
                ["gamepad"] = () => new GamepadScenario(),
                ["guide-ahead"] = () => new GuideAheadScenario(),
                ["gamepad-ui"] = () => new GamepadUiScenario(),
            };

        [Export] public NodePath StationPath { get; set; }
        [Export] public NodePath DepotPath { get; set; }

        private string _name;
        private IScenario _scenario;

        // A scenario still running when its scene went: the next scene's runner takes it up.
        private static (string name, IScenario scenario)? _carried;

        public ConstructionStationNode Station => GetNode<ConstructionStationNode>(StationPath);
        public DepotNode Depot => GetNode<DepotNode>(DepotPath);
        public WorldNode World => WorldNode.Find(this);

        /// <summary>The names <c>--scenario</c> accepts.</summary>
        public static IEnumerable<string> Names => Scenarios.Keys;

        public override void _EnterTree()
        {
            // Before any _Ready: a scenario run must not write over a real playtest's report.
            PlaytestNode.UnderScenario = Array.IndexOf(OS.GetCmdlineUserArgs(), "--scenario") >= 0
                || Array.IndexOf(OS.GetCmdlineUserArgs(), "--spike-check") >= 0
                || Array.IndexOf(OS.GetCmdlineUserArgs(), "--demo") >= 0
                || Array.IndexOf(OS.GetCmdlineUserArgs(), "--spike-demo") >= 0;
        }

        public override void _Ready()
        {
            if (_carried is (string carriedName, IScenario carried))
            {
                _carried = null;
                _name = carriedName;
                _scenario = carried;
                if (carried is IReloadingScenario reloading)
                {
                    Callable.From(() => reloading.Resumed(this)).CallDeferred();
                }
                else
                {
                    Finish(new ScenarioResult(false, "the scene reloaded mid-scenario"));
                }
                return;
            }

            string[] args = OS.GetCmdlineUserArgs();
            _name = ScenarioName(args);

            // Headless runs get a 64x64 window, which puts the bottom-left build menu above the
            // top of the screen and makes every screen-space check meaningless. Give them the
            // window a player has.
            if (DisplayServer.GetName() == "headless")
            {
                GetTree().Root.Size = new Vector2I(
                    (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1280),
                    (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 720));
            }

            if (_name == null)
            {
                if (args.Contains("--demo") || args.Contains("--spike-demo"))
                {
                    // Deferred so every sibling has finished _Ready before a golem joins the tree.
                    Callable.From(Station.Interact).CallDeferred();
                }
                SetProcess(false);
                return;
            }

            if (!Scenarios.TryGetValue(_name, out Func<IScenario> make))
            {
                Finish(new ScenarioResult(false,
                    $"no scenario named '{_name}' (known: {string.Join(", ", Names)})"));
                return;
            }

            _scenario = make();
            // Deferred for the same reason: scenarios drive nodes that must already be ready.
            Callable.From(() => _scenario.Begin(this)).CallDeferred();
        }

        public override void _ExitTree()
        {
            if (_scenario != null)
            {
                _carried = (_name, _scenario);
            }
        }

        public override void _Process(double delta)
        {
            if (_scenario == null)
            {
                return;
            }

            WatchFrame();
            ScenarioResult? result = _scenario.Step(delta);
            if (result.HasValue)
            {
                Finish(result.Value);
            }
        }

        // --- The frame watch (docs/steam-deck-design.md D8) --------------------------------------
        // `--frame-watch` reports a scenario's worst frames of wall time, each with the step it fell
        // in and its split by phase: a slow open shows as one long frame there, and its phase says
        // where to look. The frames just after a scene loads are loading, not play, and are left
        // out. Static, so a scenario that reloads the scene (to the title) is watched across the
        // reload. Don't print from the code being watched: on Windows a print costs milliseconds.

        /// <summary>Frames after a scene load that count as loading. <b>TUNING.</b></summary>
        public const int FrameWatchSettle = 30;
        /// <summary>One watched frame: its wall time, split by stamps on the engine's loop.</summary>
        private readonly struct WatchedFrame
        {
            public readonly double Ms, Process, Layout, Draw;
            public readonly string Step;

            public WatchedFrame(double ms, double process, double layout, double draw, string step)
            {
                Ms = ms;
                Process = process;
                Layout = layout;
                Draw = draw;
                Step = step;
            }

            /// <summary>The physics step and its flush, before the process step.</summary>
            public double Physics { get; init; }

            /// <summary>What's left: input (which opens most screens), before the physics step.</summary>
            public double Input => Ms - Process - Layout - Draw - Physics;

            public override string ToString() =>
                $"{Ms:0.0} ms (input {Input:0.0}, physics {Physics:0.0}, process {Process:0.0}, layout {Layout:0.0}, draw {Draw:0.0}) at '{Step}'";
        }

        private static readonly List<WatchedFrame> AllFrames = new List<WatchedFrame>();
        private ulong _lastFrameUsec;
        private int _framesSinceLoad;

        private bool FrameWatchOn => OS.GetCmdlineUserArgs().Contains("--frame-watch");

        private void WatchFrame()
        {
            ulong now = Time.GetTicksUsec();
            ulong last = _lastFrameUsec;
            _lastFrameUsec = now;
            if (!FrameWatchOn)
            {
                return;
            }
            if (last == 0)
            {
                StartStamps();
            }
            if (last == 0 || ++_framesSinceLoad <= FrameWatchSettle)
            {
                return;
            }
            // The frame just gone, split by stamps on the engine's own loop: the nodes' process step
            // (a node first and a node last), then the layout and deferred calls up to the
            // renderer's pre-draw signal, the drawing to its post-draw, and the rest (input, which
            // opens most screens, and the wait for the next frame).
            // This frame's first node has run; its last hasn't: every stamp but the newest start is
            // the frame before's, which runs from the start before to this one.
            double ms = (_stampProcessStart - _stampPreviousStart) / 1000.0;
            double process = (_stampProcessEnd - _stampPreviousStart) / 1000.0;
            double layout = (_stampPreDraw - _stampProcessEnd) / 1000.0;
            double draw = (_stampPostDraw - _stampPreDraw) / 1000.0;
            string step = (_scenario as StepScenario)?.CurrentStepName ?? _name;
            // The physics step before this frame's process: its nodes, and the deferred calls and
            // layout the engine flushes after it (a screen opened by input lays out there).
            double physics = (_stampProcessStart - _stampPhysicsStart) / 1000.0;
            AllFrames.Add(new WatchedFrame(ms, process, layout, draw, step) { Physics = physics });
        }

        private static ulong _stampPhysicsStart;

        /// <summary>Notes the time the physics step starts: first of it, by its priority.</summary>
        private partial class PhysicsStamp : Node
        {
            public override void _PhysicsProcess(double delta) => _stampPhysicsStart = Time.GetTicksUsec();
        }

        // Stamps on the engine's loop, for the frame watch.
        private static ulong _stampPreviousStart, _stampProcessStart, _stampProcessEnd, _stampPreDraw, _stampPostDraw;

        private void StartStamps()
        {
            AddChild(new FrameStamp
            {
                ProcessPriority = int.MinValue,
                Stamp = t =>
                {
                    _stampPreviousStart = _stampProcessStart;
                    _stampProcessStart = t;
                },
            });
            AddChild(new FrameStamp { ProcessPriority = int.MaxValue, Stamp = t => _stampProcessEnd = t });
            AddChild(new PhysicsStamp { ProcessPhysicsPriority = int.MinValue });
            RenderingServer.FramePreDraw += () => _stampPreDraw = Time.GetTicksUsec();
            RenderingServer.FramePostDraw += () => _stampPostDraw = Time.GetTicksUsec();
        }

        /// <summary>Notes the time its process runs: first or last of the frame, by its priority.</summary>
        private partial class FrameStamp : Node
        {
            public Action<ulong> Stamp;

            public override void _Process(double delta) => Stamp?.Invoke(Time.GetTicksUsec());
        }

        /// <summary>
        /// The median frame, then the five worst, split by phase; with <c>--frame-watch-detail</c>,
        /// the three frames either side of the worst three. With vsync on, the wait for the display
        /// sits in "draw" (a normal frame's 18 ms at 50 Hz), and a frame that misses a refresh
        /// waits for the next whole one.
        /// </summary>
        private static string FrameWatchReport()
        {
            if (AllFrames.Count == 0)
            {
                return "[frame-watch] no frames after loading";
            }
            List<double> sorted = AllFrames.Select(f => f.Ms).OrderBy(f => f).ToList();
            double median = sorted[sorted.Count / 2];
            double medianProcess = AllFrames.Select(f => f.Process).OrderBy(f => f).ElementAt(AllFrames.Count / 2);
            IEnumerable<int> worst = Enumerable.Range(0, AllFrames.Count).OrderByDescending(i => AllFrames[i].Ms).Take(5);
            return $"[frame-watch] median {median:0.0} ms (process {medianProcess:0.0}) over {AllFrames.Count} frames; worst: "
                + string.Join("; ", worst.Select(i => AllFrames[i].ToString()))
                + (OS.GetCmdlineUserArgs().Contains("--frame-watch-detail")
                    ? "\n[frame-watch] around the worst three:\n" + string.Join("\n---\n", worst.Take(3).Select(i =>
                        string.Join("\n", Enumerable.Range(Math.Max(0, i - 3), Math.Min(AllFrames.Count, i + 4) - Math.Max(0, i - 3))
                            .Select(j => (j == i ? "  > " : "    ") + AllFrames[j]))))
                    : "");
        }

        private static string ScenarioName(string[] args)
        {
            int index = Array.IndexOf(args, "--scenario");
            if (index >= 0)
            {
                return index + 1 < args.Length ? args[index + 1] : "";
            }
            return args.Contains("--spike-check") ? "loop" : null;
        }

        private void Finish(ScenarioResult result)
        {
            if (FrameWatchOn)
            {
                GD.Print(FrameWatchReport());
            }
            GD.Print($"[scenario {_name}] {(result.Passed ? "PASS" : "FAIL")} {result.Report}");
            _scenario = null;
            SetProcess(false);
            GetTree().Quit(result.Passed ? 0 : 1);
        }
    }
}
