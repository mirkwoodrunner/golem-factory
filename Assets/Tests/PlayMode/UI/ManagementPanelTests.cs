using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode since ManagementPanel.Start() (button listener wiring, initial
    // Close()) only runs in Play Mode, same reason WorkbenchControllerTests is PlayMode.
    public class ManagementPanelTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private ManagementPanel Build()
        {
            _root = new GameObject("Root");
            return _root.AddComponent<ManagementPanel>();
        }

        [UnityTest]
        public IEnumerator Toggle_OpensAndCloses()
        {
            ManagementPanel panel = Build();
            yield return null;

            Assert.IsFalse(panel.IsOpen);
            panel.Toggle();
            Assert.IsTrue(panel.IsOpen);
            panel.Toggle();
            Assert.IsFalse(panel.IsOpen);
        }

        [UnityTest]
        public IEnumerator Open_ClosesWorkbenchAndConstructionPanel()
        {
            _root = new GameObject("Root");
            var panel = _root.AddComponent<ManagementPanel>();
            var workbench = _root.AddComponent<WorkbenchController>();
            var construction = _root.AddComponent<GolemConstructionPanel>();
            panel.Configure(
                null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, workbench, construction);
            workbench.Open();
            construction.Open(null);
            yield return null;

            panel.Open();

            Assert.IsFalse(workbench.IsOpen);
            Assert.IsFalse(construction.IsOpen);
        }

        [UnityTest]
        public IEnumerator SelectTab_ActivatesOnlyTheChosenTabContent()
        {
            _root = new GameObject("Root");
            var panel = _root.AddComponent<ManagementPanel>();
            var inventoryTab = new GameObject("InventoryTab");
            var assemblyLineTab = new GameObject("AssemblyLineTab");
            var patentsTab = new GameObject("PatentsTab");
            var saveLoadTab = new GameObject("SaveLoadTab");
            inventoryTab.transform.SetParent(_root.transform);
            assemblyLineTab.transform.SetParent(_root.transform);
            patentsTab.transform.SetParent(_root.transform);
            saveLoadTab.transform.SetParent(_root.transform);
            panel.Configure(
                null, null, inventoryTab, assemblyLineTab, patentsTab, saveLoadTab,
                null, null, null, null, null, null, null, null, null, null, null);
            yield return null;

            panel.SelectTab(ManagementTab.Patents);

            Assert.IsFalse(inventoryTab.activeSelf);
            Assert.IsFalse(assemblyLineTab.activeSelf);
            Assert.IsTrue(patentsTab.activeSelf);
            Assert.IsFalse(saveLoadTab.activeSelf);
            Assert.AreEqual(ManagementTab.Patents, panel.ActiveTab);
        }

        [UnityTest]
        public IEnumerator SelectTab_HighlightsExactlyOneTabButton()
        {
            _root = new GameObject("Root");
            var panel = _root.AddComponent<ManagementPanel>();
            UnityEngine.UI.Button inventory = MakeButton("InventoryButton");
            UnityEngine.UI.Button assemblyLine = MakeButton("AssemblyLineButton");
            UnityEngine.UI.Button patents = MakeButton("PatentsButton");
            UnityEngine.UI.Button saveLoad = MakeButton("SaveLoadButton");
            panel.Configure(
                null, null, null, null, null, null, null, null, null, null,
                inventory, assemblyLine, patents, saveLoad, null, null, null);
            yield return null;

            panel.SelectTab(ManagementTab.AssemblyLine);

            // Every tab button shares the same brass sprite, so the ONLY thing telling the
            // player which screen they are on is this tint. Assert it is unique rather than
            // just "the selected one changed".
            Color selected = assemblyLine.GetComponent<UnityEngine.UI.Image>().color;
            Assert.AreNotEqual(selected, inventory.GetComponent<UnityEngine.UI.Image>().color);
            Assert.AreEqual(inventory.GetComponent<UnityEngine.UI.Image>().color, patents.GetComponent<UnityEngine.UI.Image>().color);
            Assert.AreEqual(inventory.GetComponent<UnityEngine.UI.Image>().color, saveLoad.GetComponent<UnityEngine.UI.Image>().color);

            // The caption inverts with its plate, otherwise one of the two states is
            // unreadable.
            Assert.AreNotEqual(
                assemblyLine.GetComponentInChildren<TMPro.TextMeshProUGUI>().color,
                inventory.GetComponentInChildren<TMPro.TextMeshProUGUI>().color);

            panel.SelectTab(ManagementTab.SaveLoad);
            Assert.AreEqual(selected, saveLoad.GetComponent<UnityEngine.UI.Image>().color);
            Assert.AreNotEqual(selected, assemblyLine.GetComponent<UnityEngine.UI.Image>().color);
        }

        private UnityEngine.UI.Button MakeButton(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            go.transform.SetParent(_root.transform);
            var label = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            label.transform.SetParent(go.transform);
            return go.GetComponent<UnityEngine.UI.Button>();
        }
    }
}
