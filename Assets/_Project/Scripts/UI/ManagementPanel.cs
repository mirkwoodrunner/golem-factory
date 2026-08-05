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

        // Selected/unselected tab tints. Brightness is the channel that separates them:
        // the tab sprites are all the same brass button, so the only thing distinguishing
        // "you are here" is how lit it is. Without this every tab looked identical and the
        // screen gave no indication of which one was open.
        private static readonly Color SelectedTabColor = new Color(0.86f, 0.66f, 0.34f, 1f);
        private static readonly Color UnselectedTabColor = new Color(0.34f, 0.30f, 0.26f, 1f);
        // The caption has to invert with the plate: dark ink on the lit brass tab, warm
        // parchment on the dim ones. A single fixed caption colour is unreadable against
        // one state or the other.
        private static readonly Color SelectedTabLabelColor = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedTabLabelColor = new Color(0.82f, 0.78f, 0.71f, 1f);

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
            ApplyTabHighlight();
            RefreshActiveTab();
        }

        /// <summary>
        /// Tints exactly one tab button as selected. Public so a test can assert the
        /// highlight without reaching through <see cref="SelectTab"/>'s side effects.
        /// </summary>
        public void ApplyTabHighlight()
        {
            Tint(inventoryTabButton, ActiveTab == ManagementTab.Inventory);
            Tint(assemblyLineTabButton, ActiveTab == ManagementTab.AssemblyLine);
            Tint(patentsTabButton, ActiveTab == ManagementTab.Patents);
            Tint(saveLoadTabButton, ActiveTab == ManagementTab.SaveLoad);
        }

        private static void Tint(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected ? SelectedTabColor : UnselectedTabColor;
            }

            TMPro.TextMeshProUGUI label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = selected ? SelectedTabLabelColor : UnselectedTabLabelColor;
            }
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
