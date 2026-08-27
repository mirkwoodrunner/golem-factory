using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;

namespace GolemFactory.UI
{
    // Minimal Save/Load buttons. UGUI-based (converted from the original OnGUI panel as
    // part of the Management HUD consolidation) -- lives in ManagementPanel's SaveLoadTab.
    public sealed class SaveLoadPanel : MonoBehaviour
    {
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private PatentRegistryHolder patentRegistryHolder;
        [SerializeField] private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        [SerializeField] private LogicCoreDefinition[] logicCoreRoster = new LogicCoreDefinition[0];
        [SerializeField] private AppendageActionDefinition[] appendageRoster = new AppendageActionDefinition[0];

        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private TextMeshProUGUI statusTextMeshProUGUI;

        private string _statusMessage = "";

        public void Configure(
            StorageBufferRegistryHolder buffers, PatentRegistryHolder patents,
            ChassisDefinition[] chassis, LogicCoreDefinition[] logicCores, AppendageActionDefinition[] appendages)
        {
            bufferRegistryHolder = buffers;
            patentRegistryHolder = patents;
            chassisRoster = chassis ?? new ChassisDefinition[0];
            logicCoreRoster = logicCores ?? new LogicCoreDefinition[0];
            appendageRoster = appendages ?? new AppendageActionDefinition[0];
        }

        public void ConfigureUI(Button save, Button load, TextMeshProUGUI status)
        {
            saveButton = save;
            loadButton = load;
            statusTextMeshProUGUI = status;
        }

        private void Start()
        {
            saveButton?.onClick.AddListener(Save);
            loadButton?.onClick.AddListener(Load);
        }

        // No dynamic list content to rebuild -- exists so ManagementPanel can treat every
        // tab uniformly (Refresh() on whichever tab is active).
        public void Refresh()
        {
            if (statusTextMeshProUGUI != null)
            {
                statusTextMeshProUGUI.text = _statusMessage;
            }
        }

        // A scene that forgot to wire the holders used to NullReferenceException on the
        // first click (Sandbox.unity shipped in exactly that state). Reporting the gap in
        // the panel's own status line is both safe and diagnosable.
        private bool HasDataSources
        {
            get
            {
                if (bufferRegistryHolder != null && patentRegistryHolder != null)
                {
                    return true;
                }

                _statusMessage = "Save/Load unavailable: this scene's data sources are not wired.";
                Refresh();
                return false;
            }
        }

        private void Save()
        {
            if (!HasDataSources)
            {
                return;
            }

            GolemEntity[] golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
            GolemFactory.Buildings.PlaceableBuilding[] buildings =
                Object.FindObjectsByType<GolemFactory.Buildings.PlaceableBuilding>(FindObjectsSortMode.None);
            SaveData data = SaveLoadService.CaptureState(
                bufferRegistryHolder.Registry, patentRegistryHolder.Registry,
                golems, buildings);
            SaveFileIO.WriteToFile(data, SaveFileIO.DefaultPath);
            _statusMessage = $"Saved {golems.Length} golems and {data.buildings.Count} buildings.";
            Refresh();
        }

        private void Load()
        {
            if (!HasDataSources)
            {
                return;
            }

            SaveData data = SaveFileIO.ReadFromFile(SaveFileIO.DefaultPath);
            if (data == null)
            {
                _statusMessage = "No save file found.";
                Refresh();
                return;
            }

            var catalog = new DefinitionCatalog(chassisRoster, logicCoreRoster, appendageRoster);
            GolemEntity[] golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
            SaveLoadService.RestoreReport report = SaveLoadService.RestoreState(
                data, bufferRegistryHolder.Registry, patentRegistryHolder.Registry,
                golems, catalog, StationGolemRespawner.FindInScene(),
                BuildModeBuildingRebuilder.FindInScene());

            // Reports what the load DID, not how many entries the file held. The old line said
            // "Loaded N golem programs" whether or not a single one of them found a golem to
            // load into -- which, for a player-built factory in a fresh session, was all of them.
            string golemLine = report.Skipped > 0
                ? $"Loaded {report.Restored} golems, rebuilt {report.Respawned}, skipped {report.Skipped}"
                : $"Loaded {report.Restored} golems, rebuilt {report.Respawned}";
            string buildingLine = report.BuildingsSkipped > 0
                ? $"{report.BuildingsRebuilt} buildings, {report.BuildingsSkipped} skipped"
                : $"{report.BuildingsRebuilt} buildings";
            _statusMessage = golemLine + "; " + buildingLine + ".";
            Refresh();
        }
    }
}
