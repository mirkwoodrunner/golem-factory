using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using GolemFactory.World;

namespace GolemFactory.Editor
{
    // First script under GolemFactory.Editor -- repaints the active scene's floor Tilemap to
    // FloorLayout's current HalfExtent and (optionally) places wall segments along the two
    // "back" diamond edges. Reruns cleanly: clears all tiles before repainting, and destroys
    // any previously-generated Walls parent before recreating it.
    public static class SandboxFloorGenerator
    {
        private const string FloorTilePath = "Assets/_Project/Tilemaps/FloorTile.asset";
        private const string WallSegmentNEPath = "Assets/_Project/Prefabs/WallSegmentNE.prefab";
        private const string WallSegmentNWPath = "Assets/_Project/Prefabs/WallSegmentNW.prefab";

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
            TileBase floorTile = AssetDatabase.LoadAssetAtPath<TileBase>(FloorTilePath);

            tilemap.ClearAllTiles();
            int tileCount = 0;
            foreach (Vector2Int cell in FloorLayout.GetFloorCells())
            {
                tilemap.SetTile(new Vector3Int(cell.x, cell.y, 0), floorTile);
                tileCount++;
            }

            Transform existingWalls = gridObject.transform.Find("Walls");
            if (existingWalls != null)
            {
                Object.DestroyImmediate(existingWalls.gameObject);
            }

            int wallCount = 0;
            if (spawnWalls)
            {
                var wallsParent = new GameObject("Walls");
                wallsParent.transform.SetParent(gridObject.transform, worldPositionStays: false);

                GameObject wallNEPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallSegmentNEPath);
                GameObject wallNWPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallSegmentNWPath);

                foreach (Vector2Int cell in FloorLayout.GetNorthEastEdgeCells())
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(wallNEPrefab, wallsParent.transform);
                    instance.transform.position = converter.CellToWorldCenter(cell);
                    instance.name = $"WallNE_{cell.x}_{cell.y}";
                    wallCount++;
                }

                foreach (Vector2Int cell in FloorLayout.GetNorthWestEdgeCells())
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(wallNWPrefab, wallsParent.transform);
                    instance.transform.position = converter.CellToWorldCenter(cell);
                    instance.name = $"WallNW_{cell.x}_{cell.y}";
                    wallCount++;
                }
            }

            EditorSceneManager.MarkSceneDirty(gridObject.scene);
            Debug.Log($"SandboxFloorGenerator: painted {tileCount} floor tiles and spawned {wallCount} wall segments.");
        }
    }
}
