using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using GolemFactory.World;

namespace GolemFactory.Editor
{
    // Builds the whole workshop shell of the active scene: repaints the floor Tilemap to
    // FloorLayout's current HalfExtent, walls the two back edges, skirts the two open front
    // edges, caps the wall runs with corner posts, and scatters crates/barrels along the walls.
    // Reruns cleanly and deterministically -- it clears every tile and destroys the previously
    // generated parents before rebuilding, and every placement comes from FloorTileVariant /
    // FloorLayout rather than a random scatter, so regenerating never produces scene churn.
    //
    // Everything is built as plain GameObjects rather than prefab instances. Seven distinct
    // piece types (two wall runs, two lamp variants, two skirting runs, posts, two props) would
    // otherwise mean seven prefabs to keep in sync with this one code path, and the pieces are
    // pure decoration with no per-instance authoring -- one generator is the simpler contract.
    public static class SandboxFloorGenerator
    {
        private const string ArtRoot = "Assets/_Project/Art/";
        private const string TileRoot = "Assets/_Project/Tilemaps/";
        private const string LitSpriteMaterialPath = "Assets/Settings/SpriteLit2D.mat";

        private const string WallsParentName = "Walls";
        private const string PropsParentName = "Props";

        // Sprite name -> the Tile asset built from it. Index order must match FloorTileVariant:
        // 0..3 plank variants, 4 brass plate, 5 grate.
        private static readonly string[] FloorSpriteNames =
        {
            "floor_tile", "floor_tile_wood_b", "floor_tile_wood_c", "floor_tile_wood_d",
            "floor_tile_accent", "floor_tile_grate",
        };

        private static readonly string[] FloorTileAssetNames =
        {
            "FloorTile", "FloorTileWoodB", "FloorTileWoodC", "FloorTileWoodD",
            "FloorTileAccent", "FloorTileGrate",
        };

        // Every environment sprite is authored at 32 art pixels per world unit and upscaled x4,
        // so all of them import at PPU 128 -- that is what makes a 128x64 floor tile exactly one
        // 1 x 0.5 cell. They had been importing at PPU 64, i.e. 2 x 1 world units, which is why
        // the painted floor diamonds never lined up with the grid.
        private const float EnvironmentPixelsPerUnit = 128f;

        // Custom pivots, all normalized from the bottom-left. For a wall or skirting segment the
        // pivot is the MIDPOINT OF ITS BASE LINE -- the point that has to land exactly on the
        // FloorLayout.GetEdgeAnchor position for consecutive segments to butt together. These
        // values are printed by Tools/Art/generate_placeholder_art.py (wall_pivot / edge_pivot /
        // post_pivot / prop_pivot) and must be updated together with the art if its native
        // canvas geometry ever changes.
        private static readonly Dictionary<string, Vector2> EnvironmentPivots = new Dictionary<string, Vector2>
        {
            { "wall_segment_ne", new Vector2(0.5f, 1f / 15f) },
            { "wall_segment_ne_lamp", new Vector2(0.5f, 1f / 15f) },
            { "wall_segment_nw", new Vector2(0.5f, 1f / 12f) },
            { "wall_segment_nw_lamp", new Vector2(0.5f, 1f / 12f) },
            { "floor_edge_se", new Vector2(0.5f, 0.8f) },
            { "floor_edge_sw", new Vector2(0.5f, 0.85f) },
            { "wall_corner_post", new Vector2(0.5f, 0.0625f) },
            { "prop_crate", new Vector2(0.5f, 7f / 30f) },
            { "prop_barrel", new Vector2(0.5f, 0.2f) },
            { "ground_shadow", new Vector2(0.5f, 0.5f) },
        };

        // Wall sconces every fourth segment, offset between the two runs so they never bunch up
        // at the shared corner. Sparser spacing left long stretches of wall in near-darkness
        // between pools, which hid the panelling and brickwork entirely.
        private const int LampSpacing = 4;
        private const int NorthWestLampPhase = 2;
        private static readonly Color LampColor = new Color(1f, 0.72f, 0.42f, 1f);
        private const float LampHeight = 1.28f;

        [MenuItem("Tools/Golem Factory/Generate Floor (with Walls)")]
        public static void GenerateFloorWithWalls()
        {
            Generate(spawnWalls: true);
        }

        [MenuItem("Tools/Golem Factory/Generate Floor (no Walls)")]
        public static void GenerateFloorOnly()
        {
            Generate(spawnWalls: false);
        }

        // Kept as its own menu item rather than folded into Generate: import settings are an
        // asset-wide, one-off concern, and re-applying them forces a texture reimport that is
        // far slower than a repaint.
        [MenuItem("Tools/Golem Factory/Reimport Environment Art")]
        public static void ReimportEnvironmentArt()
        {
            int changed = 0;
            var names = new List<string>(FloorSpriteNames);
            names.AddRange(EnvironmentPivots.Keys);

            foreach (string spriteName in names)
            {
                string path = ArtRoot + spriteName + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("SandboxFloorGenerator: missing environment texture " + path);
                    continue;
                }

                Vector2 pivot;
                bool custom = EnvironmentPivots.TryGetValue(spriteName, out pivot);

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteAlignment = (int)(custom ? SpriteAlignment.Custom : SpriteAlignment.Center);
                settings.spritePixelsPerUnit = EnvironmentPixelsPerUnit;
                settings.filterMode = FilterMode.Point;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                if (custom)
                {
                    settings.spritePivot = pivot;
                }
                importer.SetTextureSettings(settings);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed++;
            }

            AssetDatabase.Refresh();
            Debug.Log("SandboxFloorGenerator: reimported " + changed + " environment textures at PPU "
                      + EnvironmentPixelsPerUnit + ".");
        }

        private static void Generate(bool spawnWalls)
        {
            GameObject gridObject = GameObject.Find("Grid");
            if (gridObject == null)
            {
                Debug.LogError("SandboxFloorGenerator: no 'Grid' GameObject found in the active scene.");
                return;
            }

            Grid grid = gridObject.GetComponent<Grid>();
            Tilemap tilemap = gridObject.GetComponentInChildren<Tilemap>();
            if (grid == null || tilemap == null)
            {
                Debug.LogError("SandboxFloorGenerator: 'Grid' is missing a Grid and/or child Tilemap component.");
                return;
            }

            var converter = new GridCoordinateConverter(grid.cellSize);
            TileBase[] tiles = LoadOrCreateFloorTiles();

            tilemap.ClearAllTiles();
            int tileCount = 0;
            foreach (Vector2Int cell in FloorLayout.GetFloorCells())
            {
                tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), tiles[FloorTileVariant.Select(cell.x, cell.y)]);
                tileCount++;
            }

            DestroyChild(gridObject.transform, WallsParentName);
            DestroyChild(gridObject.transform, PropsParentName);

            int pieceCount = 0;
            if (spawnWalls)
            {
                pieceCount = BuildWalls(gridObject.transform, converter) + BuildProps(gridObject.transform, converter);
            }

            EditorSceneManager.MarkSceneDirty(gridObject.scene);
            Debug.Log("SandboxFloorGenerator: painted " + tileCount + " floor tiles and spawned "
                      + pieceCount + " wall/prop pieces.");
        }

        private static int BuildWalls(Transform parent, GridCoordinateConverter converter)
        {
            var wallsParent = new GameObject(WallsParentName);
            wallsParent.transform.SetParent(parent, worldPositionStays: false);

            Sprite ne = LoadSprite("wall_segment_ne");
            Sprite neLamp = LoadSprite("wall_segment_ne_lamp");
            Sprite nw = LoadSprite("wall_segment_nw");
            Sprite nwLamp = LoadSprite("wall_segment_nw_lamp");
            Sprite se = LoadSprite("floor_edge_se");
            Sprite sw = LoadSprite("floor_edge_sw");
            Sprite postSprite = LoadSprite("wall_corner_post");

            int count = 0;
            foreach (int index in FloorLayout.GetEdgeIndices())
            {
                bool neLit = Mod(index, LampSpacing) == 0;
                bool nwLit = Mod(index, LampSpacing) == NorthWestLampPhase;

                count += PlaceEdgePiece(wallsParent.transform, converter, FloorLayout.Edge.NorthEast,
                    index, neLit ? neLamp : ne, "WallNE", neLit);
                count += PlaceEdgePiece(wallsParent.transform, converter, FloorLayout.Edge.NorthWest,
                    index, nwLit ? nwLamp : nw, "WallNW", nwLit);
                count += PlaceEdgePiece(wallsParent.transform, converter, FloorLayout.Edge.SouthEast,
                    index, se, "EdgeSE", false);
                count += PlaceEdgePiece(wallsParent.transform, converter, FloorLayout.Edge.SouthWest,
                    index, sw, "EdgeSW", false);
            }

            int postIndex = 0;
            foreach (Vector2 anchor in FloorLayout.GetWallPostAnchors())
            {
                Vector3 world = converter.CellFractionToWorld(anchor);
                CreateSpriteObject(wallsParent.transform, "WallPost_" + postIndex, postSprite, world);
                postIndex++;
                count++;
            }

            return count;
        }

        private static int PlaceEdgePiece(Transform parent, GridCoordinateConverter converter,
            FloorLayout.Edge edge, int index, Sprite sprite, string namePrefix, bool withLight)
        {
            Vector3 world = converter.CellFractionToWorld(FloorLayout.GetEdgeAnchor(edge, index));
            GameObject piece = CreateSpriteObject(parent, namePrefix + "_" + index, sprite, world);
            if (withLight)
            {
                CreateSconceLight(piece.transform);
            }
            return 1;
        }

        private static int BuildProps(Transform parent, GridCoordinateConverter converter)
        {
            var propsParent = new GameObject(PropsParentName);
            propsParent.transform.SetParent(parent, worldPositionStays: false);

            Sprite crate = LoadSprite("prop_crate");
            Sprite barrel = LoadSprite("prop_barrel");
            Sprite shadow = LoadSprite("ground_shadow");

            // Deterministic, irregular-looking clutter hugging all four edges. Each run uses a
            // different phase so the room never looks mirrored, and the interior stays clear
            // for the actual factory. Determinism matters as much here as in FloorTileVariant:
            // a random scatter would rewrite the scene file on every regeneration.
            int count = 0;
            int he = FloorLayout.HalfExtent;
            for (int i = -he + 1; i <= he - 1; i++)
            {
                if (Mod(i * 3, 7) < 2)
                {
                    count += PlaceProp(propsParent.transform, converter, new Vector2Int(he, i),
                        Mod(i, 2) == 0 ? crate : barrel, shadow);
                }
                if (Mod(i * 5 + 2, 7) < 2)
                {
                    count += PlaceProp(propsParent.transform, converter, new Vector2Int(i, he),
                        Mod(i, 2) == 0 ? barrel : crate, shadow);
                }
                if (Mod(i * 3 + 4, 9) < 2)
                {
                    count += PlaceProp(propsParent.transform, converter, new Vector2Int(-he, i),
                        Mod(i, 2) == 0 ? crate : barrel, shadow);
                }
                if (Mod(i * 7 + 1, 9) < 2)
                {
                    count += PlaceProp(propsParent.transform, converter, new Vector2Int(i, -he),
                        Mod(i, 2) == 0 ? barrel : crate, shadow);
                }
            }
            return count;
        }

        private static int PlaceProp(Transform parent, GridCoordinateConverter converter,
            Vector2Int cell, Sprite sprite, Sprite shadowSprite)
        {
            Vector3 world = converter.CellToWorldCenter(cell);
            GameObject prop = CreateSpriteObject(parent, "Prop_" + cell.x + "_" + cell.y, sprite, world);
            var shadow = prop.AddComponent<GroundShadow>();
            shadow.Configure(shadowSprite, Vector2.zero, 0.95f, 0.9f, anchorToBottom: false);
            shadow.Refresh();
            return 1;
        }

        // Sorting order is baked here rather than by adding a YSortSpriteRenderer: none of these
        // pieces ever move, so a per-frame LateUpdate on ~120 static objects would buy nothing,
        // and baking means they also sort correctly in EditMode (YSortSpriteRenderer, like every
        // other gameplay MonoBehaviour here, has no [ExecuteAlways]). The value is exactly what
        // YSortSpriteRenderer would compute, so walls, props, golems and the player all share
        // one depth ordering.
        private static GameObject CreateSpriteObject(Transform parent, string name, Sprite sprite, Vector3 world)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = world;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = YSortUtility.ComputeSortingOrder(world.y);

            var litMaterial = AssetDatabase.LoadAssetAtPath<Material>(LitSpriteMaterialPath);
            if (litMaterial != null)
            {
                renderer.sharedMaterial = litMaterial;
            }
            return go;
        }

        private static void CreateSconceLight(Transform wallSegment)
        {
            var lightObject = new GameObject("SconceLight");
            lightObject.transform.SetParent(wallSegment, worldPositionStays: false);
            lightObject.transform.localPosition = new Vector3(0f, LampHeight, 0f);

            var light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = LampColor;
            // Nine sconces overlap heavily across a 25-cell room; anything brighter than this
            // stacks into a flat, evenly over-lit floor instead of pools of warmth.
            light.intensity = 0.85f;
            light.pointLightInnerRadius = 0.4f;
            light.pointLightOuterRadius = 3.0f;
            light.falloffIntensity = 0.6f;
        }

        // Creates the Tile assets on first run so the whole floor is reproducible from the PNGs
        // plus this one menu item, instead of needing a Tile Palette pass that cannot be
        // scripted over the MCP bridge anyway.
        private static TileBase[] LoadOrCreateFloorTiles()
        {
            var tiles = new TileBase[FloorTileVariant.TileCount];
            for (int i = 0; i < tiles.Length; i++)
            {
                string tilePath = TileRoot + FloorTileAssetNames[i] + ".asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                Sprite sprite = LoadSprite(FloorSpriteNames[i]);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = sprite;
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                else if (tile.sprite != sprite)
                {
                    tile.sprite = sprite;
                    EditorUtility.SetDirty(tile);
                }
                tiles[i] = tile;
            }
            AssetDatabase.SaveAssets();
            return tiles;
        }

        private static Sprite LoadSprite(string spriteName)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + spriteName + ".png");
            if (sprite == null)
            {
                Debug.LogError("SandboxFloorGenerator: missing sprite " + ArtRoot + spriteName + ".png");
            }
            return sprite;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static int Mod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }
    }
}
