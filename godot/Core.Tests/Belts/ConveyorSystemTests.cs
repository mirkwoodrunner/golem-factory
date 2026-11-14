using NUnit.Framework;
using GolemFactory.Belts;

namespace GolemFactory.Tests.EditMode
{
    public class ConveyorSystemTests
    {
        [Test]
        public void TryEnqueue_UnknownSegmentId_ReturnsFalse()
        {
            var system = new ConveyorSystem();

            bool result = system.TryEnqueue("Missing", new ItemStack { ItemType = "Scrap" });

            Assert.IsFalse(result);
        }

        [Test]
        public void Tick_AdvancesAllRegisteredSegments()
        {
            var system = new ConveyorSystem();
            var segment = new BeltSegment("Belt", 5);
            system.Register(segment);
            system.TryEnqueue("Belt", new ItemStack { ItemType = "Scrap" });

            system.Tick(1);

            Assert.AreEqual(1f, segment.Items[0].Progress);
        }

        [Test]
        public void Tick_HandsOffHeadItemToNextSegment_WhenNextHasRoom()
        {
            var system = new ConveyorSystem();
            var beltA = new BeltSegment("BeltA", 1);
            var beltB = new BeltSegment("BeltB", 5);
            beltA.Next = beltB;
            system.Register(beltA);
            system.Register(beltB);
            system.TryEnqueue("BeltA", new ItemStack { ItemType = "Scrap" });

            system.Tick(1);

            Assert.AreEqual(0, beltA.Items.Count);
            Assert.AreEqual(1, beltB.Items.Count);
            Assert.AreEqual(0f, beltB.Items[0].Progress);
            Assert.AreEqual("Scrap", beltB.Items[0].ItemType);
        }

        [Test]
        public void Tick_DoesNotDoubleAdvanceHandedOffItem_InSameTick()
        {
            var system = new ConveyorSystem();
            var beltA = new BeltSegment("BeltA", 1);
            var beltB = new BeltSegment("BeltB", 5);
            beltA.Next = beltB;
            system.Register(beltA);
            system.Register(beltB);
            system.TryEnqueue("BeltA", new ItemStack { ItemType = "Scrap" });

            system.Tick(1);

            // If the handed-off item were advanced again in the same tick it would read 1f
            // instead of the fresh 0f it was enqueued onto BeltB with.
            Assert.AreEqual(0f, beltB.Items[0].Progress);
        }

        [Test]
        public void Tick_NextSegmentFull_BlocksHandoff_HeadStaysParkedAtLength()
        {
            var system = new ConveyorSystem();
            var beltA = new BeltSegment("BeltA", 1);
            var beltB = new BeltSegment("BeltB", 1);
            beltA.Next = beltB;
            system.Register(beltA);
            system.Register(beltB);
            system.TryEnqueue("BeltA", new ItemStack { ItemType = "Scrap" });
            // Fill BeltB to its capacity (2, for Length 1) so it rejects the handoff outright.
            beltB.TryEnqueue(new ItemStack { ItemType = "Blocker1" });
            beltB.Advance(1f);
            beltB.TryEnqueue(new ItemStack { ItemType = "Blocker2" });

            system.Tick(1);

            Assert.AreEqual(1, beltA.Items.Count);
            Assert.AreEqual(1f, beltA.Items[0].Progress);
            Assert.AreEqual(2, beltB.Items.Count);
        }

        [Test]
        public void TryDequeueHead_OnlySucceedsWhenHeadReachedEnd()
        {
            var system = new ConveyorSystem();
            var segment = new BeltSegment("Belt", 2);
            system.Register(segment);
            system.TryEnqueue("Belt", new ItemStack { ItemType = "Scrap" });

            Assert.IsFalse(system.TryDequeueHead("Belt", out _));

            system.Tick(1);
            system.Tick(2);

            Assert.IsTrue(system.TryDequeueHead("Belt", out ItemStack item));
            Assert.AreEqual("Scrap", item.ItemType);
        }

        /// <summary>
        /// Where two belts feed one, the belt registered first hands off first, whatever was
        /// pulled up and relaid in between. A dictionary gave a removed belt's slot to the
        /// next one laid, so the newest belt took the oldest belt's turn, and the same factory
        /// loaded from a save (laid again in order) gave the free slot to the other belt.
        /// </summary>
        [Test]
        public void Tick_WhereTwoBeltsFeedOne_TheOlderHandsOffFirst_EvenAfterARemoval()
        {
            var system = new ConveyorSystem();
            var pulledUp = new BeltSegment("PulledUp", 1);
            var older = new BeltSegment("Older", 1);
            var merge = new BeltSegment("Merge", 5);
            system.Register(pulledUp);
            system.Register(older);
            system.Register(merge);
            system.Unregister("PulledUp");
            var newer = new BeltSegment("Newer", 1);
            system.Register(newer);
            older.Next = merge;
            newer.Next = merge;
            system.TryEnqueue("Older", new ItemStack { ItemType = "Scrap" });
            system.TryEnqueue("Newer", new ItemStack { ItemType = "Coal" });

            system.Tick(1); // both heads reach the end; the merge has room for one

            Assert.AreEqual(1, merge.Items.Count);
            Assert.AreEqual("Scrap", merge.Items[0].ItemType, "the older belt's item went first");
            Assert.AreEqual(1, newer.Items.Count, "the newer belt's item waits its turn");
        }

        [Test]
        public void Register_ASecondSegmentUnderOneId_TakesTheFirstOnesPlace()
        {
            var system = new ConveyorSystem();
            var first = new BeltSegment("Belt", 3);
            var second = new BeltSegment("Belt", 3);
            system.Register(first);
            system.Register(second);
            second.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            first.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            system.Tick(1);

            Assert.AreEqual(1f, second.Items[0].Progress, "the registered segment is ticked");
            Assert.AreEqual(0f, first.Items[0].Progress, "the replaced one is not");
        }
    }
}
