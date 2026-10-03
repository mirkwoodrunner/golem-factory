using NUnit.Framework;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §5.3(c)'s costed sink: voids Slag at 1 Coke per 4. Disposal competes with the boilers and
    /// the smelters for the scarcest intermediate, which is what makes expanding the Glass line
    /// a reward rather than flavour.
    /// </summary>
    public class SlagHeapTests
    {
        [Test]
        public void FourSlagCostExactlyOneCoke()
        {
            var heap = new SlagHeap("Heap", startingCoke: 1);

            for (int i = 0; i < SlagHeap.SlagPerCoke; i++)
            {
                Assert.IsTrue(heap.TryVoid(), "unit " + i);
            }

            Assert.AreEqual(0, heap.CokeStock);
            Assert.AreEqual(4, heap.TotalVoided);
            Assert.AreEqual(0, heap.PendingSlag);
        }

        [Test]
        public void TheRemainderIsCarried_SoTheRatioIsExactAtEveryScale()
        {
            // Integer accumulator, no floats -- §1.4's boiler discipline. 100 Slag is exactly
            // 25 Coke however it is delivered.
            var heap = new SlagHeap("Heap", startingCoke: 25);

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(heap.TryVoid());
            }

            Assert.AreEqual(0, heap.CokeStock);
            Assert.AreEqual(100, heap.TotalVoided);
        }

        [Test]
        public void TheLastCokeVoidsFourSlag_NotOne()
        {
            // Only the unit that CROSSES the threshold needs fuel in hand; the three before it
            // ride the accumulator.
            var heap = new SlagHeap("Heap", startingCoke: 1);

            Assert.IsTrue(heap.TryVoid());
            Assert.AreEqual(1, heap.CokeStock, "not charged until the fourth");
            Assert.IsTrue(heap.TryVoid());
            Assert.IsTrue(heap.TryVoid());
            Assert.IsTrue(heap.TryVoid());
            Assert.AreEqual(0, heap.CokeStock);
        }

        [Test]
        public void AHeapOutOfCoke_RefusesRatherThanVoidingFree()
        {
            // The refusal IS the mechanic: the Slag backs up, the smelter stalls, and the player
            // is told by the thing stopping that disposal has a running cost they stopped paying.
            var heap = new SlagHeap("Heap", startingCoke: 0);

            Assert.IsTrue(heap.TryVoid(), "the first three ride the accumulator");
            Assert.IsTrue(heap.TryVoid());
            Assert.IsTrue(heap.TryVoid());

            Assert.IsFalse(heap.CanVoid());
            Assert.IsFalse(heap.TryVoid(), "the fourth needs a Coke and there is none");
            Assert.AreEqual(3, heap.TotalVoided);
        }

        [Test]
        public void RefuellingResumesTheLine()
        {
            var heap = new SlagHeap("Heap", startingCoke: 0);
            heap.TryVoid();
            heap.TryVoid();
            heap.TryVoid();
            Assert.IsFalse(heap.TryVoid());

            heap.AddCoke(1);

            Assert.IsTrue(heap.TryVoid());
            Assert.AreEqual(0, heap.CokeStock);
        }

        [Test]
        public void CokeNeededFor_AnswersBeforeTheLineStalls()
        {
            var heap = new SlagHeap("Heap");

            Assert.AreEqual(24, heap.CokeNeededFor(96), "§5.3(c)'s stage-4 figure: 96 Slag/min");
            heap.TryVoid();
            heap.TryVoid();
            heap.TryVoid();
            Assert.AreEqual(1, heap.CokeNeededFor(1), "the carried remainder counts");
        }

        // --- the tile -------------------------------------------------------------------------

        [Test]
        public void TheTileTakesSlagToVoidAndCokeToBurnIt()
        {
            var heap = new SlagHeap("Heap", startingCoke: 1);
            var endpoint = new SlagHeapEndpoint(heap);

            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.Slag }));
            Assert.AreEqual(1, heap.TotalVoided);

            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.Coke }));
            Assert.AreEqual(2, heap.CokeStock);
        }

        [Test]
        public void TheTileRefusesEverythingElse_SoAMixedHoldKeepsIt()
        {
            // The per-type CanGive is what makes BeginPush deliver the Slag and keep the rest,
            // rather than feeding Iron Plate onto a rubbish heap.
            var endpoint = new SlagHeapEndpoint(new SlagHeap("Heap", startingCoke: 4));

            Assert.IsFalse(endpoint.CanGive(ItemType.IronPlate));
            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.IronPlate }));
        }

        [Test]
        public void AHeapOutOfCokeStillAcceptsCoke_WhichIsHowItRecovers()
        {
            // The untyped CanGive stays true so a push carrying the fix is not abandoned at the
            // door -- answering false here would make an empty heap unrefuellable by golem.
            var heap = new SlagHeap("Heap", startingCoke: 0);
            heap.TryVoid();
            heap.TryVoid();
            heap.TryVoid();
            var endpoint = new SlagHeapEndpoint(heap);

            Assert.IsTrue(endpoint.CanGive(), "still reachable");
            Assert.IsFalse(endpoint.CanGive(ItemType.Slag), "but not for more Slag");
            Assert.IsTrue(endpoint.CanGive(ItemType.Coke));
            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.Coke }));
        }

        [Test]
        public void TheHeapNeverGivesAnythingBack()
        {
            // Otherwise it is an uncapped Coke warehouse the §1.2 per-type cap does not apply
            // to -- the same exploit BoilerFuelEndpoint closes.
            var endpoint = new SlagHeapEndpoint(new SlagHeap("Heap", startingCoke: 10));

            Assert.IsFalse(endpoint.TryTake(out _));
            Assert.IsNull(endpoint.PeekAvailableType());
            Assert.IsFalse(endpoint.TryTake(ItemType.Coke, 1, out int taken));
            Assert.AreEqual(0, taken);
        }
    }
}
