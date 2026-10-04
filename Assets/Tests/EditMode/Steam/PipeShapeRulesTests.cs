using NUnit.Framework;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Which of the five pipe pieces a cell draws, and at which quarter turn.
    //
    // The suite walks ALL SIXTEEN neighbour masks, because the whole claim of the five-piece set
    // is that five pictures plus a rotation cover sixteen situations. A mask that resolved to the
    // wrong piece would draw an arm into an empty cell or leave a joined neighbour looking
    // orphaned -- which is exactly the bug the pieces exist to fix, one shape further along.
    public class PipeShapeRulesTests
    {
        private static void Resolve(
            string open, out PipeShape shape, out Facing orientation, Facing isolated = Facing.East)
        {
            PipeShapeRules.Resolve(
                open.Contains("n"), open.Contains("e"), open.Contains("s"), open.Contains("w"),
                isolated, out shape, out orientation);
        }

        [Test]
        public void AnIsolatedPipe_IsAnEndPointingWhereThePlayerTurnedIt()
        {
            // The ONE case the topology cannot answer, and therefore the whole of what R still
            // means while a pipe is in hand.
            PipeShape shape;
            Facing orientation;
            Resolve("", out shape, out orientation, Facing.South);
            Assert.AreEqual(PipeShape.End, shape);
            Assert.AreEqual(Facing.South, orientation);
        }

        [Test]
        public void AStubPointsAtItsOneNeighbour()
        {
            foreach (var pair in new[]
                     {
                         new object[] { "n", Facing.North }, new object[] { "e", Facing.East },
                         new object[] { "s", Facing.South }, new object[] { "w", Facing.West },
                     })
            {
                PipeShape shape;
                Facing orientation;
                // Deliberately a WEST fallback, so a stub that ignored its neighbour and used the
                // isolated facing would fail rather than accidentally agree.
                Resolve((string)pair[0], out shape, out orientation, Facing.West);
                Assert.AreEqual(PipeShape.End, shape, (string)pair[0]);
                Assert.AreEqual((Facing)pair[1], orientation, (string)pair[0]);
            }
        }

        [Test]
        public void TwoOppositeSides_AreAStraightRun()
        {
            PipeShape shape;
            Facing orientation;
            Resolve("ew", out shape, out orientation);
            Assert.AreEqual(PipeShape.Straight, shape);
            Assert.AreEqual(Facing.East, orientation, "authored east-west, so no turn");

            Resolve("ns", out shape, out orientation);
            Assert.AreEqual(PipeShape.Straight, shape);
            Assert.AreEqual(Facing.North, orientation, "a quarter turn onto the vertical");
        }

        [Test]
        public void TheFourElbows_AreOneSpriteAtFourQuarterTurns()
        {
            // Authored as {North, East} pointing East. Rotating the sprite +90 degrees maps each
            // open side one step anticlockwise, which is what these four pairs check.
            foreach (var pair in new[]
                     {
                         new object[] { "ne", Facing.East }, new object[] { "nw", Facing.North },
                         new object[] { "sw", Facing.West }, new object[] { "es", Facing.South },
                     })
            {
                PipeShape shape;
                Facing orientation;
                Resolve((string)pair[0], out shape, out orientation);
                Assert.AreEqual(PipeShape.Corner, shape, (string)pair[0]);
                Assert.AreEqual((Facing)pair[1], orientation, (string)pair[0]);
            }
        }

        [Test]
        public void ATeesStemPointsAwayFromItsGap()
        {
            // Authored as {North, East, South} -- missing West -- pointing East.
            foreach (var pair in new[]
                     {
                         new object[] { "nes", Facing.East }, new object[] { "nsw", Facing.West },
                         new object[] { "new", Facing.North }, new object[] { "esw", Facing.South },
                     })
            {
                PipeShape shape;
                Facing orientation;
                Resolve((string)pair[0], out shape, out orientation);
                Assert.AreEqual(PipeShape.Tee, shape, (string)pair[0]);
                Assert.AreEqual((Facing)pair[1], orientation, (string)pair[0]);
            }
        }

        [Test]
        public void FourWaysOpen_IsTheCross()
        {
            PipeShape shape;
            Facing orientation;
            Resolve("nesw", out shape, out orientation);
            Assert.AreEqual(PipeShape.Cross, shape);
        }

        [Test]
        public void EveryOneOfTheSixteenMasks_ResolvesToAPieceWithTheRightNumberOfArms()
        {
            // The property behind the whole set: a piece's arm count must equal the number of
            // neighbours it was asked about. A tee drawn where a corner belongs is an arm
            // reaching into an empty cell, and that is the whole failure this guards.
            var arms = new System.Collections.Generic.Dictionary<PipeShape, int>
            {
                { PipeShape.End, 1 }, { PipeShape.Straight, 2 }, { PipeShape.Corner, 2 },
                { PipeShape.Tee, 3 }, { PipeShape.Cross, 4 },
            };

            for (int mask = 0; mask < 16; mask++)
            {
                bool n = (mask & 1) != 0, e = (mask & 2) != 0;
                bool s = (mask & 4) != 0, w = (mask & 8) != 0;
                int count = (n ? 1 : 0) + (e ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0);

                PipeShape shape;
                Facing orientation;
                PipeShapeRules.Resolve(n, e, s, w, Facing.East, out shape, out orientation);

                // The empty mask is the documented exception: nothing to join, so the stub the
                // player turned is drawn rather than a piece with zero arms (there is no such
                // piece, and an invisible pipe would be worse than an over-long one).
                Assert.AreEqual(count == 0 ? 1 : count, arms[shape], "mask " + mask);
            }
        }
    }
}
