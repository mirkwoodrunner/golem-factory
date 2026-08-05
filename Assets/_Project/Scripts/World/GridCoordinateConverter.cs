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
            Vector2 fraction = WorldToCellFraction(worldPosition);
            return new Vector2Int(Mathf.RoundToInt(fraction.x), Mathf.RoundToInt(fraction.y));
        }

        public Vector3 CellToWorldCenter(Vector2Int cell)
        {
            return CellFractionToWorld(new Vector2(cell.x, cell.y));
        }

        // Unrounded siblings of the above, needed for smooth analog clamping (see
        // FloorLayout.ClampToFloor) where snapping to a cell would make movement jerky.
        public Vector2 WorldToCellFraction(Vector3 worldPosition)
        {
            float halfWidth = CellSize.x * 0.5f;
            float halfHeight = CellSize.y * 0.5f;
            float a = worldPosition.x / halfWidth;
            float b = worldPosition.y / halfHeight;

            return new Vector2((a + b) * 0.5f, (b - a) * 0.5f);
        }

        public Vector3 CellFractionToWorld(Vector2 cellFraction)
        {
            float halfWidth = CellSize.x * 0.5f;
            float halfHeight = CellSize.y * 0.5f;
            float x = (cellFraction.x - cellFraction.y) * halfWidth;
            float y = (cellFraction.x + cellFraction.y) * halfHeight;
            return new Vector3(x, y, 0f);
        }
    }
}
