using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The rosters <c>SaveLoadPanel</c> hands to <c>DefinitionCatalog</c>, checked against the
    /// assets on disk -- following <c>RecipeCatalogTests</c>, and for the same reason: a bad
    /// authoring run must fail the suite rather than fail the player mid-game.
    ///
    /// <para>
    /// THE FAILURE THIS EXISTS TO CATCH IS SILENT AND DESTRUCTIVE. A load resolves every asset in
    /// a save file by NAME through those rosters; a name the catalog cannot resolve is skipped,
    /// with no error and no stall, and the golem comes back with that step missing from its
    /// program. The player sees a factory that loaded "fine" and then behaves differently.
    /// </para>
    ///
    /// <para>
    /// It had already happened. The panel was still carrying M8's four-card tutorial deck while
    /// the Workbench had been re-authored to 23, so every golem programmed with any of the
    /// nineteen Assemble cards -- which is every production golem in the game -- reloaded with an
    /// empty program. The same stale deck had already been found once, on the Workbench, where it
    /// made Iron Plate unreachable; this is the second copy of it.
    /// </para>
    /// </summary>
    public class SaveCatalogCoverageTests
    {
        private const string SandboxScene = "Assets/_Project/Scenes/Sandbox.unity";
        private const string AppendageRoot = "Assets/_Project/ScriptableObjects/Appendages";
        private const string ChassisRoot = "Assets/_Project/ScriptableObjects/Chassis";
        private const string LogicCoreRoot = "Assets/_Project/ScriptableObjects/LogicCores";

        private static List<string> AuthoredNames(string type, string root)
        {
            var names = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + type, new[] { root }))
            {
                names.Add(System.IO.Path.GetFileNameWithoutExtension(
                    AssetDatabase.GUIDToAssetPath(guid)));
            }
            return names;
        }

        private static void AssertCoversEveryAsset<T>(
            IEnumerable<T> roster, string type, string root, string what) where T : Object
        {
            var inRoster = new HashSet<string>();
            foreach (T asset in roster)
            {
                if (asset != null)
                {
                    inRoster.Add(asset.name);
                }
            }

            var missing = new List<string>();
            foreach (string name in AuthoredNames(type, root))
            {
                if (!inRoster.Contains(name))
                {
                    missing.Add(name);
                }
            }

            Assert.IsEmpty(missing,
                $"Sandbox's save catalog cannot resolve {missing.Count} authored {what}. A save "
                + "naming any of these loads with that part of the program silently dropped: "
                + string.Join(", ", missing));
        }

        private static SaveLoadPanel OpenSandboxPanel(out Scene scene)
        {
            scene = EditorSceneManager.OpenScene(SandboxScene, OpenSceneMode.Additive);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var panel = root.GetComponentInChildren<SaveLoadPanel>(true);
                if (panel != null)
                {
                    return panel;
                }
            }

            return null;
        }

        [Test]
        public void SandboxSaveCatalog_ResolvesEveryAuthoredDefinition()
        {
            SaveLoadPanel panel = OpenSandboxPanel(out Scene scene);
            try
            {
                Assert.IsNotNull(panel, "Sandbox has no SaveLoadPanel, so it cannot load a save at all");

                var so = new SerializedObject(panel);
                AssertCoversEveryAsset(
                    ReadRoster<AppendageActionDefinition>(so, "appendageRoster"),
                    "AppendageActionDefinition", AppendageRoot, "appendage cards");
                AssertCoversEveryAsset(
                    ReadRoster<ChassisDefinition>(so, "chassisRoster"),
                    "ChassisDefinition", ChassisRoot, "chassis");
                AssertCoversEveryAsset(
                    ReadRoster<LogicCoreDefinition>(so, "logicCoreRoster"),
                    "LogicCoreDefinition", LogicCoreRoot, "logic cores");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        // The catalog must be at least as large as what the player can actually put on a golem.
        // Asserted separately from the coverage test above because the two could not fail for the
        // same reason: this one compares the panel against the WORKBENCH, so it still fires if
        // both were populated from a shared-but-wrong source.
        [Test]
        public void SandboxSaveCatalog_ResolvesEveryCardTheWorkbenchOffers()
        {
            SaveLoadPanel panel = OpenSandboxPanel(out Scene scene);
            try
            {
                Assert.IsNotNull(panel);

                WorkbenchController workbench = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    workbench = root.GetComponentInChildren<WorkbenchController>(true);
                    if (workbench != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(workbench, "Sandbox has no Workbench");

                var catalogNames = new HashSet<string>();
                foreach (AppendageActionDefinition card in
                         ReadRoster<AppendageActionDefinition>(new SerializedObject(panel), "appendageRoster"))
                {
                    if (card != null)
                    {
                        catalogNames.Add(card.name);
                    }
                }

                var unresolvable = new List<string>();
                foreach (AppendageActionDefinition card in
                         ReadRoster<AppendageActionDefinition>(new SerializedObject(workbench), "availableAppendages"))
                {
                    if (card != null && !catalogNames.Contains(card.name))
                    {
                        unresolvable.Add(card.name);
                    }
                }

                Assert.IsEmpty(unresolvable,
                    "the player can program cards a load cannot resolve: " + string.Join(", ", unresolvable));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static List<T> ReadRoster<T>(SerializedObject so, string propertyName) where T : Object
        {
            var result = new List<T>();
            SerializedProperty list = so.FindProperty(propertyName);
            Assert.IsNotNull(list, propertyName + " is not a field on " + so.targetObject.GetType().Name);
            for (int i = 0; i < list.arraySize; i++)
            {
                result.Add(list.GetArrayElementAtIndex(i).objectReferenceValue as T);
            }
            return result;
        }
    }
}
