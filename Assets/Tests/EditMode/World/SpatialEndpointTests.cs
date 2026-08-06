using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class SpatialEndpointRegistryTests
    {
        [Test]
        public void UnregisteredCell_ReturnsFalseRatherThanThrowing()
        {
            var registry = new SpatialEndpointRegistry();

            IItemEndpoint endpoint;
            Assert.IsFalse(registry.TryGetEndpoint(new Vector2Int(4, 4), out endpoint));
            Assert.IsNull(endpoint);
            Assert.IsFalse(registry.HasEndpoint(new Vector2Int(4, 4)));
        }

        [Test]
        public void RegisteredEndpoint_IsFoundAtItsCell()
        {
            var registry = new SpatialEndpointRegistry();
            var node = new ResourceNodeEndpoint(new ResourceNode("ScrapNode", ItemType.Scrap));
            registry.Register(new Vector2Int(2, -3), node);

            IItemEndpoint found;
            Assert.IsTrue(registry.TryGetEndpoint(new Vector2Int(2, -3), out found));
            Assert.AreSame(node, found);
        }

        [Test]
        public void NegativeAndPositiveCells_DoNotCollide()
        {
            var registry = new SpatialEndpointRegistry();
            var a = new ResourceNodeEndpoint(new ResourceNode("A", ItemType.Scrap));
            var b = new ResourceNodeEndpoint(new ResourceNode("B", ItemType.Brass));
            registry.Register(new Vector2Int(1, -1), a);
            registry.Register(new Vector2Int(-1, 1), b);

            IItemEndpoint found;
            Assert.IsTrue(registry.TryGetEndpoint(new Vector2Int(1, -1), out found));
            Assert.AreSame(a, found);
            Assert.IsTrue(registry.TryGetEndpoint(new Vector2Int(-1, 1), out found));
            Assert.AreSame(b, found);
            Assert.AreEqual(2, registry.Count);
        }

        [Test]
        public void RegisteringNull_ClearsTheCellInsteadOfStoringAHole()
        {
            // A null endpoint would make a cell report occupied while yielding nothing, which
            // reads to the player as "this tile is broken".
            var registry = new SpatialEndpointRegistry();
            registry.Register(Vector2Int.zero, new ResourceNodeEndpoint(new ResourceNode("N", ItemType.Scrap)));
            registry.Register(Vector2Int.zero, null);

            Assert.IsFalse(registry.HasEndpoint(Vector2Int.zero));
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void RegisteringTwice_ReplacesRatherThanDuplicates()
        {
            var registry = new SpatialEndpointRegistry();
            var second = new ResourceNodeEndpoint(new ResourceNode("Second", ItemType.Brass));
            registry.Register(Vector2Int.one, new ResourceNodeEndpoint(new ResourceNode("First", ItemType.Scrap)));
            registry.Register(Vector2Int.one, second);

            IItemEndpoint found;
            Assert.IsTrue(registry.TryGetEndpoint(Vector2Int.one, out found));
            Assert.AreSame(second, found);
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void Unregister_RemovesTheEndpoint()
        {
            var registry = new SpatialEndpointRegistry();
            registry.Register(Vector2Int.zero, new ResourceNodeEndpoint(new ResourceNode("N", ItemType.Scrap)));
            registry.Unregister(Vector2Int.zero);

            Assert.IsFalse(registry.HasEndpoint(Vector2Int.zero));
        }

        [Test]
        public void UnregisteringAnEmptyCell_IsHarmless()
        {
            var registry = new SpatialEndpointRegistry();
            Assert.DoesNotThrow(() => registry.Unregister(new Vector2Int(9, 9)));
        }
    }

    public class ItemEndpointAdapterTests
    {
        // --- ResourceNode: take only ---------------------------------------------------

        [Test]
        public void NodeEndpoint_TakesAndDepletes()
        {
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 2);
            var endpoint = new ResourceNodeEndpoint(node);

            ItemStack item;
            Assert.IsTrue(endpoint.TryTake(out item));
            Assert.AreEqual(ItemType.Scrap, item.ItemType);
            Assert.AreEqual(1, node.RemainingQuantity);

            Assert.IsTrue(endpoint.TryTake(out item));
            Assert.IsFalse(endpoint.TryTake(out item), "a depleted node kept yielding items");
        }

        [Test]
        public void NodeEndpoint_NeverAccepts()
        {
            var endpoint = new ResourceNodeEndpoint(new ResourceNode("ScrapNode", ItemType.Scrap));

            Assert.IsFalse(endpoint.CanGive(), "you cannot push an item back into the ground");
            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));
        }

        [Test]
        public void NodeEndpoint_DisplayNameIsTheNodeId()
        {
            Assert.AreEqual("ScrapNode",
                new ResourceNodeEndpoint(new ResourceNode("ScrapNode", ItemType.Scrap)).DisplayName);
        }

        // --- BeltSegment: take from head, give at tail -----------------------------------

        [Test]
        public void BeltEndpoint_GivesAtTheTailAndTakesFromTheHead()
        {
            var segment = new BeltSegment("ScrapBeltA", 2);
            var endpoint = new BeltSegmentEndpoint(segment);

            Assert.IsTrue(endpoint.CanGive());
            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));

            ItemStack item;
            Assert.IsFalse(endpoint.TryTake(out item),
                "an item should not be takeable before it has travelled the segment");

            segment.Advance(segment.Length);
            Assert.IsTrue(endpoint.TryTake(out item));
            Assert.AreEqual(ItemType.Scrap, item.ItemType);
        }

        [Test]
        public void BeltEndpoint_CanGiveGoesFalseWhenFull()
        {
            var segment = new BeltSegment("ScrapBeltA", 1);
            var endpoint = new BeltSegmentEndpoint(segment);

            while (endpoint.CanGive())
            {
                Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));
            }

            Assert.IsFalse(endpoint.CanGive());
            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));
        }

        // --- StorageBuffer: both directions ----------------------------------------------

        [Test]
        public void BufferEndpoint_RoundTripsAnItem()
        {
            var buffer = new StorageBuffer("ScrapBuffer");
            var endpoint = new StorageBufferEndpoint(buffer);

            Assert.IsTrue(endpoint.CanGive());
            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));
            Assert.AreEqual(1, buffer.GetQuantity(ItemType.Scrap));

            ItemStack item;
            Assert.IsTrue(endpoint.TryTake(out item));
            Assert.AreEqual(ItemType.Scrap, item.ItemType);
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void BufferEndpoint_EmptyBufferGivesNothing()
        {
            var endpoint = new StorageBufferEndpoint(new StorageBuffer("Empty"));

            ItemStack item;
            Assert.IsFalse(endpoint.TryTake(out item));
        }

        [Test]
        public void BufferEndpoint_PrefersTheRequestedItemType()
        {
            var buffer = new StorageBuffer("Mixed");
            buffer.Deposit(ItemType.Scrap);
            buffer.Deposit(ItemType.Brass);
            var endpoint = new StorageBufferEndpoint(buffer) { PreferredItemType = ItemType.Brass };

            ItemStack item;
            Assert.IsTrue(endpoint.TryTake(out item));
            Assert.AreEqual(ItemType.Brass, item.ItemType);
            Assert.AreEqual(1, buffer.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void BufferEndpoint_FallsBackToWhateverIsStockedWhenThePreferenceIsAbsent()
        {
            var buffer = new StorageBuffer("Mixed");
            buffer.Deposit(ItemType.Scrap);
            var endpoint = new StorageBufferEndpoint(buffer) { PreferredItemType = ItemType.Brass };

            ItemStack item;
            Assert.IsTrue(endpoint.TryTake(out item));
            Assert.AreEqual(ItemType.Scrap, item.ItemType);
        }

        [Test]
        public void BufferEndpoint_DoesNotDispenseAZeroQuantityLeftover()
        {
            // TryWithdraw leaves a 0-valued entry behind; taking again must not invent an item.
            var buffer = new StorageBuffer("Drained");
            buffer.Deposit(ItemType.Scrap);
            buffer.TryWithdraw(ItemType.Scrap);

            var endpoint = new StorageBufferEndpoint(buffer);
            ItemStack item;
            Assert.IsFalse(endpoint.TryTake(out item));
        }
    }

    // The typed, quantified take progression-design section 2 requires so a golem's Haul can
    // name what it is pulling. Partial takes are the normal case, not an error case.
    public class ItemEndpointTypedTakeTests
    {
        // --- ResourceNode ------------------------------------------------------------------

        [Test]
        public void NodeEndpoint_PeekNamesItsTypeUntilItIsDepleted()
        {
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 1);
            var endpoint = new ResourceNodeEndpoint(node);

            Assert.AreEqual(ItemType.Scrap, endpoint.PeekAvailableType());
            Assert.IsTrue(endpoint.TryTake(out ItemStack _));
            Assert.IsNull(endpoint.PeekAvailableType(), "a spent node kept advertising its type");
        }

        [Test]
        public void NodeEndpoint_TypedTakeRefusesAnotherType()
        {
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 5);
            var endpoint = new ResourceNodeEndpoint(node);

            Assert.IsFalse(endpoint.TryTake(ItemType.Brass, 2, out int taken));
            Assert.AreEqual(0, taken);
            Assert.AreEqual(5, node.RemainingQuantity, "a mismatched take drained the node anyway");
        }

        [Test]
        public void NodeEndpoint_TypedTakeStopsAtDepletionRatherThanFailingOutright()
        {
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 3);
            var endpoint = new ResourceNodeEndpoint(node);

            Assert.IsTrue(endpoint.TryTake(ItemType.Scrap, 8, out int taken));
            Assert.AreEqual(3, taken, "a partial take should yield what the node had");
            Assert.IsTrue(node.IsDepleted);
        }

        [Test]
        public void NodeEndpoint_TypedTakeFromAnInfiniteNode_YieldsExactlyWhatWasAsked()
        {
            var endpoint = new ResourceNodeEndpoint(
                new ResourceNode("ScrapNode", ItemType.Scrap, ResourceNode.Infinite));

            Assert.IsTrue(endpoint.TryTake(ItemType.Scrap, 4, out int taken));
            Assert.AreEqual(4, taken, "an infinite node must not over- or under-deliver");
        }

        // --- BeltSegment -------------------------------------------------------------------

        [Test]
        public void BeltEndpoint_PeekRespectsTheHasTravelledTheFullLengthRule()
        {
            // The same condition TryPeekHead/TryRemoveHead use. Reporting an in-transit item as
            // available would make a Haul appear to stall for no reason one tick and work the
            // next -- see BeltSegment.TryPeekHead.
            var segment = new BeltSegment("ScrapBeltA", 3);
            var endpoint = new BeltSegmentEndpoint(segment);
            segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });

            Assert.IsNull(endpoint.PeekAvailableType(), "an item still in transit was offered");

            segment.Advance(segment.Length);
            Assert.AreEqual(ItemType.Scrap, endpoint.PeekAvailableType());
        }

        [Test]
        public void BeltEndpoint_TypedTakeWillNotPullAnItemThatHasNotArrived()
        {
            var segment = new BeltSegment("ScrapBeltA", 3);
            var endpoint = new BeltSegmentEndpoint(segment);
            segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });

            Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 1, out int taken));
            Assert.AreEqual(0, taken);
            Assert.AreEqual(1, segment.Items.Count);
        }

        [Test]
        public void BeltEndpoint_TypedTakeStopsAtTheFirstHeadOfAnotherType()
        {
            // A rigid golem may not reach past a wrong-typed head to find a matching one --
            // a mixed belt is a player-visible problem, not something to reorder around.
            var segment = new BeltSegment("Mixed", 1);
            var endpoint = new BeltSegmentEndpoint(segment);
            segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            segment.Advance(segment.Length);
            segment.TryEnqueue(new ItemStack { ItemType = ItemType.Brass });
            segment.Advance(segment.Length);

            Assert.IsTrue(endpoint.TryTake(ItemType.Scrap, 4, out int taken));
            Assert.AreEqual(1, taken);
            Assert.AreEqual(1, segment.Items.Count, "it reached past the Brass");
            Assert.AreEqual(ItemType.Brass, segment.Items[0].ItemType);
        }

        [Test]
        public void BeltEndpoint_TypedTakeOfTheWrongType_TakesNothing()
        {
            var segment = new BeltSegment("ScrapBeltA", 1);
            var endpoint = new BeltSegmentEndpoint(segment);
            segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            segment.Advance(segment.Length);

            Assert.IsFalse(endpoint.TryTake(ItemType.Aether, 1, out int taken));
            Assert.AreEqual(0, taken);
            Assert.AreEqual(1, segment.Items.Count);
        }

        // --- StorageBuffer -----------------------------------------------------------------

        [Test]
        public void BufferEndpoint_PeekPrefersThePreferredTypeThenFallsBack()
        {
            var buffer = new StorageBuffer("Mixed");
            buffer.Deposit(ItemType.Scrap);
            var endpoint = new StorageBufferEndpoint(buffer) { PreferredItemType = ItemType.Brass };

            Assert.AreEqual(ItemType.Scrap, endpoint.PeekAvailableType());

            buffer.Deposit(ItemType.Brass);
            Assert.AreEqual(ItemType.Brass, endpoint.PeekAvailableType());
        }

        [Test]
        public void BufferEndpoint_PeekOnAnEmptyBufferIsNull()
        {
            Assert.IsNull(new StorageBufferEndpoint(new StorageBuffer("Empty")).PeekAvailableType());
        }

        [Test]
        public void BufferEndpoint_TypedTakeClampsToWhatIsHeld()
        {
            var buffer = new StorageBuffer("Mixed");
            buffer.Deposit(ItemType.Scrap, 3);
            buffer.Deposit(ItemType.Brass, 7);
            var endpoint = new StorageBufferEndpoint(buffer);

            Assert.IsTrue(endpoint.TryTake(ItemType.Scrap, 10, out int taken));
            Assert.AreEqual(3, taken);
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(7, buffer.GetQuantity(ItemType.Brass), "it took a type it was not asked for");
        }

        [Test]
        public void BufferEndpoint_TypedTakeOfAnAbsentType_TakesNothing()
        {
            var buffer = new StorageBuffer("Mixed");
            buffer.Deposit(ItemType.Brass, 4);
            var endpoint = new StorageBufferEndpoint(buffer);

            Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 2, out int taken));
            Assert.AreEqual(0, taken);
            Assert.AreEqual(4, buffer.GetQuantity(ItemType.Brass));
        }

        // --- Shared contract ---------------------------------------------------------------

        [Test]
        public void EveryEndpoint_TreatsANullTypeOrNonPositiveQuantityAsARefusal()
        {
            var buffer = new StorageBuffer("B");
            buffer.Deposit(ItemType.Scrap, 5);
            var segment = new BeltSegment("S", 1);
            segment.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            segment.Advance(segment.Length);

            IItemEndpoint[] endpoints =
            {
                new ResourceNodeEndpoint(new ResourceNode("N", ItemType.Scrap, 5)),
                new BeltSegmentEndpoint(segment),
                new StorageBufferEndpoint(buffer)
            };

            foreach (IItemEndpoint endpoint in endpoints)
            {
                Assert.IsFalse(endpoint.TryTake(null, 1, out int taken), endpoint.DisplayName);
                Assert.AreEqual(0, taken);
                Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 0, out taken), endpoint.DisplayName);
                Assert.AreEqual(0, taken);
            }
        }
    }
}
