using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.Steam
{
    // Which cells a Boiler can push steam to. Pure cell math -- no MonoBehaviour, no scene, no
    // SteamNetwork -- the same engine-free-static-plus-thin-applier split as
    // World/BeltPlacementRules, World/FacingUtility and World/GridCoordinateConverter, and the
    // reason the flood fill is unit-testable at all.
    //
    // WHY THIS IS NOT BeltPlacementRules (docs/progression-design.md §11 item 4).
    // Revision 2 of the progression design claimed steam could reuse BeltNetwork/
    // BeltPlacementRules wholesale. That was wrong, and §11 corrects it. BeltPlacementRules
    // .ShouldLink is DIRECTIONAL in two separate ways, and steam has neither property:
    //
    //   * it requires FacingUtility.TargetCell(from, fromFacing) == to, i.e. the upstream belt
    //     must POINT INTO the downstream one. A steam pipe has no facing and no flow direction
    //     -- it is a length of pipe, and pressure does not care which end you built first.
    //   * it rejects a head-on pair (A points at B while B points back at A) to avoid building
    //     a two-cycle that shuffles one item back and forth forever. A steam network is a plain
    //     undirected graph where cycles are meaningless rather than harmful: a ring of pipe
    //     round a boiler is a perfectly good ring of pipe, and the flood fill's visited set
    //     terminates on it without any rule about pairs.
    //
    // So this is an UNDIRECTED flood fill over orthogonally adjacent pipe cells -- a much
    // smaller component than BeltNetwork, not a reuse of it. Reusing ShouldLink would have
    // silently made half the player's pipe runs dead, depending purely on which way the ghost
    // happened to be rotated when they clicked.
    public static class SteamPipeRules
    {
        // Fixed order, and deliberately an array rather than a yield-return: the flood fill's
        // enqueue order is part of what makes the whole system reproducible, and iteration
        // order over a compile-time array is the one ordering C# actually guarantees.
        // (The RESULT of a flood fill is a set and so is order-independent anyway; this just
        // means two runs also visit in the same order, which keeps a debugger session honest.)
        public static readonly Vector2Int[] OrthogonalOffsets =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0)
        };

        /// <summary>
        /// Orthogonal (4-way) adjacency. DIAGONALS DO NOT CONNECT: a pipe touching another only
        /// at a corner is two separate runs, which is what makes pipe layout a real spatial
        /// puzzle rather than a blob-fill. Same 4-adjacency the golem's own SourceCell/TargetCell
        /// uses, so "next to" means one thing everywhere in the game.
        /// </summary>
        public static bool AreOrthogonallyAdjacent(Vector2Int a, Vector2Int b)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            if (dx < 0)
            {
                dx = -dx;
            }

            if (dy < 0)
            {
                dy = -dy;
            }

            return dx + dy == 1;
        }

        /// <summary>
        /// Every pipe cell reachable from <paramref name="boilerCell"/> by walking orthogonally
        /// from pipe to pipe. The boiler's own cell is never included (it is not a pipe), and a
        /// gap of even one cell ends the run -- that gap is exactly progression-design §9's
        /// Phase 6 beat, where a stage bar freezes because one extractor lost steam when a pipe
        /// was paved over.
        /// </summary>
        public static void CollectConnectedPipes(
            Vector2Int boilerCell, HashSet<Vector2Int> allPipeCells, HashSet<Vector2Int> connected)
        {
            if (connected == null)
            {
                return;
            }

            connected.Clear();
            if (allPipeCells == null || allPipeCells.Count == 0)
            {
                return;
            }

            // Breadth-first from the pipes touching the boiler. An explicit queue rather than
            // recursion: a long pipe corridor is unbounded in length and a recursive fill would
            // put the stack depth in the player's hands.
            var frontier = new Queue<Vector2Int>();
            for (int i = 0; i < OrthogonalOffsets.Length; i++)
            {
                Vector2Int seed = boilerCell + OrthogonalOffsets[i];
                if (allPipeCells.Contains(seed) && connected.Add(seed))
                {
                    frontier.Enqueue(seed);
                }
            }

            while (frontier.Count > 0)
            {
                Vector2Int cell = frontier.Dequeue();
                for (int i = 0; i < OrthogonalOffsets.Length; i++)
                {
                    Vector2Int neighbour = cell + OrthogonalOffsets[i];
                    // Add returns false on an already-visited cell, so a ring of pipe terminates
                    // rather than looping -- the undirected analogue of the two-cycle guard
                    // BeltPlacementRules needs a whole rule for.
                    if (allPipeCells.Contains(neighbour) && connected.Add(neighbour))
                    {
                        frontier.Enqueue(neighbour);
                    }
                }
            }
        }

        /// <summary>
        /// The cells a golem could stand on and be powered: orthogonally adjacent to the boiler
        /// itself, or to any pipe connected back to it. Takes the connected set from
        /// <see cref="CollectConnectedPipes"/> so the two halves stay separately testable.
        /// </summary>
        public static void CollectPoweredCells(
            Vector2Int boilerCell, HashSet<Vector2Int> connectedPipes, HashSet<Vector2Int> poweredCells)
        {
            if (poweredCells == null)
            {
                return;
            }

            poweredCells.Clear();

            // The boiler powers its own four neighbours with no pipe at all -- §3.1's "the golem
            // must be orthogonally adjacent to the Boiler OR to a Steam Pipe". A one-boiler,
            // zero-pipe starting workshop therefore already runs four golems, which is what
            // makes Phase 1 playable before Iron Plate (and therefore pipe) exists.
            for (int i = 0; i < OrthogonalOffsets.Length; i++)
            {
                poweredCells.Add(boilerCell + OrthogonalOffsets[i]);
            }

            if (connectedPipes == null)
            {
                return;
            }

            foreach (Vector2Int pipe in connectedPipes)
            {
                for (int i = 0; i < OrthogonalOffsets.Length; i++)
                {
                    poweredCells.Add(pipe + OrthogonalOffsets[i]);
                }
            }
        }

        /// <summary>
        /// Total order over cells, used to make every "which one first?" question in the steam
        /// system reproducible. Column-major (x, then y) -- an arbitrary but FIXED choice; what
        /// matters is only that it is a total order derived from the factory's own layout rather
        /// than from a Dictionary bucket, a scene sibling index or the order the player built in.
        /// </summary>
        public static int CompareCells(Vector2Int a, Vector2Int b)
        {
            if (a.x != b.x)
            {
                return a.x < b.x ? -1 : 1;
            }

            if (a.y != b.y)
            {
                return a.y < b.y ? -1 : 1;
            }

            return 0;
        }
    }
}
