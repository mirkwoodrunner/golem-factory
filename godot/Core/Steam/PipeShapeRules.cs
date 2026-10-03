using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Steam
{
    /// <summary>
    /// Which of the five pipe pictures a cell draws. Purely presentational: a pipe's *reach* is
    /// <see cref="SteamPipeRules"/>' undirected flood fill, which never asks a pipe what it
    /// looks like.
    /// </summary>
    public enum PipeShape
    {
        /// <summary>Capped, open on one side only. Also what an isolated pipe draws.</summary>
        End = 0,

        /// <summary>Open on two opposite sides.</summary>
        Straight = 1,

        /// <summary>Open on two adjacent sides -- the elbow.</summary>
        Corner = 2,

        /// <summary>Open on three sides.</summary>
        Tee = 3,

        /// <summary>Open on all four. The one shape with no orientation to speak of.</summary>
        Cross = 4,
    }

    // Which pipe piece a cell wears, from which of its four neighbours it joins on to.
    //
    // Pure cell math, no MonoBehaviour and no SteamNetwork -- the same split SteamPipeRules
    // itself uses, and it sits beside that file for the same reason BeltShapeRules sits beside
    // BeltPlacementRules: one file owns where the steam GOES, this one owns what the player
    // SEES, and the picture is read off the topology rather than the topology off the picture.
    //
    // WHY THIS EXISTS AT ALL, given a pipe has no facing. It has no facing in the simulation --
    // that is SteamPipeRules' whole argument and none of it is being walked back. But a length
    // of pipe still has to LOOK like it joins the pipe next to it, and until now every pipe in
    // the game drew the same east-west run: a north-south column of pipe rendered as a stack of
    // disconnected rungs that were, in fact, one connected network. The picture contradicted
    // the flood fill. Deriving the picture from the same adjacency the flood fill walks is what
    // makes them agree by construction.
    //
    // ORIENTATION CONVENTION. Every piece is authored pointing EAST (zero degrees) and rotated
    // by FacingVisuals.ScreenAngleDegrees, exactly as the belt's chevron and the build ghost's
    // arrow already are -- so a projection change moves the pipes with everything else instead
    // of leaving them as the one system holding hardcoded quarter turns.
    public static class PipeShapeRules
    {
        /// <summary>
        /// The piece and the rotation for a cell open on the given sides.
        ///
        /// <para>
        /// <paramref name="isolatedFacing"/> is used ONLY when the cell touches nothing, which
        /// is the single case the topology cannot answer -- and the reason <c>R</c> still means
        /// something while a pipe is in hand. Every other case is decided by the neighbours, so
        /// rotating the ghost cannot make a connected run draw wrong.
        /// </para>
        /// </summary>
        public static void Resolve(
            bool north, bool east, bool south, bool west, Facing isolatedFacing,
            out PipeShape shape, out Facing orientation)
        {
            int count = (north ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (west ? 1 : 0);
            switch (count)
            {
                case 0:
                    // Nothing to join. The stub points wherever the player last turned it, which
                    // is the whole of what R does for a pipe -- and it matters, because the very
                    // next pipe is usually laid off that open end.
                    shape = PipeShape.End;
                    orientation = isolatedFacing;
                    return;

                case 1:
                    // The piece is authored open to the EAST, so it points at its one neighbour.
                    shape = PipeShape.End;
                    orientation = north ? Facing.North : east ? Facing.East
                        : south ? Facing.South : Facing.West;
                    return;

                case 4:
                    shape = PipeShape.Cross;
                    orientation = Facing.East;
                    return;

                case 3:
                    // Authored as {North, East, South} -- i.e. missing WEST -- pointing East. So
                    // the stem points away from the gap, whichever side the gap is on.
                    shape = PipeShape.Tee;
                    orientation = FacingUtility.Opposite(
                        !north ? Facing.North : !east ? Facing.East
                        : !south ? Facing.South : Facing.West);
                    return;

                default:
                    // Exactly two. north == south says the pair is OPPOSITE -- both vertical
                    // sides open, or both closed and therefore both horizontal ones open --
                    // which is the only way two sides make a straight run.
                    if (north == south)
                    {
                        shape = PipeShape.Straight;
                        // Authored east-west; a quarter turn makes it north-south. Naming the
                        // pair by its first compass member rather than by "horizontal/vertical"
                        // keeps the whole file speaking one language.
                        orientation = north ? Facing.North : Facing.East;
                        return;
                    }

                    // Two open sides that are not opposite each other: the elbow. Authored as
                    // {North, East} pointing East, so the rotation is named by whichever of the
                    // pair has the OTHER one anticlockwise of it (North is one step
                    // anticlockwise of East).
                    shape = PipeShape.Corner;
                    orientation = ResolveCorner(north, east, south, west);
                    return;
            }
        }

        private static Facing ResolveCorner(bool north, bool east, bool south, bool west)
        {
            for (int i = 0; i < 4; i++)
            {
                var candidate = (Facing)i;
                if (!IsOpen(candidate, north, east, south, west))
                {
                    continue;
                }

                if (IsOpen(FacingUtility.RotateCounterClockwise(candidate), north, east, south, west))
                {
                    return candidate;
                }
            }

            // Unreachable for a genuine adjacent pair; East is the authored orientation, so an
            // impossible input draws the piece as it was drawn rather than as a null rotation.
            return Facing.East;
        }

        private static bool IsOpen(Facing side, bool north, bool east, bool south, bool west)
        {
            switch (side)
            {
                case Facing.North:
                    return north;
                case Facing.East:
                    return east;
                case Facing.South:
                    return south;
                default:
                    return west;
            }
        }
    }
}
