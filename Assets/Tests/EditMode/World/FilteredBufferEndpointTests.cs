using NUnit.Framework;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// A labelled crate (docs/cozy-automation-design.md §1): it accepts only its good on the way
    /// in and dispenses only its good on the way out.
    /// </summary>
    public class FilteredBufferEndpointTests
    {
        private static StorageBuffer Stocked(params (string type, int qty)[] contents)
        {
            var buffer = new StorageBuffer("FactoryStockpile");
            foreach ((string type, int qty) in contents)
            {
                buffer.Deposit(type, qty);
            }

            return buffer;
        }

        [Test]
        public void ItTakesItsOwnGood()
        {
            var buffer = Stocked();
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.IronPlate);

            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.IronPlate }));
            Assert.AreEqual(1, buffer.GetQuantity(ItemType.IronPlate));
        }

        [Test]
        public void ItRefusesEverythingElse()
        {
            var buffer = Stocked();
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.IronPlate);

            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.Slag }));
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Slag), "the refused unit must not land");
            Assert.IsFalse(endpoint.CanGive(ItemType.Slag));
        }

        [Test]
        public void ItDispensesOnlyItsOwnGood_EvenThoughTheBufferIsShared()
        {
            // THE HALF THAT WAS ACTUALLY BROKEN. Every depot publishes the same FactoryStockpile,
            // and StorageBufferEndpoint's untyped take hands over whichever type the dictionary
            // enumerates first. A label is what makes two lines off one stockpile possible.
            var buffer = Stocked((ItemType.Scrap, 40), (ItemType.Coke, 10));
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.Coke);

            Assert.AreEqual(ItemType.Coke, endpoint.PeekAvailableType());
            Assert.IsTrue(endpoint.TryTake(out ItemStack taken));
            Assert.AreEqual(ItemType.Coke, taken.ItemType);
            Assert.AreEqual(40, buffer.GetQuantity(ItemType.Scrap), "the Scrap must be untouched");
        }

        [Test]
        public void ATypedTakeForTheWrongGoodIsRefused()
        {
            var buffer = Stocked((ItemType.Scrap, 40), (ItemType.Coke, 10));
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.Coke);

            Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 5, out int taken));
            Assert.AreEqual(0, taken);
            Assert.AreEqual(40, buffer.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void ATypedTakeIsPartial_LikeEveryOtherEndpoint()
        {
            var buffer = Stocked((ItemType.Coke, 3));
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.Coke);

            Assert.IsTrue(endpoint.TryTake(ItemType.Coke, 8, out int taken));
            Assert.AreEqual(3, taken, "a Haul against a short crate takes what is there");
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Coke));
        }

        [Test]
        public void AnEmptyCrateOffersNothing()
        {
            var buffer = Stocked((ItemType.Scrap, 40));
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.Coke);

            Assert.IsNull(endpoint.PeekAvailableType(), "a crate labelled Coke holding none is empty");
            Assert.IsFalse(endpoint.TryTake(out ItemStack _));
        }

        [Test]
        public void TheUntypedCanGiveTracksOnlyItsOwnGoodsRoom()
        {
            // Deliberately DIFFERENT from StorageBufferEndpoint, whose untyped CanGive is always
            // true because it can always accept *something*. A labelled crate has one type by
            // construction, so answering false when that type is full is honest -- and it is not
            // §10's deadlock, which is about one type blocking a DIFFERENT type.
            var buffer = new StorageBuffer("Capped", capacityPerType: 2);
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.IronPlate);

            Assert.IsTrue(endpoint.CanGive());
            buffer.Deposit(ItemType.IronPlate, 2);
            Assert.IsFalse(endpoint.CanGive(), "full of its own good means full");
            Assert.IsFalse(endpoint.CanGive(ItemType.IronPlate));
        }

        [Test]
        public void ACappedCrateStillRefusesOtherGoodsWhenItHasRoom()
        {
            var buffer = new StorageBuffer("Capped", capacityPerType: 12);
            var endpoint = new FilteredBufferEndpoint(buffer, ItemType.IronPlate);

            Assert.IsTrue(endpoint.CanGive(), "room for its own good");
            Assert.IsFalse(endpoint.CanGive(ItemType.Slag), "room is not permission");
        }

        [Test]
        public void ItReportsItsLabelThroughIFilteredEndpoint()
        {
            var endpoint = new FilteredBufferEndpoint(Stocked(), ItemType.Slag);

            var filtered = (IFilteredEndpoint)endpoint;
            Assert.AreEqual(ItemType.Slag, filtered.AcceptedItemType);
        }

        [Test]
        public void AnEmptyLabelIsNormalisedToNull_AndTheCrateAcceptsNothing()
        {
            // Not a supported configuration -- PlaceableDepot publishes a plain
            // StorageBufferEndpoint when unlabelled -- but it must fail closed rather than
            // silently behaving like an unfiltered one, or a bug in the publish path would be
            // invisible.
            var endpoint = new FilteredBufferEndpoint(Stocked((ItemType.Scrap, 5)), "");

            Assert.IsNull(endpoint.AcceptedItemType);
            Assert.IsFalse(endpoint.CanGive());
            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));
            Assert.IsFalse(endpoint.TryTake(out ItemStack _));
        }

        [Test]
        public void ANullBufferNeverThrows()
        {
            var endpoint = new FilteredBufferEndpoint(null, ItemType.Scrap);

            Assert.IsFalse(endpoint.CanGive());
            Assert.IsFalse(endpoint.CanGive(ItemType.Scrap));
            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.Scrap }));
            Assert.IsFalse(endpoint.TryTake(out ItemStack _));
            Assert.IsFalse(endpoint.TryTake(ItemType.Scrap, 1, out int _));
            Assert.IsNull(endpoint.PeekAvailableType());
            Assert.AreEqual("depot", endpoint.DisplayName);
        }
    }
}
