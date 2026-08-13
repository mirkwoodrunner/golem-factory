using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.World
{
    // Shape/size of the workshop floor, kept separate from GridCoordinateConverter (generic
    // world<->cell math, no opinion on map shape) and GridMap (occupancy state, no opinion on
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

        // The four boundary lines of the floor, named from the ISOMETRIC era when they sat on
        // screen diagonals. Top-down flattened those diagonals onto the axes, so the names now
        // read as compass edges of a rectangle: NorthEast is the +X (east) edge, NorthWest the
        // +Y (north) edge, SouthEast the -X (west) edge, SouthWest the -Y (south) edge.
        //
        // The names are deliberately NOT renamed yet: SandboxFloorGenerator's sprite/pivot table
        // is keyed off them, so a rename is a coordinated change across both files and belongs
        // in the environment-art pass rather than smuggled into the projection switch.
        //
        // Which edges get full walls versus open skirting is a PRESENTATION decision that the
        // top-down switch reopened, and it has now been made in SandboxFloorGenerator.BuildWalls:
        // north/east/west are walled and south is left open with a skirting slab. The isometric
        // convention (two back edges walled, two camera-facing edges open) could not carry over,
        // because a diagonal "back" covers two compass directions and an axis-aligned one does
        // not -- inherited unchanged it walled north and east and left the room's left side open.
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

        // THE TWO NORTH CORNERS ONLY -- the two places where two wall runs actually meet.
        //
        // Isometric returned three. A first pass at top-down returned four, on the reasoning that
        // walling three edges makes every corner a run termination. That was wrong, and wrong in
        // a way worth recording, because the anchor was never the problem: wall_corner_post is a
        // front-on ELEVATION, so its body rises ~1.5 cells in +Y from wherever it is anchored.
        // At a north corner that puts it outside the room, which is the point. At a SOUTH corner
        // it rises INTO the room, standing on the two southernmost floor rows -- and because
        // sorting order is baked from world Y, a piece at y = -12.5 gets the largest order
        // anywhere in the room and draws in front of the player, the golems and the props.
        //
        // The south ends of the side walls are open ends facing the camera. They terminate into
        // the opening; there is nothing there for a pillar to cap. Capping them would need
        // plan-view art, not a fourth copy of the elevation.
        public static IEnumerable<Vector2> GetWallPostAnchors(int halfExtent = HalfExtent)
        {
            float outer = halfExtent + 0.5f;
            yield return new Vector2(outer, outer);
            yield return new Vector2(-outer, outer);
        }

        // Clamps in cell-fraction space, not world space. Under the old isometric projection
        // this was load-bearing: cell space mapped to a rotated diamond, so clamping raw world
        // X/Y to the bounding rectangle let a player walk out through the diamond's corners.
        // Top-down makes cell space and world space differ only by scale, so the two clamps now
        // agree -- but this stays in cell space anyway, because it is the definition that is
        // correct under BOTH projections and the one that survived the switch untested-and-
        // unchanged. Uses floats (not Mathf.RoundToInt) so movement stays smooth instead of
        // snapping to cell centers.
        public static Vector3 ClampToFloor(Vector3 worldPosition, GridCoordinateConverter converter, int halfExtent = HalfExtent)
        {
            Vector2 cellFraction = converter.WorldToCellFraction(worldPosition);
            float clampedX = Mathf.Clamp(cellFraction.x, -halfExtent, halfExtent);
            float clampedY = Mathf.Clamp(cellFraction.y, -halfExtent, halfExtent);
            return converter.CellFractionToWorld(new Vector2(clampedX, clampedY));
        }
    }
}
