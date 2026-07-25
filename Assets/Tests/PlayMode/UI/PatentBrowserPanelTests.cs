using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode: Refresh() instantiates real GameObjects (rows + a Load Button)
    // under a RectTransform, and the test drives the real Button.onClick.
    public class PatentBrowserPanelTests
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

        private (PatentBrowserPanel panel, PatentRegistryHolder patents, WorkbenchController workbench, RectTransform content)
            Build()
        {
            _root = new GameObject("Root");
            var patents = _root.AddComponent<PatentRegistryHolder>();
            var workbench = _root.AddComponent<WorkbenchController>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_root.transform);
            var panel = _root.AddComponent<PatentBrowserPanel>();
            panel.Configure(patents, workbench, null);
            panel.ConfigureUI(content);
            return (panel, patents, workbench, content);
        }

        [UnityTest]
        public IEnumerator Refresh_ListsAllPatentedBlueprints()
        {
            (PatentBrowserPanel panel, PatentRegistryHolder patents, _, RectTransform content) = Build();
            patents.Registry.TryPatent(new Blueprint("BP-001", PlaceableBuilding.LocalPlayerOwnerId, null, null, new List<GolemFactory.PunchCards.AppendageActionDefinition>()));
            patents.Registry.TryPatent(new Blueprint("BP-002", PlaceableBuilding.LocalPlayerOwnerId, null, null, new List<GolemFactory.PunchCards.AppendageActionDefinition>()));
            yield return null;

            panel.Refresh();

            Assert.AreEqual(2, content.childCount);
        }

        [UnityTest]
        public IEnumerator LoadButton_LoadsBlueprintIntoDraft_AndOpensWorkbench()
        {
            var chassis = ScriptableObject.CreateInstance<GolemFactory.PunchCards.ChassisDefinition>();
            (PatentBrowserPanel panel, PatentRegistryHolder patents, WorkbenchController workbench, RectTransform content) = Build();
            patents.Registry.TryPatent(new Blueprint("BP-001", PlaceableBuilding.LocalPlayerOwnerId, chassis, null, new List<GolemFactory.PunchCards.AppendageActionDefinition>()));
            yield return null;

            panel.Refresh();
            Transform row = content.GetChild(0);
            Button loadButton = row.Find("Load").GetComponent<Button>();
            loadButton.onClick.Invoke();

            Assert.IsTrue(workbench.IsOpen);
        }
    }
}
