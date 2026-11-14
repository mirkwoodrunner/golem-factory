using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Auto-chaining rules for player-placed belts, as pure cell/facing math with no
    // BeltSegment and no scene involved.
    public class BeltPlacementRulesTests
    {
        // Belt A sits at the origin; each case puts B beside it and asks whether A feeds B.
        [TestCase(Facing.North, 0, 1, Facing.North, true, TestName = "ABeltPointingIntoTheNextOne_Links")]
        // A run is allowed to bend: the downstream belt only has to not point back.
        [TestCase(Facing.North, 0, 1, Facing.East, true, TestName = "ATurn_StillLinks")]
        // Adjacent but parallel: two independent lanes running side by side.
        [TestCase(Facing.North, 1, 0, Facing.North, false, TestName = "ABeltNotPointingAtItsNeighbour_DoesNotLink")]
        // A points at B, B points back at A. Linking these would build a two-cycle that
        // shuffles the same item forever.
        [TestCase(Facing.North, 0, 1, Facing.South, false, TestName = "HeadOnBelts_DoNotLink")]
        [TestCase(Facing.South, 0, 1, Facing.North, false, TestName = "ABeltPointingAwayFromItsNeighbour_DoesNotLink")]
        public void ShouldLink_ABeltFeedsOnlyTheNeighbourItPointsInto(
            Facing facing, int neighbourX, int neighbourY, Facing neighbourFacing, bool expected)
        {
            Assert.AreEqual(expected, BeltPlacementRules.ShouldLink(
                new Vector2Int(0, 0), facing, new Vector2Int(neighbourX, neighbourY), neighbourFacing));
        }

        [Test]
        public void LinkingIsDirectional_NotSymmetric()
        {
            // A -> B where B runs crosswise: A feeds B, but B does not feed A.
            Vector2Int a = new Vector2Int(0, 0);
            Vector2Int b = new Vector2Int(0, 1);
            Assert.IsTrue(BeltPlacementRules.ShouldLink(a, Facing.North, b, Facing.East));
            Assert.IsFalse(BeltPlacementRules.ShouldLink(b, Facing.East, a, Facing.North));
        }
    }
}
