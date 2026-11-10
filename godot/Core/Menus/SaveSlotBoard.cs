using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Save;

namespace GolemFactory.Menus
{
    /// <summary>What a slot's button does.</summary>
    public enum SlotAction
    {
        Save,
        Load,
        Delete,
    }

    /// <summary>Where the slots are shown: in a game (save, load, delete) or on the title (load only).</summary>
    public enum SlotBoardMode
    {
        InGame,
        Title,
    }

    /// <summary>What a press on a slot's button came to.</summary>
    public enum SlotPress
    {
        /// <summary>Not allowed on this slot (an empty slot can't be loaded, the autosave can't be saved into).</summary>
        Refused,

        /// <summary>It asks to be pressed again first: the button now says so, and the prompt says why.</summary>
        Armed,

        /// <summary>Do it now.</summary>
        Go,
    }

    /// <summary>One slot as the list draws it: a title and three lines, and the picture.</summary>
    public sealed class SlotRow
    {
        public string Key;
        public string Title;
        public bool Empty;
        public bool Readable;

        /// <summary>Where the game was: "Chapter 3 · Copies, turning and stalls", or "Empty".</summary>
        public string Where;

        /// <summary>"6 golems · 14 buildings · 1 tower stage done".</summary>
        public string Size;

        /// <summary>"Saved today at 14:32", "Autosaved yesterday at 9:05 · On quit".</summary>
        public string When;

        public string Thumbnail;
        public SaveSummary? Save;
    }

    /// <summary>
    /// The save slots as a list (docs/menus-design.md §7): what each row says and which of Save,
    /// Load and Delete it offers. Anything that would lose something asks to be pressed twice:
    /// saving over a slot that holds a save, loading over the game being played, deleting. The
    /// second press must be the same button; any other press, or the list closing, disarms it.
    /// On the title the keyboard walks the rows that can be loaded.
    /// </summary>
    public sealed class SaveSlotBoard
    {
        public const string EmptyText = "Empty";
        public const string UnreadableText = "This save can't be read";
        public const string FreePlay = "Free play";

        private readonly List<SlotRow> _rows = new List<SlotRow>();
        private DateTime _now;

        public SaveSlotBoard(SlotBoardMode mode, IEnumerable<SaveSummary> found, DateTime now)
        {
            Mode = mode;
            Show(found, now);
        }

        public SlotBoardMode Mode { get; }

        public IReadOnlyList<SlotRow> Rows => _rows;

        public SlotRow Row(string key) => _rows.FirstOrDefault(r => r.Key == key);

        /// <summary>The button waiting for its second press, or null.</summary>
        public (string key, SlotAction action)? Armed { get; private set; }

        /// <summary>The keyboard's row on the title; null when nothing there can be loaded.</summary>
        public string Selected { get; private set; }

        /// <summary>Redraws the rows from what is on disk now; anything armed is let go.</summary>
        public void Show(IEnumerable<SaveSummary> found, DateTime now)
        {
            _now = now;
            Dictionary<string, SaveSummary> byKey = (found ?? Enumerable.Empty<SaveSummary>())
                .Where(s => s.Key != null)
                .GroupBy(s => s.Key)
                .ToDictionary(g => g.Key, g => g.First());
            _rows.Clear();
            foreach (string key in SaveSlots.Keys)
            {
                _rows.Add(byKey.TryGetValue(key, out SaveSummary save) ? Describe(key, save, now) : EmptyRow(key));
            }
            Armed = null;
            if (Selected == null || !Can(Selected, SlotAction.Load))
            {
                Selected = _rows.Where(r => Can(r.Key, SlotAction.Load)).OrderByDescending(r => r.Save.Value.SavedAt).Select(r => r.Key).FirstOrDefault();
            }
        }

        private static SlotRow EmptyRow(string key) => new SlotRow
        {
            Key = key,
            Title = SaveSlots.Title(key),
            Empty = true,
            Where = EmptyText,
            Size = "",
            When = "",
        };

        private static SlotRow Describe(string key, SaveSummary save, DateTime now)
        {
            MetaEntry meta = save.Meta;
            var row = new SlotRow
            {
                Key = key,
                Title = SaveSlots.Title(key),
                Readable = save.Readable,
                Thumbnail = save.Thumbnail,
                Save = save,
            };
            if (!save.Readable)
            {
                row.Where = UnreadableText;
                row.Size = "";
                row.When = "Written " + TitleMenu.When(save.SavedAt, now);
                return row;
            }
            row.Where = meta == null ? "" : meta.chapter > 0 ? $"Chapter {meta.chapter} · {meta.chapterTitle}" : FreePlay;
            row.Size = meta == null ? "" : SizeLine(meta);
            row.When = (meta?.autosave == true ? "Autosaved " : "Saved ") + TitleMenu.When(save.SavedAt, now)
                + (string.IsNullOrEmpty(meta?.reason) ? "" : " · " + meta.reason);
            return row;
        }

        private static string SizeLine(MetaEntry meta)
        {
            string line = Count(meta.golems, "golem") + " · " + Count(meta.buildings, "building");
            if (meta.towerComplete)
            {
                return line + " · Clock Tower complete";
            }
            return meta.towerStagesDone > 0 ? line + " · " + Count(meta.towerStagesDone, "tower stage") + " done" : line;
        }

        private static string Count(int n, string noun) => n + " " + noun + (n == 1 ? "" : "s");

        /// <summary>Whether the slot offers <paramref name="action"/> at all.</summary>
        public bool Can(string key, SlotAction action)
        {
            SlotRow row = Row(key);
            if (row == null)
            {
                return false;
            }
            switch (action)
            {
                case SlotAction.Save:
                    return Mode == SlotBoardMode.InGame && !SaveSlots.IsAutosave(key);
                case SlotAction.Load:
                    return !row.Empty && row.Readable;
                default:
                    return Mode == SlotBoardMode.InGame && !row.Empty;
            }
        }

        /// <summary>Whether <paramref name="action"/> on the slot loses something, and so asks twice.</summary>
        public bool AsksTwice(string key, SlotAction action)
        {
            SlotRow row = Row(key);
            switch (action)
            {
                case SlotAction.Save:
                    return row != null && !row.Empty;
                case SlotAction.Load:
                    return Mode == SlotBoardMode.InGame;
                default:
                    return true;
            }
        }

        /// <summary>A press on the slot's button: refused, armed (press again), or go.</summary>
        public SlotPress Press(string key, SlotAction action)
        {
            if (!Can(key, action))
            {
                Armed = null;
                return SlotPress.Refused;
            }
            if (AsksTwice(key, action) && Armed != (key, action))
            {
                Armed = (key, action);
                return SlotPress.Armed;
            }
            Armed = null;
            return SlotPress.Go;
        }

        public void Disarm() => Armed = null;

        public bool IsArmed(string key, SlotAction action) => Armed == (key, action);

        /// <summary>The button's word: "Save", or "Save over" while it waits for its second press.</summary>
        public string Label(string key, SlotAction action)
        {
            bool armed = IsArmed(key, action);
            switch (action)
            {
                case SlotAction.Save:
                    return armed ? "Save over" : "Save";
                case SlotAction.Load:
                    return armed ? "Load now" : "Load";
                default:
                    return armed ? "Delete now" : "Delete";
            }
        }

        /// <summary>Why the armed button asks twice, or "" with nothing armed; "Press A on" with the pad.</summary>
        public string Prompt => Settings.TextTokens.Fill(ArmedPrompt);

        private string ArmedPrompt
        {
            get
            {
                if (Armed is not (string key, SlotAction action))
                {
                    return "";
                }
                string title = SaveSlots.Title(key);
                switch (action)
                {
                    case SlotAction.Save:
                        SlotRow row = Row(key);
                        string held = row.Readable ? "a save from " + TitleMenu.When(row.Save.Value.SavedAt, _now) : "a save that can't be read";
                        return $"{title} holds {held}. Click Save over to replace it.";
                    case SlotAction.Load:
                        return $"Anything you haven't saved will be lost. Click Load now to load {title}.";
                    default:
                        return $"Click Delete now to remove {title} for good.";
                }
            }
        }

        // --- The keyboard, on the title ---------------------------------------------------------

        /// <summary>Up (-1) or down (+1) to the next row that can be loaded, wrapping round.</summary>
        public void Move(int step)
        {
            List<string> loadable = _rows.Where(r => Can(r.Key, SlotAction.Load)).Select(r => r.Key).ToList();
            if (loadable.Count == 0)
            {
                Selected = null;
                return;
            }
            int at = Selected == null ? -1 : loadable.IndexOf(Selected);
            if (at < 0)
            {
                Selected = loadable[0];
                return;
            }
            Selected = loadable[((at + Math.Sign(step)) % loadable.Count + loadable.Count) % loadable.Count];
        }

        /// <summary>Highlights a row (the pointer is over it); one that can't be loaded is refused.</summary>
        public bool Select(string key)
        {
            if (!Can(key, SlotAction.Load))
            {
                return false;
            }
            Selected = key;
            return true;
        }
    }
}
