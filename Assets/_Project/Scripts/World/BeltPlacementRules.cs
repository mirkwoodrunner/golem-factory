using UnityEngine;

namespace GolemFactory.World
{
    // When does one placed belt feed the next? Pure cell/facing math, no BeltSegment and no
    // scene -- the same engine-free-static-plus-thin-applier split as FacingUtility and
    // GridCoordinateConverter, and the reason belt auto-chaining is unit-testable at all.
    //
    // Lives in World/ rather than Belts/ because Belts/ deliberately knows nothing about the
    // spatial layer (it has no reverse reference to Golems/ or World/); BeltNetwork wraps
    // BeltSegment strictly from the outside using its existing public surface.
    public static class BeltPlacementRules
    {
        /// <summary>
        /// Whether a belt at <paramref name="fromCell"/> facing <paramref name="fromFacing"/>
        /// should hand its items to a belt at <paramref name="toCell"/> facing
        /// <paramref name="toFacing"/>.
        /// </summary>
        public static bool ShouldLink(
            Vector2Int fromCell, Facing fromFacing, Vector2Int toCell, Facing toFacing)
        {
            // Must actually point into it. Adjacency alone is not enough -- two belts running
            // side by side in parallel lanes are neighbours and must stay independent.
            if (FacingUtility.TargetCell(fromCell, fromFacing) != toCell)
            {
                return false;
            }

            // Head-on pair: A points at B and B points back at A. Linking these would build an
            // instant two-cycle that shuffles the same item back and forth forever, so the
            // player gets two dead-ended belts instead -- visibly wrong, which is the point.
            if (toFacing == FacingUtility.Opposite(fromFacing))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a SPLITTER at <paramref name="splitterCell"/> should feed the belt at
        /// <paramref name="toCell"/> facing <paramref name="toFacing"/>.
        ///
        /// <para>
        /// A splitter has no facing of its own -- that is what makes it a splitter rather than a
        /// belt -- so the rule is inverted: the neighbour opts IN by facing away. A belt whose
        /// tail is against the splitter is being fed by it; a belt pointing back into it is
        /// feeding it, and a belt running past it sideways is a neighbour and nothing more.
        /// </para>
        ///
        /// <para>
        /// Expressed as "the cell behind the neighbour is the splitter", which is exactly
        /// <see cref="ShouldLink"/>'s test read from the other end -- so an input and an output
        /// can never both be true of the same pair, and a two-cycle is impossible by
        /// construction rather than by a second head-on guard.
        /// </para>
        /// </summary>
        public static bool ShouldSplitTo(Vector2Int splitterCell, Vector2Int toCell, Facing toFacing) =>
            FacingUtility.TargetCell(toCell, FacingUtility.Opposite(toFacing)) == splitterCell;
    }
}
