using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.World
{
    // Shape/size of the ground, kept separate from GridCoordinateConverter (generic world<->cell
    // math, no opinion on map shape) and GridMap (occupancy state, no opinion on bounds) -- same
    // math/state split those two already establish. SandboxFloorGenerator (Editor-only) reads it
    // to repaint the Tilemap and place walls, and PlayerController.ClampToFloor reads it to keep
    // analog movement on the ground.
    //
    // TWO REGIONS NOW, NOT ONE. Until the market street, the workshop WAS the map: one square of
    // planks, walls on three sides, and nothing beyond them. docs/game-design.md -- the tabletop
    // source of truth -- describes something else: "Resource markets on the edge of the board
    // where raw materials arrive in full truckload shipments." So the workshop becomes a building
    // standing on a larger world, and the street is ground outside it.
    //
    // The split matters because the two regions answer different questions, and conflating them
    // is what made "grow the world" look impossible:
    //
    //   WORKSHOP  what gets plank floor and walls. Unchanged at 25 x 25, because §3.3 sizes the
    //             44-golem Phase-5 factory against exactly that and shrinking it would be a
    //             silent difficulty change.
    //   WORLD     every cell that exists as ground, workshop and street together. This is what
    //             bounds the PLAYER, so walking out of the shop onto the street is possible --
    //             which it must be, since the goods are bought out there.
    //
    // The south edge stays open: no wall, no door. A Victorian workshop opening onto the street
    // keeps the convention that lets the camera see inside, and the existing skirting slab
    // becomes the kerb between planks and cobbles.
    public static class FloorLayout
    {
        /// <summary>
        /// Half-width of the workshop room, in cells -- the plank floor that gets walls. The name
        /// is unchanged because §3.3, SandboxFloorGenerator, PlayerController and the whole test
        /// suite already speak in terms of it, and it still means exactly what it always did.
        /// </summary>
        public const int HalfExtent = 12;

        /// <summary>
        /// How far the street runs south of the workshop's open front, in cells.
        ///
        /// <para>
        /// Eight is two cart pitches plus walking room: the street lays stalls on a two-cell
        /// pitch (a 96px cart overhangs its cell, and §3.2's extractor cap needs two free
        /// approach tiles per stall), so eight rows holds a stall row with clear ground both in
        /// front of it and behind.
        /// </para>
        /// </summary>
        public const int StreetDepth = 8;

        /// <summary>Southernmost world row. The street hangs below the workshop's south edge.</summary>
        public const int WorldMinY = -HalfExtent - StreetDepth;

        /// <summary>Northernmost world row -- the workshop's back wall.</summary>
        public const int WorldMaxY = HalfExtent;

        /// <summary>
        /// The workshop's plank floor. Named GetFloorCells still, and still the workshop only:
        /// every existing caller (the tile painter, the wall runs, the prop scatter) means the
        /// ROOM by it, and quietly widening it to the whole world would have painted planks down
        /// the market street.
        /// </summary>
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

        /// <summary>
        /// The cobbled street: the rows south of the workshop, same width as it.
        /// </summary>
        public static IEnumerable<Vector2Int> GetStreetCells(
            int halfExtent = HalfExtent, int streetDepth = StreetDepth)
        {
            for (int x = -halfExtent; x <= halfExtent; x++)
            {
                for (int y = -halfExtent - streetDepth; y < -halfExtent; y++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        /// <summary>
        /// Every cell of ground that exists -- workshop and street. What bounds the player.
        /// </summary>
        public static IEnumerable<Vector2Int> GetWorldCells(
            int halfExtent = HalfExtent, int streetDepth = StreetDepth)
        {
            foreach (Vector2Int cell in GetFloorCells(halfExtent))
            {
                yield return cell;
            }

            foreach (Vector2Int cell in GetStreetCells(halfExtent, streetDepth))
            {
                yield return cell;
            }
        }

        /// <summary>Whether a cell is inside the workshop room (as opposed to out on the street).</summary>
        public static bool IsInsideWorkshop(Vector2Int cell, int halfExtent = HalfExtent) =>
            cell.x >= -halfExtent && cell.x <= halfExtent &&
            cell.y >= -halfExtent && cell.y <= halfExtent;

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

        // The four boundary lines of the floor, one per compass direction, each named for the
        // axis it actually sits on: North is +Y, East is +X, South is -Y, West is -X.
        //
        // These were NorthEast/NorthWest/SouthEast/SouthWest until the top-down switch had
        // settled, inherited from the isometric era when the four boundaries were screen
        // diagonals. Flattening them onto the axes left the names pointing at the wrong
        // directions -- SouthEast meant the WEST edge -- so SandboxFloorGenerator was reading
        // `Edge.SouthEast` to place the west wall, and getting it right by knowing the names
        // lied. That is the sort of thing that stays correct exactly until somebody trusts it.
        //
        // Ordered clockwise from North, matching Facing, so the two enums read the same way.
        // Nothing serializes Edge (it is used by the Editor-only generator and by tests), which
        // is what made reordering as safe as renaming.
        //
        // Which edges get full walls versus open skirting is a PRESENTATION decision that the
        // top-down switch reopened, and it has now been made in SandboxFloorGenerator.BuildWalls:
        // north/east/west are walled and south is left open with a skirting slab. The isometric
        // convention (two back edges walled, two camera-facing edges open) could not carry over,
        // because a diagonal "back" covers two compass directions and an axis-aligned one does
        // not -- inherited unchanged it walled north and east and left the room's left side open.
        public enum Edge
        {
            North,
            East,
            South,
            West,
        }

        // Anchor for the wall/skirting segment covering ONE cell of the given boundary,
        // returned in cell-fraction space so it is converter-independent and unit-testable.
        //
        // This is the whole fix for the "staircase wall" defect, and it survived the projection
        // switch because it was never about the projection. A wall segment does not live at a
        // perimeter *cell centre* (halfExtent + 1) -- it lives on the boundary LINE at
        // halfExtent + 0.5, and its anchor is the MIDPOINT of the one-cell-long piece of that
        // line, matching the sprite's pivot.
        //
        // Consecutive anchors are therefore exactly one cell apart along a single world axis.
        // Under isometric that ran diagonally (0.5 x 0.25 world units, the 2:1 run) and a
        // segment had to be 0.5 world wide with a base line rising 0.25 across that width to
        // butt against its neighbour; top-down runs it straight, so a segment is one world unit
        // wide with a flat base. The defect the anchor prevents is the same either way: place a
        // sprite whose width does not match the run, once per perimeter cell -- which is what
        // the earlier attempt did -- and the segments read as stacked blocks, i.e. a staircase,
        // no matter how they are nudged.
        public static Vector2 GetEdgeAnchor(Edge edge, int index, int halfExtent = HalfExtent)
        {
            float outer = halfExtent + 0.5f;
            switch (edge)
            {
                case Edge.North: return new Vector2(index, outer);
                case Edge.East: return new Vector2(outer, index);
                case Edge.West: return new Vector2(-outer, index);
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
        public static Vector3 ClampToFloor(
            Vector3 worldPosition, GridCoordinateConverter converter,
            int halfExtent = HalfExtent, int streetDepth = StreetDepth)
        {
            // BOUNDS THE WORLD, NOT THE ROOM, and that is the point of the split. The player buys
            // raw goods at stalls out on the street, so a clamp at the workshop's south wall would
            // pin them inside the shop and make the market unreachable -- the one movement rule
            // that would quietly undo the whole change.
            //
            // Only the south side opens up. North, east and west are still the workshop's walls,
            // because that is where the building actually ends.
            Vector2 cellFraction = converter.WorldToCellFraction(worldPosition);
            float clampedX = Mathf.Clamp(cellFraction.x, -halfExtent, halfExtent);
            float clampedY = Mathf.Clamp(cellFraction.y, -halfExtent - streetDepth, halfExtent);
            return converter.CellFractionToWorld(new Vector2(clampedX, clampedY));
        }
    }
}
