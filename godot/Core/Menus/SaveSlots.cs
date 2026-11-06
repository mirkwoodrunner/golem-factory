using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GolemFactory.Save;

namespace GolemFactory.Menus
{
    /// <summary>
    /// The save slots (docs/menus-design.md §7): five the player saves into and one the game
    /// autosaves into, each a file in one folder the Godot layer names (<c>user://saves</c>).
    /// A slot is <c>&lt;key&gt;.json</c> with its picture beside it as <c>&lt;key&gt;.png</c>.
    /// Only the keys below are slots, so the playtest kit's own save in the same folder is never
    /// offered. Files hold no absolute path: a slot is found by its key in whatever folder.
    /// </summary>
    public static class SaveSlots
    {
        public const int ManualCount = 5;
        public const string AutosaveKey = "autosave";

        /// <summary>Where the playtest kit's save step writes: in the folder, never a slot.</summary>
        public const string KitFileName = "kit-save.json";

        /// <summary>Every slot, in the order the list shows them: the autosave first.</summary>
        public static readonly IReadOnlyList<string> Keys = BuildKeys();

        private static string[] BuildKeys()
        {
            var keys = new string[ManualCount + 1];
            keys[0] = AutosaveKey;
            for (int i = 1; i <= ManualCount; i++)
            {
                keys[i] = ManualKey(i);
            }
            return keys;
        }

        public static string ManualKey(int number) => "slot-" + number.ToString(CultureInfo.InvariantCulture);

        public static bool IsAutosave(string key) => key == AutosaveKey;

        /// <summary>"Autosave", "Slot 3".</summary>
        public static string Title(string key) =>
            IsAutosave(key) ? "Autosave" : "Slot " + key.Substring("slot-".Length);

        public static string PathIn(string folder, string key) => Path.Combine(folder, key + ".json");

        public static string ThumbnailIn(string folder, string key) => Path.Combine(folder, key + ".png");

        /// <summary>
        /// Every slot in <paramref name="folder"/> that holds a file, readable or not.
        /// <paramref name="creative"/>: the folder is Creative's (docs/menus-design.md §9), whose
        /// saves are its own; a save of the other kind found there reads as unreadable.
        /// </summary>
        public static List<SaveSummary> Read(string folder, bool creative = false)
        {
            var found = new List<SaveSummary>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return found;
            }
            foreach (string key in Keys)
            {
                string path = PathIn(folder, key);
                if (File.Exists(path))
                {
                    found.Add(Summarize(folder, key, creative));
                }
            }
            return found;
        }

        /// <summary>
        /// One slot's file, read once: its summary, its time (the save's own, else the file's),
        /// and whether it reads. A file that doesn't read is reported, never thrown, so one bad
        /// slot can't take the list down with it.
        /// </summary>
        public static SaveSummary Summarize(string folder, string key, bool creative = false)
        {
            string path = PathIn(folder, key);
            DateTime written = File.GetLastWriteTime(path);
            MetaEntry meta = null;
            bool readable;
            try
            {
                SaveData data = SaveFileIO.ReadFromFile(path);
                readable = data != null && data.creative == creative; // never offered as the other kind
                meta = data?.meta;
            }
            catch (Exception)
            {
                readable = false;
            }
            if (meta != null && DateTime.TryParse(meta.savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime saved))
            {
                written = saved.ToLocalTime();
            }
            string thumbnail = ThumbnailIn(folder, key);
            return new SaveSummary(path, written, readable, key, meta, File.Exists(thumbnail) ? thumbnail : null, creative);
        }

        /// <summary>Removes a slot's save and its picture.</summary>
        public static void Delete(string folder, string key)
        {
            foreach (string path in new[] { PathIn(folder, key), ThumbnailIn(folder, key) })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// The single save from before slots (<c>user://save.json</c>), moved into the first empty
        /// slot so the player's game is still there. Returns the slot it went to, or null when
        /// there was nothing to move or no slot free (it is then left where it is, untouched).
        /// </summary>
        public static string MigrateLegacy(string legacyFile, string folder)
        {
            if (string.IsNullOrEmpty(legacyFile) || !File.Exists(legacyFile))
            {
                return null;
            }
            for (int i = 1; i <= ManualCount; i++)
            {
                string key = ManualKey(i);
                string target = PathIn(folder, key);
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(folder);
                    // A picture left without its save would show beside the moved one.
                    string stale = ThumbnailIn(folder, key);
                    if (File.Exists(stale))
                    {
                        File.Delete(stale);
                    }
                    File.Move(legacyFile, target);
                    return key;
                }
            }
            return null;
        }
    }
}
