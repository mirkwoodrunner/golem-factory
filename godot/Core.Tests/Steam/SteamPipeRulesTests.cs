using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Steam;

namespace GolemFactory.Tests.EditMode
{
    // The undirected flood fill (docs/progression-design.md §3.1, §11 item 4). Pure cell math,
    // so none of this needs a scene, a boiler object or a tick.
    //
    // The four shapes that matter are a chain (steam travels), a branch (it travels BOTH ways,
    // which a directional linker could never do), a gap (the chain breaks, which is §9's Phase 6
    // beat) and a diagonal (it does NOT connect, which is what keeps pipe layout a puzzle).
    public class SteamPipeRulesTests
    {
        private static HashSet<Vector2Int> Pipes(params Vector2Int[] cells) =>
            new HashSet<Vector2Int>(cells);

        private static Vector2Int C(int x, int y) => new Vector2Int(x, y);

        private static HashSet<Vector2Int> Connected(Vector2Int boiler, HashSet<Vector2Int> pipes)
        {
            var result = new HashSet<Vector2Int>();
            SteamPipeRules.CollectConnectedPipes(boiler, pipes, result);
            return result;
        }

        private static HashSet<Vector2Int> Powered(Vector2Int boiler, HashSet<Vector2Int> pipes)
        {
            var powered = new HashSet<Vector2Int>();
            SteamPipeRules.CollectPoweredCells(boiler, Connected(boiler, pipes), powered);
            return powered;
        }

        [Test]
        public void OrthogonalAdjacency_IsTheFourNeighbours_AndNotTheFourCorners()
        {
            Assert.IsTrue(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(0, 1)));
            Assert.IsTrue(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(1, 0)));
            Assert.IsTrue(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(0, -1)));
            Assert.IsTrue(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(-1, 0)));

            Assert.IsFalse(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(1, 1)),
                "a diagonal is not adjacency");
            Assert.IsFalse(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(0, 0)),
                "a cell is not adjacent to itself");
            Assert.IsFalse(SteamPipeRules.AreOrthogonallyAdjacent(C(0, 0), C(0, 2)),
                "two cells apart is not adjacency");
        }

        [Test]
        public void AChainOfPipes_CarriesSteamToItsFarEnd()
        {
            // Boiler at (0,0); pipes running east from (1,0) to (4,0).
            HashSet<Vector2Int> pipes = Pipes(C(1, 0), C(2, 0), C(3, 0), C(4, 0));

            HashSet<Vector2Int> connected = Connected(C(0, 0), pipes);

            Assert.AreEqual(4, connected.Count, "the whole run should be reached");
            Assert.IsTrue(connected.Contains(C(4, 0)), "the far end is on the network");

            // A golem standing past the last pipe is powered.
            Assert.IsTrue(Powered(C(0, 0), pipes).Contains(C(5, 0)));
        }

        [Test]
        public void ABranch_CarriesSteamDownBothArms()
        {
            //        (2,2)
            //          |
            // B-(1,0)-(2,0)-(3,0)
            //          |
            //        (2,-2) is NOT reached (gap at (2,-1) omitted deliberately below)
            HashSet<Vector2Int> pipes = Pipes(
                C(1, 0), C(2, 0), C(3, 0),
                C(2, 1), C(2, 2));

            HashSet<Vector2Int> connected = Connected(C(0, 0), pipes);

            Assert.AreEqual(5, connected.Count);
            Assert.IsTrue(connected.Contains(C(3, 0)), "east arm");
            Assert.IsTrue(connected.Contains(C(2, 2)), "north arm");

            // A directional linker could not do this: BeltPlacementRules.ShouldLink needs each
            // pipe to POINT INTO the next, so a T-junction would have to choose one arm.
        }

        [Test]
        public void AGapBreaksTheChain_AndEverythingPastItIsUnreachable()
        {
            // (3,0) is missing -- paved over. progression-design §9 Phase 6.
            HashSet<Vector2Int> pipes = Pipes(C(1, 0), C(2, 0), C(4, 0), C(5, 0));

            HashSet<Vector2Int> connected = Connected(C(0, 0), pipes);

            Assert.AreEqual(2, connected.Count, "only the near side of the gap");
            Assert.IsTrue(connected.Contains(C(2, 0)));
            Assert.IsFalse(connected.Contains(C(4, 0)), "past the gap is dead");
            Assert.IsFalse(Powered(C(0, 0), pipes).Contains(C(6, 0)),
                "a golem beyond the gap has no steam");
        }

        [Test]
        public void ADiagonalPipe_DoesNotConnect()
        {
            // (1,0) touches the boiler; (2,1) only touches (1,0) at a corner.
            HashSet<Vector2Int> pipes = Pipes(C(1, 0), C(2, 1));

            HashSet<Vector2Int> connected = Connected(C(0, 0), pipes);

            Assert.AreEqual(1, connected.Count, "the corner-touching pipe is a separate run");
            Assert.IsTrue(connected.Contains(C(1, 0)));
            Assert.IsFalse(connected.Contains(C(2, 1)));
        }

        [Test]
        public void ARingOfPipe_TerminatesInsteadOfLooping()
        {
            // The undirected analogue of the two-cycle BeltPlacementRules needs a whole rule
            // for: here a cycle is harmless, and the visited set is what ends the walk.
            HashSet<Vector2Int> pipes = Pipes(
                C(1, 0), C(2, 0), C(2, 1), C(2, 2), C(1, 2), C(0, 2), C(0, 1));

            HashSet<Vector2Int> connected = Connected(C(0, 0), pipes);

            Assert.AreEqual(7, connected.Count, "every pipe in the ring, exactly once");
        }

        [Test]
        public void ABoilerWithNoPipesAtAll_StillPowersItsOwnFourNeighbours()
        {
            // §3.1: "orthogonally adjacent to the Boiler OR to a Steam Pipe". This is what makes
            // Phase 1 playable before Iron Plate -- and therefore before any pipe -- exists.
            HashSet<Vector2Int> powered = Powered(C(0, 0), Pipes());

            Assert.AreEqual(4, powered.Count);
            Assert.IsTrue(powered.Contains(C(0, 1)));
            Assert.IsTrue(powered.Contains(C(1, 0)));
            Assert.IsTrue(powered.Contains(C(0, -1)));
            Assert.IsTrue(powered.Contains(C(-1, 0)));
            Assert.IsFalse(powered.Contains(C(1, 1)), "the diagonals are not powered");
        }

        [Test]
        public void CompareCells_IsATotalOrder_ColumnMajor()
        {
            Assert.Less(SteamPipeRules.CompareCells(C(0, 5), C(1, -5)), 0, "x dominates");
            Assert.Greater(SteamPipeRules.CompareCells(C(1, 0), C(0, 99)), 0);
            Assert.Less(SteamPipeRules.CompareCells(C(3, 1), C(3, 2)), 0, "then y");
            Assert.AreEqual(0, SteamPipeRules.CompareCells(C(3, 2), C(3, 2)));
        }
    }
}
