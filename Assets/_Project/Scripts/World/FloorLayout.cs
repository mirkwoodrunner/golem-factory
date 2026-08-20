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

        /// <summary>
        /// Half-width of the market street, in cells. WIDER THAN THE WORKSHOP, which is the
        /// Game Director's call recorded in progression-design §13.1: §3.2 wants ~8 node sites
        /// at endgame and the street at the building's own width held five.
        ///
        /// <para>
        /// The alternative was a second stall row, and it was rejected for the player's sake
        /// rather than the map's: one row lets a factory run clean parallel vertical buses north
        /// into the workshop, where a second row would sit across every one of them. It would
        /// also eat the free approach tiles §3.2's two-extractor cap needs in front of a stall.
        /// </para>
        ///
        /// <para>
        /// EIGHTEEN, so nine stalls fit at the existing four-cell pitch (x = 0, ±4, ±8, ±12,
        /// ±16) with two cells of margin at each end. Nine rather than eight because keeping
        /// the pitch and the origin means **the five existing stalls do not move** -- their
        /// walk distances are on the Director's do-not-tune list, and shifting them to make a
        /// symmetric eight would have retuned §9's manual era as a side effect.
        /// </para>
        /// </summary>
        public const int StreetHalfExtent = 18;

        /// <summary>
        /// How far north the workshop reaches, in cells. Equal to <see cref="HalfExtent"/> for
        /// the room as authored, and RAISED BY FLOOR EXPANSION (§11 item 15) -- which is why it
        /// is a parameter everywhere below rather than a second constant.
        ///
        /// <para>
        /// <b>Expansion grows the room NORTHWARD ONLY, and that is a deliberate constraint.</b>
        /// Growing it symmetrically would move the south edge, and the street is defined as the
        /// eight rows south of the shop front -- so the road, the kerb and all nine market
        /// stalls would slide south with every purchase, moving landmarks the player navigates
        /// by. Growing north instead extends the workshop away from the camera into empty space:
        /// the shop front, the street and the traders never move, and the new floor appears
        /// behind the factory where there is room to build.
        /// </para>
        ///
        /// <para>
        /// Capped at <see cref="MaxNorthExtent"/>: §11 is explicit that "the design needs land to
        /// be finite and expensive, not continuously paveable".
        /// </para>
        /// </summary>
        public const int DefaultNorthExtent = HalfExtent;

        /// <summary>The furthest north expansion may ever reach. Land is finite.</summary>
        public const int MaxNorthExtent = HalfExtent + 12;

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
        public static IEnumerable<Vector2Int> GetFloorCells(int halfExtent = HalfExtent) =>
            GetFloorCells(halfExtent, halfExtent);

        /// <summary>
        /// The workshop's plank floor, with its north edge stated separately so Floor Expansion
        /// can raise it. <paramref name="northExtent"/> below <paramref name="halfExtent"/> is
        /// clamped up rather than producing an empty or inside-out room.
        /// </summary>
        public static IEnumerable<Vector2Int> GetFloorCells(int halfExtent, int northExtent)
        {
            int north = northExtent < halfExtent ? halfExtent : northExtent;
            for (int x = -halfExtent; x <= halfExtent; x++)
            {
                for (int y = -halfExtent; y <= north; y++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        /// <summary>
        /// The cobbled street: the rows south of the workshop, same width as it.
        /// </summary>
        public static IEnumerable<Vector2Int> GetStreetCells(
            int halfExtent = HalfExtent, int streetDepth = StreetDepth,
            int streetHalfExtent = StreetHalfExtent)
        {
            // The street is WIDER than the room it runs past, so the world is a T rather than a
            // rectangle. Everything that used to be able to say "the world is halfExtent wide"
            // has to ask which row it means -- see IsInsideWorld and ClampToFloor, which are the
            // two places that answer it.
            int width = streetHalfExtent < halfExtent ? halfExtent : streetHalfExtent;
            for (int x = -width; x <= width; x++)
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
            int halfExtent = HalfExtent, int streetDepth = StreetDepth) =>
            GetWorldCells(halfExtent, streetDepth, halfExtent);

        public static IEnumerable<Vector2Int> GetWorldCells(
            int halfExtent, int streetDepth, int northExtent)
        {
            // Workshop rows first, then street rows -- two regions of different widths.
            foreach (Vector2Int cell in GetFloorCells(halfExtent, northExtent))
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
            IsInsideWorkshop(cell, halfExtent, halfExtent);

        public static bool IsInsideWorkshop(Vector2Int cell, int halfExtent, int northExtent) =>
            cell.x >= -halfExtent && cell.x <= halfExtent &&
            cell.y >= -halfExtent && cell.y <= (northExtent < halfExtent ? halfExtent : northExtent);

        /// <summary>
        /// Whether a cell is ground at all -- workshop OR street. The predicate form of
        /// <see cref="GetWorldCells"/>, and what bounds BUILDING, for the same reason
        /// <c>ClampToFloor</c> bounds the player by the world rather than by the room: the five
        /// traders stand out on the street, so a workshop-only rule would forbid the belts and
        /// depots that reach them and quietly make the market unautomatable.
        /// </summary>
        public static bool IsInsideWorld(
            Vector2Int cell, int halfExtent = HalfExtent, int streetDepth = StreetDepth) =>
            IsInsideWorld(cell, halfExtent, streetDepth, StreetHalfExtent);

        /// <summary>
        /// The T-shaped test. A workshop row is the building's width; a street row is the
        /// street's, which is wider. Asking one rectangle would either forbid the outer stalls
        /// or allow building in the empty space north-east and north-west of the building.
        /// </summary>
        public static bool IsInsideWorld(
            Vector2Int cell, int halfExtent, int streetDepth, int streetHalfExtent) =>
            IsInsideWorld(cell, halfExtent, streetDepth, streetHalfExtent, halfExtent);

        public static bool IsInsideWorld(
            Vector2Int cell, int halfExtent, int streetDepth, int streetHalfExtent, int northExtent)
        {
            int north = northExtent < halfExtent ? halfExtent : northExtent;
            if (cell.y > north || cell.y < -halfExtent - streetDepth)
            {
                return false;
            }

            int width = cell.y >= -halfExtent ? halfExtent : streetHalfExtent;
            return cell.x >= -width && cell.x <= width;
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
        public static Vector2 GetEdgeAnchor(Edge edge, int index, int halfExtent = HalfExtent) =>
            GetEdgeAnchor(edge, index, halfExtent, halfExtent);

        /// <summary>
        /// With the north edge stated separately, so Floor Expansion can move the back wall
        /// without touching the shop front or the two side walls' distance from the middle.
        /// </summary>
        public static Vector2 GetEdgeAnchor(Edge edge, int index, int halfExtent, int northExtent)
        {
            float outer = halfExtent + 0.5f;
            float north = (northExtent < halfExtent ? halfExtent : northExtent) + 0.5f;
            switch (edge)
            {
                case Edge.North: return new Vector2(index, north);
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

        // THE WORLD HAS A BOUNDARY TOO, AND IT IS NOT THE WORKSHOP'S. Three of its four sides
        // coincide -- north, east and west are the building's own walls -- but the world runs
        // StreetDepth rows further south, and until this existed nothing described that line. So
        // the side-wall runs stopped dead at the shop front and the street's three outer edges
        // were undrawn: cobbles cut straight into background on the west, east and south, which
        // is what made the market read as an unfinished tilemap rather than as outside.
        //
        // Kept as a separate pair of methods rather than as extra parameters on GetEdgeIndices/
        // GetEdgeAnchor, because the existing pair means "the room" to every one of its callers
        // (the skirting run under the plank floor still has to stop at the plank floor) and
        // widening them in place would have moved that skirting to the far kerb.

        /// <summary>
        /// Indices for a run along the world's full height -- the east and west walls, which
        /// carry on past the shop front and down the street.
        /// </summary>
        public static IEnumerable<int> GetWorldEdgeIndices(
            int halfExtent = HalfExtent, int streetDepth = StreetDepth) =>
            GetWorldEdgeIndices(halfExtent, streetDepth, halfExtent);

        public static IEnumerable<int> GetWorldEdgeIndices(
            int halfExtent, int streetDepth, int northExtent)
        {
            int north = northExtent < halfExtent ? halfExtent : northExtent;
            for (int i = -halfExtent - streetDepth; i <= north; i++)
            {
                yield return i;
            }
        }

        /// <summary>
        /// Anchor for one cell of the world's boundary. Identical to <see cref="GetEdgeAnchor"/>
        /// on north, east and west -- the building's walls are the world's on those three sides --
        /// and differs only on the south, which sits at the far kerb rather than the shop front.
        /// </summary>
        public static Vector2 GetWorldEdgeAnchor(
            Edge edge, int index, int halfExtent = HalfExtent, int streetDepth = StreetDepth) =>
            GetWorldEdgeAnchor(edge, index, halfExtent, streetDepth, halfExtent);

        public static Vector2 GetWorldEdgeAnchor(
            Edge edge, int index, int halfExtent, int streetDepth, int northExtent)
        {
            if (edge == Edge.South)
            {
                return new Vector2(index, -halfExtent - streetDepth - 0.5f);
            }

            // THE SIDE RUNS STEP OUT AT THE SHOP FRONT. North of it they are the building's own
            // walls; south of it they are the street's, which is StreetHalfExtent - HalfExtent
            // cells further out on each side. This is the cost the Director accepted with §13.1:
            // the world is no longer a rectangle the side walls bound in one straight run.
            if (edge == Edge.East || edge == Edge.West)
            {
                float outer = index < -halfExtent ? StreetHalfExtent + 0.5f : halfExtent + 0.5f;
                return new Vector2(edge == Edge.East ? outer : -outer, index);
            }

            return GetEdgeAnchor(edge, index, halfExtent, northExtent);
        }

        /// <summary>
        /// Indices for the far kerb and any other run that spans the STREET's width rather than
        /// the workshop's. Separate from <see cref="GetEdgeIndices"/> for exactly the reason
        /// that pair is separate from the world one: the skirting under the plank deck still has
        /// to stop where the planks do.
        /// </summary>
        public static IEnumerable<int> GetStreetEdgeIndices(int streetHalfExtent = StreetHalfExtent)
        {
            for (int i = -streetHalfExtent; i <= streetHalfExtent; i++)
            {
                yield return i;
            }
        }

        /// <summary>
        /// The two "shoulders": the stretches of wall running east and west along the workshop's
        /// south face, from the corner of the building out to the street's own edge.
        ///
        /// <para>
        /// They exist only because the street is wider than the building. Without them the
        /// ground north of the outer street -- the empty space beside the shop front -- has no
        /// boundary drawn at all, and the cobbles run off into background exactly as they did
        /// on three sides before the world edge existed. Anchored on the workshop's south line,
        /// facing the street, so they read as the outside of the building's flank.
        /// </para>
        /// </summary>
        public static IEnumerable<Vector2> GetShoulderAnchors(
            int halfExtent = HalfExtent, int streetHalfExtent = StreetHalfExtent)
        {
            for (int x = halfExtent + 1; x <= streetHalfExtent; x++)
            {
                yield return new Vector2(x, -halfExtent - 0.5f);
                yield return new Vector2(-x, -halfExtent - 0.5f);
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
        public static IEnumerable<Vector2> GetWallPostAnchors(int halfExtent = HalfExtent) =>
            GetWallPostAnchors(halfExtent, halfExtent);

        public static IEnumerable<Vector2> GetWallPostAnchors(int halfExtent, int northExtent)
        {
            // The posts cap the two NORTH corners, so they ride the back wall outward with it.
            float outer = halfExtent + 0.5f;
            float north = (northExtent < halfExtent ? halfExtent : northExtent) + 0.5f;
            yield return new Vector2(outer, north);
            yield return new Vector2(-outer, north);
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
            int halfExtent = HalfExtent, int streetDepth = StreetDepth) =>
            ClampToFloor(worldPosition, converter, halfExtent, streetDepth, halfExtent);

        public static Vector3 ClampToFloor(
            Vector3 worldPosition, GridCoordinateConverter converter,
            int halfExtent, int streetDepth, int northExtent)
        {
            // BOUNDS THE WORLD, NOT THE ROOM, and that is the point of the split. The player buys
            // raw goods at stalls out on the street, so a clamp at the workshop's south wall would
            // pin them inside the shop and make the market unreachable -- the one movement rule
            // that would quietly undo the whole change.
            //
            // Only the south side opens up. North, east and west are still the workshop's walls,
            // because that is where the building actually ends.
            //
            // THE STREET IS WIDER THAN THE ROOM (§13.1), so this is a T and not a rectangle, and
            // a T cannot be clamped one axis at a time. Both orders are wrong in their own way:
            //
            //   Y THEN X pulled a player sideways THROUGH the building's flank. Standing on the
            //   outer street at x = 16 and walking north put them on a workshop row, where the
            //   legal width is only the room's, and the X clamp then moved them 4 cells east into
            //   the shop. That is the wall-clip found in play.
            //
            //   X THEN Y sent anyone standing outside the world's north-east corner all the way
            //   down the road, because it preserved an x that only the street could justify.
            //
            // So: clamp into each RECTANGLE the T is made of, and take whichever is nearer. That
            // is a wall for a walking player -- movement is small steps, so the road stays the
            // nearer rectangle right up until the room genuinely is -- and it is the honest
            // nearest-legal-point answer for anything teleported in from outside.
            Vector2 cellFraction = converter.WorldToCellFraction(worldPosition);
            float north = northExtent < halfExtent ? halfExtent : northExtent;

            // The room.
            var inRoom = new Vector2(
                Mathf.Clamp(cellFraction.x, -halfExtent, halfExtent),
                Mathf.Clamp(cellFraction.y, -halfExtent, north));

            // The road. Its north edge STEPS BACK beside the building: level with the shop front
            // the road is only as wide as the room, and further out there is wall.
            float roadX = Mathf.Clamp(cellFraction.x, -StreetHalfExtent, StreetHalfExtent);
            float roadNorth = Mathf.Abs(roadX) > halfExtent ? -halfExtent - 1f : -halfExtent;
            var onRoad = new Vector2(
                roadX, Mathf.Clamp(cellFraction.y, -halfExtent - streetDepth, roadNorth));

            Vector2 nearest = (cellFraction - inRoom).sqrMagnitude <= (cellFraction - onRoad).sqrMagnitude
                ? inRoom
                : onRoad;

            return converter.CellFractionToWorld(nearest);
        }
    }
}
