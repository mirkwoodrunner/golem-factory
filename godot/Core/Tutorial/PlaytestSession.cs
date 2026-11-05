using GolemFactory.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GolemFactory.Diagnostics;

namespace GolemFactory.Tutorial
{
    /// <summary>
    /// A judgement question the playtest needs answered: one no test can reach (G10, at the
    /// user's call: "integrate the playtest script into the tutorial"). Asked when the guide
    /// first reaches <see cref="AfterStepId"/>, the moment the player has just lived through
    /// what it asks about.
    /// </summary>
    public sealed class PlaytestQuestion
    {
        public string Id { get; }
        private readonly string _prompt;

        /// <summary>The question, its key names and colour words filled (TextTokens).</summary>
        public string Prompt => TextTokens.Fill(_prompt);
        public IReadOnlyList<string> Options { get; }
        public string AfterStepId { get; }

        public PlaytestQuestion(string id, string afterStepId, string prompt, params string[] options)
        {
            Id = id;
            AfterStepId = afterStepId;
            _prompt = prompt;
            Options = options;
        }
    }

    /// <summary>One answer: the option picked (or "skipped") and the player's note.</summary>
    public sealed class PlaytestAnswer
    {
        public string QuestionId;
        public string Prompt;
        public string Choice;
        public string Note;
        public float AtSeconds;
    }

    /// <summary>
    /// Playtest mode: the questions from testscript/phase-1-playtest.md asked in the guide at the
    /// right moment, how long each step took, and anything the game logged as an error, gathered
    /// into one report the player sends back. Replaces the hand-annotated markdown checklist.
    ///
    /// <para>
    /// Engine-free: the clock is the caller's (real seconds, not simulation ticks -- "how long did
    /// the manual era take" is a question about the player's afternoon, not the factory's), and
    /// <see cref="Compose"/> returns text; the Godot layer writes it to <c>user://</c>.
    /// </para>
    /// </summary>
    public sealed class PlaytestSession
    {
        /// <summary>
        /// The script's questions, keyed to the step that FOLLOWS the experience they ask about:
        /// the light on the first step, the manual era once the first Presser runs, the market
        /// once Coal has been bought, the burn once the coal line feeds its boiler. Each chapter's
        /// question is asked as the next chapter opens; only chapter 10's, the run's and the guide's own wait
        /// for the end. (Nine used to wait for "done", after the Clock Tower's Foundation, so a
        /// playtest that stopped sooner never asked them, and one that didn't asked hours late.)
        /// </summary>
        public static IReadOnlyList<PlaytestQuestion> Questions { get; } = new[]
        {
            new PlaytestQuestion("light", "scrap",
                "Look around the workshop. Is the room too dark?",
                "Too dark", "About right", "Not dramatic enough"),
            new PlaytestQuestion("workbench", "depot",
                "You just programmed your first golem. Did the Workbench feel like making a decision, or like busywork?",
                "A real decision", "Somewhere between", "Busywork"),
            new PlaytestQuestion("hint", "depot",
                "The glowing card and socket: did they show you what to drag?",
                "Clear", "Took a moment", "In my way"),
            new PlaytestQuestion("manual-era", "coking-card",
                "Your first machine is running. How did the hand-gathering and cranking before it feel?",
                "Miserable", "Long but fine", "About right", "Too short"),
            new PlaytestQuestion("walking", "coking-card",
                "How much of that time was walking between the workshop and the stalls?",
                "Most of it", "About half", "Not much"),
            new PlaytestQuestion("carry", "gears",
                "When you carried the golem, did the outline on the floor make it clear where it would land?",
                "Clear", "Mostly", "I still guessed"),
            new PlaytestQuestion("market", "program-coker",
                "You bought Coal by the truckload. Does the market feel like an economy, or a toll booth?",
                "An economy", "In between", "A toll booth"),
            new PlaytestQuestion("patent", "depot3",
                "You stamped a patent onto a new golem. Did that feel obviously better than programming it again?",
                "Obviously", "A little", "No difference"),
            new PlaytestQuestion("labels", "unloader",
                "You labelled a depot on the crate screen. Did the picker work?",
                "Good", "Fiddly", "Couldn't find it"),
            new PlaytestQuestion("drag", "scav3",
                "Did laying belts and pipes by dragging feel good?",
                "Good", "Fiddly", "Didn't work how I expected"),
            new PlaytestQuestion("tower", "done",
                "The Clock Tower now waits in the town square until the end game. Does it feel like the goal "
                + "the whole factory was for?",
                "Yes", "Not yet (say why)", "It should come sooner"),
            new PlaytestQuestion("zeppelin", "tower-visit",
                "The Zeppelin was the end of a long chain. Did flying goods to a mast feel worth it?",
                "Worth it", "Underwhelming", "Didn't get it working (say what)"),
            new PlaytestQuestion("goals", "zeppelin-card",
                "Chapter 8 named a good and left the building to you. Did you know what to build next?",
                "Yes", "Mostly", "I was lost (say where)"),
            new PlaytestQuestion("save-load", "copper-ingot",
                "After you loaded, did everything come back as you left it?",
                "Everything", "Something was missing (say what)", "Something was wrong (say what)"),
            new PlaytestQuestion("slag", "copper",
                "Slag had to be carried away, with Coke to burn it. A real problem worth solving, or a chore?",
                "A real problem", "Somewhere between", "A chore"),
            new PlaytestQuestion("marked-tiles", "copper",
                "Chapter 5 marked a tile for everything you built. Did the marks help, or did you want to place things your own way?",
                "They helped", "Fine either way", "In my way (say where)"),
            // Phase 2's hint, turning, grey and red, and hover (testscript/phase-2-playtest.md).
            new PlaytestQuestion("turning", "r4-card",
                "Turning golems with [[rotate]]: how did it go?",
                "Easy", "Fiddly", "I turned the wrong one"),
            new PlaytestQuestion("grey-red", "r4-card",
                "Waiting golems go grey, stuck ones red. Could you tell which needed you?",
                "Always", "Mostly", "No"),
            new PlaytestQuestion("hover", "r4-card",
                "Hovering a golem shows its arrows and, when stuck, why. Did you use it?",
                "Often", "Once or twice", "Didn't know"),
            new PlaytestQuestion("badge", "r4-card",
                "When the golem stopped red, did its badge and hover tell you what was wrong?",
                "Clearly", "Vaguely", "I didn't hover it"),
            new PlaytestQuestion("coke-burn", "patent",
                "Your coal line feeds its boiler. Is Coke upkeep a cost worth managing, or a coal simulator?",
                "Burns too fast", "About right", "Too slow to matter"),
            // Phase 1's crates and pack (testscript/phase-1-playtest.md, "The questions").
            new PlaytestQuestion("collect", "gears",
                "Collecting from the crate with [[interact]]: did taking what fits feel easy, or like a chore?",
                "Easy", "Fine", "A chore"),
            new PlaytestQuestion("pack-full", "coking-card",
                "Did you fill your pack to 100 of something? Did \"pack full\" tell you what to do?",
                "Never filled it", "Filled it and knew", "Filled it and was stuck"),
            new PlaytestQuestion("hand-feed", "slag-heap",
                "You fed the smelter's crate by hand on the crate screen. Was it clear?",
                "Clear", "Fiddly", "I couldn't find it"),
            new PlaytestQuestion("crate-stall", "copper",
                "Did a golem stop because its crate was full or empty? Did its badge say why?",
                "Never saw it", "Yes, clearly", "Yes, but I was confused"),
            new PlaytestQuestion("straining", "expand",
                "A busy golem's \"nearly full\" is on hover only. Did you ever miss a warning?",
                "Never", "Once (say where)", "Often"),
            new PlaytestQuestion("layout", "expand",
                "Has where you put things started to matter: walks between crates, or a line that needed a belt?",
                "It matters", "A little", "Not yet"),
            // docs/pack-upgrades-design.md §6.
            new PlaytestQuestion("pack-upgrade", "ledger",
                "The pack upgrade: did you need the room, and was it easy to find?",
                "Needed it", "Nice to have", "Didn't notice it"),
            new PlaytestQuestion("dear-room", "frame-section",
                "Later extensions cost Brass, Casing, Glass and Mainsprings. Was spending them on room worth it?",
                "Worth it", "Too dear", "Never got that far"),
            new PlaytestQuestion("post", "glass",
                "The Pneumatic Post: did it take a chore off you, or add one?",
                "Took one off", "No difference", "Added one"),
            new PlaytestQuestion("mast-crate", "tower-visit",
                "The Copper landed in the mast's crate. Did you find it?",
                "Straight away", "Eventually", "No"),
            // Phase 3's full playthrough (testscript/full-playthrough.md, "The questions"). Its
            // `market` is `market-late` here: chapter 3's `market` question already holds that
            // saved name. The last three are about chapter 10 and the run, so they wait for the end.
            new PlaytestQuestion("coal-share", "copper-ingot",
                "How much of your factory was feeding the boilers?",
                "About right", "Too much", "Hardly noticed it"),
            new PlaytestQuestion("market-late", "post",
                "Ordering truckloads at the stalls:",
                "Fine", "A chore", "I ran out a lot"),
            new PlaytestQuestion("lens-glass", "aether-cell",
                "Was there enough Glass for your Lenses?",
                "Plenty", "Just enough", "Never enough"),
            new PlaytestQuestion("overclocker", "done",
                "Did you use Repeat on the Overclocker?",
                "Often", "Tried it", "Didn't know it existed"),
            new PlaytestQuestion("stage-rate", "done",
                "How fast did the Foundation build at your rate?",
                "Fair", "Slow", "It kept freezing"),
            new PlaytestQuestion("whole-run", "done",
                "Which chapter dragged?",
                "None", "5", "6", "7", "8", "9", "10"),
            new PlaytestQuestion("guide", "done",
                "Was this guide clear?",
                "Clear", "Mostly", "Confusing"),
        };

        private readonly List<PlaytestAnswer> _answers = new List<PlaytestAnswer>();
        private readonly List<(string stepId, string title, float enteredAt, int chapter)> _steps = new List<(string, string, float, int)>();
        private readonly List<string> _errors = new List<string>();
        private readonly HashSet<string> _asked = new HashSet<string>();
        private readonly Queue<PlaytestQuestion> _pending = new Queue<PlaytestQuestion>();

        /// <summary>Bumped on every change, so the Godot layer rewrites the report only when it moved.</summary>
        public int Version { get; private set; }

        /// <summary>The question on screen now, or null.</summary>
        public PlaytestQuestion Current => _pending.Count > 0 ? _pending.Peek() : null;

        public IReadOnlyList<PlaytestAnswer> Answers => _answers;
        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> KitUses => _kitUses;
        private readonly List<string> _kitUses = new List<string>();

        /// <summary>The guide entered a step: time it, and queue any question that waits for it.</summary>
        public void StepEntered(string stepId, string title, float atSeconds, int chapter = 0)
        {
            if (_steps.Count > 0 && _steps[_steps.Count - 1].stepId == stepId)
            {
                return;
            }
            _steps.Add((stepId, title, atSeconds, chapter));
            foreach (PlaytestQuestion question in Questions)
            {
                if (question.AfterStepId == stepId && _asked.Add(question.Id))
                {
                    _pending.Enqueue(question);
                }
            }
            Version++;
        }

        /// <summary>The player picked <paramref name="choice"/> (one of the options, or "skipped").</summary>
        public void Answer(string choice, string note, float atSeconds)
        {
            PlaytestQuestion question = Current;
            if (question == null)
            {
                return;
            }
            _pending.Dequeue();
            _answers.Add(new PlaytestAnswer
            {
                QuestionId = question.Id,
                Prompt = question.Prompt,
                Choice = choice,
                Note = string.IsNullOrWhiteSpace(note) ? "" : note.Trim(),
                AtSeconds = atSeconds,
            });
            Version++;
        }

        public void Skip(float atSeconds) => Answer("skipped", "", atSeconds);

        /// <summary>Something the game logged as an error or warning, with when.</summary>
        public void RecordError(string text, float atSeconds)
        {
            if (string.IsNullOrWhiteSpace(text) || _errors.Count >= 200)
            {
                return;
            }
            _errors.Add($"{Clock(atSeconds)}  {text.Trim()}");
            Version++;
        }

        /// <summary>The playtest kit was used to jump ahead; the report must say so.</summary>
        public void RecordKit(string what, float atSeconds)
        {
            _kitUses.Add($"{Clock(atSeconds)}  {what}");
            _kitTimes.Add(atSeconds);
            Version++;
        }

        // --- Frame rate (docs/performance-report-template.md §5) -------------------------------

        private sealed class ChapterFrames
        {
            public readonly FrameTimeStats Ms = new FrameTimeStats(4096);
            public int Golems;
            public int BeltItems;
        }

        private readonly SortedDictionary<int, ChapterFrames> _frames = new SortedDictionary<int, ChapterFrames>();

        /// <summary>
        /// One frame's wall milliseconds, in the guide's current <paramref name="chapter"/>, with
        /// the golems and belt items standing then (the table keeps a chapter's last). Doesn't
        /// bump <see cref="Version"/>: the report picks the frames up at its next rewrite.
        /// </summary>
        public void RecordFrame(int chapter, double milliseconds, int golems, int beltItems)
        {
            if (!_frames.TryGetValue(chapter, out ChapterFrames frames))
            {
                _frames[chapter] = frames = new ChapterFrames();
            }
            frames.Ms.Add(milliseconds);
            frames.Golems = golems;
            frames.BeltItems = beltItems;
        }

        // --- Chapters ------------------------------------------------------------------------

        /// <summary>Each chapter's human budget, in seconds (docs/tuning-data-spec.md §14), chapters 1-10.</summary>
        public static readonly float[] ChapterBudgetSeconds = { 300, 270, 210, 120, 1200, 900, 600, 2700, 1800, 2700 };

        /// <summary>The guide's last step: chapters 1-10 are timed to it.</summary>
        public const string DoneStepId = "done";

        /// <summary>The full playthrough's target (testscript/full-playthrough.md), in seconds.</summary>
        public const float ChaptersOneToTenTargetSeconds = 3 * 3600;

        /// <summary>
        /// Each chapter's time, from its first step to the first step of the next (or to
        /// <c>done</c> for the last), and whether it is still running.
        /// </summary>
        private IEnumerable<(int chapter, float seconds, bool running)> ChapterTimes(float nowSeconds)
        {
            var starts = new SortedDictionary<int, float>();
            foreach ((string _, string _, float at, int chapter) in _steps)
            {
                if (chapter > 0 && !starts.ContainsKey(chapter))
                {
                    starts[chapter] = at;
                }
            }
            float? done = _steps.Where(st => st.stepId == DoneStepId).Select(st => (float?)st.enteredAt).FirstOrDefault();
            List<int> chapters = starts.Keys.ToList();
            for (int i = 0; i < chapters.Count; i++)
            {
                float start = starts[chapters[i]];
                float? end = i + 1 < chapters.Count ? starts[chapters[i + 1]] : done;
                yield return (chapters[i], (end ?? nowSeconds) - start, end == null);
            }
        }

        /// <summary>
        /// "Chapters 1–10: 2:41:07 (target 3:00:00) · kit used: no", from the session's start to
        /// reaching <c>done</c>; null until then.
        /// </summary>
        public string ChaptersOneToTen()
        {
            int at = _steps.FindIndex(st => st.stepId == DoneStepId);
            if (at < 0)
            {
                return null;
            }
            float reached = _steps[at].enteredAt;
            bool kit = _kitTimes.Any(t => t <= reached);
            return $"Chapters 1–10: {Clock(reached)} (target {Clock(ChaptersOneToTenTargetSeconds)}) · kit used: {(kit ? "yes" : "no")}";
        }

        private static string Ms(double ms) => ms.ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>The report, as Markdown: answers, step timings, kit uses, errors.</summary>
        public string Compose(string header, float nowSeconds)
        {
            var b = new StringBuilder();
            b.AppendLine("# Golem Factory playtest report");
            b.AppendLine();
            if (!string.IsNullOrEmpty(header))
            {
                b.AppendLine(header);
                b.AppendLine();
            }
            b.AppendLine($"Session length: {Clock(nowSeconds)}");
            b.AppendLine();
            string chapters = ChaptersOneToFour();
            if (chapters != null)
            {
                b.AppendLine(chapters);
                b.AppendLine();
            }
            string allChapters = ChaptersOneToTen();
            if (allChapters != null)
            {
                b.AppendLine(allChapters);
                b.AppendLine();
            }

            b.AppendLine("## Time per chapter");
            b.AppendLine();
            b.AppendLine("| Chapter | Time | Budget |");
            b.AppendLine("|---|---|---|");
            foreach ((int chapter, float seconds, bool running) in ChapterTimes(nowSeconds))
            {
                string budget = chapter >= 1 && chapter <= ChapterBudgetSeconds.Length ? Clock(ChapterBudgetSeconds[chapter - 1]) : "";
                b.AppendLine($"| {chapter} | {Clock(seconds)}{(running ? " (so far)" : "")} | {budget} |");
            }
            b.AppendLine();

            b.AppendLine("## Your answers");
            b.AppendLine();
            if (_answers.Count == 0)
            {
                b.AppendLine("None yet.");
            }
            foreach (PlaytestAnswer a in _answers)
            {
                b.AppendLine($"- **{a.Prompt}** {a.Choice}{(a.Note.Length > 0 ? " — " + a.Note : "")} _(at {Clock(a.AtSeconds)})_");
            }
            b.AppendLine();

            b.AppendLine("## Time on each step");
            b.AppendLine();
            b.AppendLine("| Step | Started | Took |");
            b.AppendLine("|---|---|---|");
            for (int i = 0; i < _steps.Count; i++)
            {
                float end = i + 1 < _steps.Count ? _steps[i + 1].enteredAt : nowSeconds;
                b.AppendLine($"| {_steps[i].title} | {Clock(_steps[i].enteredAt)} | {Clock(end - _steps[i].enteredAt)} |");
            }
            b.AppendLine();

            b.AppendLine("## Frame rate");
            b.AppendLine();
            b.AppendLine("| Chapter | Frames | p50 ms | p95 ms | worst ms | golems | belt items |");
            b.AppendLine("|---|---|---|---|---|---|---|");
            foreach (KeyValuePair<int, ChapterFrames> entry in _frames)
            {
                FrameTimeStats ms = entry.Value.Ms;
                b.AppendLine($"| {entry.Key} | {ms.Count} | {Ms(ms.P50)} | {Ms(ms.P95)} | {Ms(ms.Max)} | {entry.Value.Golems} | {entry.Value.BeltItems} |");
            }
            b.AppendLine();

            b.AppendLine("## Playtest kit");
            b.AppendLine();
            b.AppendLine(_kitUses.Count == 0 ? "Not used: everything above was played for real." : "Used, so timings after these points are not real play:");
            foreach (string k in _kitUses)
            {
                b.AppendLine("- " + k);
            }
            b.AppendLine();

            b.AppendLine("## Errors the game logged");
            b.AppendLine();
            b.AppendLine(_errors.Count == 0 ? "None." : "```");
            foreach (string e in _errors)
            {
                b.AppendLine(e);
            }
            if (_errors.Count > 0)
            {
                b.AppendLine("```");
            }
            return b.ToString();
        }

        /// <summary>The step that opens chapter 5: chapters 1-4 are timed to it.</summary>
        public const string ChapterFiveStepId = "r4-card";

        /// <summary>Phase 2's target (testscript/phase-2-playtest.md), in seconds.</summary>
        public const float ChaptersOneToFourTargetSeconds = 15 * 60;

        /// <summary>
        /// "Chapters 1–4: 12:34 (target 15:00) · kit used: no", from the session's start to
        /// entering chapter 5; null until the player gets there. Any kit use before then voids
        /// the timing, and the line says so.
        /// </summary>
        public string ChaptersOneToFour()
        {
            int at = _steps.FindIndex(s => s.stepId == ChapterFiveStepId);
            if (at < 0)
            {
                return null;
            }
            float reached = _steps[at].enteredAt;
            bool kit = _kitTimes.Any(t => t <= reached);
            return $"Chapters 1–4: {Clock(reached)} (target {Clock(ChaptersOneToFourTargetSeconds)}) · kit used: {(kit ? "yes" : "no")}";
        }

        private readonly List<float> _kitTimes = new List<float>();

        private static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)seconds);
            return s >= 3600
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", s / 60, s % 60);
        }
    }
}
