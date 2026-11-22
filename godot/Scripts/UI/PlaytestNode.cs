using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Godot;
using GolemFactory.Tutorial;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Playtest mode's Godot half (G10): the hook that records what the game logs as an error,
    /// each frame's wall time, and Core's <see cref="PlaytestSession"/> report, kept current on
    /// disk as the session changes so a crash still leaves one behind. (Its "Your call" card,
    /// which asked the playtest questions, was removed on 2026-11-21.)
    /// </summary>
    public partial class PlaytestNode : CanvasLayer
    {
        public const string ReportPath = "user://playtest-report.md";

        private WorldNode _world;
        private PlaytestSession _session;
        private PlaytestLogger _logger;
        private int _writtenVersion = -1;
        private float _sinceWrite;

        /// <summary>
        /// Where the report goes. Scenario runs write their own file, so a test run can never
        /// overwrite the report from a real playtest.
        /// </summary>
        public static string ReportFile => ProjectSettings.GlobalizePath(UnderScenario ? ScenarioReportPath : ReportPath);

        public const string ScenarioReportPath = "user://scenario-playtest-report.md";

        /// <summary>
        /// Read from the command line itself, never from node order: a flag set by the
        /// ScenarioRunner arrived after ManagementScreen had already chosen the player's real
        /// save slot, and a kit run overwrote a real save.json.
        /// </summary>
        public static bool UnderScenario
        {
            get => _underScenario ?? (bool)(_underScenario = System.Array.Exists(OS.GetCmdlineUserArgs(),
                a => a == "--scenario" || a == "--demo" || a == "--spike-demo"));
            set => _underScenario = value;
        }

        private static bool? _underScenario;

        public override void _Ready()
        {
            Layer = 41;
            _world = WorldNode.Find(this);
            _session = _world?.Sandbox?.Tutorial?.Playtest;
            if (_session == null)
            {
                return;
            }

            _logger = new PlaytestLogger();
            OS.AddLogger(_logger);
            ArchivePreviousReport();
            WriteReport();
        }

        public override void _ExitTree()
        {
            if (_logger != null)
            {
                OS.RemoveLogger(_logger);
                _logger = null;
            }
            if (_session != null)
            {
                WriteReport();
            }
        }

        private float Now => _world.Sandbox.Tutorial.Now;

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.playtest");
            if (_session == null)
            {
                return;
            }

            while (_logger.Lines.TryDequeue(out string line))
            {
                _session.RecordError(line, Now);
            }

            RecordFrame();

            // Rewrite the report when it changed, at most once a second.
            _sinceWrite += (float)delta;
            if (_session.Version != _writtenVersion && _sinceWrite >= 1f)
            {
                WriteReport();
            }
        }

        private ulong _lastFrameUsec;

        /// <summary>
        /// The frame's wall time, in the guide's chapter, for the report's "Frame rate" table
        /// (docs/performance-report-template.md §5): the time since this node's last frame, the
        /// whole frame, process and render.
        /// </summary>
        private void RecordFrame()
        {
            ulong now = Time.GetTicksUsec();
            ulong last = _lastFrameUsec;
            _lastFrameUsec = now;
            World.SandboxWorld sandbox = _world?.Sandbox;
            if (last == 0 || sandbox?.Tutorial == null)
            {
                return;
            }
            int belts = 0;
            foreach (World.PlacedBelt belt in sandbox.Belts.Belts)
            {
                belts += belt.Segment?.Items.Count ?? 0;
            }
            _session.RecordFrame(sandbox.Tutorial.ChapterOf(sandbox.Tutorial.Index), (now - last) / 1000.0,
                sandbox.Golems.Count(g => !g.IsRemoved), belts);
        }

        /// <summary>
        /// Keeps the last session's report under a dated name before this session starts its
        /// own. Every launch used to overwrite it on the first frame, so a playtest played over
        /// several sittings kept only the last one, and relaunching after a crash erased the
        /// report the crash had left (from review). Scenario reports are scratch and are not kept.
        /// </summary>
        private static void ArchivePreviousReport()
        {
            if (UnderScenario)
            {
                return;
            }
            try
            {
                string current = ReportFile;
                if (!File.Exists(current))
                {
                    return;
                }
                string archive = PlaytestReportArchive.NameFor(current, File.GetLastWriteTime(current), File.Exists);
                File.Move(current, archive);
            }
            catch (Exception e)
            {
                GD.Print("[playtest] could not keep the previous report: " + e.Message);
            }
        }

        private void WriteReport()
        {
            _sinceWrite = 0f;
            _writtenVersion = _session.Version;
            try
            {
                string header =
                    $"Written {DateTime.Now:yyyy-MM-dd HH:mm} · Godot {Engine.GetVersionInfo()["string"]} · {OS.GetName()}";
                File.WriteAllText(ReportFile, _session.Compose(header, Now));
            }
            catch (Exception e)
            {
                // Never let the report break the game it is reporting on.
                GD.Print("[playtest] could not write the report: " + e.Message);
            }
        }

        /// <summary>
        /// Every error and warning the engine or the game logs, queued for the main thread: a
        /// Logger may be called from any thread.
        /// </summary>
        private sealed partial class PlaytestLogger : Logger
        {
            public readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();

            public override void _LogError(string function, string file, int line, string code, string rationale,
                bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
            {
                string kind = errorType == (int)ErrorType.Warning ? "WARNING" : "ERROR";
                string what = string.IsNullOrEmpty(rationale) ? code : rationale;
                Lines.Enqueue($"{kind}: {what} ({Path.GetFileName(file)}:{line} {function})");
            }
        }
    }
}
