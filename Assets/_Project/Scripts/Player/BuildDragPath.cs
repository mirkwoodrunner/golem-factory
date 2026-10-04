using System.Collections.Generic;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Player
{
    // The cells a click-and-drag lays down, and in what order.
    //
    // Pure cell math, no camera, no pointer and no scene, so the whole of "what did that drag
    // build" is unit-testable without simulating a mouse -- the same split PlayerMovement
    // .ComputeDisplacement and GridCoordinateConverter use, and the reason BuildModeController
    // can keep its drag handler down to bookkeeping.
    //
    // ORTHOGONAL ONLY, and axis-first. A drag from (0,0) to (3,2) walks x then y, so it builds
    // an L rather than a staircase. Two reasons, and neither is a shortcut:
    //
    //   * A belt run has to be orthogonal -- there is no diagonal facing, so a staircase would
    //     be a corner every single cell, which is both ugly and (at 4 belts per turn) far more
    //     expensive than the run the player thinks they are drawing.
    //   * An L is PREDICTABLE from the two endpoints alone. A path that depended on the exact
    //     wiggle of the cursor would build a different factory every time the same gesture was
    //     made, and a player laying a run down a wall cannot hold a mouse to one pixel row.
    //
    // The cursor's own wiggle is still not thrown away: BuildModeController re-anchors the drag
    // at each cell it commits, so a deliberate turn mid-drag is honoured on the next step. What
    // this rules out is a single step turning into a diagonal.
    public static class BuildDragPath
    {
        /// <summary>
        /// Appends every cell strictly BETWEEN <paramref name="from"/> and
        /// <paramref name="to"/>, plus <paramref name="to"/> itself, in walk order.
        /// <paramref name="from"/> is never appended -- it is already placed, which is what
        /// started the drag.
        /// </summary>
        public static void AppendCells(Vector2Int from, Vector2Int to, List<Vector2Int> cells)
        {
            if (cells == null || from == to)
            {
                return;
            }

            Vector2Int cursor = from;
            int stepX = to.x > from.x ? 1 : -1;
            while (cursor.x != to.x)
            {
                cursor.x += stepX;
                cells.Add(cursor);
            }

            int stepY = to.y > from.y ? 1 : -1;
            while (cursor.y != to.y)
            {
                cursor.y += stepY;
                cells.Add(cursor);
            }
        }

        /// <summary>
        /// The facing a placeable takes when the drag steps from <paramref name="from"/> to the
        /// orthogonally adjacent <paramref name="to"/>.
        ///
        /// <para>
        /// Returns <paramref name="fallback"/> for anything that is not a single orthogonal
        /// step, so a caller that has lost track of its own path gets the cursor's facing rather
        /// than a silently wrong one. Every cell <see cref="AppendCells"/> produces IS one such
        /// step from the cell before it, which is the property that makes a dragged belt run
        /// point along itself.
        /// </para>
        /// </summary>
        public static Facing StepFacing(Vector2Int from, Vector2Int to, Facing fallback)
        {
            Vector2Int delta = to - from;
            if (delta == new Vector2Int(0, 1))
            {
                return Facing.North;
            }

            if (delta == new Vector2Int(1, 0))
            {
                return Facing.East;
            }

            if (delta == new Vector2Int(0, -1))
            {
                return Facing.South;
            }

            if (delta == new Vector2Int(-1, 0))
            {
                return Facing.West;
            }

            return fallback;
        }
    }
}
