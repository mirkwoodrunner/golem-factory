using NUnit.Framework;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The recycler's tile: two meanings on one cell (fuel and feedstock), and -- unlike a Slag
    /// Heap's -- traffic in both directions.
    /// </summary>
    public class ScrapRecyclerEndpointTests
    {
        private static ScrapRecyclerEndpoint Endpoint(out ScrapRecycler hopper, int coke = 0)
        {
            hopper = new ScrapRecycler("Hopper", coke);
            return new ScrapRecyclerEndpoint(hopper);
        }

        private static ItemStack Unit(string itemType) => new ItemStack { ItemType = itemType };

        [Test]
        public void CokeIsStored_AndEverythingElseIsConsumed()
        {
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler hopper);

            Assert.IsTrue(endpoint.TryGive(Unit(ItemType.Coke)));
            Assert.AreEqual(1, hopper.CokeStock);

            Assert.IsTrue(endpoint.TryGive(Unit(ItemType.Slag)));
            Assert.IsTrue(endpoint.TryGive(Unit(ItemType.Slag)));
            Assert.AreEqual(1, hopper.ScrapStock);
            Assert.AreEqual(0, hopper.CokeStock, "the batch was paid for");
        }

        [Test]
        public void ADryHopperStillTakesCoke_SoThePushThatFixesItIsNotAbandoned()
        {
            // The bug SlagHeapEndpoint.CanGive()'s comment records, and the same one here: an
            // untyped "no" would make Push give up on the whole hold, Coke included.
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler hopper);
            endpoint.TryGive(Unit(ItemType.Slag));

            Assert.IsFalse(endpoint.CanGive(ItemType.Slag), "it cannot pay for another");
            Assert.IsTrue(endpoint.CanGive(ItemType.Coke), "but it can still be refuelled");
            Assert.IsTrue(endpoint.CanGive(), "so the untyped question is still yes");
        }

        [Test]
        public void RecoveredScrapCanBeHauledBackOff()
        {
            // The half a Slag Heap's tile does NOT have. Output that appeared in the stockpile by
            // magic would cost no logistics, and the carrying is the subject of the game.
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler hopper, coke: 10);
            endpoint.TryGive(Unit(ItemType.Casing));

            Assert.AreEqual(1, hopper.ScrapStock);
            Assert.AreEqual(ItemType.Scrap, endpoint.PeekAvailableType());
            Assert.IsTrue(endpoint.TryTake(out ItemStack taken));
            Assert.AreEqual(ItemType.Scrap, taken.ItemType);
            Assert.AreEqual(0, hopper.ScrapStock);
        }

        [Test]
        public void AnEmptyHopperOffersNothing()
        {
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler _);

            Assert.IsNull(endpoint.PeekAvailableType());
            Assert.IsFalse(endpoint.TryTake(out ItemStack _));
            Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 4, out int _));
        }

        [Test]
        public void OnlyScrapCanBeTaken()
        {
            // Fuel that could be withdrawn again would make this an uncapped Coke warehouse the
            // per-item-type cap does not apply to -- the exploit BoilerFuelEndpoint closes.
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler hopper, coke: 10);
            endpoint.TryGive(Unit(ItemType.Slag));
            endpoint.TryGive(Unit(ItemType.Slag));

            Assert.IsFalse(endpoint.TryTake(ItemType.Coke, 1, out int taken));
            Assert.AreEqual(0, taken);
            // 9, not 10: the two Slag completed a batch on the way in and burnt one.
            Assert.AreEqual(9, hopper.CokeStock);
        }

        [Test]
        public void ATypedTakeIsPartial()
        {
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler hopper, coke: 10);
            endpoint.TryGive(Unit(ItemType.Mechanism));

            Assert.AreEqual(2, hopper.ScrapStock, "8 points is two batches");
            Assert.IsTrue(endpoint.TryTake(ItemType.Scrap, 12, out int taken));
            Assert.AreEqual(2, taken);
        }

        [Test]
        public void ARefusedUnitIsNeverSwallowed()
        {
            // The no-item-loss invariant. TryGive returning true on a rejection would make Push
            // consume the unit out of the golem's stock and destroy it.
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler hopper, coke: 500);
            while (endpoint.TryGive(Unit(ItemType.Slag))) { }

            Assert.AreEqual(ScrapRecycler.OutputCapacity, hopper.ScrapStock);
            Assert.IsFalse(endpoint.TryGive(Unit(ItemType.Slag)));
        }

        [Test]
        public void AnUnknownGoodIsRefusedAtTheTileToo()
        {
            ScrapRecyclerEndpoint endpoint = Endpoint(out ScrapRecycler _, coke: 10);

            Assert.IsFalse(endpoint.CanGive("Unobtanium"));
            Assert.IsFalse(endpoint.TryGive(Unit("Unobtanium")));
        }

        [Test]
        public void ANullRecyclerNeverThrows()
        {
            var endpoint = new ScrapRecyclerEndpoint(null);

            Assert.IsFalse(endpoint.CanGive());
            Assert.IsFalse(endpoint.CanGive(ItemType.Coke));
            Assert.IsFalse(endpoint.TryGive(Unit(ItemType.Coke)));
            Assert.IsFalse(endpoint.TryTake(out ItemStack _));
            Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 1, out int _));
            Assert.IsNull(endpoint.PeekAvailableType());
            Assert.AreEqual("Scrap Recycler", endpoint.DisplayName);
        }

        [Test]
        public void ItNamesItselfFromItsId()
        {
            var hopper = new ScrapRecycler("ScrapRecycler(3,7)");
            var endpoint = new ScrapRecyclerEndpoint(hopper);

            Assert.AreEqual("ScrapRecycler(3,7)", endpoint.DisplayName);
        }
    }
}
