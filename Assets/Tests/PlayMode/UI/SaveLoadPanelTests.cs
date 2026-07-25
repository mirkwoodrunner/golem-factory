using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using GolemFactory.Blueprints;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode: SaveLoadPanel.Start() wires the real Button.onClick listeners this
    // test drives, same reason WorkbenchControllerTests is PlayMode. Writes/reads
    // SaveFileIO.DefaultPath (SaveLoadPanel has no path-injection hook, same as before
    // this class's OnGUI-to-UGUI conversion) -- cleaned up in TearDown like
    // SaveFileIOTests does for its own temp file.
    public class SaveLoadPanelTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
            if (File.Exists(SaveFileIO.DefaultPath))
            {
                File.Delete(SaveFileIO.DefaultPath);
            }
        }

        private (SaveLoadPanel panel, Text status) Build()
        {
            _root = new GameObject("Root");
            var buffers = _root.AddComponent<StorageBufferRegistryHolder>();
            var focus = _root.AddComponent<ArtificerFocusMeterHolder>();
            var patents = _root.AddComponent<PatentRegistryHolder>();

            var saveButton = new GameObject("Save", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            saveButton.transform.SetParent(_root.transform);
            var loadButton = new GameObject("Load", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            loadButton.transform.SetParent(_root.transform);
            var status = new GameObject("Status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            status.transform.SetParent(_root.transform);

            var panel = _root.AddComponent<SaveLoadPanel>();
            panel.Configure(buffers, focus, patents, new ChassisDefinition[0], new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            panel.ConfigureUI(saveButton, loadButton, status);
            return (panel, status);
        }

        [UnityTest]
        public IEnumerator SaveButton_CapturesState()
        {
            (SaveLoadPanel panel, Text status) = Build();
            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", null);
            yield return null;

            Button saveButton = _root.transform.Find("Save").GetComponent<Button>();
            saveButton.onClick.Invoke();

            Assert.AreEqual("Saved 1 golems.", status.text);
            Assert.IsTrue(File.Exists(SaveFileIO.DefaultPath));
        }

        [UnityTest]
        public IEnumerator LoadButton_MissingFile_ShowsStatusMessage()
        {
            (SaveLoadPanel panel, Text status) = Build();
            if (File.Exists(SaveFileIO.DefaultPath))
            {
                File.Delete(SaveFileIO.DefaultPath);
            }
            yield return null;

            Button loadButton = _root.transform.Find("Load").GetComponent<Button>();
            loadButton.onClick.Invoke();

            Assert.AreEqual("No save file found.", status.text);
        }

        [UnityTest]
        public IEnumerator LoadButton_RestoresState()
        {
            (SaveLoadPanel panel, Text status) = Build();
            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", null);
            yield return null;

            Button saveButton = _root.transform.Find("Save").GetComponent<Button>();
            Button loadButton = _root.transform.Find("Load").GetComponent<Button>();
            saveButton.onClick.Invoke();
            loadButton.onClick.Invoke();

            Assert.AreEqual("Loaded 1 golem programs.", status.text);
        }
    }
}
