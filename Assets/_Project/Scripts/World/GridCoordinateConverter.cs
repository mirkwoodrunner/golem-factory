using UnityEngine;

namespace GolemFactory.World
{
    // World<->cell math, decoupled from Unity's Tilemap component so it stays EditMode-testable
    // without a scene. Must match the cell size configured on the scene's Grid/Tilemap (see the
    // M1 manual setup steps in docs/unity-implementation-plan.md).
    //
    // TOP-DOWN (RECTANGULAR). This is the whole projection: the two Fraction methods below are
    // the only place the game decides how a cell maps to a screen position, which is why moving
    // off isometric costs six lines here and nothing at the 20+ call sites. That was by design --
    // GridMap has always been a plain rectangular grid and isometric was presentation only.
    // Pair this with m_CellLayout: 0 (Rectangle) and a square m_CellSize on the scene's Grid.
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
            return new Vector2(worldPosition.x / CellSize.x, worldPosition.y / CellSize.y);
        }

        public Vector3 CellFractionToWorld(Vector2 cellFraction)
        {
            return new Vector3(cellFraction.x * CellSize.x, cellFraction.y * CellSize.y, 0f);
        }
    }
}
