using NUnit.Framework;
using GolemFactory.Belts;

namespace GolemFactory.Tests.EditMode
{
    public class BeltSegmentTests
    {
        [Test]
        public void TryEnqueue_OnEmptySegment_Succeeds()
        {
            var segment = new BeltSegment("Belt", 5);

            bool result = segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            Assert.IsTrue(result);
            Assert.AreEqual(1, segment.Items.Count);
        }

        [Test]
        public void TryEnqueue_WhenTailItemWithinMinSpacing_Fails()
        {
            var segment = new BeltSegment("Belt", 5);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            bool result = segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            Assert.IsFalse(result);
            Assert.AreEqual(1, segment.Items.Count);
        }

        [Test]
        public void TryEnqueue_WhenAtCapacity_Fails()
        {
            var segment = new BeltSegment("Belt", 2);
            segment.TryEnqueue(new ItemStack { ItemType = "A" });
            segment.Advance(1f);
            segment.TryEnqueue(new ItemStack { ItemType = "B" });
            segment.Advance(1f);
            segment.TryEnqueue(new ItemStack { ItemType = "C" });

            bool result = segment.TryEnqueue(new ItemStack { ItemType = "D" });

            Assert.IsFalse(result);
            Assert.AreEqual(3, segment.Items.Count);
        }

        [Test]
        public void Advance_SingleItem_ProgressesOnePerTick_CappedAtLength()
        {
            var segment = new BeltSegment("Belt", 3);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            segment.Advance(1f);
            Assert.AreEqual(1f, segment.Items[0].Progress);

            segment.Advance(1f);
            segment.Advance(1f);
            segment.Advance(1f);
            Assert.AreEqual(3f, segment.Items[0].Progress);
        }

        [Test]
        public void Advance_TrailingItem_CannotPassOrOverlapLeadingItem()
        {
            var segment = new BeltSegment("Belt", 10);
            segment.TryEnqueue(new ItemStack { ItemType = "Lead" });
            segment.Advance(1f);
            segment.TryEnqueue(new ItemStack { ItemType = "Trail" });

            for (int i = 0; i < 10; i++)
            {
                segment.Advance(1f);
            }

            float leadProgress = segment.Items[0].Progress;
            float trailProgress = segment.Items[1].Progress;
            Assert.LessOrEqual(trailProgress, leadProgress - BeltSegment.MinSpacing);
        }

        [Test]
        public void TryPeekHead_BeforeReachingEnd_ReturnsFalse()
        {
            var segment = new BeltSegment("Belt", 5);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            segment.Advance(1f);

            bool result = segment.TryPeekHead(out _);

            Assert.IsFalse(result);
        }

        [Test]
        public void TryPeekHead_AtEnd_ReturnsTrue()
        {
            var segment = new BeltSegment("Belt", 2);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            segment.Advance(1f);
            segment.Advance(1f);

            bool result = segment.TryPeekHead(out ItemStack head);

            Assert.IsTrue(result);
            Assert.AreEqual("Scrap", head.ItemType);
        }

        [Test]
        public void TryRemoveHead_AtEnd_RemovesItem()
        {
            var segment = new BeltSegment("Belt", 1);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });
            segment.Advance(1f);

            bool result = segment.TryRemoveHead(out ItemStack head);

            Assert.IsTrue(result);
            Assert.AreEqual("Scrap", head.ItemType);
            Assert.AreEqual(0, segment.Items.Count);
        }

        [Test]
        public void TryRemoveHead_BeforeEnd_Fails()
        {
            var segment = new BeltSegment("Belt", 5);
            segment.TryEnqueue(new ItemStack { ItemType = "Scrap" });

            bool result = segment.TryRemoveHead(out _);

            Assert.IsFalse(result);
            Assert.AreEqual(1, segment.Items.Count);
        }
    }
}
