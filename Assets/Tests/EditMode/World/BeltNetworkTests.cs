using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // The player-placeable belt layer: a placed belt has to become a real ticking lane, a
    // spatial endpoint on its cell, and a link in whatever run it joins -- and has to undo all
    // three cleanly when pulled up.
    public class BeltNetworkTests
    {
        private ConveyorSystem _conveyor;
        private SpatialEndpointRegistry _endpoints;
        private BeltNetwork _network;

        [SetUp]
        public void SetUp()
        {
            _conveyor = new ConveyorSystem();
            _endpoints = new SpatialEndpointRegistry();
            _network = new BeltNetwork();
            _network.Configure(_conveyor, _endpoints, 4);
        }

        private PlacedBelt Place(int x, int y, Facing facing)
        {
            PlacedBelt placed;
            Assert.IsTrue(_network.TryPlace(new Vector2Int(x, y), facing, out placed),
                $"failed to place belt at ({x},{y})");
            return placed;
        }

        [Test]
        public void APlacedBelt_RegistersASpatialEndpointOnItsOwnCell()
        {
            PlacedBelt placed = Place(2, 3, Facing.North);

            IItemEndpoint endpoint;
            Assert.IsTrue(_endpoints.TryGetEndpoint(new Vector2Int(2, 3), out endpoint),
                "a placed belt is invisible to facing-based routing");
            Assert.IsInstanceOf<BeltSegmentEndpoint>(endpoint);
            Assert.AreSame(placed.Segment, ((BeltSegmentEndpoint)endpoint).Segment);
        }

        [Test]
        public void APlacedBelt_IsRegisteredWithTheConveyorSystemSoItTicks()
        {
            PlacedBelt placed = Place(0, 0, Facing.North);

            BeltSegment found;
            Assert.IsTrue(_conveyor.TryGetSegment(placed.Segment.SegmentId, out found));
            Assert.AreSame(placed.Segment, found);
        }

        [Test]
        public void PlacingOnAnOccupiedCell_Fails()
        {
            Place(1, 1, Facing.North);

            PlacedBelt second;
            Assert.IsFalse(_network.TryPlace(new Vector2Int(1, 1), Facing.East, out second),
                "replacing a belt in place would strand the old segment's items");
            Assert.IsNull(second);
            Assert.AreEqual(1, _network.Count);
        }

        [Test]
        public void BeltsLaidInARun_AutoChainIntoOneLane()
        {
            PlacedBelt a = Place(0, 0, Facing.North);
            PlacedBelt b = Place(0, 1, Facing.North);
            PlacedBelt c = Place(0, 2, Facing.North);

            Assert.AreSame(b.Segment, a.Segment.Next);
            Assert.AreSame(c.Segment, b.Segment.Next);
            Assert.IsNull(c.Segment.Next, "the last belt in a run should dead-end");
        }

        [Test]
        public void ARunLaidBackwards_StillChainsCorrectly()
        {
            // Order of placement must not matter: Relink recomputes the whole network.
            PlacedBelt c = Place(0, 2, Facing.North);
            PlacedBelt b = Place(0, 1, Facing.North);
            PlacedBelt a = Place(0, 0, Facing.North);

            Assert.AreSame(b.Segment, a.Segment.Next);
            Assert.AreSame(c.Segment, b.Segment.Next);
        }

        [Test]
        public void ABeltPlacedBesideARun_DoesNotJoinIt()
        {
            PlacedBelt a = Place(0, 0, Facing.North);
            PlacedBelt parallel = Place(1, 0, Facing.North);

            Assert.IsNull(a.Segment.Next);
            Assert.IsNull(parallel.Segment.Next);
        }

        [Test]
        public void HeadOnBelts_AreNotLinkedIntoATwoCycle()
        {
            PlacedBelt a = Place(0, 0, Facing.North);
            PlacedBelt b = Place(0, 1, Facing.South);

            Assert.IsNull(a.Segment.Next);
            Assert.IsNull(b.Segment.Next);
        }

        [Test]
        public void RemovingABelt_UnlinksTheBeltUpstreamOfIt()
        {
            // The load-bearing removal case. A stale Next is worse than a dead end:
            // ConveyorSystem.Tick would keep handing items to a segment that is no longer
            // registered, so it never advances and never hands on -- items vanish into a lane
            // the player cannot see.
            PlacedBelt a = Place(0, 0, Facing.North);
            PlacedBelt b = Place(0, 1, Facing.North);
            Assert.AreSame(b.Segment, a.Segment.Next, "precondition: they should be chained");

            Assert.IsTrue(_network.TryRemove(new Vector2Int(0, 1)));

            Assert.IsNull(a.Segment.Next, "the upstream belt still points at the removed segment");
            Assert.IsFalse(_endpoints.HasEndpoint(new Vector2Int(0, 1)));
            Assert.IsFalse(_conveyor.TryGetSegment(b.Segment.SegmentId, out _));
        }

        [Test]
        public void RemovingTheMiddleOfARun_LeavesTwoIndependentStubs()
        {
            PlacedBelt a = Place(0, 0, Facing.North);
            Place(0, 1, Facing.North);
            PlacedBelt c = Place(0, 2, Facing.North);

            Assert.IsTrue(_network.TryRemove(new Vector2Int(0, 1)));

            Assert.IsNull(a.Segment.Next);
            Assert.IsNull(c.Segment.Next);
            Assert.AreEqual(2, _network.Count);
        }

        [Test]
        public void ReplacingARemovedBelt_RechainsTheRun()
        {
            PlacedBelt a = Place(0, 0, Facing.North);
            Place(0, 1, Facing.North);
            _network.TryRemove(new Vector2Int(0, 1));

            PlacedBelt replacement = Place(0, 1, Facing.North);

            Assert.AreSame(replacement.Segment, a.Segment.Next);
        }

        [Test]
        public void RemovingANonExistentBelt_ReportsFailure()
        {
            Assert.IsFalse(_network.TryRemove(new Vector2Int(7, 7)));
        }

        [Test]
        public void RemovingABelt_LeavesAnEndpointSomethingElseHasSinceClaimed()
        {
            // The endpoint on that cell is no longer this belt's, so tearing the belt up must
            // not silently delete the replacement.
            PlacedBelt placed = Place(4, 4, Facing.North);
            var other = new GolemFactory.Economy.StorageBuffer("Chest");
            _endpoints.Register(new Vector2Int(4, 4), new StorageBufferEndpoint(other));

            Assert.IsTrue(_network.TryRemove(new Vector2Int(4, 4)));

            IItemEndpoint endpoint;
            Assert.IsTrue(_endpoints.TryGetEndpoint(new Vector2Int(4, 4), out endpoint));
            Assert.IsInstanceOf<StorageBufferEndpoint>(endpoint);
            Assert.IsFalse(_conveyor.TryGetSegment(placed.Segment.SegmentId, out _),
                "the belt's own segment should still have been unregistered");
        }

        [Test]
        public void ItemsFlowAlongAPlacedRun_WhenTheConveyorTicks()
        {
            // End to end, through the real ConveyorSystem: the whole point of auto-chaining is
            // that a run of separately placed belts behaves as one lane.
            PlacedBelt a = Place(0, 0, Facing.North);
            PlacedBelt b = Place(0, 1, Facing.North);

            Assert.IsTrue(a.Segment.TryEnqueue(new GolemFactory.Belts.ItemStack
            {
                ItemType = GolemFactory.Economy.ItemType.Scrap
            }));

            for (int tick = 0; tick < 12 && b.Segment.Items.Count == 0; tick++)
            {
                _conveyor.Tick(tick);
            }

            Assert.AreEqual(0, a.Segment.Items.Count, "the item never left the first belt");
            Assert.AreEqual(1, b.Segment.Items.Count, "the item never arrived on the second belt");
        }

        [Test]
        public void ANetworkWithNoCollaborators_StillPlacesWithoutThrowing()
        {
            // A BeltNetwork that was never Configured (or was given nulls) must degrade
            // quietly, matching how every registry in this project returns false rather than
            // throwing on unset wiring.
            var bare = new BeltNetwork();

            PlacedBelt placed;
            Assert.IsTrue(bare.TryPlace(new Vector2Int(0, 0), Facing.North, out placed));
            Assert.IsNotNull(placed.Segment);
            Assert.IsTrue(bare.TryRemove(new Vector2Int(0, 0)));
        }
    }
}
