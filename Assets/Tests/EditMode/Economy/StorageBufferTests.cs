using NUnit.Framework;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    public class StorageBufferTests
    {
        [Test]
        public void Deposit_NewItemType_SetsQuantityToAmount()
        {
            var buffer = new StorageBuffer("Buffer");

            buffer.Deposit("Scrap", 3);

            Assert.AreEqual(3, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void Deposit_ExistingItemType_Accumulates()
        {
            var buffer = new StorageBuffer("Buffer");
            buffer.Deposit("Scrap", 2);

            buffer.Deposit("Scrap", 3);

            Assert.AreEqual(5, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void Deposit_DifferentItemTypes_TrackedIndependently()
        {
            var buffer = new StorageBuffer("Buffer");

            buffer.Deposit("Scrap", 2);
            buffer.Deposit("Brass", 1);

            Assert.AreEqual(2, buffer.GetQuantity("Scrap"));
            Assert.AreEqual(1, buffer.GetQuantity("Brass"));
        }

        [Test]
        public void TryWithdraw_SufficientQuantity_SucceedsAndDecrements()
        {
            var buffer = new StorageBuffer("Buffer");
            buffer.Deposit("Scrap", 5);

            bool result = buffer.TryWithdraw("Scrap", 2);

            Assert.IsTrue(result);
            Assert.AreEqual(3, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void TryWithdraw_InsufficientQuantity_FailsAndLeavesUnchanged()
        {
            var buffer = new StorageBuffer("Buffer");
            buffer.Deposit("Scrap", 1);

            bool result = buffer.TryWithdraw("Scrap", 2);

            Assert.IsFalse(result);
            Assert.AreEqual(1, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void TryWithdraw_UnknownItemType_Fails()
        {
            var buffer = new StorageBuffer("Buffer");

            Assert.IsFalse(buffer.TryWithdraw("Aether", 1));
        }

        [Test]
        public void GetQuantity_UnknownItemType_ReturnsZero()
        {
            var buffer = new StorageBuffer("Buffer");

            Assert.AreEqual(0, buffer.GetQuantity("Aether"));
        }
    }

    public class StorageBufferRegistryTests
    {
        [Test]
        public void Deposit_UnknownBufferId_CreatesBufferOnFirstUse()
        {
            var registry = new StorageBufferRegistry();

            registry.Deposit("ScrapBuffer", "Scrap", 4);

            Assert.IsTrue(registry.TryGetBuffer("ScrapBuffer", out StorageBuffer buffer));
            Assert.AreEqual(4, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void TryGetBuffer_NullId_ReturnsFalse()
        {
            var registry = new StorageBufferRegistry();

            Assert.IsFalse(registry.TryGetBuffer(null, out _));
        }

        [Test]
        public void TryWithdraw_UnregisteredBuffer_ReturnsFalse()
        {
            var registry = new StorageBufferRegistry();

            Assert.IsFalse(registry.TryWithdraw("NoSuchBuffer", "Scrap"));
        }

        [Test]
        public void TryWithdraw_RegisteredBufferWithStock_Succeeds()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("ScrapBuffer", "Scrap", 1);

            Assert.IsTrue(registry.TryWithdraw("ScrapBuffer", "Scrap"));
        }

        [Test]
        public void TryWithdrawScrapAndBrass_SufficientOfBoth_WithdrawsBoth()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("Wallet", "Scrap", 20);
            registry.Deposit("Wallet", "Brass", 5);

            bool result = registry.TryWithdrawScrapAndBrass("Wallet", 15, 3);

            Assert.IsTrue(result);
            Assert.AreEqual(5, registry.GetOrCreate("Wallet").GetQuantity("Scrap"));
            Assert.AreEqual(2, registry.GetOrCreate("Wallet").GetQuantity("Brass"));
        }

        [Test]
        public void TryWithdrawScrapAndBrass_InsufficientScrap_Fails_BrassUntouched()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("Wallet", "Brass", 5);

            bool result = registry.TryWithdrawScrapAndBrass("Wallet", 15, 3);

            Assert.IsFalse(result);
            Assert.AreEqual(5, registry.GetOrCreate("Wallet").GetQuantity("Brass"));
        }

        [Test]
        public void TryWithdrawScrapAndBrass_InsufficientBrass_Fails_RefundsScrap()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("Wallet", "Scrap", 20);

            bool result = registry.TryWithdrawScrapAndBrass("Wallet", 15, 3);

            Assert.IsFalse(result);
            Assert.AreEqual(20, registry.GetOrCreate("Wallet").GetQuantity("Scrap"));
        }

        [Test]
        public void TryWithdrawScrapAndBrass_ZeroCostForBoth_SucceedsOnUntouchedBuffer()
        {
            // A zero-cost purchase must succeed even if the buffer has never seen a
            // deposit of either item type -- see the method's own comment for why a naive
            // TryWithdraw(id, type, 0) against an untouched buffer would otherwise fail.
            var registry = new StorageBufferRegistry();

            Assert.IsTrue(registry.TryWithdrawScrapAndBrass("NeverTouchedBuffer", 0, 0));
        }

        [Test]
        public void TryWithdrawScrapAndBrass_ZeroBrassCost_OnlyChecksScrap()
        {
            var registry = new StorageBufferRegistry();
            registry.Deposit("Wallet", "Scrap", 10);

            bool result = registry.TryWithdrawScrapAndBrass("Wallet", 10, 0);

            Assert.IsTrue(result);
            Assert.AreEqual(0, registry.GetOrCreate("Wallet").GetQuantity("Scrap"));
        }
    }
}
