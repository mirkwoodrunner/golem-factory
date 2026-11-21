using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GolemFactory.Diagnostics;

namespace GolemFactory.Tutorial
{
    /// <summary>
    /// Playtest mode: how long each chapter and step took, the frame rate, any use of the playtest
    /// kit, and anything the game logged as an error, gathered into one report the player sends
    /// back. (It also asked the playtest script's questions in the guide until 2026-11-21, when
    /// the user cleared them.)
    ///
    /// <para>
    /// Engine-free: the clock is the caller's (real seconds, not simulation ticks -- "how long did
    /// the manual era take" is a question about the player's afternoon, not the factory's), and
    /// <see cref="Compose"/> returns text; the Godot layer writes it to <c>user://</c>.
    /// </para>
    /// </summary>
    public sealed class PlaytestSession
    {
        private readonly List<(string stepId, string title, float enteredAt, int chapter)> _steps = new List<(string, string, float, int)>();
        private readonly List<string> _errors = new List<string>();

        /// <summary>Bumped on every change, so the Godot layer rewrites the report only when it moved.</summary>
        public int Version { get; private set; }

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> KitUses => _kitUses;
        private readonly List<string> _kitUses = new List<string>();

        /// <summary>The guide entered a step: time it.</summary>
        public void StepEntered(string stepId, string title, float atSeconds, int chapter = 0)
        {
            if (_steps.Count > 0 && _steps[_steps.Count - 1].stepId == stepId)
            {
                return;
            }
            _steps.Add((stepId, title, atSeconds, chapter));
            Version++;
        }

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

        /// <summary>The report, as Markdown: chapter and step timings, frame rate, kit uses, errors.</summary>
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
