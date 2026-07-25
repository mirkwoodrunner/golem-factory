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
    }
}
