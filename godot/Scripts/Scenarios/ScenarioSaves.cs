using System.IO;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The scenario runs' save folder (<c>user://scenario-saves</c>) and its pre-slots file,
    /// emptied: a scenario starts from no saves and leaves none. Never the player's own
    /// (<see cref="SavePaths"/> names the scratch folder under a scenario).
    /// </summary>
    public static class ScenarioSaves
    {
        public static void Clear()
        {
            if (!PlaytestNode.UnderScenario)
            {
                return; // only ever the scratch folder
            }
            if (Directory.Exists(SavePaths.Folder))
            {
                Directory.Delete(SavePaths.Folder, true);
            }
            if (File.Exists(SavePaths.Legacy))
            {
                File.Delete(SavePaths.Legacy);
            }
        }
    }
}
