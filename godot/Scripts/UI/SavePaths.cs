using System.Collections.Generic;
using Godot;
using GolemFactory.Menus;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Where the save slots live (docs/menus-design.md §7): <c>user://saves</c>, or
    /// <c>user://scenario-saves</c> on a scenario run, which never touches the player's saves
    /// (Core/Tutorial/CLAUDE.md). Creative's slots are their own, in <c>creative</c> inside it
    /// (§9). Core's <see cref="SaveSlots"/> names the files inside.
    /// </summary>
    public static class SavePaths
    {
        /// <summary>The slots' folder, globalized: Core reads and writes it with System.IO.</summary>
        public static string Folder => ProjectSettings.GlobalizePath(PlaytestNode.UnderScenario ? "user://scenario-saves" : "user://saves");

        /// <summary>Creative's slots: never the campaign's (decision 2).</summary>
        public static string CreativeFolder => System.IO.Path.Combine(Folder, "creative");

        public static string FolderFor(bool creative) => creative ? CreativeFolder : Folder;

        /// <summary>The one save from before slots, which <see cref="Saves"/> moves into a slot.</summary>
        public static string Legacy => ProjectSettings.GlobalizePath(PlaytestNode.UnderScenario ? "user://scenario-save.json" : "user://save.json");

        /// <summary>
        /// Every slot that holds a file, readable or not, each read once: the title greys what
        /// can't be read rather than offering it. A save from before slots moves into the first
        /// empty slot first, so the player's game is still there.
        /// </summary>
        public static List<SaveSummary> Saves(bool creative = false)
        {
            if (creative)
            {
                return Checked(SaveSlots.Read(CreativeFolder, creative: true));
            }
            try
            {
                string moved = SaveSlots.MigrateLegacy(Legacy, Folder);
                if (moved != null)
                {
                    GD.Print($"The save from before slots is now {SaveSlots.Title(moved)}.");
                }
            }
            catch (System.Exception e)
            {
                GD.PushWarning($"The save from before slots stays at {Legacy}: {e.Message}");
            }
            return Checked(SaveSlots.Read(Folder));
        }

        /// <summary>Both kinds: what the title's Continue chooses from.</summary>
        public static List<SaveSummary> AllSaves()
        {
            List<SaveSummary> all = Saves();
            all.AddRange(Saves(creative: true));
            return all;
        }

        private static List<SaveSummary> Checked(List<SaveSummary> found)
        {
            foreach (SaveSummary save in found)
            {
                if (!save.Readable)
                {
                    GD.PushWarning($"{save.Path} can't be read as a save.");
                }
            }
            return found;
        }

        /// <summary>A slot's picture, or null without one (or one that won't load).</summary>
        public static Texture2D Thumbnail(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                return null;
            }
            Image image = Image.LoadFromFile(path);
            return image == null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
        }
    }
}
