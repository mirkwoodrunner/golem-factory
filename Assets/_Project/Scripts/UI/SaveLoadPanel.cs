using UnityEngine;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;

namespace GolemFactory.UI
{
    // Minimal Save/Load buttons -- OnGUI like the rest of the HUD panels. Bottom-right
    // corner, clear of the Workbench's Card Vault (right column, but full-height at
    // 0.68-1 width) -- see InventoryPanel's M8 note on OnGUI-over-UGUI layering; this
    // panel is small enough that a little corner overlap there is a non-issue.
    public sealed class SaveLoadPanel : MonoBehaviour
    {
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private ArtificerFocusMeterHolder focusMeterHolder;
        [SerializeField] private PatentRegistryHolder patentRegistryHolder;
        [SerializeField] private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        [SerializeField] private LogicCoreDefinition[] logicCoreRoster = new LogicCoreDefinition[0];
        [SerializeField] private AppendageActionDefinition[] appendageRoster = new AppendageActionDefinition[0];

        private string _statusMessage = "";

        public void Configure(
            StorageBufferRegistryHolder buffers, ArtificerFocusMeterHolder focus, PatentRegistryHolder patents,
            ChassisDefinition[] chassis, LogicCoreDefinition[] logicCores, AppendageActionDefinition[] appendages)
        {
            bufferRegistryHolder = buffers;
            focusMeterHolder = focus;
            patentRegistryHolder = patents;
            chassisRoster = chassis ?? new ChassisDefinition[0];
            logicCoreRoster = logicCores ?? new LogicCoreDefinition[0];
            appendageRoster = appendages ?? new AppendageActionDefinition[0];
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 260, Screen.height - 90, 250, 80), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                Save();
            }
            if (GUILayout.Button("Load"))
            {
                Load();
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Label(_statusMessage);
            }
            GUILayout.EndArea();
        }

        private void Save()
        {
            GolemEntity[] golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
            SaveData data = SaveLoadService.CaptureState(
                bufferRegistryHolder.Registry, focusMeterHolder.Meter, patentRegistryHolder.Registry, golems);
            SaveFileIO.WriteToFile(data, SaveFileIO.DefaultPath);
            _statusMessage = $"Saved {golems.Length} golems.";
        }

        private void Load()
        {
            SaveData data = SaveFileIO.ReadFromFile(SaveFileIO.DefaultPath);
            if (data == null)
            {
                _statusMessage = "No save file found.";
                return;
            }

            var catalog = new DefinitionCatalog(chassisRoster, logicCoreRoster, appendageRoster);
            GolemEntity[] golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
            SaveLoadService.RestoreState(
                data, bufferRegistryHolder.Registry, focusMeterHolder.Meter, patentRegistryHolder.Registry, golems, catalog);
            _statusMessage = $"Loaded {data.golems.Count} golem programs.";
        }
    }
}
