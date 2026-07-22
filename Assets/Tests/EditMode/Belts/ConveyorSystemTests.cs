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
    }
}
