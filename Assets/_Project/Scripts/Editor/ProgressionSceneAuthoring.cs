using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Editor
{
    // The Editor passes docs/open-items.md §3 lists as "finished, tested C# with no prefab or
    // scene slot". Six systems were complete and unreachable: nothing in Sandbox.unity could
    // place a Boiler, a Steam Pipe or a Clock Tower, neither ITickable holder was in the scene
    // to tick, the two §8 readouts had nowhere to draw, the Workbench was a socket short of the
    // Zeppelin, and two of the five resource nodes had no marker to walk up to.
    //
    // SCRIPTED RATHER THAN HAND-WIRED, which the backlog flagged as unproven for prefabs and
    // scenes (only .asset files had been done this way, by ProgressionAssetAuthoring). It
    // works: PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset and EditorSceneManager.OpenScene
    // /SaveScene reach both, and SerializedObject reaches the private [SerializeField]s that
    // Configure(...) cannot (a Configure call does not survive being written to disk).
    //
    // IDEMPOTENT, for the same reason ProgressionAssetAuthoring is: every step looks for what it
    // would create and updates it in place, so re-running after a tuning edit never duplicates a
    // GameObject or renumbers a GUID. Run headless, with the Editor closed:
    //
    //   "<UnityPath>/Unity.exe" -batchmode -quit \
    //     -projectPath "<repo>" \
    //     -executeMethod GolemFactory.Editor.ProgressionSceneAuthoring.AuthorAll \
    //     -logFile "<somewhere>/author.log"
    //
    // ---------------------------------------------------------------------------------------
    // PLACEHOLDER ART, DELIBERATELY AND VISIBLY. There is no sprite in Assets/_Project/Art for
    // a boiler, a steam pipe, a clock tower, a coal seam, a copper vein or a zinc vein. Every
    // new object below reuses an existing sprite under a distinguishing tint, which is the same
    // trick golem_generic_brass/copper/steel already plays -- enough to tell them apart on the
    // floor, and not a substitute for art. Tints are collected in one table so replacing them
    // with real sprites is one edit, not a hunt.
    // ---------------------------------------------------------------------------------------
    public static class ProgressionSceneAuthoring
    {
        private const string PrefabRoot = "Assets/_Project/Prefabs/";
        private const string ArtRoot = "Assets/_Project/Art/";
        private const string SandboxScene = "Assets/_Project/Scenes/Sandbox.unity";
        private const string ClockTowerStageRoot = "Assets/_Project/ScriptableObjects/ClockTower/";

        private const string BoilerPrefabPath = PrefabRoot + "BoilerPrefab.prefab";
        private const string SteamPipePrefabPath = PrefabRoot + "SteamPipePrefab.prefab";
        private const string ClockTowerPrefabPath = PrefabRoot + "ClockTowerPrefab.prefab";
        private const string ManagerHoldersPath = PrefabRoot + "ManagerHolders.prefab";
        private const string WorkbenchCanvasPath = PrefabRoot + "WorkbenchCanvas.prefab";

        // Placeholder tints -- see the header note. Replace with real sprites, not new colours.
        private static readonly Color BoilerTint = new Color(0.82f, 0.40f, 0.26f, 1f);
        private static readonly Color SteamPipeTint = new Color(0.60f, 0.67f, 0.72f, 1f);
        private static readonly Color ClockTowerTint = new Color(0.88f, 0.74f, 0.36f, 1f);
        private static readonly Color CoalTint = new Color(0.24f, 0.23f, 0.25f, 1f);
        private static readonly Color CopperOreTint = new Color(0.80f, 0.45f, 0.24f, 1f);
        private static readonly Color ZincOreTint = new Color(0.72f, 0.76f, 0.80f, 1f);

        // Same warm-brass HUD vocabulary SteamFuelGaugeView and ClockTowerPanelView already use.
        private static readonly Color HudPlateColor = new Color(0.14f, 0.12f, 0.10f, 0.78f);
        private static readonly Color HudTextColor = new Color(0.85f, 0.66f, 0.32f, 1f);

        private static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Golem Factory/Author Progression Scene Wiring")]
        public static void AuthorAll()
        {
            Log.Clear();
            try
            {
                AuthorPlaceablePrefabs();
                AuthorManagerHolders();
                AuthorWorkbenchCanvas();
                AuthorSandboxScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("ProgressionSceneAuthoring OK\n" + Log);
            }
            catch (Exception e)
            {
                Debug.LogError("ProgressionSceneAuthoring FAILED\n" + Log + "\n" + e);
                throw;
            }
        }

        private static void Note(string message) => Log.AppendLine("  " + message);

        // ===================================================================================
        // 1. The three missing placeable prefabs.
        // ===================================================================================

        private static void AuthorPlaceablePrefabs()
        {
            Log.AppendLine("[prefabs]");

            // §3.1: a Boiler is 30 Scrap + 10 Iron Plate. CHARGED NOW, not merely recorded --
            // §1.4 could only record it because Iron Plate did not exist yet and
            // PlaceableBuilding still priced things as a scrapCost/brassCost int pair. §1.5
            // authored the item and §11 item 8 replaced the pair with a bundle, so both halves
            // of the reason are gone and the constants on SteamNetwork are the figures used.
            BuildPlaceable(
                BoilerPrefabPath, "BoilerPrefab", BoilerTint,
                new[]
                {
                    new RecipeIngredient(ItemType.Scrap, SteamNetwork.BoilerScrapCost),
                    new RecipeIngredient(ItemType.IronPlate, SteamNetwork.BoilerIronPlateCost),
                },
                go =>
                {
                    PlaceableBoiler boiler = Ensure<PlaceableBoiler>(go);
                    // A PLAYER-BUILT BOILER STARTS EMPTY. PlaceableBoiler.DefaultStartingCoke is
                    // 240 because §9 Phase 1 hands the player one already holding that; shipping
                    // it on the prefab would let anyone mint 240 Coke for 30 Scrap and 10 Iron
                    // Plate, which is cheaper than R1 makes it and would collapse §3.1's entire
                    // "Coke is the contended throat" premise into a build-a-boiler exploit.
                    var so = new SerializedObject(boiler);
                    so.FindProperty("startingCoke").intValue = 0;
                    // Left blank on purpose: RegisterWithSteamNetwork stamps a cell-based id
                    // ("Boiler(3,-2)") at placement, so two placed boilers cannot share a name.
                    so.FindProperty("boilerId").stringValue = "";
                    so.ApplyModifiedPropertiesWithoutUndo();
                });

            // §3.1: a Steam Pipe is 1 Iron Plate.
            BuildPlaceable(
                SteamPipePrefabPath, "SteamPipePrefab", SteamPipeTint,
                new[] { new RecipeIngredient(ItemType.IronPlate, SteamNetwork.SteamPipeIronPlateCost) },
                go => Ensure<PlaceableSteamPipe>(go));

            // NO COST, per PlaceableClockTower's own note: §7 places the tower in the world and
            // prices it at nothing. The megaproject is the goal, not a purchase.
            BuildPlaceable(
                ClockTowerPrefabPath, "ClockTowerPrefab", ClockTowerTint,
                Array.Empty<RecipeIngredient>(),
                go => Ensure<PlaceableClockTower>(go));
        }

        // Every placeable in this project is the same shape (DepotPrefab is the reference):
        // SpriteRenderer + the sealed PlaceableBuilding + YSortSpriteRenderer + one sibling
        // component carrying the actual behaviour. Built from that shape rather than by
        // duplicating DepotPrefab so the cost and the sibling are explicit at the call site.
        private static void BuildPlaceable(
            string path, string name, Color tint, RecipeIngredient[] cost, Action<GameObject> addBehaviour)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null
                ? PrefabUtility.LoadPrefabContents(path)
                : new GameObject(name);

            try
            {
                root.name = name;

                SpriteRenderer renderer = Ensure<SpriteRenderer>(root);
                renderer.sprite = LoadSprite("building_block.png");
                renderer.color = tint;

                Ensure<YSortSpriteRenderer>(root);
                PlaceableBuilding building = Ensure<PlaceableBuilding>(root);
                building.ConfigureCost(cost);
                // ConfigureCost writes a private field on a live instance; only SerializedObject
                // makes that survive to disk. EditorUtility.SetDirty is not enough on prefab
                // contents, so the cost is re-applied through the serialization API.
                WriteCost(building, cost);

                addBehaviour(root);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                Note(name + " -> " + path + "  cost=" + DescribeCost(cost));
            }
            finally
            {
                if (PrefabUtility.IsPartOfPrefabAsset(root) || root.scene.IsValid())
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                else
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void WriteCost(PlaceableBuilding building, RecipeIngredient[] cost)
        {
            var so = new SerializedObject(building);
            SerializedProperty list = so.FindProperty("cost");
            list.arraySize = cost.Length;
            for (int i = 0; i < cost.Length; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("itemType").stringValue = cost[i].itemType;
                element.FindPropertyRelative("quantity").intValue = cost[i].quantity;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string DescribeCost(RecipeIngredient[] cost)
        {
            if (cost.Length == 0)
            {
                return "free";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < cost.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(" + ");
                }

                sb.Append(cost[i].quantity).Append(' ').Append(cost[i].itemType);
            }

            return sb.ToString();
        }

        // ===================================================================================
        // 2. The two ITickable holders (plus the node-extractor registry, owed by the same
        //    "adding a holder to ManagerHolders.prefab is an Editor pass" note in
        //    SandboxBootstrap.WireSpatialGameplay).
        // ===================================================================================

        private static void AuthorManagerHolders()
        {
            Log.AppendLine("[ManagerHolders.prefab]");
            GameObject root = PrefabUtility.LoadPrefabContents(ManagerHoldersPath);
            try
            {
                Ensure<SteamNetworkHolder>(EnsureChild(root, "Steam"));
                Ensure<NodeExtractorRegistryHolder>(EnsureChild(root, "NodeExtractors"));

                ClockTowerSiteHolder site = Ensure<ClockTowerSiteHolder>(EnsureChild(root, "ClockTower"));
                WriteClockTowerStages(site);

                PrefabUtility.SaveAsPrefabAsset(root, ManagerHoldersPath);
                Note("Steam / ClockTower / NodeExtractors holders present");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // §7's four stages, in authored order. Sorted by asset name so Stage1..Stage4 land in
        // sequence rather than in whatever order the asset database happens to return -- the
        // site advances through this list by index, so the order IS the progression.
        private static void WriteClockTowerStages(ClockTowerSiteHolder holder)
        {
            var stages = new List<ClockTowerStageDefinition>();
            string[] guids = AssetDatabase.FindAssets(
                "t:ClockTowerStageDefinition", new[] { ClockTowerStageRoot.TrimEnd('/') });
            var paths = new List<string>();
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                stages.Add(AssetDatabase.LoadAssetAtPath<ClockTowerStageDefinition>(path));
            }

            var so = new SerializedObject(holder);
            SerializedProperty list = so.FindProperty("stages");
            list.arraySize = stages.Count;
            for (int i = 0; i < stages.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("ClockTower stages wired: " + stages.Count);
        }

        // ===================================================================================
        // 3. The Workbench's sixth appendage socket, and the two §8 HUD readouts.
        // ===================================================================================

        private static void AuthorWorkbenchCanvas()
        {
            Log.AppendLine("[WorkbenchCanvas.prefab]");
            GameObject root = PrefabUtility.LoadPrefabContents(WorkbenchCanvasPath);
            try
            {
                AddSixthAppendageSocket(root);
                GameObject gauge = BuildSteamGauge(root);
                GameObject tower = BuildClockTowerPanel(root);
                RegisterHudChrome(root, gauge, tower);

                PrefabUtility.SaveAsPrefabAsset(root, WorkbenchCanvasPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // The Zeppelin Freight Loader has SIX slots (§1.1 reset chassis to 2/3/4/5/6) and the
        // screen had five sockets, so its sixth step was unreachable -- and §1.5 made that live
        // content by authoring R14 and R17, the two 4-input recipes.
        //
        // The new socket is a CLONE of AppendageSlot4 rather than a hand-built RectTransform,
        // so every piece of styling on it (plate sprite, caption font, hint colour, drop-zone
        // wiring) comes along without being restated here and cannot drift from its siblings.
        private static void AddSixthAppendageSocket(GameObject root)
        {
            Transform stack = FindDeep(root.transform, "SlotStack");
            if (stack == null)
            {
                throw new InvalidOperationException("SlotStack not found in WorkbenchCanvas.prefab");
            }

            Transform template = FindDeep(root.transform, "AppendageSlot4");
            Transform existing = FindDeep(root.transform, "AppendageSlot5");
            GameObject slot5 = existing != null
                ? existing.gameObject
                : UnityEngine.Object.Instantiate(template.gameObject, stack);
            slot5.name = "AppendageSlot5";

            WorkbenchDropZone zone = slot5.GetComponent<WorkbenchDropZone>();
            zone.Configure(DropZoneKind.Appendage, 5);
            var zoneSo = new SerializedObject(zone);
            // Configure() writes private fields on the instance; SerializedObject is what makes
            // them survive SaveAsPrefabAsset.
            zoneSo.FindProperty("kind").enumValueIndex = (int)DropZoneKind.Appendage;
            zoneSo.FindProperty("appendageIndex").intValue = 5;
            zoneSo.ApplyModifiedPropertiesWithoutUndo();

            RespaceSlotStack(root);

            // Finally, the array the controller actually reads. _draftAppendages is sized from
            // appendageSlotZones.Length, so the socket does nothing until it is in this list.
            WorkbenchController controller = root.GetComponentInChildren<WorkbenchController>(true);
            var so = new SerializedObject(controller);
            SerializedProperty zones = so.FindProperty("appendageSlotZones");
            zones.arraySize = 6;
            for (int i = 0; i < 6; i++)
            {
                Transform slot = FindDeep(root.transform, "AppendageSlot" + i);
                zones.GetArrayElementAtIndex(i).objectReferenceValue = slot.GetComponent<WorkbenchDropZone>();
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("AppendageSlot5 added; appendageSlotZones = 6");
        }

        // Seven rows (one logic core + six appendages) now share the height six used to.
        // Divided evenly rather than squeezed in at the bottom: the sockets are a vertical list
        // the player reads as a program, and one short row would read as a different kind of
        // slot. Row pitch 0.14 of the stack with a 0.02 gap, against the old 0.17/0.02 -- so
        // each socket loses about a fifth of its height. THAT IS THE ONE JUDGEMENT CALL HERE
        // and it is worth a look on a real screen before it is called final.
        private static void RespaceSlotStack(GameObject root)
        {
            string[] rows =
            {
                "LogicCoreSlot", "AppendageSlot0", "AppendageSlot1", "AppendageSlot2",
                "AppendageSlot3", "AppendageSlot4", "AppendageSlot5",
            };

            const float top = 0.99f;
            const float pitch = 0.14f;
            const float height = 0.12f;

            for (int i = 0; i < rows.Length; i++)
            {
                Transform row = FindDeep(root.transform, rows[i]);
                var rect = (RectTransform)row;
                float rowTop = top - pitch * i;
                rect.anchorMin = new Vector2(rect.anchorMin.x, rowTop - height);
                rect.anchorMax = new Vector2(rect.anchorMax.x, rowTop);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                // Order in the hierarchy has to match order on screen, or a future layout group
                // (or anyone reading the prefab) sees a different program than the player does.
                row.SetSiblingIndex(i);
            }

            Note("SlotStack respaced for 7 rows");
        }

        // §8's fuel gauge: "240 Coke - 24/min - 10:00 left". Top-left, opposite the alerts
        // strip's top-centre, because it is a standing readout rather than an interruption.
        private static GameObject BuildSteamGauge(GameObject root)
        {
            Transform canvas = FindDeep(root.transform, "WorkbenchCanvas");
            GameObject panel = EnsureUiChild(canvas, "SteamGauge");
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -10f);
            rect.sizeDelta = new Vector2(268f, 28f);

            Image plate = Ensure<Image>(panel);
            plate.color = HudPlateColor;
            plate.raycastTarget = false;

            TextMeshProUGUI text = EnsureLabel(panel.transform, "GaugeText", 14f, TextAlignmentOptions.Left);

            SteamFuelGaugeView view = Ensure<SteamFuelGaugeView>(panel);
            var so = new SerializedObject(view);
            // steamNetworkHolder is deliberately NOT set here: it lives on ManagerHolders, a
            // DIFFERENT prefab, and a prefab cannot hold a reference into another prefab -- it
            // resolves to null on instantiation. SandboxBootstrap hands it over at runtime
            // instead, which is the Configure(...) idiom this project already uses for exactly
            // this trap.
            so.FindProperty("gaugeText").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            Note("SteamGauge built (holder wired at runtime by SandboxBootstrap)");
            return panel;
        }

        // §8's four columns (Required / Delivered / Fresh / xmultiplier) plus the starved-item
        // alert. Top-right, tall enough for a headline, a four-row table and one alert line.
        private static GameObject BuildClockTowerPanel(GameObject root)
        {
            Transform canvas = FindDeep(root.transform, "WorkbenchCanvas");
            GameObject panel = EnsureUiChild(canvas, "ClockTowerPanel");
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-12f, -10f);
            rect.sizeDelta = new Vector2(320f, 148f);

            Image plate = Ensure<Image>(panel);
            plate.color = HudPlateColor;
            plate.raycastTarget = false;

            TextMeshProUGUI headline = EnsureLabel(panel.transform, "Headline", 14f, TextAlignmentOptions.TopLeft);
            StretchRow((RectTransform)headline.transform, 0.80f, 0.98f);

            TextMeshProUGUI table = EnsureLabel(panel.transform, "DemandTable", 12f, TextAlignmentOptions.TopLeft);
            StretchRow((RectTransform)table.transform, 0.20f, 0.78f);

            TextMeshProUGUI alert = EnsureLabel(panel.transform, "Alert", 12f, TextAlignmentOptions.TopLeft);
            StretchRow((RectTransform)alert.transform, 0.02f, 0.18f);

            ClockTowerPanelView view = Ensure<ClockTowerPanelView>(panel);
            var so = new SerializedObject(view);
            // siteHolder left null for the same cross-prefab reason as the gauge above.
            so.FindProperty("headlineText").objectReferenceValue = headline;
            so.FindProperty("demandTableText").objectReferenceValue = table;
            so.FindProperty("alertText").objectReferenceValue = alert;
            so.ApplyModifiedPropertiesWithoutUndo();

            Note("ClockTowerPanel built (holder wired at runtime by SandboxBootstrap)");
            return panel;
        }

        // Both readouts are world HUD, so they must vanish behind the full-screen Workbench and
        // Management modals exactly as the build menu does -- otherwise a fuel gauge floats over
        // the punch-card screen. hideWhileOpen is the mechanism that already exists for this.
        private static void RegisterHudChrome(GameObject root, params GameObject[] chrome)
        {
            WorkbenchController controller = root.GetComponentInChildren<WorkbenchController>(true);
            var so = new SerializedObject(controller);
            SerializedProperty list = so.FindProperty("hideWhileOpen");

            var kept = new List<UnityEngine.Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object existing = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (existing != null && Array.IndexOf(chrome, existing) < 0)
                {
                    kept.Add(existing);
                }
            }

            kept.AddRange(chrome);
            list.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("hideWhileOpen now covers " + kept.Count + " HUD objects");
        }

        // ===================================================================================
        // 4. Sandbox.unity: the three new placeables in the build menu, the two missing node
        //    markers, and the bootstrap references the new holders need.
        // ===================================================================================

        private static void AuthorSandboxScene()
        {
            Log.AppendLine("[Sandbox.unity]");
            Scene scene = EditorSceneManager.OpenScene(SandboxScene, OpenSceneMode.Single);

            AddPlaceablesToBuildMenu(scene);
            AddNodeMarkers(scene);
            WireBootstrapHolders(scene);
            RegisterSceneHudChrome(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void AddPlaceablesToBuildMenu(Scene scene)
        {
            BuildModeController build = FindInScene<BuildModeController>(scene);
            var so = new SerializedObject(build);
            SerializedProperty list = so.FindProperty("_availablePrefabs");

            var prefabs = new List<PlaceableBuilding>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var existing = list.GetArrayElementAtIndex(i).objectReferenceValue as PlaceableBuilding;
                if (existing != null)
                {
                    prefabs.Add(existing);
                }
            }

            foreach (string path in new[] { BoilerPrefabPath, SteamPipePrefabPath, ClockTowerPrefabPath })
            {
                PlaceableBuilding prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<PlaceableBuilding>();
                if (!prefabs.Contains(prefab))
                {
                    prefabs.Add(prefab);
                }
            }

            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("build menu offers " + prefabs.Count + " placeables");
        }

        // §5.1's copper and zinc veins had nowhere to stand: SandboxBootstrap registers the
        // nodes so a golem can reach them BY ID, but nothing in the world let the player walk up
        // to one -- and a spatially placed golem finds a node by what is on the tile, so an
        // unmarked node is unreachable to the machine model too.
        //
        // Placed at 5 world units from the origin on the same ring as the existing three, which
        // is the spacing ScrapNode and CoalNode already sit at.
        private static void AddNodeMarkers(Scene scene)
        {
            ResourceNodeRegistryHolder registry = FindInScene<ResourceNodeRegistryHolder>(scene);

            EnsureNodeMarker(scene, registry, "CopperOreNodeMarker", "CopperOreNode",
                new Vector3(5f, 2.5f, 0f), CopperOreTint);
            EnsureNodeMarker(scene, registry, "ZincOreNodeMarker", "ZincOreNode",
                new Vector3(-5f, 2.5f, 0f), ZincOreTint);

            // The coal marker exists but still wears the brass sprite it inherited when §1.5
            // repointed the deleted BrassNodeMarker at CoalNode. Retinted here so a coal seam at
            // least does not read as a brass deposit.
            GameObject coal = FindRoot(scene, "CoalNodeMarker");
            if (coal != null)
            {
                coal.GetComponent<SpriteRenderer>().color = CoalTint;
                Note("CoalNodeMarker retinted off the brass sprite");
            }
        }

        private static void EnsureNodeMarker(
            Scene scene, ResourceNodeRegistryHolder registry, string name, string nodeId,
            Vector3 position, Color tint)
        {
            GameObject go = FindRoot(scene, name);
            if (go == null)
            {
                go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            go.transform.position = position;

            SpriteRenderer renderer = Ensure<SpriteRenderer>(go);
            renderer.sprite = LoadSprite("item_scrap.png");
            renderer.color = tint;
            renderer.size = new Vector2(0.5f, 0.5f);

            GroundShadow shadow = Ensure<GroundShadow>(go);
            var shadowSo = new SerializedObject(shadow);
            shadowSo.FindProperty("shadowSprite").objectReferenceValue = LoadSprite("ground_shadow.png");
            shadowSo.ApplyModifiedPropertiesWithoutUndo();

            ResourceNodeMarker marker = Ensure<ResourceNodeMarker>(go);
            var so = new SerializedObject(marker);
            so.FindProperty("nodeRegistryHolder").objectReferenceValue = registry;
            so.FindProperty("nodeId").stringValue = nodeId;
            so.ApplyModifiedPropertiesWithoutUndo();

            Note(name + " at " + position + " -> " + nodeId);
        }

        // The scene's ManagerHolders is a prefab instance, so the Steam/ClockTower/NodeExtractor
        // children arrive with it -- but SandboxBootstrap's own [SerializeField]s still have to
        // be pointed at them, and the two HUD views need their holders handed over at runtime.
        private static void WireBootstrapHolders(Scene scene)
        {
            SandboxBootstrap bootstrap = FindInScene<SandboxBootstrap>(scene);
            var so = new SerializedObject(bootstrap);

            AssignIfPresent(so, "steamNetworkHolder", FindInScene<SteamNetworkHolder>(scene));
            AssignIfPresent(so, "clockTowerSiteHolder", FindInScene<ClockTowerSiteHolder>(scene));
            AssignIfPresent(so, "nodeExtractorHolder", FindInScene<NodeExtractorRegistryHolder>(scene));
            AssignIfPresent(so, "steamFuelGaugeView", FindInScene<SteamFuelGaugeView>(scene));
            AssignIfPresent(so, "clockTowerPanelView", FindInScene<ClockTowerPanelView>(scene));

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("SandboxBootstrap holders wired");
        }

        /// <summary>
        /// Adds the two new readouts to the SCENE's copy of <c>hideWhileOpen</c>.
        ///
        /// <para>
        /// Doing it on the prefab is not enough and this is the trap worth remembering:
        /// Sandbox.unity carries a prefab OVERRIDE pinning <c>hideWhileOpen.Array.size</c> to 1
        /// (its own BuildMenuPanel, which is a scene object and therefore could never have been
        /// authored on the prefab). An override replaces the prefab value wholesale, so the
        /// gauge and the tower panel would have drawn straight over the full-screen Workbench
        /// in the one scene anybody actually plays -- exactly the HUD-overlap class of bug the
        /// UGUI conversion was done to kill.
        /// </para>
        /// </summary>
        private static void RegisterSceneHudChrome(Scene scene)
        {
            WorkbenchController controller = FindInScene<WorkbenchController>(scene);
            SteamFuelGaugeView gauge = FindInScene<SteamFuelGaugeView>(scene);
            ClockTowerPanelView panel = FindInScene<ClockTowerPanelView>(scene);
            if (controller == null || gauge == null || panel == null)
            {
                Note("WARNING: could not find the controller or both readouts in the scene");
                return;
            }

            var so = new SerializedObject(controller);
            SerializedProperty list = so.FindProperty("hideWhileOpen");
            var kept = new List<UnityEngine.Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object existing = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (existing != null &&
                    existing != gauge.gameObject &&
                    existing != panel.gameObject)
                {
                    kept.Add(existing);
                }
            }

            kept.Add(gauge.gameObject);
            kept.Add(panel.gameObject);

            list.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("scene hideWhileOpen now covers " + kept.Count + " HUD objects");
        }

        private static void AssignIfPresent(SerializedObject so, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Note("WARNING: no serialized field '" + propertyName + "' on " + so.targetObject.GetType().Name);
                return;
            }

            if (value == null)
            {
                Note("WARNING: nothing in the scene to assign to '" + propertyName + "'");
                return;
            }

            property.objectReferenceValue = value;
        }

        // ===================================================================================
        // Helpers
        // ===================================================================================

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

        private static TextMeshProUGUI EnsureLabel(
            Transform parent, string name, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject go = EnsureUiChild(parent, name);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 4f);
            rect.offsetMax = new Vector2(-8f, -4f);

            TextMeshProUGUI text = Ensure<TextMeshProUGUI>(go);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = HudTextColor;
            text.raycastTarget = false;
            return text;
        }

        private static void StretchRow(RectTransform rect, float bottom, float top)
        {
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.offsetMin = new Vector2(8f, 0f);
            rect.offsetMax = new Vector2(-8f, 0f);
        }

        private static Sprite LoadSprite(string fileName)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + fileName);
            if (sprite == null)
            {
                throw new InvalidOperationException("Sprite not found: " + ArtRoot + fileName);
            }

            return sprite;
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

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
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
