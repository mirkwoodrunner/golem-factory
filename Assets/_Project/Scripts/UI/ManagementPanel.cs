using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GolemFactory.UI
{
    public enum ManagementTab
    {
        Inventory,
        AssemblyLine,
        Patents,
        SaveLoad
    }

    // Consolidates the four small always-on HUD panels (Inventory/AssemblyLine/Patents/
    // SaveLoad) into one toggleable UGUI screen, replacing their old overlapping OnGUI
    // corner boxes. Lives on its own always-active GameObject (separate from the
    // ManagementScreen it shows/hides) so its Tab-key listener keeps running even while
    // the screen itself is inactive -- same split WorkbenchController uses between itself
    // and canvasRoot.
    public sealed class ManagementPanel : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private GameObject screenRoot;

        [SerializeField] private GameObject inventoryTab;
        [SerializeField] private GameObject assemblyLineTab;
        [SerializeField] private GameObject patentsTab;
        [SerializeField] private GameObject saveLoadTab;

        [SerializeField] private InventoryPanel inventoryPanel;
        [SerializeField] private AssemblyLinePanel assemblyLinePanel;
        [SerializeField] private PatentBrowserPanel patentBrowserPanel;
        [SerializeField] private SaveLoadPanel saveLoadPanel;

        [SerializeField] private Button inventoryTabButton;
        [SerializeField] private Button assemblyLineTabButton;
        [SerializeField] private Button patentsTabButton;
        [SerializeField] private Button saveLoadTabButton;
        [SerializeField] private Button closeButton;

        [SerializeField] private WorkbenchController workbenchController;
        [SerializeField] private GolemConstructionPanel constructionPanel;

        private InputAction _toggleMenuAction;

        public bool IsOpen { get; private set; }
        public ManagementTab ActiveTab { get; private set; } = ManagementTab.Inventory;

        // Test/bootstrap-friendly setup, same Configure* idiom as the rest of this
        // codebase's UI/Player scripts -- avoids requiring Inspector-assigned references.
        public void Configure(
            InputActionAsset inputActions, GameObject screen,
            GameObject inventoryTabRoot, GameObject assemblyLineTabRoot, GameObject patentsTabRoot, GameObject saveLoadTabRoot,
            InventoryPanel inventory, AssemblyLinePanel assemblyLine, PatentBrowserPanel patents, SaveLoadPanel saveLoad,
            Button inventoryButton, Button assemblyLineButton, Button patentsButton, Button saveLoadButton, Button close,
            WorkbenchController workbench, GolemConstructionPanel construction)
        {
            actions = inputActions;
            screenRoot = screen;
            inventoryTab = inventoryTabRoot;
            assemblyLineTab = assemblyLineTabRoot;
            patentsTab = patentsTabRoot;
            saveLoadTab = saveLoadTabRoot;
            inventoryPanel = inventory;
            assemblyLinePanel = assemblyLine;
            patentBrowserPanel = patents;
            saveLoadPanel = saveLoad;
            inventoryTabButton = inventoryButton;
            assemblyLineTabButton = assemblyLineButton;
            patentsTabButton = patentsButton;
            saveLoadTabButton = saveLoadButton;
            closeButton = close;
            workbenchController = workbench;
            constructionPanel = construction;
        }

        private void Awake()
        {
            if (actions != null)
            {
                _toggleMenuAction = actions.FindActionMap("Gameplay")?.FindAction("ToggleMenu");
            }
        }

        private void OnEnable()
        {
            if (_toggleMenuAction != null)
            {
                _toggleMenuAction.Enable();
                _toggleMenuAction.performed += OnToggleMenuPerformed;
            }
        }

        private void OnDisable()
        {
            if (_toggleMenuAction != null)
            {
                _toggleMenuAction.performed -= OnToggleMenuPerformed;
                _toggleMenuAction.Disable();
            }
        }

        private void Start()
        {
            inventoryTabButton?.onClick.AddListener(() => SelectTab(ManagementTab.Inventory));
            assemblyLineTabButton?.onClick.AddListener(() => SelectTab(ManagementTab.AssemblyLine));
            patentsTabButton?.onClick.AddListener(() => SelectTab(ManagementTab.Patents));
            saveLoadTabButton?.onClick.AddListener(() => SelectTab(ManagementTab.SaveLoad));
            closeButton?.onClick.AddListener(Close);

            Close();
        }

        private void Update()
        {
            if (IsOpen)
            {
                RefreshActiveTab();
            }
        }

        private void OnToggleMenuPerformed(InputAction.CallbackContext context) => Toggle();

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            IsOpen = true;
            if (screenRoot != null)
            {
                screenRoot.SetActive(true);
            }
            workbenchController?.Close();
            constructionPanel?.Close();
            SelectTab(ActiveTab);
        }

        public void Close()
        {
            IsOpen = false;
            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }
        }

        public void SelectTab(ManagementTab tab)
        {
            ActiveTab = tab;
            inventoryTab?.SetActive(tab == ManagementTab.Inventory);
            assemblyLineTab?.SetActive(tab == ManagementTab.AssemblyLine);
            patentsTab?.SetActive(tab == ManagementTab.Patents);
            saveLoadTab?.SetActive(tab == ManagementTab.SaveLoad);
            RefreshActiveTab();
        }

        private void RefreshActiveTab()
        {
            switch (ActiveTab)
            {
                case ManagementTab.Inventory:
                    inventoryPanel?.Refresh();
                    break;
                case ManagementTab.AssemblyLine:
                    assemblyLinePanel?.Refresh();
                    break;
                case ManagementTab.Patents:
                    patentBrowserPanel?.Refresh();
                    break;
                case ManagementTab.SaveLoad:
                    saveLoadPanel?.Refresh();
                    break;
            }
        }
    }
}
