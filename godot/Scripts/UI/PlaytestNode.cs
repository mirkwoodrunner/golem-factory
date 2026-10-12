using System;
using System.Collections.Concurrent;
using System.IO;
using Godot;
using GolemFactory.Tutorial;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Playtest mode's Godot half (G10): the "Your call" card that asks Core's
    /// <see cref="PlaytestSession"/> questions, the hook that records what the game logs as an
    /// error, and the report file, kept current on disk as the session changes so a crash still
    /// leaves one behind.
    ///
    /// <para>
    /// The card takes the guide plate's place, under the Clock Tower panel, and the guide hides
    /// while it is up; neither shows over a full screen. Typing a note does not walk the player
    /// (<see cref="TextEntry"/>).
    /// </para>
    /// </summary>
    public partial class PlaytestNode : CanvasLayer
    {
        public const string ReportPath = "user://playtest-report.md";

        private static readonly Color Amber = new Color(0.95f, 0.72f, 0.33f, 1f);
        private static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f, 1f);
        private static readonly Color Dim = new Color(0.70f, 0.66f, 0.58f, 1f);
        private const float CardWidth = 340f;

        private WorldNode _world;
        private PlaytestSession _session;
        private PlaytestLogger _logger;
        private Panel _card;
        private Label _prompt;
        private HFlowContainer _options;
        private LineEdit _note;
        private Button _skip;
        private VBoxContainer _column;
        private PlaytestQuestion _shown;
        private int _writtenVersion = -1;
        private float _sinceWrite;

        /// <summary>For scenarios.</summary>
        public Control Card => _card;
        public string PromptText => _prompt?.Text ?? "";
        public HFlowContainer Options => _options;
        public LineEdit Note => _note;
        public Button SkipButton => _skip;
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
                a => a == "--scenario" || a == "--spike-check" || a == "--demo" || a == "--spike-demo"));
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
            BuildCard();
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
            if (_session == null)
            {
                return;
            }

            while (_logger.Lines.TryDequeue(out string line))
            {
                _session.RecordError(line, Now);
            }

            PlaytestQuestion question = _session.Current;
            bool show = question != null && !ModalScreens.AnyOpen(GetTree());
            _card.Visible = show;
            if (show && question != _shown)
            {
                ShowQuestion(question);
            }
            if (show)
            {
                // Grows to the question: a long prompt or a second row of answers pushed the Skip
                // button out of a fixed-height card.
                _card.Size = new Vector2(CardWidth, _column.GetCombinedMinimumSize().Y + 18f);
            }

            // Rewrite the report when it changed, at most once a second.
            _sinceWrite += (float)delta;
            if (_session.Version != _writtenVersion && _sinceWrite >= 1f)
            {
                WriteReport();
            }
        }

        private void BuildCard()
        {
            _card = Ugui.Image("PlaytestCard", Ugui.NineSlice("res://art/UI/Steampunk/steampunk_panel_iron_bolt.png", 12, 10, 12, 10), new Color(0.12f, 0.08f, 0.05f, 0.97f));
            // Only its buttons and note field take clicks; the plate's empty area passes them to
            // the world beneath, as the guide plate's does (a build click there was swallowed).
            _card.MouseFilter = Control.MouseFilterEnum.Ignore;
            _card.Visible = false;
            Ugui.Place(_card, 1f, 1f, 1f, 1f, -12f, -168f, CardWidth, 210f, 1f, 1f);
            AddChild(_card);

            var column = Ugui.Fill(new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore });
            _column = column;
            column.OffsetLeft = 14f;
            column.OffsetRight = -14f;
            column.OffsetTop = 10f;
            column.OffsetBottom = -8f;
            column.AddThemeConstantOverride("separation", 6);
            _card.AddChild(column);

            Label counter = Ugui.Text("Counter", "PLAYTEST  ·  your call  ·  goes in your report", 12, Dim);
            column.AddChild(counter);
            _prompt = Ugui.Text("Prompt", "", 14, Amber, bold: true);
            _prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            // Ugui.Text clips (a one-line HUD label); a clipping label asks for no height, so in a
            // container a wrapped prompt collapsed to nothing. This one grows to its lines.
            _prompt.ClipText = false;
            _prompt.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            _prompt.CustomMinimumSize = new Vector2(CardWidth - 28f, 0f);
            column.AddChild(_prompt);

            _options = new HFlowContainer { Name = "Options", MouseFilter = Control.MouseFilterEnum.Ignore };
            _options.AddThemeConstantOverride("h_separation", 6);
            _options.AddThemeConstantOverride("v_separation", 6);
            column.AddChild(_options);

            _note = new LineEdit { Name = "Note", PlaceholderText = "Add a note (optional), then pick an answer" };
            _note.AddThemeFontSizeOverride("font_size", 12);
            column.AddChild(_note);

            var footer = new HBoxContainer();
            footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            _skip = new Button { Name = "Skip", Flat = true, FocusMode = Control.FocusModeEnum.None, Text = "Skip question" };
            _skip.AddThemeFontSizeOverride("font_size", 12);
            _skip.AddThemeColorOverride("font_color", Dim);
            _skip.Pressed += () => Answer(null);
            footer.AddChild(_skip);
            column.AddChild(footer);
        }

        private void ShowQuestion(PlaytestQuestion question)
        {
            _shown = question;
            _prompt.Text = question.Prompt;
            foreach (Node child in _options.GetChildren())
            {
                _options.RemoveChild(child);
                child.QueueFree();
            }
            foreach (string option in question.Options)
            {
                var button = new Button
                {
                    Name = option.Replace(" ", "").Replace(",", ""),
                    Text = option,
                    FocusMode = Control.FocusModeEnum.None,
                };
                button.AddThemeFontSizeOverride("font_size", 12);
                string choice = option;
                button.Pressed += () => Answer(choice);
                _options.AddChild(button);
            }
            _note.Text = "";
        }

        private void Answer(string choice)
        {
            if (choice == null)
            {
                _session.Skip(Now);
            }
            else
            {
                _session.Answer(choice, _note.Text, Now);
            }
            _note.Text = "";
            _note.ReleaseFocus();
            _shown = null;
            WriteReport();
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
