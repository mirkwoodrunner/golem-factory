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
            // The boiler holds exactly a base pack (BoilerCapacityTests): it still fits an empty slot.
            Assert.LessOrEqual(SteamBoiler.CokeCapacity, pack, "a boiler's Coke");
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

                Assert.AreEqual(CrateScreenReadout.NothingToDiscard, world.Inventory.Discard(ItemType.Coke, shift: true));
                Assert.AreEqual(30, heard.Where(e => e.ItemType == ItemType.Coke).Sum(e => e.Quantity),
                    "goods thrown away are spent, as every place that destroys goods says");
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
            StringAssert.Contains(held + " goods", world.Inventory.DiscardAll(confirmed: true));

            Assert.IsEmpty(world.Inventory.Pack.HeldGoods(), "the pack is empty");
            Assert.AreEqual(7, world.Buffers.GetQuantity(SandboxWorld.PostBufferId, ItemType.Coke), "the Post is untouched");
            Assert.AreEqual(CrateScreenReadout.NothingToDiscard, world.Inventory.DiscardAll(confirmed: true));
        }
    }
}
