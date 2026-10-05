using System.IO;
using System.Text.Json;

namespace GolemFactory.Save
{
    /// <summary>
    /// Reads and writes a <see cref="SaveData"/> as JSON (milestone G9). Unity's version used
    /// JsonUtility and Application.persistentDataPath; this one is plain System.Text.Json over a
    /// path the caller supplies -- the Godot layer passes <c>user://save.json</c>, globalized --
    /// so it stays engine-free and a unit test can point it at a temp file.
    ///
    /// <para>
    /// Fields, not properties: SaveData's members are public fields (as JsonUtility needed), so
    /// <see cref="JsonSerializerOptions.IncludeFields"/> is on. No compatibility with Unity's save
    /// files is attempted -- a decision recorded in the conversion plan.
    /// </para>
    /// </summary>
    public static class SaveFileIO
    {
        public const string DefaultFileName = "save.json";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
        };

        public static void WriteToFile(SaveData data, string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(data, Options));
        }

        /// <summary>The save at <paramref name="path"/>, or null when there is none.</summary>
        public static SaveData ReadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(path), Options);
        }
    }
}
