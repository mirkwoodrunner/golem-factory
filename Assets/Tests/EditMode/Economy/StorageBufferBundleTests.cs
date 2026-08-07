using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // StorageBufferRegistry.TryWithdrawBundle (docs/progression-design.md §11 item 8).
    //
    // The property under test throughout is ATOMICITY WITH A FULL REFUND. §6's Zeppelin costs
    // five goods and ~12-18 minutes of a mature factory's output; a purchase that took four of
    // them and refused would be indistinguishable from theft, and a rigid golem economy has no
    // mechanism anywhere that could give them back.
    public class StorageBufferBundleTests
    {
        private static List<RecipeIngredient> Bundle(params (string type, int quantity)[] entries)
        {
            var bundle = new List<RecipeIngredient>();
            foreach ((string type, int quantity) in entries)
            {
                bundle.Add(new RecipeIngredient(type, quantity));
            }

            return bundle;
        }

        private static StorageBufferRegistry Stocked(params (string type, int quantity)[] entries)
        {
            var registry = new StorageBufferRegistry();
            foreach ((string type, int quantity) in entries)
            {
                registry.Deposit("Wallet", type, quantity);
            }

            return registry;
        }

        [Test]
        public void TryWithdrawBundle_SufficientOfEverything_WithdrawsEverything()
        {
            StorageBufferRegistry registry = Stocked(
                (ItemType.Mainspring, 2), (ItemType.Brass, 20),
                (ItemType.Casing, 24), (ItemType.Gear, 12));

            bool result = registry.TryWithdrawBundle("Wallet", Bundle(
                (ItemType.Mainspring, 2), (ItemType.Brass, 20),
                (ItemType.Casing, 24), (ItemType.Gear, 12)));

            Assert.IsTrue(result);
            Assert.AreEqual(0, registry.GetQuantity("Wallet", ItemType.Mainspring));
            Assert.AreEqual(0, registry.GetQuantity("Wallet", ItemType.Brass));
            Assert.AreEqual(0, registry.GetQuantity("Wallet", ItemType.Casing));
            Assert.AreEqual(0, registry.GetQuantity("Wallet", ItemType.Gear));
        }

        // THE LOAD-BEARING TEST. Shortfall on the LAST entry, after three successful
        // withdrawals -- the case a naive loop gets wrong, and the case the old
        // TryWithdrawScrapAndBrass could only ever have one instance of.
        [Test]
        public void TryWithdrawBundle_ShortOnTheLastEntry_RefundsEverythingAlreadyTaken()
        {
            StorageBufferRegistry registry = Stocked(
                (ItemType.Mainspring, 2), (ItemType.Brass, 20),
                (ItemType.Casing, 24), (ItemType.Gear, 11));

            bool result = registry.TryWithdrawBundle("Wallet", Bundle(
                (ItemType.Mainspring, 2), (ItemType.Brass, 20),
                (ItemType.Casing, 24), (ItemType.Gear, 12)));

            Assert.IsFalse(result);
            Assert.AreEqual(2, registry.GetQuantity("Wallet", ItemType.Mainspring));
            Assert.AreEqual(20, registry.GetQuantity("Wallet", ItemType.Brass));
            Assert.AreEqual(24, registry.GetQuantity("Wallet", ItemType.Casing));
            Assert.AreEqual(11, registry.GetQuantity("Wallet", ItemType.Gear));
        }

        [Test]
        public void TryWithdrawBundle_ShortOnTheFirstEntry_TakesNothing()
        {
            StorageBufferRegistry registry = Stocked((ItemType.Scrap, 5), (ItemType.Brass, 40));

            bool result = registry.TryWithdrawBundle("Wallet", Bundle(
                (ItemType.Scrap, 60), (ItemType.Brass, 40)));

            Assert.IsFalse(result);
            Assert.AreEqual(5, registry.GetQuantity("Wallet", ItemType.Scrap));
            Assert.AreEqual(40, registry.GetQuantity("Wallet", ItemType.Brass));
        }

        // The zero-cost rule TryWithdrawScrapAndBrass encodes, carried over: a free thing is
        // payable out of a buffer nobody has ever deposited into. M1's default zero-cost
        // PlaceableBuilding still depends on it.
        [Test]
        public void TryWithdrawBundle_EmptyOrNullOrFree_SucceedsOnAnUntouchedBuffer()
        {
            var registry = new StorageBufferRegistry();

            Assert.IsTrue(registry.TryWithdrawBundle("NeverTouchedBuffer", Bundle()));
            Assert.IsTrue(registry.TryWithdrawBundle("NeverTouchedBuffer", null));
            Assert.IsTrue(registry.TryWithdrawBundle("NeverTouchedBuffer",
                Bundle((ItemType.Scrap, 0), (ItemType.Brass, -5))));
        }

        // Same null-id guard every registry in the project extends (see the class comment on
        // ResourceNodeRegistry.TryGetNode): an unset buffer id is a false, not an exception
        // inside a UI callback or a tick.
        [Test]
        public void TryWithdrawBundle_NullOrUnknownBuffer_FailsWithoutThrowing()
        {
            var registry = new StorageBufferRegistry();

            Assert.IsFalse(registry.TryWithdrawBundle(null, Bundle((ItemType.Scrap, 1))));
            Assert.IsFalse(registry.TryWithdrawBundle("NeverTouchedBuffer", Bundle((ItemType.Scrap, 1))));

            // ...but a null id with nothing to charge is still free, matching the rule above.
            Assert.IsTrue(registry.TryWithdrawBundle(null, Bundle()));
        }

        // A refund must go back into the SAME buffer, at the same quantities, even when that
        // buffer is per-type capped -- otherwise the refund itself could clamp goods away and
        // turn a refusal into a partial loss after all.
        [Test]
        public void TryWithdrawBundle_RefundIsExactEvenAgainstACappedBuffer()
        {
            var registry = new StorageBufferRegistry();
            registry.SetCapacity("Wallet", 100);
            registry.Deposit("Wallet", ItemType.Scrap, 60);
            registry.Deposit("Wallet", ItemType.IronPlate, 20);

            bool result = registry.TryWithdrawBundle("Wallet", Bundle(
                (ItemType.Scrap, 60), (ItemType.IronPlate, 20), (ItemType.Gear, 10)));

            Assert.IsFalse(result);
            Assert.AreEqual(60, registry.GetQuantity("Wallet", ItemType.Scrap));
            Assert.AreEqual(20, registry.GetQuantity("Wallet", ItemType.IronPlate));
        }

        [Test]
        public void GetQuantity_ReadsZeroForAnUnknownOrNullBuffer()
        {
            StorageBufferRegistry registry = Stocked((ItemType.Scrap, 7));

            Assert.AreEqual(7, registry.GetQuantity("Wallet", ItemType.Scrap));
            Assert.AreEqual(0, registry.GetQuantity("Wallet", ItemType.Brass));
            Assert.AreEqual(0, registry.GetQuantity("NoSuchBuffer", ItemType.Scrap));
            Assert.AreEqual(0, registry.GetQuantity(null, ItemType.Scrap));
        }
    }
}
