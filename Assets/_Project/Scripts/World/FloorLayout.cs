using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.World
{
    // Shape/size of the workshop floor, kept separate from GridCoordinateConverter (generic
    // isometric math, no opinion on map shape) and GridMap (occupancy state, no opinion on
    // bounds) -- same math/state split those two already establish. HalfExtent is the one
    // knob to turn to resize the floor; SandboxFloorGenerator (Editor-only) reads it to
    // repaint the Tilemap and place walls, and PlayerController.ClampToFloor reads it to
    // keep analog movement inside the painted area.
    public static class FloorLayout
    {
        public const int HalfExtent = 12;

        public static IEnumerable<Vector2Int> GetFloorCells(int halfExtent = HalfExtent)
        {
            for (int x = -halfExtent; x <= halfExtent; x++)
            {
                for (int y = -halfExtent; y <= halfExtent; y++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        // The ring one cell beyond the floor -- wall placement sits here, one full cell
        // outside the walkable area so wall sprites never overlap floor tiles.
        public static IEnumerable<Vector2Int> GetPerimeterCells(int halfExtent = HalfExtent)
        {
            int ring = halfExtent + 1;
            for (int x = -ring; x <= ring; x++)
            {
                for (int y = -ring; y <= ring; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) == ring)
                    {
                        yield return new Vector2Int(x, y);
                    }
                }
            }
        }

        // Only the two "back" edges get visible walls (classic isometric room convention --
        // the two "front" edges nearest the camera stay open so the room interior stays
        // visible; SE/SW are still boundary-clamped via ClampToFloor, just not walled).
        // NE: TOP corner to RIGHT corner (cellX fixed at the ring, cellY varies).
        public static IEnumerable<Vector2Int> GetNorthEastEdgeCells(int halfExtent = HalfExtent)
        {
            int ring = halfExtent + 1;
            for (int y = -ring; y <= ring; y++)
            {
                yield return new Vector2Int(ring, y);
            }
        }

        // NW: TOP corner to LEFT corner (cellY fixed at the ring, cellX varies). Excludes the
        // shared TOP corner (x == ring) since GetNorthEastEdgeCells already places it there.
        public static IEnumerable<Vector2Int> GetNorthWestEdgeCells(int halfExtent = HalfExtent)
        {
            int ring = halfExtent + 1;
            for (int x = -ring; x < ring; x++)
            {
                yield return new Vector2Int(x, ring);
            }
        }

        // Clamps in cell-fraction space, not world space: CellToWorldCenter maps a square in
        // cell space to a rotated diamond in world space, so clamping raw world X/Y to the
        // floor's axis-aligned bounding rectangle would let a player walk through the
        // rectangle's corners, which sit outside the diamond in empty space with no tile and
        // no wall. Clamping x/y independently in cell space first keeps the result inside the
        // actual diamond the walls trace. Uses floats (not Mathf.RoundToInt) so movement stays
        // smooth instead of snapping to cell centers.
        public static Vector3 ClampToFloor(Vector3 worldPosition, GridCoordinateConverter converter, int halfExtent = HalfExtent)
        {
            Vector2 cellFraction = converter.WorldToCellFraction(worldPosition);
            float clampedX = Mathf.Clamp(cellFraction.x, -halfExtent, halfExtent);
            float clampedY = Mathf.Clamp(cellFraction.y, -halfExtent, halfExtent);
            return converter.CellFractionToWorld(new Vector2(clampedX, clampedY));
        }
    }
}
