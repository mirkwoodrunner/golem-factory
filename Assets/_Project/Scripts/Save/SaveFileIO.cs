using System.IO;
using UnityEngine;

namespace GolemFactory.Save
{
    // Thin JSON file I/O wrapper around SaveData -- kept separate from
    // SaveLoadService's pure capture/restore logic so that logic stays
    // Application.persistentDataPath-free and testable without touching disk.
    public static class SaveFileIO
    {
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "save.json");

        public static void WriteToFile(SaveData data, string path)
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json);
        }

        public static SaveData ReadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<SaveData>(json);
        }
    }
}
