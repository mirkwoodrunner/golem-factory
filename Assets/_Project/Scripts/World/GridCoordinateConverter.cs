using UnityEngine;

namespace GolemFactory.World
{
    // Isometric world<->cell math, decoupled from Unity's Tilemap component so it stays
    // EditMode-testable without a scene. Must match the cell size configured on the scene's
    // Grid/Tilemap (see the M1 manual setup steps in docs/unity-implementation-plan.md).
    public readonly struct GridCoordinateConverter
    {
        public Vector2 CellSize { get; }

        public GridCoordinateConverter(Vector2 cellSize)
        {
            CellSize = cellSize;
        }

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            float halfWidth = CellSize.x * 0.5f;
            float halfHeight = CellSize.y * 0.5f;
            float a = worldPosition.x / halfWidth;
            float b = worldPosition.y / halfHeight;

            int cellX = Mathf.RoundToInt((a + b) * 0.5f);
            int cellY = Mathf.RoundToInt((b - a) * 0.5f);
            return new Vector2Int(cellX, cellY);
        }

        public Vector3 CellToWorldCenter(Vector2Int cell)
        {
            float halfWidth = CellSize.x * 0.5f;
            float halfHeight = CellSize.y * 0.5f;
            float x = (cell.x - cell.y) * halfWidth;
            float y = (cell.x + cell.y) * halfHeight;
            return new Vector3(x, y, 0f);
        }
    }
}
