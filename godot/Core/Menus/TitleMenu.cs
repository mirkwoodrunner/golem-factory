using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GolemFactory.Menus
{
    /// <summary>A title screen entry (docs/menus-design.md §6).</summary>
    public enum TitleChoice
    {
        Continue,
        Load,
        NewGame,
        Creative,
        Settings,
        Quit,
    }

    /// <summary>
    /// A save found in a slot: where it is, when it was written, whether it reads, and what its
    /// slot shows (<see cref="SaveSlots.Summarize"/>).
    /// </summary>
    public readonly struct SaveSummary
    {
        public readonly string Path;
        public readonly DateTime SavedAt;

        /// <summary>False when the file is there but isn't a save the game can read.</summary>
        public readonly bool Readable;

        /// <summary>The slot it is in (<see cref="SaveSlots.Keys"/>).</summary>
        public readonly string Key;

        /// <summary>The save's own summary, or null (a save from before slots, or one that doesn't read).</summary>
        public readonly Save.MetaEntry Meta;

        /// <summary>The slot's picture, or null without one.</summary>
        public readonly string Thumbnail;

        /// <summary>A Creative world's save, from Creative's own slots (docs/menus-design.md §9).</summary>
        public readonly bool Creative;

        public SaveSummary(string path, DateTime savedAt, bool readable = true, string key = null, Save.MetaEntry meta = null,
            string thumbnail = null, bool creative = false)
        {
            Creative = creative;
            Path = path;
            SavedAt = savedAt;
            Readable = readable;
            Key = key;
            Meta = meta;
            Thumbnail = thumbnail;
        }

        /// <summary>
        /// The most recently written of <paramref name="saves"/> that can be read, or null with
        /// none: Continue never offers a save that would only bring the player back.
        /// </summary>
        public static SaveSummary? Newest(IEnumerable<SaveSummary> saves)
        {
            SaveSummary? newest = null;
            foreach (SaveSummary save in saves ?? Enumerable.Empty<SaveSummary>())
            {
                if (save.Readable && (newest == null || save.SavedAt > newest.Value.SavedAt))
                {
                    newest = save;
                }
            }
            return newest;
        }
    }

    /// <summary>
    /// The title screen's entries and what each says: Continue resumes the newest save and is
    /// greyed without one, Load picks a slot (greyed likewise), New game starts the guide,
    /// Settings opens the same tab Management has, Quit leaves. The node only draws these and
    /// feeds clicks and keys.
    /// </summary>
    public sealed class TitleMenu
    {
        public static readonly IReadOnlyList<TitleChoice> Order =
            new[] { TitleChoice.Continue, TitleChoice.Load, TitleChoice.NewGame, TitleChoice.Creative, TitleChoice.Settings, TitleChoice.Quit };

        public const string GameName = "Golem Factory";
        public const string Subtitle = "The Clockwork Metropolis";
        public const string NoSave = "No saved game yet";
        public const string Unreadable = "Your save can't be read";
        public const string NoneReadable = "None of your saves can be read";
        public const string CreativeDetail = "Build freely";

        private readonly SaveSummary? _newest;
        private readonly int _found;
        private readonly int _readable;
        private readonly DateTime _now;

        /// <summary>The title's menu over <paramref name="saves"/>: every save found, readable or not.</summary>
        public TitleMenu(IEnumerable<SaveSummary> saves, DateTime now)
        {
            List<SaveSummary> all = (saves ?? Enumerable.Empty<SaveSummary>()).ToList();
            _newest = SaveSummary.Newest(all);
            _found = all.Count;
            _readable = all.Count(s => s.Readable);
            _now = now;
            Selected = DefaultFocus;
        }

        /// <summary>The highlighted entry: the one Enter picks, and the one the pointer is over.</summary>
        public TitleChoice Selected { get; private set; }

        /// <summary>Up (-1) or down (+1) to the next entry that can be picked, wrapping round.</summary>
        public void Move(int step)
        {
            int at = IndexOf(Selected);
            for (int i = 0; i < Order.Count; i++)
            {
                at = ((at + Math.Sign(step)) % Order.Count + Order.Count) % Order.Count;
                if (IsEnabled(Order[at]))
                {
                    Selected = Order[at];
                    return;
                }
            }
        }

        /// <summary>Highlights <paramref name="choice"/> (the pointer moved onto it); a greyed entry is refused.</summary>
        public bool Select(TitleChoice choice)
        {
            if (!IsEnabled(choice))
            {
                return false;
            }
            Selected = choice;
            return true;
        }

        private static int IndexOf(TitleChoice choice)
        {
            for (int i = 0; i < Order.Count; i++)
            {
                if (Order[i] == choice)
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>The save Continue loads, or null.</summary>
        public SaveSummary? Newest => _newest;

        public bool IsEnabled(TitleChoice choice) =>
            (choice != TitleChoice.Continue && choice != TitleChoice.Load) || _newest != null;

        /// <summary>Where the keyboard starts: Continue when there is a save, else New game.</summary>
        public TitleChoice DefaultFocus => IsEnabled(TitleChoice.Continue) ? TitleChoice.Continue : TitleChoice.NewGame;

        public static string Label(TitleChoice choice) => choice switch
        {
            TitleChoice.Continue => "Continue",
            TitleChoice.Load => "Load",
            TitleChoice.NewGame => "New game",
            TitleChoice.Creative => "Creative",
            TitleChoice.Settings => "Settings",
            TitleChoice.Quit => "Quit",
            _ => "",
        };

        /// <summary>
        /// The line beside an entry: when Continue's save was written, or why it is greyed; how
        /// many saves Load offers.
        /// </summary>
        public string Detail(TitleChoice choice)
        {
            if (choice == TitleChoice.Creative)
            {
                return CreativeDetail;
            }
            if (choice == TitleChoice.Load)
            {
                return _readable == 0 ? "" : _readable == 1 ? "1 save" : _readable + " saves";
            }
            if (choice != TitleChoice.Continue)
            {
                return "";
            }
            if (_newest == null)
            {
                return _found == 0 ? NoSave : _found == 1 ? Unreadable : NoneReadable;
            }
            if (_newest.Value.Creative)
            {
                return "Creative, " + When(_newest.Value.SavedAt, _now); // which world Continue opens
            }
            return (_newest.Value.Meta?.autosave == true ? "Autosaved " : "Saved ") + When(_newest.Value.SavedAt, _now);
        }

        /// <summary>"today at 14:32", "yesterday at 9:05", "3 Nov at 9:05", "3 Nov 2025".</summary>
        public static string When(DateTime at, DateTime now)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            string time = at.ToString("H:mm", c);
            int days = (now.Date - at.Date).Days;
            if (days == 0)
            {
                return "today at " + time;
            }
            if (days == 1)
            {
                return "yesterday at " + time;
            }
            if (at.Year == now.Year)
            {
                return at.ToString("d MMM", c) + " at " + time;
            }
            return at.ToString("d MMM yyyy", c);
        }
    }
}
