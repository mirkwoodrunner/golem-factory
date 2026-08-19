using System;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GolemFactory.Progression;
using GolemFactory.UI;

namespace GolemFactory.Editor
{
    /// <summary>
    /// Imports the Artificer's Ledger art and wires the Tech Tree tab into
    /// <c>WorkbenchCanvas.prefab</c>, <c>ManagerHolders.prefab</c> and <c>Sandbox.unity</c>.
    ///
    /// <para>
    /// Idempotent, in the same find-or-create shape as <c>ProgressionSceneAuthoring</c>: re-run it
    /// after regenerating the art or retuning the chart and it updates in place rather than
    /// stacking a second tab. And it ends by <em>reading the scene back</em>, because Sandbox
    /// carries prefab overrides -- a value written successfully to a prefab can be silently
    /// replaced by the scene's own copy, which is exactly how <c>hideWhileOpen</c> was reported
    /// wired while the HUD still drew over the Workbench modal.
    /// </para>
    /// </summary>
    public static class TechTreeAuthoring
    {
        private const string ArtRoot = "Assets/_Project/Art/UI/TechTree/";
        private const string PrefabRoot = "Assets/_Project/Prefabs/";
        private const string WorkbenchCanvasPath = PrefabRoot + "WorkbenchCanvas.prefab";
        private const string ManagerHoldersPath = PrefabRoot + "ManagerHolders.prefab";
        private const string SandboxScene = "Assets/_Project/Scenes/Sandbox.unity";

        private const string TabName = "TechTreeTab";
        private const string TabButtonName = "TechTreeButton";
        private const string TabCaption = "Ledger";

        // The 32x32 plaques are 9-sliced with a 10px border: that keeps each plaque's rivets,
        // punch-hole column and stamped seal at native size on a 196x52 card instead of smearing
        // them across it. The badges, the field and the dashed rules are never sliced -- the
        // badges are drawn at native size and the other two are tiled.
        private static readonly (string File, int Border)[] SpriteTable =
        {
            ("tt_field.png", 0),
            ("tt_phase_plate.png", 10),
            ("tt_node_locked.png", 10),
            ("tt_node_available.png", 10),
            ("tt_node_researched.png", 10),
            ("tt_node_planned.png", 10),
            ("tt_badge_cog.png", 0),
            ("tt_badge_crucible.png", 0),
            ("tt_badge_anvil.png", 0),
            ("tt_badge_hand.png", 0),
            ("tt_badge_tower.png", 0),
            ("tt_line_h.png", 0),
            ("tt_line_v.png", 0)
        };

        private static readonly Color ViewportColor = new Color(0.06f, 0.05f, 0.04f, 1f);
        private static readonly Color HeaderColor = new Color(0.85f, 0.66f, 0.32f, 1f);

        private const float HeaderHeight = 22f;
        private const float LegendHeight = 20f;

        private static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Tools/Golem Factory/Author Tech Tree Chart")]
        public static void AuthorAll()
        {
            Log.Clear();
            try
            {
                ImportSprites();
                AuthorManagerHolders();
                AuthorWorkbenchCanvas();
                VerifySandboxScene();
                AssetDatabase.SaveAssets();
                Debug.Log("Tech Tree authoring complete.\n" + Log);
            }
            catch (Exception e)
            {
                Debug.LogError("Tech Tree authoring FAILED.\n" + Log + "\n" + e);
                throw;
            }
        }

        private static void Note(string message) => Log.AppendLine("  " + message);

        // ---- art ------------------------------------------------------------------------

        private static void ImportSprites()
        {
            Log.AppendLine("[art]");
            foreach ((string file, int border) in SpriteTable)
            {
                string path = ArtRoot + file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException("Missing sprite: " + path +
                        " -- run python Tools/Art/generate_tech_tree_art.py first.");
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 64f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.spriteBorder = new Vector4(border, border, border, border);

                // spriteBorder on the importer is not read for Single-mode sprites in every
                // Unity version; the TextureImporterSettings copy is the one that sticks.
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteBorder = new Vector4(border, border, border, border);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);

                importer.SaveAndReimport();
            }

            Note($"{SpriteTable.Length} sprites imported at PPU 64, point, uncompressed");
        }

        private static Sprite LoadSprite(string file)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + file);
            if (sprite == null)
            {
                throw new InvalidOperationException("Sprite not found: " + ArtRoot + file);
            }

            return sprite;
        }

        // ---- ManagerHolders: the progress tracker ----------------------------------------

        private static void AuthorManagerHolders()
        {
            Log.AppendLine("[ManagerHolders.prefab]");
            GameObject root = PrefabUtility.LoadPrefabContents(ManagerHoldersPath);
            try
            {
                GameObject host = EnsureChild(root, "TechTreeProgress");
                var tracker = Ensure<TechTreeProgressTracker>(host);

                // The tracker reads both of these, and both live on THIS prefab -- which is the
                // whole reason it lives here too rather than on the canvas beside the panel.
                var so = new SerializedObject(tracker);
                AssignIfPresent(so, "bufferRegistryHolder",
                    root.GetComponentInChildren<Economy.StorageBufferRegistryHolder>(true));
                AssignIfPresent(so, "clockTowerHolder",
                    root.GetComponentInChildren<ClockTower.ClockTowerSiteHolder>(true));
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, ManagerHoldersPath);
                Note("TechTreeProgress host + TechTreeProgressTracker wired");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- WorkbenchCanvas: the fifth Management tab -----------------------------------

        private static void AuthorWorkbenchCanvas()
        {
            Log.AppendLine("[WorkbenchCanvas.prefab]");
            GameObject root = PrefabUtility.LoadPrefabContents(WorkbenchCanvasPath);
            try
            {
                Transform screen = FindDeep(root.transform, "ManagementScreen")
                    ?? throw new InvalidOperationException("ManagementScreen not found.");
                Transform tabBar = FindDeep(screen, "TabBar")
                    ?? throw new InvalidOperationException("TabBar not found.");
                Transform tabContent = FindDeep(screen, "TabContent")
                    ?? throw new InvalidOperationException("TabContent not found.");

                Button button = BuildTabButton(tabBar);
                TechTreePanel panel = BuildTab(tabContent);

                ManagementPanel management = root.GetComponentInChildren<ManagementPanel>(true);
                var so = new SerializedObject(management);
                AssignIfPresent(so, "techTreeTab", panel.gameObject);
                AssignIfPresent(so, "techTreePanel", panel);
                AssignIfPresent(so, "techTreeTabButton", button);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, WorkbenchCanvasPath);
                Note("TechTreeButton + TechTreeTab authored and wired into ManagementPanel");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The tab button is a CLONE of the Save/Load button, not a hand-built one: its brass
        /// plate sprite, LayoutElement width, font and caption colour all come along, and cannot
        /// drift from the four buttons beside it. Same reasoning as the sixth appendage socket.
        /// </summary>
        private static Button BuildTabButton(Transform tabBar)
        {
            Transform existing = tabBar.Find(TabButtonName);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                Transform template = tabBar.Find("SaveLoadButton")
                    ?? throw new InvalidOperationException("SaveLoadButton template not found.");
                go = UnityEngine.Object.Instantiate(template.gameObject, tabBar);
                go.name = TabButtonName;
            }

            go.transform.SetAsLastSibling();

            TextMeshProUGUI label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = TabCaption;
            }

            return go.GetComponent<Button>();
        }

        private static TechTreePanel BuildTab(Transform tabContent)
        {
            GameObject tab = EnsureUiChild(tabContent, TabName);
            Stretch((RectTransform)tab.transform, 0f, 0f, 0f, 0f);
            var panel = Ensure<TechTreePanel>(tab);

            GameObject headerGo = EnsureUiChild(tab.transform, "Header");
            var headerRect = (RectTransform)headerGo.transform;
            TopStrip(headerRect, HeaderHeight, 4f);
            var header = Ensure<TextMeshProUGUI>(headerGo);
            header.fontSize = 14f;
            header.fontStyle = FontStyles.Bold;
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.color = HeaderColor;
            header.raycastTarget = false;
            header.text = "THE ARTIFICER'S LEDGER";

            GameObject legendGo = EnsureUiChild(tab.transform, "Legend");
            var legendRect = (RectTransform)legendGo.transform;
            TopStrip(legendRect, LegendHeight, 4f + HeaderHeight + 2f);

            GameObject scrollGo = EnsureUiChild(tab.transform, "ChartScroll");
            var scrollRect = (RectTransform)scrollGo.transform;
            Stretch(scrollRect, 0f, 0f, 0f, 4f + HeaderHeight + 2f + LegendHeight + 4f);

            GameObject viewportGo = EnsureUiChild(scrollGo.transform, "Viewport");
            Stretch((RectTransform)viewportGo.transform, 0f, 0f, 0f, 0f);
            Image viewportImage = Ensure<Image>(viewportGo);
            viewportImage.color = ViewportColor;
            // The mask is what keeps a 1720px chart inside the panel. Without it the chart draws
            // straight over the tab bar and the close button.
            Ensure<RectMask2D>(viewportGo);

            GameObject chartGo = EnsureUiChild(viewportGo.transform, "Chart");
            var chart = (RectTransform)chartGo.transform;
            chart.anchorMin = new Vector2(0f, 1f);
            chart.anchorMax = new Vector2(0f, 1f);
            chart.pivot = new Vector2(0f, 1f);
            chart.anchoredPosition = Vector2.zero;

            var scroll = Ensure<ScrollRect>(scrollGo);
            scroll.content = chart;
            scroll.viewport = (RectTransform)viewportGo.transform;
            // Both axes: the chart is wider than any screen and taller than the panel.
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var so = new SerializedObject(panel);
            AssignIfPresent(so, "chartRoot", chart);
            AssignIfPresent(so, "legendRoot", legendRect);
            AssignIfPresent(so, "headerText", header);
            AssignIfPresent(so, "fieldSprite", LoadSprite("tt_field.png"));
            AssignIfPresent(so, "phasePlateSprite", LoadSprite("tt_phase_plate.png"));
            AssignIfPresent(so, "nodeLockedSprite", LoadSprite("tt_node_locked.png"));
            AssignIfPresent(so, "nodeAvailableSprite", LoadSprite("tt_node_available.png"));
            AssignIfPresent(so, "nodeResearchedSprite", LoadSprite("tt_node_researched.png"));
            AssignIfPresent(so, "nodePlannedSprite", LoadSprite("tt_node_planned.png"));
            AssignIfPresent(so, "lineHorizontalSprite", LoadSprite("tt_line_h.png"));
            AssignIfPresent(so, "lineVerticalSprite", LoadSprite("tt_line_v.png"));
            AssignIfPresent(so, "badgeChassisSprite", LoadSprite("tt_badge_cog.png"));
            AssignIfPresent(so, "badgeRecipeSprite", LoadSprite("tt_badge_crucible.png"));
            AssignIfPresent(so, "badgeBuildingSprite", LoadSprite("tt_badge_anvil.png"));
            AssignIfPresent(so, "badgeTechniqueSprite", LoadSprite("tt_badge_hand.png"));
            AssignIfPresent(so, "badgeMilestoneSprite", LoadSprite("tt_badge_tower.png"));
            so.ApplyModifiedPropertiesWithoutUndo();

            // Inactive on disk, exactly like PatentsTab/AssemblyLineTab/SaveLoadTab: the screen
            // opens on Inventory and SelectTab turns exactly one on.
            tab.SetActive(false);
            return panel;
        }

        // ---- read-back -------------------------------------------------------------------

        /// <summary>
        /// Opens Sandbox and checks what the GAME will load, not what the authoring pass wrote.
        /// The prefab instance in the scene can carry overrides that shadow every value above.
        /// </summary>
        private static void VerifySandboxScene()
        {
            Log.AppendLine("[Sandbox.unity read-back]");
            Scene scene = EditorSceneManager.OpenScene(SandboxScene, OpenSceneMode.Single);
            try
            {
                var panel = FindInScene<TechTreePanel>(scene);
                var management = FindInScene<ManagementPanel>(scene);
                var tracker = FindInScene<TechTreeProgressTracker>(scene);

                Note(panel != null ? "TechTreePanel present" : "MISSING: TechTreePanel");
                Note(tracker != null ? "TechTreeProgressTracker present" : "MISSING: TechTreeProgressTracker");

                if (management == null)
                {
                    Note("MISSING: ManagementPanel");
                    return;
                }

                var so = new SerializedObject(management);
                bool rewired = false;
                rewired |= RepairReference(so, "techTreePanel", panel);
                rewired |= RepairReference(so, "techTreeTab", panel != null ? panel.gameObject : null);
                rewired |= RepairReference(
                    so, "techTreeTabButton",
                    panel != null ? FindButtonInScene(scene) : null);

                if (rewired)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Note("scene instance carried a null override; repaired in the scene and saved");
                }
                else
                {
                    Note("ManagementPanel's tech-tree references survive into the scene");
                }
            }
            finally
            {
                // Left open deliberately -- this is the scene an author is about to press Play in.
            }
        }

        private static Button FindButtonInScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindDeep(root.transform, TabButtonName);
                if (found != null)
                {
                    return found.GetComponent<Button>();
                }
            }

            return null;
        }

        private static bool RepairReference(
            SerializedObject so, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null || value == null || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        // ---- helpers ---------------------------------------------------------------------

        private static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static GameObject EnsureChild(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            if (child != null)
            {
                return child.gameObject;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static GameObject EnsureUiChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                return child.gameObject;
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void TopStrip(RectTransform rect, float height, float topOffset)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(8f, 0f);
            rect.offsetMax = new Vector2(-8f, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            rect.anchoredPosition = new Vector2(0f, -topOffset);
        }

        private static void AssignIfPresent(
            SerializedObject so, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"No serialized property '{propertyName}' on {so.targetObject.GetType().Name}.");
            }

            property.objectReferenceValue = value;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
    }
}
