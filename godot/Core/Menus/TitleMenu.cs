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
        NewGame,
        Settings,
        Quit,
    }

    /// <summary>A save the title can offer: where it is, when it was written, and whether it reads.</summary>
    public readonly struct SaveSummary
    {
        public readonly string Path;
        public readonly DateTime SavedAt;

        /// <summary>False when the file is there but isn't a save the game can read.</summary>
        public readonly bool Readable;

        public SaveSummary(string path, DateTime savedAt, bool readable = true)
        {
            Path = path;
            SavedAt = savedAt;
            Readable = readable;
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
    /// greyed without one, New game starts the guide, Settings opens the same tab Management
    /// has, Quit leaves. The node only draws these and feeds clicks and keys.
    /// </summary>
    public sealed class TitleMenu
    {
        public static readonly IReadOnlyList<TitleChoice> Order =
            new[] { TitleChoice.Continue, TitleChoice.NewGame, TitleChoice.Settings, TitleChoice.Quit };

        public const string GameName = "Golem Factory";
        public const string Subtitle = "The Clockwork Metropolis";
        public const string NoSave = "No saved game yet";
        public const string Unreadable = "Your save can't be read";

        private readonly SaveSummary? _newest;
        private readonly bool _unreadable;
        private readonly DateTime _now;

        /// <summary>The title's menu over <paramref name="saves"/>: every save found, readable or not.</summary>
        public TitleMenu(IEnumerable<SaveSummary> saves, DateTime now)
        {
            List<SaveSummary> all = (saves ?? Enumerable.Empty<SaveSummary>()).ToList();
            _newest = SaveSummary.Newest(all);
            _unreadable = _newest == null && all.Count > 0;
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

        public bool IsEnabled(TitleChoice choice) => choice != TitleChoice.Continue || _newest != null;

        /// <summary>Where the keyboard starts: Continue when there is a save, else New game.</summary>
        public TitleChoice DefaultFocus => IsEnabled(TitleChoice.Continue) ? TitleChoice.Continue : TitleChoice.NewGame;

        public static string Label(TitleChoice choice) => choice switch
        {
            TitleChoice.Continue => "Continue",
            TitleChoice.NewGame => "New game",
            TitleChoice.Settings => "Settings",
            TitleChoice.Quit => "Quit",
            _ => "",
        };

        /// <summary>The line under an entry: when Continue's save was written, or why it is greyed.</summary>
        public string Detail(TitleChoice choice)
        {
            if (choice != TitleChoice.Continue)
            {
                return "";
            }
            if (_newest == null)
            {
                return _unreadable ? Unreadable : NoSave;
            }
            return "Saved " + When(_newest.Value.SavedAt, _now);
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
