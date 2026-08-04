using NUnit.Framework;
using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Auto-chaining rules for player-placed belts, as pure cell/facing math with no
    // BeltSegment and no scene involved.
    public class BeltPlacementRulesTests
    {
        [Test]
        public void ABeltPointingIntoTheNextOne_Links()
        {
            Assert.IsTrue(BeltPlacementRules.ShouldLink(
                new Vector2Int(0, 0), Facing.North, new Vector2Int(0, 1), Facing.North));
        }

        [Test]
        public void ATurn_StillLinks()
        {
            // A run is allowed to bend: the downstream belt only has to not point back.
            Assert.IsTrue(BeltPlacementRules.ShouldLink(
                new Vector2Int(0, 0), Facing.North, new Vector2Int(0, 1), Facing.East));
        }

        [Test]
        public void ABeltNotPointingAtItsNeighbour_DoesNotLink()
        {
            // Adjacent but parallel: two independent lanes running side by side.
            Assert.IsFalse(BeltPlacementRules.ShouldLink(
                new Vector2Int(0, 0), Facing.North, new Vector2Int(1, 0), Facing.North));
        }

        [Test]
        public void HeadOnBelts_DoNotLink()
        {
            // A points at B, B points back at A. Linking these would build a two-cycle that
            // shuffles the same item forever.
            Assert.IsFalse(BeltPlacementRules.ShouldLink(
                new Vector2Int(0, 0), Facing.North, new Vector2Int(0, 1), Facing.South));
        }

        [Test]
        public void ABeltPointingAwayFromItsNeighbour_DoesNotLink()
        {
            Assert.IsFalse(BeltPlacementRules.ShouldLink(
                new Vector2Int(0, 0), Facing.South, new Vector2Int(0, 1), Facing.North));
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
