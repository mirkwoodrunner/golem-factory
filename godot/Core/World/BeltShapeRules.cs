using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Which picture one belt tile draws. Not a routing concept -- a belt's routing is entirely
    /// its <see cref="Facing"/>, and this never changes it.
    /// </summary>
    public enum BeltShape
    {
        /// <summary>Runs straight across the cell, entering at the back edge.</summary>
        Straight = 0,

        /// <summary>Enters at a side and turns LEFT to leave along the facing.</summary>
        CornerLeft = 1,

        /// <summary>Enters at a side and turns RIGHT to leave along the facing.</summary>
        CornerRight = 2,
    }

    // Which corner piece a placed belt should wear, derived from what is already feeding it.
    //
    // Pure cell/facing math with no BeltSegment, no BeltNetwork and no scene -- the same
    // engine-free-static-plus-thin-applier split as BeltPlacementRules (which it deliberately
    // sits beside rather than inside) and FacingUtility.
    //
    // THE SPLIT FROM BeltPlacementRules IS THE POINT. ShouldLink answers "do items move from
    // here to there", and it is load-bearing simulation: get it wrong and goods go the wrong
    // way. This answers "which sprite do I draw", and getting it wrong is a cosmetic lie. They
    // are kept apart so a corner piece can never be the thing that decides where cargo goes --
    // the shape is READ OFF the links, never the other way round, which is why every entry
    // this file is given has already passed ShouldLink.
    public static class BeltShapeRules
    {
        /// <summary>
        /// The shape for a belt leaving along <paramref name="outputFacing"/> that is entered by
        /// exactly the travel directions in <paramref name="entryDirections"/>.
        ///
        /// <para>
        /// An entry is a DIRECTION OF TRAVEL, not the side the neighbour sits on: a belt to our
        /// north pointing south enters us travelling <see cref="Facing.South"/>. Stated that way
        /// the turn is a plain comparison against the output, and the two corner cases read as
        /// what they are -- a left turn and a right turn.
        /// </para>
        ///
        /// <para>
        /// <b>Two or more entries draw STRAIGHT, and that is not a fallback.</b> A tile fed from
        /// the back and from a side is a merge, and there is no picture of a merge in this set:
        /// drawing either corner would claim the other feed does not exist. A straight lane with
        /// the neighbour visibly butting into its flank is the honest reading, and it is the one
        /// every belt game draws.
        /// </para>
        /// </summary>
        public static BeltShape Resolve(Facing outputFacing, IReadOnlyList<Facing> entryDirections)
        {
            if (entryDirections == null || entryDirections.Count != 1)
            {
                return BeltShape.Straight;
            }

            return Resolve(outputFacing, entryDirections[0]);
        }

        /// <summary>Single-entry overload -- the case every corner in the game actually is.</summary>
        public static BeltShape Resolve(Facing outputFacing, Facing entryDirection)
        {
            // Entered from behind: a straight run. Also the head-on case (entry is the exact
            // opposite of the output), which ShouldLink already refuses to build, so it can only
            // arrive here as a caller's mistake -- and a straight lane is the safe drawing.
            if (entryDirection == outputFacing || entryDirection == FacingUtility.Opposite(outputFacing))
            {
                return BeltShape.Straight;
            }

            // Travelling south and leaving east is a LEFT turn: east is on the left of something
            // facing south. Said in rotations, the entry is one clockwise step from the output.
            return FacingUtility.RotateClockwise(outputFacing) == entryDirection
                ? BeltShape.CornerLeft
                : BeltShape.CornerRight;
        }

        /// <summary>
        /// The travel direction a neighbour on the <paramref name="side"/> side of a cell enters
        /// it by. Trivial, and named because "the neighbour is north of me, so the item is
        /// travelling south" is the step that gets written backwards.
        /// </summary>
        public static Facing EntryDirectionFromSide(Facing side) => FacingUtility.Opposite(side);
    }
}
