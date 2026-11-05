using System;
using Godot;
using GolemFactory.Menus;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Where the campaign is saved: <c>user://save.json</c>, or <c>user://scenario-save.json</c> on
    /// a scenario run, which never touches the player's save (Core/Tutorial/CLAUDE.md). The
    /// SaveLoad tab writes it and the title's Continue reads it (docs/menus-design.md §6); step 4
    /// adds the slots.
    /// </summary>
    public static class SavePaths
    {
        public static string Campaign => PlaytestNode.UnderScenario
            ? "user://scenario-save.json"
            : "user://" + Save.SaveFileIO.DefaultFileName;

        /// <summary>
        /// Every save Continue could resume: the campaign's, when it exists. Each is read once
        /// here, so one the game can't read is greyed on the title rather than offered.
        /// </summary>
        public static SaveSummary[] Saves()
        {
            string path = Campaign;
            if (!FileAccess.FileExists(path))
            {
                return Array.Empty<SaveSummary>();
            }
            string file = ProjectSettings.GlobalizePath(path);
            DateTime written = DateTimeOffset.FromUnixTimeSeconds((long)FileAccess.GetModifiedTime(path)).LocalDateTime;
            return new[] { new SaveSummary(file, written, Readable(file)) };
        }

        private static bool Readable(string file)
        {
            try
            {
                return Save.SaveFileIO.ReadFromFile(file) != null;
            }
            catch (Exception e)
            {
                GD.PushWarning($"{file} can't be read: {e.Message}");
                return false;
            }
        }
    }
}
