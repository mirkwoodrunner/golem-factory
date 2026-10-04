using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Which picture a belt tile draws, as pure facing math with no BeltSegment and no scene.
    //
    // The shape set is deliberately three, not nine: a belt has one output and the only thing
    // that varies is where it is entered from. These tests pin the whole of that mapping,
    // including the two cases where "no corner" is the right answer rather than a fallback.
    public class BeltShapeRulesTests
    {
        [Test]
        public void EnteredFromBehind_IsStraight()
        {
            Assert.AreEqual(BeltShape.Straight, BeltShapeRules.Resolve(Facing.East, Facing.East));
        }

        [Test]
        public void EnteredFromNowhere_IsStraight()
        {
            // The first belt of a run, and the commonest tile in the game.
            Assert.AreEqual(
                BeltShape.Straight, BeltShapeRules.Resolve(Facing.North, new List<Facing>()));
        }

        [Test]
        public void TravellingSouthAndLeavingEast_TurnsLeft()
        {
            // East is on the left of something facing south.
            Assert.AreEqual(BeltShape.CornerLeft, BeltShapeRules.Resolve(Facing.East, Facing.South));
        }

        [Test]
        public void TravellingNorthAndLeavingEast_TurnsRight()
        {
            Assert.AreEqual(BeltShape.CornerRight, BeltShapeRules.Resolve(Facing.East, Facing.North));
        }

        [Test]
        public void EveryOutputHasBothTurns()
        {
            // The property that makes ONE corner sprite plus a quarter turn enough art for all
            // eight bends in the game: for any output, one entry turns left and the other right.
            for (int i = 0; i < 4; i++)
            {
                var output = (Facing)i;
                Facing left = FacingUtility.RotateClockwise(output);
                Facing right = FacingUtility.RotateCounterClockwise(output);

                Assert.AreEqual(BeltShape.CornerLeft, BeltShapeRules.Resolve(output, left),
                    "output " + output + " entered travelling " + left);
                Assert.AreEqual(BeltShape.CornerRight, BeltShapeRules.Resolve(output, right),
                    "output " + output + " entered travelling " + right);
            }
        }

        [Test]
        public void AMerge_DrawsStraightRatherThanPickingOneCorner()
        {
            // Fed from behind AND from the flank. There is no picture of a merge in the set, and
            // drawing either corner would claim the other feed does not exist.
            var entries = new List<Facing> { Facing.East, Facing.North };
            Assert.AreEqual(BeltShape.Straight, BeltShapeRules.Resolve(Facing.East, entries));
        }

        [Test]
        public void TwoSideFeeds_AlsoDrawStraight()
        {
            var entries = new List<Facing> { Facing.North, Facing.South };
            Assert.AreEqual(BeltShape.Straight, BeltShapeRules.Resolve(Facing.East, entries));
        }

        [Test]
        public void AHeadOnEntry_DrawsStraight()
        {
            // BeltPlacementRules refuses to build this link at all, so it can only reach the
            // shape rules as a caller's mistake -- and a straight lane is the safe drawing.
            Assert.AreEqual(BeltShape.Straight, BeltShapeRules.Resolve(Facing.East, Facing.West));
        }

        [Test]
        public void ANeighbourToTheNorth_EntersTravellingSouth()
        {
            // The step that gets written backwards: a side is where the neighbour SITS, an entry
            // is the direction the item is MOVING.
            Assert.AreEqual(Facing.South, BeltShapeRules.EntryDirectionFromSide(Facing.North));
        }

        [Test]
        public void ANullEntryList_IsStraight()
        {
            Assert.AreEqual(BeltShape.Straight, BeltShapeRules.Resolve(Facing.West, null));
        }
    }
}
