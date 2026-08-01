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

        // The four boundary lines of the floor diamond, named for where they sit on screen.
        // NorthEast/NorthWest are the two "back" edges that get full walls; SouthEast/
        // SouthWest are the two camera-facing edges, which stay open (classic isometric room
        // convention) but get a short slab skirting so the floor has a visible thickness.
        public enum Edge
        {
            NorthEast,
            NorthWest,
            SouthEast,
            SouthWest,
        }

        // Anchor for the wall/skirting segment covering ONE cell of the given boundary,
        // returned in cell-fraction space so it is converter-independent and unit-testable.
        //
        // This is the whole fix for the "staircase wall" defect. A wall segment does not live
        // at a perimeter *cell centre* (halfExtent + 1) -- it lives on the boundary LINE at
        // halfExtent + 0.5, and its anchor is the MIDPOINT of the one-cell-long piece of that
        // line, matching the sprite's pivot. Consecutive anchors are exactly one cell-edge
        // apart (0.5 x 0.25 world units, the 2:1 isometric run), so segments whose sprites are
        // 0.5 world wide with a base line rising 0.25 across that width butt together into a
        // continuous wall. Placing 1.375-world-wide, flat-bottomed sprites one per perimeter
        // cell -- which is what the earlier attempt did -- can never do that: the silhouette
        // slope does not match the run, so the segments read as stacked blocks, i.e. a
        // staircase, no matter how they are nudged.
        public static Vector2 GetEdgeAnchor(Edge edge, int index, int halfExtent = HalfExtent)
        {
            float outer = halfExtent + 0.5f;
            switch (edge)
            {
                case Edge.NorthEast: return new Vector2(outer, index);
                case Edge.NorthWest: return new Vector2(index, outer);
                case Edge.SouthEast: return new Vector2(-outer, index);
                default: return new Vector2(index, -outer);
            }
        }

        // One segment per floor cell along the edge -- indices run over the floor's own extent,
        // not the perimeter ring, so the run covers the floor exactly corner to corner.
        public static IEnumerable<int> GetEdgeIndices(int halfExtent = HalfExtent)
        {
            for (int i = -halfExtent; i <= halfExtent; i++)
            {
                yield return i;
            }
        }

        // The three diamond corners a wall run terminates at: the far corner where the two
        // back walls meet, and the left/right corners where each back wall meets an open
        // front edge. The near (bottom) corner is left bare -- it is the closest point to the
        // camera and a post there would sit in front of the room.
        public static IEnumerable<Vector2> GetWallPostAnchors(int halfExtent = HalfExtent)
        {
            float outer = halfExtent + 0.5f;
            yield return new Vector2(outer, outer);
            yield return new Vector2(outer, -outer);
            yield return new Vector2(-outer, outer);
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
