using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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
        public string Prompt { get; }
        public IReadOnlyList<string> Options { get; }
        public string AfterStepId { get; }

        public PlaytestQuestion(string id, string afterStepId, string prompt, params string[] options)
        {
            Id = id;
            AfterStepId = afterStepId;
            Prompt = prompt;
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
        /// once Coal has been bought, the burn once the coal line feeds its boiler.
        /// </summary>
        public static IReadOnlyList<PlaytestQuestion> Questions { get; } = new[]
        {
            new PlaytestQuestion("light", "scrap",
                "Look around the workshop. Is the room too dark?",
                "Too dark", "About right", "Not dramatic enough"),
            new PlaytestQuestion("workbench", "depot",
                "You just programmed your first golem. Did the Workbench feel like making a decision, or like busywork?",
                "A real decision", "Somewhere between", "Busywork"),
            new PlaytestQuestion("manual-era", "coking-card",
                "Your first machine is running. How did the hand-gathering and cranking before it feel?",
                "Miserable", "Long but fine", "About right", "Too short"),
            new PlaytestQuestion("walking", "coking-card",
                "How much of that time was walking between the workshop and the stalls?",
                "Most of it", "About half", "Not much"),
            new PlaytestQuestion("market", "program-coker",
                "You bought Coal by the truckload. Does the market feel like an economy, or a toll booth?",
                "An economy", "In between", "A toll booth"),
            new PlaytestQuestion("patent", "depot3",
                "You stamped a patent onto a new golem. Did that feel obviously better than programming it again?",
                "Obviously", "A little", "No difference"),
            new PlaytestQuestion("labels", "unloader",
                "You labelled a depot by pressing E until it said Copper Ore. A decent control, or do you want a picker?",
                "Decent", "Fiddly", "Want a picker"),
            new PlaytestQuestion("drag", "done",
                "Did laying belts and pipes by dragging feel good?",
                "Good", "Fiddly", "Didn't work how I expected"),
            new PlaytestQuestion("save-load", "done",
                "After you loaded, did everything come back as you left it?",
                "Everything", "Something was missing (say what)", "Something was wrong (say what)"),
            new PlaytestQuestion("slag", "done",
                "Slag had to be carried away, with Coke to burn it. A real problem worth solving, or a chore?",
                "A real problem", "Somewhere between", "A chore"),
            new PlaytestQuestion("badge", "done",
                "When the golem stalled, did its badge tell you what was wrong?",
                "Clearly", "Vaguely", "I didn't notice it"),
            new PlaytestQuestion("coke-burn", "done",
                "Your coal line feeds its boiler. Is Coke upkeep a cost worth managing, or a coal simulator?",
                "Burns too fast", "About right", "Too slow to matter"),
            new PlaytestQuestion("guide", "done",
                "Was this guide clear?",
                "Clear", "Mostly", "Confusing"),
        };

        private readonly List<PlaytestAnswer> _answers = new List<PlaytestAnswer>();
        private readonly List<(string stepId, string title, float enteredAt)> _steps = new List<(string, string, float)>();
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
        public void StepEntered(string stepId, string title, float atSeconds)
        {
            if (_steps.Count > 0 && _steps[_steps.Count - 1].stepId == stepId)
            {
                return;
            }
            _steps.Add((stepId, title, atSeconds));
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
            Version++;
        }

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

        private static string Clock(float seconds)
        {
            int s = Math.Max(0, (int)seconds);
            return s >= 3600
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", s / 60, s % 60);
        }
    }
}
