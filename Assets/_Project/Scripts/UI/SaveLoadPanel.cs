using UnityEngine;
using UnityEngine.UI;
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
        [SerializeField] private ArtificerFocusMeterHolder focusMeterHolder;
        [SerializeField] private PatentRegistryHolder patentRegistryHolder;
        [SerializeField] private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        [SerializeField] private LogicCoreDefinition[] logicCoreRoster = new LogicCoreDefinition[0];
        [SerializeField] private AppendageActionDefinition[] appendageRoster = new AppendageActionDefinition[0];

        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Text statusText;

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

        public void ConfigureUI(Button save, Button load, Text status)
        {
            saveButton = save;
            loadButton = load;
            statusText = status;
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
            if (statusText != null)
            {
                statusText.text = _statusMessage;
            }
        }

        private void Save()
        {
            GolemEntity[] golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
            SaveData data = SaveLoadService.CaptureState(
                bufferRegistryHolder.Registry, focusMeterHolder.Meter, patentRegistryHolder.Registry, golems);
            SaveFileIO.WriteToFile(data, SaveFileIO.DefaultPath);
            _statusMessage = $"Saved {golems.Length} golems.";
            Refresh();
        }

        private void Load()
        {
            SaveData data = SaveFileIO.ReadFromFile(SaveFileIO.DefaultPath);
            if (data == null)
            {
                _statusMessage = "No save file found.";
                Refresh();
                return;
            }

            var catalog = new DefinitionCatalog(chassisRoster, logicCoreRoster, appendageRoster);
            GolemEntity[] golems = Object.FindObjectsByType<GolemEntity>(FindObjectsSortMode.None);
            SaveLoadService.RestoreState(
                data, bufferRegistryHolder.Registry, focusMeterHolder.Meter, patentRegistryHolder.Registry, golems, catalog);
            _statusMessage = $"Loaded {data.golems.Count} golem programs.";
            Refresh();
        }
    }
}
