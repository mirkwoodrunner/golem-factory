using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolemFactory.Editor
{
    // Read-only inspection of the scenes and prefabs ProgressionSceneAuthoring writes, in two
    // modes. Neither ever mutates anything the game ships.
    //
    //   Run()    -- dumps a hierarchy (names, components, RectTransform anchors). Sandbox.unity
    //               is ~25k lines of YAML and WorkbenchCanvas.prefab ~10k, so reading structure
    //               out of the files by hand is not practical; loading them through the Editor
    //               API and printing the tree is. It also round-trips a throwaway prefab, which
    //               is what originally answered the backlog's open question -- batch-mode
    //               -executeMethod does reach prefabs and scenes, not only .asset files.
    //
    //   Verify() -- reads back what Sandbox.unity ACTUALLY ends up with after an authoring run.
    //
    // VERIFY IS NOT REDUNDANT WITH THE AUTHORING LOG, and the reason is worth keeping: the scene
    // carries prefab OVERRIDES, so a value successfully written to WorkbenchCanvas.prefab can be
    // silently replaced by the scene's own copy. That is exactly what happened to hideWhileOpen
    // -- the authoring pass reported success, and the two new HUD readouts would still have
    // drawn over the full-screen Workbench in the one scene anybody plays. An authoring pass
    // reports what it wrote; only a read-back reports what the game will load.
    //
    //   "<UnityPath>/Unity.exe" -batchmode -quit -projectPath "<repo>" \
    //     -executeMethod GolemFactory.Editor.SceneProbe.Verify -logFile "<somewhere>/verify.log"
    public static class SceneProbe
    {
        // NOT Temp/: Unity wipes that directory on shutdown, so a report written there is gone
        // by the time the batch run's exit code comes back. Overridable from the command line.
        private static string OutputPath =>
            System.Environment.GetEnvironmentVariable("GF_PROBE_OUT") ?? "probe-report.txt";

        public static void Run()
        {
            var report = new StringBuilder();

            report.AppendLine("=== WRITE PROBE: can batch mode create a prefab? ===");
            report.AppendLine(ProbePrefabWrite());

            report.AppendLine();
            report.AppendLine("=== WorkbenchCanvas.prefab ===");
            report.AppendLine(DumpPrefab("Assets/_Project/Prefabs/WorkbenchCanvas.prefab"));

            report.AppendLine();
            report.AppendLine("=== ManagerHolders.prefab ===");
            report.AppendLine(DumpPrefab("Assets/_Project/Prefabs/ManagerHolders.prefab"));

            report.AppendLine();
            report.AppendLine("=== Sandbox.unity (roots only, depth 2) ===");
            report.AppendLine(DumpScene("Assets/_Project/Scenes/Sandbox.unity", 2));

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, report.ToString());
            Debug.Log("SceneProbe wrote " + OutputPath);
        }

        // Creates a throwaway prefab, reloads it from disk to confirm it really serialized, then
        // deletes it. Round-tripping matters: PrefabUtility can succeed in memory and still leave
        // nothing on disk if the asset database is never flushed in a headless run.
        private static string ProbePrefabWrite()
        {
            const string probePath = "Assets/_Project/Prefabs/__ProbeDelete.prefab";
            try
            {
                var go = new GameObject("__ProbeDelete");
                go.AddComponent<SpriteRenderer>();
                PrefabUtility.SaveAsPrefabAsset(go, probePath);
                UnityEngine.Object.DestroyImmediate(go);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(probePath);
                bool onDisk = File.Exists(probePath);
                string result = "SaveAsPrefabAsset -> reloaded=" + (reloaded != null) +
                                ", fileOnDisk=" + onDisk;
                AssetDatabase.DeleteAsset(probePath);
                return result;
            }
            catch (Exception e)
            {
                return "FAILED: " + e;
            }
        }

        /// <summary>
        /// Reads back what Sandbox.unity ACTUALLY ends up with, as opposed to what the authoring
        /// pass believes it wrote. Worth a separate pass because the scene carries prefab
        /// overrides: a value written to WorkbenchCanvas.prefab can be silently replaced by the
        /// scene's own copy, which is precisely what happened to <c>hideWhileOpen</c>.
        /// </summary>
        public static void Verify()
        {
            var sb = new StringBuilder();
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Sandbox.unity", OpenSceneMode.Single);

            var controller = FindInScene<GolemFactory.UI.WorkbenchController>(scene);
            var controllerSo = new SerializedObject(controller);
            sb.AppendLine("appendageSlotZones = " +
                          controllerSo.FindProperty("appendageSlotZones").arraySize + " (want 6)");

            SerializedProperty hide = controllerSo.FindProperty("hideWhileOpen");
            sb.AppendLine("hideWhileOpen = " + hide.arraySize);
            for (int i = 0; i < hide.arraySize; i++)
            {
                UnityEngine.Object o = hide.GetArrayElementAtIndex(i).objectReferenceValue;
                sb.AppendLine("   [" + i + "] " + (o == null ? "<null>" : o.name));
            }

            var build = FindInScene<GolemFactory.Player.BuildModeController>(scene);
            SerializedProperty prefabs =
                new SerializedObject(build).FindProperty("_availablePrefabs");
            sb.AppendLine("build menu = " + prefabs.arraySize);
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                UnityEngine.Object o = prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                sb.AppendLine("   [" + i + "] " + (o == null ? "<null>" : o.name));
            }

            var bootstrap = FindInScene<GolemFactory.World.SandboxBootstrap>(scene);
            var bootstrapSo = new SerializedObject(bootstrap);
            foreach (string field in new[]
                     {
                         "steamNetworkHolder", "clockTowerSiteHolder", "nodeExtractorHolder",
                         "steamFuelGaugeView", "clockTowerPanelView", "spatialEndpointHolder",
                         "requireSteamPower",
                     })
            {
                SerializedProperty p = bootstrapSo.FindProperty(field);
                sb.AppendLine("bootstrap." + field + " = " + (
                    p == null ? "<NO SUCH FIELD>"
                    : p.propertyType == SerializedPropertyType.Boolean ? p.boolValue.ToString()
                    : p.objectReferenceValue == null ? "<null>"
                    : p.objectReferenceValue.name));
            }

            var site = FindInScene<GolemFactory.ClockTower.ClockTowerSiteHolder>(scene);
            sb.AppendLine("clock tower stages = " +
                          (site == null ? "<no holder>"
                           : new SerializedObject(site).FindProperty("stages").arraySize.ToString()));

            foreach (GolemFactory.World.ResourceNodeMarker marker in
                     UnityEngine.Object.FindObjectsByType<GolemFactory.World.ResourceNodeMarker>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sb.AppendLine("marker " + marker.name + " at " + marker.transform.position);
            }

            Debug.Log("SceneProbe.Verify\n" + sb);
            File.WriteAllText(OutputPath, sb.ToString());
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string DumpPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                return "MISSING: " + path;
            }

            var sb = new StringBuilder();
            Describe(prefab.transform, 0, sb, int.MaxValue);
            return sb.ToString();
        }

        private static string DumpScene(string path, int maxDepth)
        {
            var sb = new StringBuilder();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                sb.AppendLine("opened=" + scene.IsValid() + " rootCount=" + scene.rootCount);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Describe(root.transform, 0, sb, maxDepth);
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("FAILED: " + e);
            }

            return sb.ToString();
        }

        private static void Describe(Transform t, int depth, StringBuilder sb, int maxDepth)
        {
            var indent = new string(' ', depth * 2);
            sb.Append(indent).Append(t.name);

            Component[] components = t.GetComponents<Component>();
            sb.Append("  [");
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(components[i] == null ? "<missing>" : components[i].GetType().Name);
            }

            sb.Append("]");

            if (t is RectTransform rect)
            {
                sb.Append(" anchors=").Append(rect.anchorMin).Append("/").Append(rect.anchorMax)
                  .Append(" pos=").Append(rect.anchoredPosition)
                  .Append(" size=").Append(rect.sizeDelta)
                  .Append(" pivot=").Append(rect.pivot);
            }

            sb.AppendLine();

            if (depth + 1 > maxDepth)
            {
                return;
            }

            for (int i = 0; i < t.childCount; i++)
            {
                Describe(t.GetChild(i), depth + 1, sb, maxDepth);
            }
        }
    }
}
