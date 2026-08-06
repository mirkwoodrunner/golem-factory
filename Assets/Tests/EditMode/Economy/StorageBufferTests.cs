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

        // --- Capacity (docs/progression-design.md §11 item 3) -----------------------------
        // Capacity is PER ITEM TYPE and OPT-IN. Both halves matter: per-buffer would deadlock
        // (§10), and a capped-by-default buffer would silently change Main.unity's demo
        // economy, the player's stockpile and every pre-existing test at once.

        [Test]
        public void Capacity_DefaultsToUnlimited_AndAcceptsEverything()
        {
            var buffer = new StorageBuffer("Buffer");

            Assert.AreEqual(StorageBuffer.Unlimited, buffer.CapacityPerType);
            Assert.IsTrue(buffer.IsUnlimited);
            Assert.AreEqual(1000, buffer.Deposit("Scrap", 1000),
                "an uncapped buffer must accept the whole amount, exactly as it always did");
            Assert.AreEqual(1000, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void RoomFor_OnAnUnlimitedBuffer_ReportsNoLimit()
        {
            var buffer = new StorageBuffer("Buffer");
            buffer.Deposit("Scrap", 5000);

            Assert.AreEqual(int.MaxValue, buffer.RoomFor("Scrap"));
        }

        [Test]
        public void Deposit_WithinCapacity_AcceptsAllOfIt()
        {
            var buffer = new StorageBuffer("Buffer", 100);

            Assert.AreEqual(40, buffer.Deposit("Scrap", 40));
            Assert.AreEqual(40, buffer.GetQuantity("Scrap"));
            Assert.AreEqual(60, buffer.RoomFor("Scrap"));
        }

        [Test]
        public void Deposit_OverCapacity_ClampsAndReturnsOnlyWhatWasAccepted()
        {
            var buffer = new StorageBuffer("Buffer", 10);
            buffer.Deposit("Scrap", 7);

            int accepted = buffer.Deposit("Scrap", 9);

            Assert.AreEqual(3, accepted, "the caller was told it got more room than existed");
            Assert.AreEqual(10, buffer.GetQuantity("Scrap"), "the cap was overrun");
        }

        [Test]
        public void Deposit_IntoAFullSlot_AcceptsNothing()
        {
            var buffer = new StorageBuffer("Buffer", 4);
            buffer.Deposit("Scrap", 4);

            Assert.AreEqual(0, buffer.Deposit("Scrap", 1));
            Assert.AreEqual(0, buffer.RoomFor("Scrap"));
            Assert.AreEqual(4, buffer.GetQuantity("Scrap"));
        }

        [Test]
        public void Capacity_IsPerItemType_AFullSlotLeavesEveryOtherTypeFullyDepositable()
        {
            // THE claim this whole item rests on (progression-design §10, "Buffer capacity
            // deadlock"): a full Slag slot must never block Iron Plate. A whole-buffer cap
            // would deadlock permanently, because no rigid golem can be programmed to drain
            // the wrong type back out.
            var buffer = new StorageBuffer("Mixed", 5);

            Assert.AreEqual(5, buffer.Deposit("Slag", 5));
            Assert.AreEqual(0, buffer.Deposit("Slag", 1), "the Slag slot should be full");

            Assert.AreEqual(5, buffer.Deposit("IronPlate", 5),
                "a full Slag slot blocked Iron Plate -- capacity is being applied per buffer, " +
                "not per item type, which deadlocks the whole line permanently");
            Assert.AreEqual(5, buffer.GetQuantity("IronPlate"));
            Assert.AreEqual(5, buffer.GetQuantity("Slag"));
        }

        [Test]
        public void RoomFor_ReopensAfterAWithdrawal()
        {
            var buffer = new StorageBuffer("Buffer", 6);
            buffer.Deposit("Scrap", 6);

            Assert.IsTrue(buffer.TryWithdraw("Scrap", 2));

            Assert.AreEqual(2, buffer.RoomFor("Scrap"));
            Assert.AreEqual(2, buffer.Deposit("Scrap", 5));
        }

        [Test]
        public void Deposit_NullItemType_IsARefusalRatherThanAThrow()
        {
            // Matches the registries' null-id guard convention rather than letting
            // Dictionary<string,_> throw out of the middle of a tick.
            var buffer = new StorageBuffer("Buffer", 10);

            Assert.AreEqual(0, buffer.Deposit(null, 3));
        }

        [Test]
        public void SetCapacityPerType_KeepsContentsAlreadyAboveTheNewCap()
        {
            // Mutates in place (StorageBufferEndpoint holds a direct reference, so rebuilding
            // would orphan every published spatial endpoint) and never voids stock the player
            // legitimately owns -- the slot simply has no room until it drains.
            var buffer = new StorageBuffer("Buffer");
            buffer.Deposit("Scrap", 50);

            buffer.SetCapacityPerType(10);

            Assert.AreEqual(50, buffer.GetQuantity("Scrap"));
            Assert.AreEqual(0, buffer.RoomFor("Scrap"));
            Assert.AreEqual(0, buffer.Deposit("Scrap", 1));
        }

        [Test]
        public void SetCapacityPerType_CanUncapABufferAgain()
        {
            var buffer = new StorageBuffer("Buffer", 3);
            buffer.Deposit("Scrap", 3);

            buffer.SetCapacityPerType(StorageBuffer.Unlimited);

            Assert.AreEqual(500, buffer.Deposit("Scrap", 500));
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

        // --- Capacity policy -------------------------------------------------------------

        [Test]
        public void DefaultCapacityPerType_IsUnlimited_SoAnUntunedRegistryBehavesAsBefore()
        {
            var registry = new StorageBufferRegistry();

            Assert.AreEqual(StorageBuffer.Unlimited, registry.DefaultCapacityPerType);
            Assert.AreEqual(9999, registry.Deposit("ScrapBuffer", "Scrap", 9999));
        }

        [Test]
        public void Deposit_ReturnsWhatTheBufferAccepted_NotWhatWasAsked()
        {
            var registry = new StorageBufferRegistry { DefaultCapacityPerType = 10 };

            Assert.AreEqual(10, registry.Deposit("ScrapBuffer", "Scrap", 25));
            Assert.AreEqual(10, registry.GetOrCreate("ScrapBuffer").GetQuantity("Scrap"));
        }

        [Test]
        public void Deposit_NullBufferId_AcceptsNothing()
        {
            var registry = new StorageBufferRegistry();

            Assert.AreEqual(0, registry.Deposit(null, "Scrap", 5));
        }

        [Test]
        public void DefaultCapacityPerType_AppliesToBuffersTheRegistryAutoCreates()
        {
            var registry = new StorageBufferRegistry { DefaultCapacityPerType = 100 };

            registry.Deposit("SmelterOutput", "Slag", 1);

            Assert.AreEqual(100, registry.GetOrCreate("SmelterOutput").CapacityPerType);
        }

        [Test]
        public void SetCapacity_CreatesTheBufferEagerly_SoItNeverPicksUpTheDefaultOnFirstDeposit()
        {
            // This is exactly how the player's stockpile is exempted from the Sandbox's
            // production cap: it has to exist, uncapped, before anything deposits into it.
            var registry = new StorageBufferRegistry { DefaultCapacityPerType = 100 };

            registry.SetCapacity("FactoryStockpile", StorageBuffer.Unlimited);
            registry.Deposit("FactoryStockpile", "Scrap", 5000);

            Assert.IsTrue(registry.TryGetBuffer("FactoryStockpile", out StorageBuffer stockpile));
            Assert.IsTrue(stockpile.IsUnlimited);
            Assert.AreEqual(5000, stockpile.GetQuantity("Scrap"));
        }

        [Test]
        public void SetCapacity_OnAnExistingBuffer_KeepsTheSameInstanceSoPublishedEndpointsStillSeeIt()
        {
            var registry = new StorageBufferRegistry();
            StorageBuffer before = registry.GetOrCreate("Depot");

            StorageBuffer after = registry.SetCapacity("Depot", 12);

            Assert.AreSame(before, after,
                "re-capping replaced the buffer object, orphaning every spatial endpoint " +
                "already holding a reference to it");
            Assert.AreEqual(12, before.CapacityPerType);
        }

        [Test]
        public void SetCapacity_NullBufferId_IsANoOp()
        {
            var registry = new StorageBufferRegistry();

            Assert.IsNull(registry.SetCapacity(null, 10));
        }

        [Test]
        public void SetCapacity_SurvivesClear_SoLoadingASaveDoesNotReCapTheStockpile()
        {
            // Clear() is the save/load path: it empties every buffer and RestoreState deposits
            // the save's contents back in. Capacity is scene policy, not saved state, so the
            // exemption has to outlive the wipe -- otherwise loading a save in a capped scene
            // would re-cap the player's uncapped stockpile and clamp away everything above the
            // cap as it was deposited back in.
            var registry = new StorageBufferRegistry { DefaultCapacityPerType = 100 };
            registry.SetCapacity("FactoryStockpile", StorageBuffer.Unlimited);

            registry.Clear();
            registry.Deposit("FactoryStockpile", "Scrap", 5000);

            Assert.AreEqual(5000, registry.GetOrCreate("FactoryStockpile").GetQuantity("Scrap"),
                "the stockpile came back capped and swallowed the save's contents");
            Assert.AreEqual(100, registry.GetOrCreate("SomeOtherBuffer").CapacityPerType,
                "an ordinary buffer should still pick up the scene default");
        }

        [Test]
        public void RoomFor_UnknownBuffer_ReportsWhatItWouldBeCreatedWith()
        {
            var unlimited = new StorageBufferRegistry();
            Assert.AreEqual(int.MaxValue, unlimited.RoomFor("NeverTouched", "Scrap"));

            var capped = new StorageBufferRegistry { DefaultCapacityPerType = 100 };
            Assert.AreEqual(100, capped.RoomFor("NeverTouched", "Scrap"));
        }

        [Test]
        public void Clear_EmptiesEveryBuffer()
        {
            // Was Clear_RemovesAllBuffers, asserting TryGetBuffer went false. That is now wrong
            // by design: Clear() empties the buffers and keeps the instances, because a
            // PlaceableDepot's published endpoint holds a direct reference to one and dropping
            // the object orphaned it across a load. What callers actually depend on is that a
            // load *replaces* buffer state rather than merging into it, and that is what this
            // asserts. See StorageBufferRegistry.Clear.
            var registry = new StorageBufferRegistry();
            registry.Deposit("ScrapBuffer", "Scrap", 5);
            registry.Deposit("BrassBuffer", "Brass", 3);

            registry.Clear();

            Assert.AreEqual(0, registry.GetOrCreate("ScrapBuffer").GetQuantity("Scrap"));
            Assert.AreEqual(0, registry.GetOrCreate("BrassBuffer").GetQuantity("Brass"));
            // Emptied, not zeroed: readers that list held types (InventoryPanel walks
            // Quantities.Keys) must see "nothing here", not a row of zeroes.
            Assert.IsEmpty(registry.GetOrCreate("ScrapBuffer").Quantities,
                "an emptied buffer still reports the item types it used to hold");
        }

        [Test]
        public void ClearedBuffersKeepTheirIdentity_SoADepotsPublishedEndpointIsNotOrphaned()
        {
            // The bug this prevents: Buildings/PlaceableDepot registers a StorageBufferEndpoint
            // holding a direct reference to the buffer instance. When Clear() removed the
            // objects, loading a save left that endpoint writing into a detached buffer while
            // the registry and the HUD read a freshly created one -- so every golem Push into
            // that depot afterwards vanished, with no stall and no error.
            var registry = new StorageBufferRegistry();
            registry.Deposit("DepotBuffer", "Scrap", 5);
            StorageBuffer published = registry.GetOrCreate("DepotBuffer");

            registry.Clear();
            registry.Deposit("DepotBuffer", "Scrap", 2);

            Assert.AreSame(published, registry.GetOrCreate("DepotBuffer"),
                "the registry handed out a different instance than the one already published");
            Assert.AreEqual(2, published.GetQuantity("Scrap"),
                "a deposit after the load did not reach the buffer the endpoint points at");
        }

        [Test]
        public void DefaultCapacityPerType_ReCapsBuffersThatAlreadyExist()
        {
            // Capacity must not depend on whether a buffer happened to be auto-created before
            // or after the scene bootstrap ran -- that would make backpressure a function of
            // component execution order and read as a tuning bug, not an ordering one.
            var registry = new StorageBufferRegistry();
            registry.Deposit("EarlyBuffer", "Scrap", 1);
            Assert.IsTrue(registry.GetOrCreate("EarlyBuffer").IsUnlimited);

            registry.DefaultCapacityPerType = 100;

            Assert.AreEqual(100, registry.GetOrCreate("EarlyBuffer").CapacityPerType,
                "a buffer created before the policy was set kept a different capacity");
            Assert.AreEqual(100, registry.GetOrCreate("LateBuffer").CapacityPerType);
        }

        [Test]
        public void DefaultCapacityPerType_DoesNotOverrideAnExplicitPerBufferChoice()
        {
            var registry = new StorageBufferRegistry();
            registry.SetCapacity("FactoryStockpile", StorageBuffer.Unlimited);

            registry.DefaultCapacityPerType = 100;

            Assert.IsTrue(registry.GetOrCreate("FactoryStockpile").IsUnlimited,
                "the scene default trampled a deliberate per-buffer exemption");
        }

        [Test]
        public void ReCapping_KeepsContentsAlreadyAboveTheNewCap()
        {
            // Re-capping is a policy change, not a confiscation.
            var registry = new StorageBufferRegistry();
            registry.Deposit("Overfull", "Scrap", 500);

            registry.DefaultCapacityPerType = 100;

            Assert.AreEqual(500, registry.GetOrCreate("Overfull").GetQuantity("Scrap"));
            Assert.AreEqual(0, registry.RoomFor("Overfull", "Scrap"),
                "it should simply have no room until it drains");
        }
    }
}
