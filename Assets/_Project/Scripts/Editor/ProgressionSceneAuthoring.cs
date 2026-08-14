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
        private const string AppendageRoot = "Assets/_Project/ScriptableObjects/Appendages/";
        private const string ChassisRoot = "Assets/_Project/ScriptableObjects/Chassis/";
        private const string LogicCoreRoot = "Assets/_Project/ScriptableObjects/LogicCores/";

        private const string BoilerPrefabPath = PrefabRoot + "BoilerPrefab.prefab";
        private const string SteamPipePrefabPath = PrefabRoot + "SteamPipePrefab.prefab";
        private const string ClockTowerPrefabPath = PrefabRoot + "ClockTowerPrefab.prefab";
        private const string ManagerHoldersPath = PrefabRoot + "ManagerHolders.prefab";
        private const string HandCrankBenchPrefabPath = PrefabRoot + "HandCrankBenchPrefab.prefab";
        private const string RecipeRoot = "Assets/_Project/ScriptableObjects/Recipes/";
        private const string WorkbenchCanvasPath = PrefabRoot + "WorkbenchCanvas.prefab";

        // Placeholder tints for the three BUILDINGS. Tinting works here because nothing owns a
        // PlaceableBuilding's SpriteRenderer.color at runtime.
        //
        // IT DOES NOT WORK FOR NODE MARKERS, and that is worth writing down because the first
        // pass tried it and shipped three identical icons: ResourceNodeMarker.RefreshVisualState
        // OWNS SpriteRenderer.color -- it is the depletion readout -- and since §5.1 made every
        // node infinite it pins every marker to ResourceNodeVisualState.FullTint (white) on the
        // first frame. An authored tint is correct on disk, correct in the Editor, and gone the
        // moment you press Play. Node identity therefore has to be SPRITE, which is why
        // item_coal / item_copper_ore / item_zinc_ore now exist in the art generator.
        // Boiler is WHITE for the same reason the Clock Tower is: it has its own sprite now
        // (steam_boiler.png). SteamPipe is the last building BuildPlaceable still puts on the
        // shared box, so this is the one tint here left doing real work -- but it is not the
        // last building_block user in the project: DepotPrefab and GolemConstructionStationPrefab
        // wear it too. Those two come through ApplyCost, which only restores a cost and never
        // touches the SpriteRenderer, so giving them real art means routing them through
        // BuildPlaceable (or setting the sprite where they are actually built), not editing here.
        private static readonly Color BoilerTint = Color.white;
        private static readonly Color SteamPipeTint = new Color(0.60f, 0.67f, 0.72f, 1f);
        // WHITE, and that is the point: the Clock Tower is the first building off the shared
        // building_block box and onto its own sprite (clock_tower.png). A tint exists to tell
        // identical boxes apart; once a building has real art the tint is the thing that would
        // wreck it, so retiring the colour is part of retiring the box.
        private static readonly Color ClockTowerTint = Color.white;
        private static readonly Color HandCrankBenchTint = Color.white;

        // Near-opaque, not the 0.78 the first pass used. These strips sit over the dark wall at
        // the top of the frame, and a translucent near-black plate under dim idle-grey text was
        // legible in the Inspector and invisible in the game view.
        private static readonly Color HudPlateColor = new Color(0.09f, 0.08f, 0.07f, 0.94f);
        private static readonly Color HudTextColor = new Color(0.85f, 0.66f, 0.32f, 1f);

        // The canvas is authored at 1280x960 and the game view is smaller, so authored points
        // shrink on screen; 14 measured at roughly 11 real pixels. These are the sizes that
        // survive that.
        private const float GaugeFontSize = 18f;
        private const float TowerHeadlineFontSize = 17f;
        private const float TowerBodyFontSize = 14f;

        private static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Golem Factory/Author Progression Scene Wiring")]
        public static void AuthorAll()
        {
            Log.Clear();
            try
            {
                ImportItemIcons();
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
        // 0. Import settings for the icons Tools/Art/generate_placeholder_art.py just wrote.
        // ===================================================================================

        /// <summary>
        /// Gives the three new node icons the same import settings the existing item icons have.
        /// A freshly written PNG imports at 100 pixels-per-unit with bilinear filtering and
        /// normal compression, which in a 32px pixel-art sprite is the difference between a coal
        /// lump and a brown smudge at the wrong size. Matched to <c>item_scrap.png</c>: PPU 64,
        /// point filter, uncompressed, alpha-is-transparency.
        /// </summary>
        private static void ImportItemIcons()
        {
            Log.AppendLine("[item icons]");
            foreach (string file in new[] { "item_coal.png", "item_copper_ore.png", "item_zinc_ore.png" })
            {
                string path = ArtRoot + file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Note("WARNING: no importer for " + path + " (did the art script run?)");
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 64f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                Note(file + " imported at PPU 64, point filter");
            }
        }

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
                },
                "steam_boiler.png");

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
                go =>
                {
                    PlaceableClockTower tower = Ensure<PlaceableClockTower>(go);
                    // §7's stages live on the TOWER, not on the always-present site holder --
                    // see the long note on PlaceableClockTower.stages. Wired here so that
                    // placing the tower is what starts the megaproject.
                    WriteStageList(new SerializedObject(tower), "stages");
                },
                "clock_tower.png");

            // §11 item 7's "placed building", and §9 Phase 1's "the player begins alone in a cold
            // workshop with a Hand-Crank Bench" -- so it is both placeable and present at spawn.
            //
            // FREE TO PLACE. §11 prices it at nothing and §9 hands the player one, so any cost
            // here would be a tuning number no reviewer has seen. It is also the one building
            // that must never be unaffordable: §10 leans on it as the total-blackout backstop,
            // and a bench the player cannot rebuild is a backstop with a hole in it.
            BuildPlaceable(
                HandCrankBenchPrefabPath, "HandCrankBenchPrefab", HandCrankBenchTint,
                Array.Empty<RecipeIngredient>(),
                go =>
                {
                    HandCrankBench bench = Ensure<HandCrankBench>(go);
                    // Every authored recipe is offered as a CANDIDATE; HandCrankRules filters to
                    // the five a person can actually turn. Handing it the whole list means a
                    // future recipe becomes crankable (or not) by its own shape rather than by
                    // somebody remembering to update a second list here.
                    WriteRecipeCandidates(new SerializedObject(bench));
                },
                "hand_crank_bench.png");

            RestoreOrphanedCosts();
        }

        /// <summary>All 19 authored recipes in R1..R19 order; the bench filters them itself.</summary>
        private static void WriteRecipeCandidates(SerializedObject so)
        {
            var recipes = new List<(int number, RecipeDefinition recipe)>();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:RecipeDefinition", new[] { RecipeRoot.TrimEnd('/') }))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (recipe != null)
                {
                    recipes.Add((RecipeNumber(recipe.name), recipe));
                }
            }

            recipes.Sort((a, b) => a.number.CompareTo(b.number));

            SerializedProperty list = so.FindProperty("candidateRecipes");
            list.arraySize = recipes.Count;
            for (int i = 0; i < recipes.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = recipes[i].recipe;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("bench offered " + recipes.Count + " candidate recipes (rules filter to 1-input)");
        }

        /// <summary>
        /// Re-expresses the three pre-existing placeables' costs as item bundles.
        ///
        /// <para>
        /// §1.5 replaced <c>PlaceableBuilding</c>'s <c>scrapCost</c>/<c>brassCost</c> int pair
        /// with a <c>RecipeIngredient</c> bundle (§11 item 8) and never migrated the prefabs
        /// authored against the old pair, so DepotPrefab, GolemConstructionStationPrefab and
        /// BeltPrefab have all been FREE TO PLACE ever since -- their old values are still in
        /// the YAML as orphaned keys no field reads. It went unnoticed because everything was
        /// free, so nothing ever refused; it became obvious the moment a priced Boiler appeared
        /// in the same menu next to "Depot (Free)".
        /// </para>
        ///
        /// <para>
        /// THESE ARE THE ORIGINAL NUMBERS, recovered from the orphaned keys, not new tuning:
        /// Depot 15 Scrap, Station 25 Scrap + 5 Brass, Belt 1 Scrap. §5.1 independently
        /// corroborates the belt ("Scrap ... belts (1 ea.)").
        /// </para>
        /// </summary>
        private static void RestoreOrphanedCosts()
        {
            ApplyCost("DepotPrefab", new[] { new RecipeIngredient(ItemType.Scrap, 15) });
            ApplyCost("GolemConstructionStationPrefab", new[]
            {
                new RecipeIngredient(ItemType.Scrap, 25),
                new RecipeIngredient(ItemType.Brass, 5),
            });
            ApplyCost("BeltPrefab", new[] { new RecipeIngredient(ItemType.Scrap, 1) });
        }

        private static void ApplyCost(string prefabName, RecipeIngredient[] cost)
        {
            string path = PrefabRoot + prefabName + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                WriteCost(root.GetComponent<PlaceableBuilding>(), cost);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Note(prefabName + " cost restored -> " + DescribeCost(cost));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Every placeable in this project is the same shape (DepotPrefab is the reference):
        // SpriteRenderer + the sealed PlaceableBuilding + YSortSpriteRenderer + one sibling
        // component carrying the actual behaviour. Built from that shape rather than by
        // duplicating DepotPrefab so the cost and the sibling are explicit at the call site.
        private static void BuildPlaceable(
            string path, string name, Color tint, RecipeIngredient[] cost, Action<GameObject> addBehaviour,
            string spriteFile = "building_block.png")
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null
                ? PrefabUtility.LoadPrefabContents(path)
                : new GameObject(name);

            try
            {
                root.name = name;

                SpriteRenderer renderer = Ensure<SpriteRenderer>(root);
                renderer.sprite = LoadSprite(spriteFile);
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
                // DELIBERATELY EMPTY. Stages wired here start stage 1 in Awake, which made a
                // fresh Sandbox open with "Stage 1 Foundation - 0%" and a starvation alert for a
                // tower that does not exist. The list lives on ClockTowerPrefab now and arrives
                // when the tower is placed; until then the site is dormant, which is true.
                var siteSo = new SerializedObject(site);
                siteSo.FindProperty("stages").arraySize = 0;
                siteSo.ApplyModifiedPropertiesWithoutUndo();
                Note("ClockTowerSiteHolder.stages cleared (stages now ride the tower prefab)");

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
        private static void WriteStageList(SerializedObject so, string propertyName)
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

            SerializedProperty list = so.FindProperty(propertyName);
            list.arraySize = stages.Count;
            for (int i = 0; i < stages.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("stages wired onto " + so.targetObject.GetType().Name + ": " + stages.Count);
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
                RemoveMisparentedHud(root);
                GameObject gauge = BuildSteamGauge(root);
                GameObject tower = BuildClockTowerPanel(root);
                GameObject crank = BuildHandCrankPanel(root);
                RegisterHudChrome(root, gauge, tower, crank);

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

                // Captions are authored FROM THE INDEX, not left as whatever the row happens to
                // hold. AppendageSlot5 is a clone of slot 4 and arrived carrying its caption, so
                // the screen showed "STEP 5" twice -- two sockets claiming to be the same step,
                // in the one UI whose entire job is showing the player the order of their
                // program. Deriving every caption here means a future seventh socket cannot
                // reintroduce it.
                Transform caption = row.Find("Caption");
                if (caption != null)
                {
                    var label = caption.GetComponent<TextMeshProUGUI>();
                    if (label != null)
                    {
                        label.text = i == 0 ? "TRIGGER" : "STEP " + i;
                    }
                }
            }

            Note("SlotStack respaced for 7 rows, captions renumbered TRIGGER/STEP 1-6");
        }

        /// <summary>
        /// Deletes HUD panels left directly under the prefab ROOT by the earlier name-based
        /// canvas lookup (see <see cref="FindCanvas"/>). Without this the corrected build makes a
        /// second, properly parented copy and the broken one stays behind forever -- invisible,
        /// so nothing would ever draw attention to it, but still carrying a
        /// SteamFuelGaugeView/ClockTowerPanelView that FindAnyObjectByType could return instead
        /// of the real one.
        /// </summary>
        private static void RemoveMisparentedHud(GameObject root)
        {
            foreach (string name in new[] { "SteamGauge", "ClockTowerPanel" })
            {
                Transform stray = root.transform.Find(name);
                if (stray != null)
                {
                    UnityEngine.Object.DestroyImmediate(stray.gameObject);
                    Note("removed misparented " + name + " from the prefab root");
                }
            }
        }

        // §8's fuel gauge: "240 Coke - 24/min - 10:00 left". Top-left, opposite the alerts
        // strip's top-centre, because it is a standing readout rather than an interruption.
        private static GameObject BuildSteamGauge(GameObject root)
        {
            GameObject panel = EnsureUiChild(FindCanvas(root), "SteamGauge");
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -10f);
            rect.sizeDelta = new Vector2(320f, 34f);

            Image plate = Ensure<Image>(panel);
            plate.color = HudPlateColor;
            plate.raycastTarget = false;

            TextMeshProUGUI text = EnsureLabel(
                panel.transform, "GaugeText", GaugeFontSize, TextAlignmentOptions.Left);

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
            GameObject panel = EnsureUiChild(FindCanvas(root), "ClockTowerPanel");
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-12f, -10f);
            rect.sizeDelta = new Vector2(320f, 148f);

            Image plate = Ensure<Image>(panel);
            plate.color = HudPlateColor;
            plate.raycastTarget = false;

            TextMeshProUGUI headline = EnsureLabel(
                panel.transform, "Headline", TowerHeadlineFontSize, TextAlignmentOptions.TopLeft);
            StretchRow((RectTransform)headline.transform, 0.80f, 0.98f);

            TextMeshProUGUI table = EnsureLabel(
                panel.transform, "DemandTable", TowerBodyFontSize, TextAlignmentOptions.TopLeft);
            StretchRow((RectTransform)table.transform, 0.26f, 0.78f);

            TextMeshProUGUI alert = EnsureLabel(
                panel.transform, "Alert", TowerBodyFontSize, TextAlignmentOptions.TopLeft);
            StretchRow((RectTransform)alert.transform, 0.02f, 0.24f);

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

        /// <summary>
        /// The bench readout, bottom-centre and just above the simulation control bar.
        ///
        /// <para>
        /// Placed there because it is the only free edge -- top-left is the fuel gauge, top-right
        /// the tower, top-centre the alerts strip, bottom-left the build menu -- and because it
        /// reads as "what you are doing right now" rather than a standing gauge. It is the only
        /// HUD element that hides itself when irrelevant: the view switches its root off unless
        /// the player is actually standing at a bench.
        /// </para>
        /// </summary>
        private static GameObject BuildHandCrankPanel(GameObject root)
        {
            GameObject panel = EnsureUiChild(FindCanvas(root), "HandCrankPanel");
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 74f);
            rect.sizeDelta = new Vector2(430f, 96f);

            // THE BODY IS A CHILD, AND THAT IS LOAD-BEARING. This panel hides itself when the
            // player walks away from a bench, and a disabled GameObject does not run Update -- so
            // a view that switched off its OWN object could never switch back on. Found by
            // running it: the readout vanished on the first frame and stayed gone. The outer
            // object holds the view and stays active (only hideWhileOpen ever toggles it); the
            // body carries the plate and the text. Same arrangement BuildMenuPanel already uses.
            // An earlier build put the plate and the label directly on the panel; moving them
            // under Body orphaned both, leaving a second empty CrankText that rendered nothing
            // and a stale Image behind the real one. EnsureUiChild is idempotent per name PER
            // PARENT, so re-parenting always needs the old location swept explicitly.
            Transform strayLabel = panel.transform.Find("CrankText");
            if (strayLabel != null)
            {
                UnityEngine.Object.DestroyImmediate(strayLabel.gameObject);
                Note("removed stray CrankText left on the panel root");
            }

            Image strayPlate = panel.GetComponent<Image>();
            if (strayPlate != null)
            {
                UnityEngine.Object.DestroyImmediate(strayPlate);
                Note("removed stray plate Image left on the panel root");
            }

            GameObject body = EnsureUiChild(panel.transform, "Body");
            var bodyRect = (RectTransform)body.transform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;

            Image plate = Ensure<Image>(body);
            plate.color = HudPlateColor;
            plate.raycastTarget = false;

            TextMeshProUGUI text = EnsureLabel(
                body.transform, "CrankText", 15f, TextAlignmentOptions.TopLeft);

            HandCrankPanelView view = Ensure<HandCrankPanelView>(panel);
            var so = new SerializedObject(view);
            // playerInteractor and stockpileHolder are scene objects, so they cannot be authored
            // onto a prefab -- SandboxBootstrap hands them over at runtime.
            so.FindProperty("panelRoot").objectReferenceValue = body;
            so.FindProperty("readoutText").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();

            Note("HandCrankPanel built (holder wired at runtime by SandboxBootstrap)");
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
            ResizeBuildMenu(scene);
            AddNodeMarkers(scene);
            PlaceStartingBench(scene);
            WireBootstrapHolders(scene);
            RegisterSceneHudChrome(scene);
            PopulateWorkbenchRoster(scene);
            PopulateSaveCatalog(scene);

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

            foreach (string path in new[]
                     {
                         BoilerPrefabPath, SteamPipePrefabPath, ClockTowerPrefabPath,
                         HandCrankBenchPrefabPath,
                     })
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

        /// <summary>
        /// Grows the build menu's panel to fit however many placeables it now offers.
        ///
        /// <para>
        /// The panel was authored at 280x170 for three entries and the three new placeables took
        /// it to six, so Steam Pipe and Clock Tower fell off the bottom edge -- placeable in
        /// principle and unreachable in fact, which would have made the whole Editor pass look
        /// like it worked while two thirds of the new content stayed unbuildable.
        /// </para>
        ///
        /// <para>
        /// Derived from the row count rather than typed in, so adding a seventh placeable later
        /// cannot silently re-break it. The chrome allowance matches the RectTransform offsets
        /// the panel already uses: a 26px title plus the 48px the RowContainer is inset by.
        /// </para>
        /// </summary>
        private static void ResizeBuildMenu(Scene scene)
        {
            BuildMenuPanel menu = FindInScene<BuildMenuPanel>(scene);
            BuildModeController build = FindInScene<BuildModeController>(scene);
            if (menu == null || build == null)
            {
                Note("WARNING: no build menu to resize");
                return;
            }

            var panel = menu.transform.Find("Panel") as RectTransform;
            if (panel == null)
            {
                Note("WARNING: build menu has no Panel child");
                return;
            }

            const float rowHeight = 30f;   // BuildMenuPanel.RowHeight
            const float chromeHeight = 56f; // title plate + container inset + a little slack
            int rows = build.AvailablePrefabs != null ? build.AvailablePrefabs.Count : 0;
            float wanted = chromeHeight + rows * rowHeight;

            panel.sizeDelta = new Vector2(panel.sizeDelta.x, wanted);
            Note("build menu panel resized to fit " + rows + " rows -> height " + wanted);
        }

        // §5.1's copper and zinc veins had nowhere to stand: SandboxBootstrap registers the
        // nodes so a golem can reach them BY ID, but nothing in the world let the player walk up
        // to one -- and a spatially placed golem finds a node by what is on the tile, so an
        // unmarked node is unreachable to the machine model too.
        private static void AddNodeMarkers(Scene scene)
        {
            ResourceNodeRegistryHolder registry = FindInScene<ResourceNodeRegistryHolder>(scene);

            EnsureNodeMarker(scene, registry, "CopperOreNodeMarker", "CopperOreNode",
                StartingLayout["CopperOreNodeMarker"], "item_copper_ore.png");
            EnsureNodeMarker(scene, registry, "ZincOreNodeMarker", "ZincOreNode",
                StartingLayout["ZincOreNodeMarker"], "item_zinc_ore.png");

            // The three older markers pre-date this script and it only ever repointed their
            // sprites, so their positions were whatever the scene happened to hold -- which is
            // how three of the five ended up on half-cells. The table owns all five now.
            PlaceOnCell(scene, "ScrapNodeMarker", StartingLayout["ScrapNodeMarker"]);
            PlaceOnCell(scene, "CoalNodeMarker", StartingLayout["CoalNodeMarker"]);
            PlaceOnCell(scene, "AetherNodeMarker", StartingLayout["AetherNodeMarker"]);

            // The coal marker still wears the brass INGOT it inherited when §1.5 repointed the
            // deleted BrassNodeMarker at CoalNode, which made a coal seam and an aether node
            // read as the same object. Given a marker's colour is not ours to set (see the tint
            // note above), the sprite is the whole fix.
            GameObject coal = FindRoot(scene, "CoalNodeMarker");
            if (coal != null)
            {
                SpriteRenderer renderer = coal.GetComponent<SpriteRenderer>();
                renderer.sprite = LoadSprite("item_coal.png");
                // Reset to white explicitly: the first pass left a coal-black tint here, and
                // although RefreshVisualState overwrites it every frame in play mode, leaving it
                // makes the Editor and the game disagree about what the object looks like.
                renderer.color = Color.white;
                Note("CoalNodeMarker repointed to item_coal");
            }

            // Same bug, pre-existing and found by the same read-back: AetherNodeMarker wears the
            // BRASS INGOT under a teal tint, while item_aether.png -- a tall pointed shard drawn
            // for exactly this -- sits unused in the art folder. The tint is overwritten white on
            // the first frame, so in play mode the aether node has always rendered as a second
            // brass ingot. Pointing it at its own sprite fixes it in the only channel that works.
            GameObject aether = FindRoot(scene, "AetherNodeMarker");
            if (aether != null)
            {
                SpriteRenderer renderer = aether.GetComponent<SpriteRenderer>();
                renderer.sprite = LoadSprite("item_aether.png");
                renderer.color = Color.white;
                Note("AetherNodeMarker repointed to item_aether (was tinted brass)");
            }
        }

        /// <summary>
        /// Stands one Hand-Crank Bench in the world at spawn, per §9 Phase 1: "The player begins
        /// alone in a cold workshop with a Hand-Crank Bench and a Boiler holding 240 Coke."
        ///
        /// <para>
        /// ONLY THE BENCH, NOT THE BOILER. The bench is a soft-lock fix -- without it the Presser
        /// is unbuildable and the game stops at the first golem -- so placing it is repairing
        /// something broken. The 240-Coke starting boiler is a different thing: it is the opening
        /// §2 still lists as an open decision ("the opening changes substantially"), it only
        /// matters once <c>requireSteamPower</c> is on, and handing the player 240 free Coke is a
        /// tuning choice with no reviewer behind it. Left for a human.
        /// </para>
        ///
        /// <para>
        /// Position comes from <see cref="StartingLayout"/>, in cells, along with the rest of
        /// the opening -- see the long note there for why a world-space literal could not
        /// survive the projection change.
        /// </para>
        /// </summary>
        private static void PlaceStartingBench(Scene scene)
        {
            GameObject bench = FindRoot(scene, "StarterHandCrankBench");
            if (bench == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandCrankBenchPrefabPath);
                bench = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                bench.name = "StarterHandCrankBench";
            }

            bench.transform.position = CellToWorld(scene, StartingLayout["StarterHandCrankBench"]);
            Note("StarterHandCrankBench on cell " + StartingLayout["StarterHandCrankBench"]
                 + " -> " + bench.transform.position);

            // The station has always been a plain scene object this script never touched, so it
            // kept its isometric literal too. Moved here because it is part of the same opening
            // and belongs in the same table.
            PlaceOnCell(scene, "StarterConstructionStation", StartingLayout["StarterConstructionStation"]);
        }

        // WHERE THE OPENING STANDS, IN CELLS -- the only representation that survives a
        // projection change.
        //
        // These seven were authored as literal world-space Vector3s under the isometric camera:
        // (2.5, -1.25), (0, 2.5), (-5, 2.5) and so on. That transform was
        // world = ((cx - cy) * 0.5, (cx + cy) * 0.25), and inverting it turns every one of those
        // literals back into an exact integer cell -- which is the evidence that they were
        // designed as cells and flattened on the way to disk, not chosen as world coordinates.
        //
        // Top-down reads the same literals as world = (cx, cy), so all seven landed on HALF
        // cells (RoundToInt then decides which tile a golem thinks they are on) and the layout
        // collapsed from a spread across a 25x25 floor into roughly a 10x4 band. The cells below
        // are the recovered originals, unchanged: this restores the authored layout in the new
        // projection rather than proposing a new one.
        //
        // THE LAYOUT BELOW IS "QUADRANTS", CHOSEN BY THE PROJECT OWNER from the three proposals
        // written up in docs/open-items.md. The recovered isometric spread was not kept: designed
        // for a diamond, it put everything north and east of the player and left the west half of
        // a square room empty.
        //
        // One node per corner, Aether alone due north, and the reasoning is that each resource
        // gets a PLACE. A factory is remembered as a map, and a map needs somewhere to be. The
        // diagonal runs are also long enough (14 cells corner to corner) that belts and steam
        // pipes are a real investment rather than a formality -- §3.2's 2-extractor cap and §1.4's
        // 8-golem boiler radius both start to bite on distances like these, which they do not at 5.
        //
        // The cost, recorded so a playtest knows what to look at: every node is 7 cells Chebyshev
        // (14 Manhattan) from spawn against 5 before, which is the longest early-game walking of
        // the three proposals and lands on §9's 12-15 minute manual era (§12 already flags that as
        // +/-25%). Nodes sit at +/-7 rather than +/-9 because the generated props hug the outermost
        // ring at +/-12; measured against the real shell, the nearest prop to a corner node is 5
        // cells away and nothing collides.
        //
        // The bench is 3 north of spawn and the station 3 east and 1 south, both inside the clear
        // core, because Phase 1 is spent standing at the bench and it should not be a walk.
        //
        // Cells, never world literals: these were world-space Vector3s under the isometric camera
        // -- (2.5, -1.25), (0, 2.5) -- and top-down reads those as (cx, cy), so all seven landed
        // on HALF cells with RoundToInt deciding which tile a golem thought they were on. Inverting
        // the old transform, world = ((cx - cy) * 0.5, (cx + cy) * 0.25), turned every one of them
        // back into an exact integer cell, which is how we know they had been authored as cells
        // and flattened on the way to disk. Changing this table is how you move the opening.
        private static readonly Dictionary<string, Vector2Int> StartingLayout =
            new Dictionary<string, Vector2Int>
            {
                { "ScrapNodeMarker", new Vector2Int(-7, -7) },
                { "CoalNodeMarker", new Vector2Int(7, -7) },
                { "AetherNodeMarker", new Vector2Int(0, 9) },
                { "CopperOreNodeMarker", new Vector2Int(-7, 7) },
                { "ZincOreNodeMarker", new Vector2Int(7, 7) },
                { "StarterHandCrankBench", new Vector2Int(0, 3) },
                { "StarterConstructionStation", new Vector2Int(3, -1) },
            };

        // Reads the cell size off the scene's own Grid rather than assuming one, so this stays
        // right if the Grid is ever retuned -- the same reason FacingVisuals derives its angle
        // instead of hardcoding it. Falls back to a square unit cell if the scene has no Grid,
        // which would mean a much louder failure elsewhere anyway.
        private static Vector3 CellToWorld(Scene scene, Vector2Int cell)
        {
            Grid grid = FindInScene<Grid>(scene);
            Vector2 cellSize = grid != null ? (Vector2)grid.cellSize : Vector2.one;
            return new GridCoordinateConverter(cellSize).CellToWorldCenter(cell);
        }

        // For objects this script does not otherwise author: it only moves them onto their cell.
        private static void PlaceOnCell(Scene scene, string rootName, Vector2Int cell)
        {
            GameObject go = FindRoot(scene, rootName);
            if (go == null)
            {
                Debug.LogWarning("ProgressionSceneAuthoring: no '" + rootName + "' in the scene to place.");
                return;
            }

            go.transform.position = CellToWorld(scene, cell);
            Note(rootName + " placed on cell " + cell + " -> " + go.transform.position);
        }

        private static void EnsureNodeMarker(
            Scene scene, ResourceNodeRegistryHolder registry, string name, string nodeId,
            Vector2Int cell, string spriteFile)
        {
            GameObject go = FindRoot(scene, name);
            if (go == null)
            {
                go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            Vector3 position = CellToWorld(scene, cell);
            go.transform.position = position;

            SpriteRenderer renderer = Ensure<SpriteRenderer>(go);
            renderer.sprite = LoadSprite(spriteFile);
            // White, and left white: ResourceNodeMarker owns this channel at runtime.
            renderer.color = Color.white;
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

            Note(name + " on cell " + cell + " (" + position + ") -> " + nodeId
                 + " (" + spriteFile + ")");
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
            AssignIfPresent(so, "handCrankPanelView", FindInScene<HandCrankPanelView>(scene));

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
            HandCrankPanelView crank = FindInScene<HandCrankPanelView>(scene);
            if (controller == null || gauge == null || panel == null || crank == null)
            {
                Note("WARNING: could not find the controller or all three readouts in the scene");
                return;
            }

            var readouts = new List<GameObject>
            {
                gauge.gameObject, panel.gameObject, crank.gameObject,
            };

            var so = new SerializedObject(controller);
            SerializedProperty list = so.FindProperty("hideWhileOpen");
            var kept = new List<UnityEngine.Object>();
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object existing = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (existing != null && !readouts.Contains(existing as GameObject))
                {
                    kept.Add(existing);
                }
            }

            kept.AddRange(readouts);

            list.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("scene hideWhileOpen now covers " + kept.Count + " HUD objects");
        }

        /// <summary>
        /// Puts every authored punch card in the Workbench's vault.
        ///
        /// <para>
        /// The roster was still M8's hand-wired four (Extract / Haul / LoadIntoBuffer / Refine)
        /// while §1.5 had authored nineteen <c>Assemble</c> cards and the <c>Push</c> card, none
        /// of which anything referenced. The consequence was not cosmetic: Iron Plate's only two
        /// sources are R2 and R4, both <c>Assemble</c>, so **there was no route to Iron Plate in
        /// the game at all** -- and therefore no Boiler, no Steam Pipe, and nothing for the whole
        /// steam system to be spent on.
        /// </para>
        ///
        /// <para>
        /// UNGATED, DELIBERATELY AND TEMPORARILY. §3's standing deferral is "the Assembly Line
        /// still does not gate the Workbench roster -- every card is available from the start",
        /// and this keeps that shape rather than inventing a gating rule the progression design
        /// has not been implemented against yet. Confirmed with the project owner as the
        /// unblock-now choice; when Assembly Line gating lands it replaces this method.
        /// </para>
        ///
        /// <para>
        /// ORDERED, because the vault is a scrolling list a player reads top to bottom: the four
        /// movement verbs first, then the recipes in authored R1..R19 order, which is also
        /// roughly tier order. Sorted by the recipe's own number rather than by asset name, so
        /// R2 does not sort between R19 and R3.
        /// </para>
        ///
        /// <para>
        /// <c>RefineIronPlate</c> is deliberately LEFT OUT. It is the superseded id-routed
        /// <c>Refine</c> keyed to <c>ScrapBuffer</c>/<c>IronPlateBuffer</c>, and neither buffer
        /// exists in Sandbox -- every <c>PlaceableDepot</c> is hardcoded to
        /// <c>FactoryStockpile</c> and nothing in game can change it -- so the card is a
        /// guaranteed stall wearing the name of the thing the player is trying to make. The
        /// asset stays on disk; Main.unity's demo golems reference it directly.
        /// </para>
        /// </summary>
        private static void PopulateWorkbenchRoster(Scene scene)
        {
            WorkbenchController controller = FindInScene<WorkbenchController>(scene);
            if (controller == null)
            {
                Note("WARNING: no WorkbenchController in the scene");
                return;
            }

            var verbs = new List<AppendageActionDefinition>();
            foreach (string name in new[] { "ExtractScrap", "HaulScrap", "PushOutput", "LoadIntoScrapBuffer" })
            {
                AppendageActionDefinition card = LoadAppendage(name);
                if (card != null)
                {
                    verbs.Add(card);
                }
            }

            // Every Assemble card, ordered by its recipe's R-number.
            var assembles = new List<(int number, AppendageActionDefinition card)>();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:AppendageActionDefinition", new[] { AppendageRoot.TrimEnd('/') }))
            {
                var card = AssetDatabase.LoadAssetAtPath<AppendageActionDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (card == null || card.actionType != AppendageActionType.Assemble || card.recipe == null)
                {
                    continue;
                }

                assembles.Add((RecipeNumber(card.recipe.name), card));
            }

            assembles.Sort((a, b) => a.number.CompareTo(b.number));

            var roster = new List<AppendageActionDefinition>(verbs);
            foreach (var entry in assembles)
            {
                roster.Add(entry.card);
            }

            var so = new SerializedObject(controller);
            SerializedProperty list = so.FindProperty("availableAppendages");
            list.arraySize = roster.Count;
            for (int i = 0; i < roster.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = roster[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Note("Workbench roster = " + roster.Count + " cards (" + verbs.Count + " verbs + " +
                 assembles.Count + " recipes), Refine omitted");
        }

        /// <summary>
        /// Fills <c>SaveLoadPanel</c>'s three rosters, which are how a load turns the names in a
        /// save file back into assets (<c>DefinitionCatalog</c>). A card the catalog cannot
        /// resolve is dropped from the restored program, silently.
        ///
        /// <para>
        /// EVERY AUTHORED ASSET, NOT THE WORKBENCH'S ROSTER, and the difference is the whole
        /// point. The Workbench's list is *what the player may choose*; this one is *what a save
        /// file might name*, which is strictly larger -- it includes cards deliberately withheld
        /// from the player (RefineIronPlate) and anything an older save was written against.
        /// Populating it from the Workbench's list would look tidier and would quietly delete a
        /// program step the day the two diverge.
        /// </para>
        ///
        /// <para>
        /// They HAD diverged. The panel was still carrying M8's four-card tutorial deck
        /// (ExtractScrap / HaulScrap / LoadIntoScrapBuffer / RefineIronPlate) while the Workbench
        /// had been re-authored to 23 -- the same stale deck that had already made Iron Plate
        /// unreachable in play. So every golem programmed with any of the nineteen Assemble cards
        /// came back from a load with an empty program and no error: a save round-trip that
        /// silently wiped the player's factory logic.
        /// </para>
        /// </summary>
        private static void PopulateSaveCatalog(Scene scene)
        {
            SaveLoadPanel panel = FindInScene<SaveLoadPanel>(scene);
            if (panel == null)
            {
                Note("WARNING: no SaveLoadPanel in the scene");
                return;
            }

            var so = new SerializedObject(panel);
            int chassis = FillRosterWithEveryAsset<ChassisDefinition>(so, "chassisRoster", ChassisRoot);
            int cores = FillRosterWithEveryAsset<LogicCoreDefinition>(so, "logicCoreRoster", LogicCoreRoot);
            int cards = FillRosterWithEveryAsset<AppendageActionDefinition>(so, "appendageRoster", AppendageRoot);
            so.ApplyModifiedPropertiesWithoutUndo();

            Note("Save catalog = " + chassis + " chassis, " + cores + " logic cores, "
                 + cards + " appendage cards");
        }

        // Sorted by path so the serialized order is stable between runs -- FindAssets does not
        // promise one, and an unstable order would rewrite the scene on every authoring pass for
        // no change, which is exactly the churn SandboxFloorGenerator was just cured of.
        private static int FillRosterWithEveryAsset<T>(
            SerializedObject so, string propertyName, string root) where T : UnityEngine.Object
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { root.TrimEnd('/') }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(System.StringComparer.Ordinal);

            SerializedProperty list = so.FindProperty(propertyName);
            list.arraySize = paths.Count;
            for (int i = 0; i < paths.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<T>(paths[i]);
            }

            return paths.Count;
        }

        private static AppendageActionDefinition LoadAppendage(string assetName)
        {
            var card = AssetDatabase.LoadAssetAtPath<AppendageActionDefinition>(
                AppendageRoot + assetName + ".asset");
            if (card == null)
            {
                Note("WARNING: missing appendage asset " + assetName);
            }

            return card;
        }

        /// <summary>"R14_Regulator" -> 14. Unparseable names sort last rather than throwing.</summary>
        private static int RecipeNumber(string recipeAssetName)
        {
            if (string.IsNullOrEmpty(recipeAssetName) || recipeAssetName[0] != 'R')
            {
                return int.MaxValue;
            }

            int end = 1;
            while (end < recipeAssetName.Length && char.IsDigit(recipeAssetName[end]))
            {
                end++;
            }

            return end > 1 && int.TryParse(recipeAssetName.Substring(1, end - 1), out int number)
                ? number
                : int.MaxValue;
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

        /// <summary>
        /// The prefab's actual <see cref="Canvas"/>, found by COMPONENT rather than by name.
        ///
        /// <para>
        /// This is a scar. WorkbenchCanvas.prefab has a plain-Transform root named
        /// <c>WorkbenchCanvas</c> whose child -- also named <c>WorkbenchCanvas</c> -- carries the
        /// Canvas. <see cref="FindDeep"/> tests the node it is given before descending, so
        /// looking the canvas up by name returned the ROOT, and both HUD readouts were parented
        /// outside the canvas. They were present, active, correctly positioned and holding the
        /// right text -- and invisible, because a RectTransform with no Canvas ancestor never
        /// renders. Every property-level check passed while nothing drew on screen.
        /// </para>
        /// </summary>
        private static Transform FindCanvas(GameObject root)
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                throw new InvalidOperationException("No Canvas under " + root.name);
            }

            return canvas.transform;
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
