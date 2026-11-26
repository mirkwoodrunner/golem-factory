using System.Collections.Generic;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Steam;
using GolemFactory.Tests.World;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// A machine holds less than the pack (2026-11-26, the user's call), so whatever a demolished
    /// machine hands back fits an empty pack slot. And the Pack screen can throw goods away.
    /// </summary>
    public class MachineStorageTests
    {
        private static readonly Vector2Int Cell = new Vector2Int(6, 6);

        private static int BasePack => SandboxSetupTests.LoadReal().packCapacityPerType;

        [Test]
        public void EveryMachinesStore_IsSmallerThanTheBasePack()
        {
            int pack = BasePack;
            Assert.Less(SandboxSetupTests.LoadReal().crateCapacityPerType, pack, "a crate");
            Assert.Less(SlagHeap.CokeCapacity, pack, "a Slag Heap's Coke");
            Assert.Less(ScrapRecycler.CokeCapacity, pack, "a Scrap Recycler's Coke");
            Assert.Less(ScrapRecycler.OutputCapacity, pack, "a Scrap Recycler's Scrap");
            Assert.Less(SteamBoiler.CokeCapacity, pack, "a boiler's Coke");
        }

        [Test]
        public void AFullSlagHeap_RefusesCoke()
        {
            var heap = new SlagHeap("Heap", startingCoke: SlagHeap.CokeCapacity - 1);
            var tile = new SlagHeapEndpoint(heap);

            Assert.IsTrue(tile.TryGive(new ItemStack { ItemType = ItemType.Coke }), "the last lump fits");
            Assert.AreEqual(SlagHeap.CokeCapacity, heap.CokeStock);
            Assert.IsFalse(tile.CanGive(ItemType.Coke), "full: the golem keeps its Coke");
            Assert.IsFalse(tile.TryGive(new ItemStack { ItemType = ItemType.Coke }));
            Assert.AreEqual(SlagHeap.CokeCapacity, heap.CokeStock, "nothing went in");
            Assert.IsTrue(tile.CanGive(), "it still takes Slag");
        }

        [Test]
        public void AFullScrapRecycler_RefusesCoke()
        {
            var hopper = new ScrapRecycler("Recycler", startingCoke: ScrapRecycler.CokeCapacity);
            var tile = new ScrapRecyclerEndpoint(hopper);

            Assert.IsFalse(tile.CanGive(ItemType.Coke));
            Assert.IsFalse(tile.TryGive(new ItemStack { ItemType = ItemType.Coke }));
            Assert.AreEqual(ScrapRecycler.CokeCapacity, hopper.CokeStock);
            Assert.AreEqual(0, hopper.AddCoke(5), "AddCoke stops at the cap too");
        }

        [TestCase("SlagHeapPrefab")]
        [TestCase("ScrapRecyclerPrefab")]
        public void AMachineFedToItsCap_DemolishesIntoAnEmptyPack(string prefab)
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, PlaceableSlagHeap.ScrapCost);
            Give(world, ItemType.IronPlate, PlaceableSlagHeap.IronPlateCost);
            PlaceableBuilding building = Place(world, prefab, Cell);
            IItemEndpoint tile = world.Endpoints.TryGetEndpoint(Cell, out IItemEndpoint found) ? found : null;
            Assert.IsNotNull(tile, "the machine publishes its tile");

            int fed = 0;
            while (tile.CanGive(ItemType.Coke) && fed < 1000)
            {
                Assert.IsTrue(tile.TryGive(new ItemStack { ItemType = ItemType.Coke }));
                fed++;
            }
            Assert.Less(fed, BasePack, "it stopped short of a pack");
            Assert.AreEqual(0, Pack(world, ItemType.Coke), "precondition: no Coke in the pack");

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(Cell);
            string said = world.Build.LastStatusMessage;
            world.Build.CancelPlacement();

            Assert.IsTrue(building.IsRemoved, said);
            Assert.AreEqual(fed, Pack(world, ItemType.Coke), "every lump came back");
        }

        [Test]
        public void ASlagHeapsCokeAndRemainder_SurviveASaveAndLoad()
        {
            // Before 2026-11-26 a load restarted every heap empty, so it refused Slag.
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, PlaceableSlagHeap.ScrapCost);
            Give(world, ItemType.IronPlate, PlaceableSlagHeap.IronPlateCost);
            SlagHeap heap = Place(world, "SlagHeapPrefab", Cell).GetPart<PlaceableSlagHeap>().Heap;
            heap.AddCoke(30);
            heap.TryVoid();
            heap.TryVoid();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "golem-factory-heap-" + System.Guid.NewGuid() + ".json");
            try
            {
                world.SaveTo(path);
                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);

                SlagHeap loaded = null;
                foreach (PlaceableBuilding b in fresh.Build.Buildings)
                {
                    if (!b.IsRemoved && b.Cell == Cell)
                    {
                        loaded = b.GetPart<PlaceableSlagHeap>()?.Heap;
                    }
                }
                Assert.IsNotNull(loaded, "the heap was rebuilt");
                Assert.AreEqual(30, loaded.CokeStock, "its Coke came back");
                Assert.AreEqual(2, loaded.PendingSlag, "and the Slag toward its next Coke");
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void ARestoredHeap_KeepsCokeOverTheCap_AndBoundsItsRemainder()
        {
            var heap = new SlagHeap("Heap", slagPerCoke: 4);
            heap.Restore(SlagHeap.CokeCapacity + 70, 9);
            Assert.AreEqual(SlagHeap.CokeCapacity + 70, heap.CokeStock, "a load never clamps");
            Assert.AreEqual(3, heap.PendingSlag);
        }

        // --- [E] and [I] at a heap or a recycler (2026-11-26) --------------------------------------

        private static ICokeHolder PlaceHolder(SandboxWorld world, string prefab)
        {
            Give(world, ItemType.Scrap, PlaceableSlagHeap.ScrapCost);
            Give(world, ItemType.IronPlate, PlaceableSlagHeap.IronPlateCost);
            ICokeHolder holder = Place(world, prefab, Cell).GetPart<ICokeHolder>();
            world.Interactor.Position = new GolemFactory.Compat.Vector3(Cell.x, Cell.y + 1f, 0f);
            world.Interactor.Poll();
            return holder;
        }

        [TestCase("SlagHeapPrefab", "Slag Heap")]
        [TestCase("ScrapRecyclerPrefab", "Scrap Recycler")]
        public void EAtAHeapOrRecycler_LoadsCokeFromThePack_UpToItsCap(string prefab, string name)
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Coke, 90);
            ICokeHolder holder = PlaceHolder(world, prefab);

            StringAssert.Contains("Fuel " + name, world.Interactor.CurrentPrompt);
            StringAssert.DoesNotContain("Fuel Boiler", world.Interactor.CurrentPrompt);
            while (holder.CokeRoom > 0 && world.Interactor.TryRefuel(holder))
            {
            }

            Assert.AreEqual(holder.CokeStock + holder.CokeRoom, holder.CokeStock, "it filled");
            Assert.AreEqual(90 - holder.CokeStock, Pack(world, ItemType.Coke), "from the pack, nothing minted");
            Assert.IsFalse(world.Interactor.TryRefuel(holder), "full: refused");
            StringAssert.Contains("is full", world.Interactor.LastStatusMessage);
        }

        private sealed class FakePack : IPackScreen
        {
            public bool IsOpen { get; private set; }
            public void Open() => IsOpen = true;
        }

        // An empty one is passed over (2026-11-26): [I] opens the Pack screen, never "is empty".
        [TestCase("SlagHeapPrefab")]
        [TestCase("ScrapRecyclerPrefab")]
        public void IAtAnEmptyHeapOrRecycler_OpensThePack(string prefab)
        {
            SandboxWorld world = Compose();
            var pack = new FakePack();
            world.ConfigureInventoryScreens(pack, null);
            ICokeHolder holder = PlaceHolder(world, prefab);
            holder.TakeCoke(holder.CokeStock);
            world.Interactor.Poll();

            StringAssert.DoesNotContain("take Coke", world.Interactor.CurrentPrompt);
            Assert.IsTrue(world.Interactor.OpenInventory(), world.Interactor.LastStatusMessage);
            Assert.IsTrue(pack.IsOpen, "the Pack screen, not a refusal");
        }

        // Over its cap from an old save, the prompt still shows the cap, not the stock.
        [TestCase("SlagHeapPrefab")]
        [TestCase("ScrapRecyclerPrefab")]
        public void OneOverItsCap_ShowsItsCap_NotItsStock(string prefab)
        {
            SandboxWorld world = Compose();
            ICokeHolder holder = PlaceHolder(world, prefab);
            int over = holder.CokeCapacity + 70;
            if (holder is PlaceableSlagHeap heap)
            {
                heap.Heap.Restore(over, 0);
            }
            else
            {
                ((PlaceableScrapRecycler)holder).Recycler.Restore(over, 0, 0);
            }
            world.Interactor.Poll();

            StringAssert.Contains(over + "/" + holder.CokeCapacity + " Coke", world.Interactor.CurrentPrompt);
            Give(world, ItemType.Coke, 5);
            Assert.IsFalse(world.Interactor.TryRefuel(holder));
            StringAssert.EndsWith("is full (" + holder.CokeCapacity + " Coke).", world.Interactor.LastStatusMessage);
        }

        [TestCase("SlagHeapPrefab")]
        [TestCase("ScrapRecyclerPrefab")]
        public void OneOverThePackFromAnOldSave_IsEmptiedByI_ThenDemolished(string prefab)
        {
            SandboxWorld world = Compose();
            ICokeHolder holder = PlaceHolder(world, prefab);
            int cap = world.Wallet.Pack.CapacityPerType;
            // A load never clamps, so a save from before the cap can hold more than a pack.
            if (holder is PlaceableSlagHeap heap)
            {
                heap.Heap.Restore(cap + 40, 0);
            }
            else
            {
                ((PlaceableScrapRecycler)holder).Recycler.Restore(cap + 40, 0, 0);
            }

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(Cell);
            string said = world.Build.LastStatusMessage;
            world.Build.CancelPlacement();
            StringAssert.StartsWith("Not demolished: pack full of Coke.", said);
            StringAssert.Contains("[I]", said, "and says how to empty it");

            world.Interactor.Poll();
            StringAssert.Contains("[I] take Coke", world.Interactor.CurrentPrompt);
            Assert.IsTrue(world.Interactor.OpenInventory(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(cap, Pack(world, ItemType.Coke));
            Assert.AreEqual(40, holder.CokeStock, "the rest stays");
            Assert.IsTrue(world.Wallet.TryCharge(new[] { new GolemFactory.PunchCards.RecipeIngredient(ItemType.Coke, cap) }));

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(Cell);
            said = world.Build.LastStatusMessage;
            world.Build.CancelPlacement();
            Assert.AreEqual(40, Pack(world, ItemType.Coke), said + ": the last 40 came back with it");
        }

        // --- Discarding ---------------------------------------------------------------------------

        private static List<ItemConsumedEvent> Listen(out System.Action stop)
        {
            var heard = new List<ItemConsumedEvent>();
            System.Action<ItemConsumedEvent> on = heard.Add;
            EventBus.ItemConsumed += on;
            stop = () => EventBus.ItemConsumed -= on;
            return heard;
        }

        [Test]
        public void DiscardingAGood_ThrowsOneAway_OrAllWithShift()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 5);
            int scrap = Pack(world, ItemType.Scrap);
            Give(world, ItemType.Coke, 30);
            List<ItemConsumedEvent> heard = Listen(out System.Action stop);
            try
            {
                Assert.AreEqual("Threw away 1 Coke", world.Inventory.Discard(ItemType.Coke, shift: false));
                Assert.AreEqual(29, Pack(world, ItemType.Coke));

                Assert.AreEqual("Threw away 29 Coke", world.Inventory.Discard(ItemType.Coke, shift: true));
                Assert.AreEqual(0, Pack(world, ItemType.Coke));
                Assert.AreEqual(scrap, Pack(world, ItemType.Scrap), "other goods stay");

                Assert.AreEqual(CrateScreenReadout.NoneLeft(ItemType.Coke), world.Inventory.Discard(ItemType.Coke, shift: true),
                    "none of that good left, while the pack holds others");
                Assert.IsEmpty(heard, "goods thrown away are not goods spent: the player's hands, not the factory's work");
            }
            finally
            {
                stop();
            }
        }

        [Test]
        public void DiscardAll_AsksFirst_ThenEmptiesThePackOnly()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Coke, 10);
            Give(world, ItemType.IronPlate, 5);
            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Coke, 7);

            Assert.AreEqual(CrateScreenReadout.DiscardAllConfirm, world.Inventory.DiscardAll(confirmed: false));
            Assert.AreEqual(10, Pack(world, ItemType.Coke), "asking moves nothing");

            int held = 0;
            foreach (string good in world.Inventory.Pack.HeldGoods())
            {
                held += Pack(world, good);
            }
            List<ItemConsumedEvent> heard = Listen(out System.Action stop);
            try
            {
                StringAssert.Contains(held + " goods", world.Inventory.DiscardAll(confirmed: true));
                Assert.IsEmpty(heard, "not goods spent");
            }
            finally
            {
                stop();
            }

            Assert.IsEmpty(world.Inventory.Pack.HeldGoods(), "the pack is empty");
            Assert.AreEqual(7, world.Buffers.GetQuantity(SandboxWorld.PostBufferId, ItemType.Coke), "the Post is untouched");
            Assert.AreEqual(CrateScreenReadout.NothingToDiscard, world.Inventory.DiscardAll(confirmed: true));
        }
    }
}
