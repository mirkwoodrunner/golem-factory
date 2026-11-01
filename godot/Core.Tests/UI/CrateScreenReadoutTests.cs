using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;
using Vector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Pack and Crate screens' rules (docs/inventory-screens-design.md §2), through the real
    /// Sandbox: rows, what a click moves, refusals, labels, and what [I] opens.
    /// </summary>
    public class CrateScreenReadoutTests
    {
        private static int Total(SandboxWorld world, ICrate crate, string item) =>
            Pack(world, item) + crate.Buffer.GetQuantity(item);

        [Test]
        public void Rows_AreOnePerGoodInTierOrder_WithTheCapInTheCount()
        {
            SandboxWorld world = Compose();
            ICrate crate = PlaceDepot(world, new Vector2Int(3, -2));
            crate.Buffer.Deposit(ItemType.Coke, 3);
            crate.Buffer.Deposit(ItemType.Scrap, 37);

            List<CrateScreenRow> rows = world.Inventory.CrateRows(crate);
            CollectionAssert.AreEqual(new[] { ItemType.Scrap, ItemType.Coke }, rows.Select(r => r.ItemType));
            Assert.AreEqual("37/50", rows[0].QuantityText);
            Assert.AreEqual("Scrap", rows[0].Name);
            Assert.AreEqual(StockBarMode.AgainstCapacity, rows[0].Bar.Mode);
            Assert.IsTrue(rows.All(r => r.CanMove && r.Reason == null), "the pack has room for both");

            Assert.AreEqual("Crate (3,-2)", CrateScreenReadout.Title(crate));
            Assert.AreEqual("Holds 50 of each good", CrateScreenReadout.CapLine(crate));
            Assert.AreEqual("Label: any goods", CrateScreenReadout.LabelText(crate, picking: false));
            Assert.AreEqual("Choose a label", CrateScreenReadout.LabelText(crate, picking: true));
            Assert.AreEqual("Carries 100 of each good", world.Inventory.PackCapLine);
        }

        [Test]
        public void EmptySides_SayEmpty()
        {
            SandboxWorld world = Compose();
            ICrate crate = PlaceDepot(world, new Vector2Int(4, 4));
            world.Wallet.Pack.ClearContents();

            CrateScreenRow crateRow = world.Inventory.CrateRows(crate).Single();
            CrateScreenRow packRow = world.Inventory.PackRows(crate).Single();
            Assert.IsTrue(crateRow.IsMessage);
            Assert.AreEqual("Empty.", crateRow.Name);
            Assert.AreEqual("Your pack is empty.", packRow.Name);
            Assert.AreEqual("0 goods · 100 of each", world.Inventory.PackSummary);
        }

        [Test]
        public void Put_ShiftMovesAllThatFits_AClickMovesOne_AndAShortMoveSaysWhy()
        {
            SandboxWorld world = Compose();
            ICrate crate = PlaceDepot(world, new Vector2Int(4, 4));
            Give(world, ItemType.Coke, 30);

            Assert.AreEqual("Moved 30 Coke into the crate", world.Inventory.Put(crate, ItemType.Coke, shift: true));
            Assert.AreEqual(30, crate.Buffer.GetQuantity(ItemType.Coke));
            Assert.AreEqual(0, Pack(world, ItemType.Coke));

            Give(world, ItemType.Coke, 30);
            crate.Buffer.Deposit(ItemType.Coke, 18);
            int total = Total(world, crate, ItemType.Coke);
            Assert.AreEqual("Moved 2 Coke into the crate · Crate full of Coke", world.Inventory.Put(crate, ItemType.Coke, shift: true));
            Assert.AreEqual(50, crate.Buffer.GetQuantity(ItemType.Coke));
            Assert.AreEqual(total, Total(world, crate, ItemType.Coke), "nothing lost, nothing made");

            CrateScreenRow coke = world.Inventory.PackRows(crate).Single(r => r.ItemType == ItemType.Coke);
            Assert.IsFalse(coke.CanMove);
            Assert.AreEqual("Crate full of Coke", coke.Reason);
            Assert.AreEqual("Crate full of Coke", world.Inventory.Put(crate, ItemType.Coke, shift: true));

            Assert.AreEqual("Moved 1 Coke to your pack", world.Inventory.Take(crate, ItemType.Coke, shift: false));
            Assert.AreEqual(49, crate.Buffer.GetQuantity(ItemType.Coke));
            Assert.AreEqual(total, Total(world, crate, ItemType.Coke));
        }

        [Test]
        public void ALabel_RefusesOtherGoodsOnTheWayIn_ButAnythingComesOut()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(4, 4);
            ICrate crate = PlaceDepot(world, cell);
            crate.Buffer.Deposit(ItemType.Scrap, 5);
            Give(world, ItemType.Scrap, 20);
            world.TechTree.Ledger.RecordItem(ItemType.CopperOre);
            world.TechTree.Ledger.RecordItem(ItemType.Scrap);

            IReadOnlyList<string> options = world.Inventory.LabelOptions(crate);
            Assert.IsNull(options[0], "any goods first");
            Assert.IsTrue(options.Skip(1).All(o => world.TechTree.Ledger.HasItem(o)), "only goods the factory has seen");
            CollectionAssert.Contains(options.ToList(), ItemType.CopperOre);

            Assert.AreEqual("Label: Copper Ore", world.Inventory.SetLabel(crate, ItemType.CopperOre));
            Assert.AreEqual(ItemType.CopperOre, crate.FilterItemType);
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(cell, out IItemEndpoint endpoint));
            Assert.IsInstanceOf<FilteredBufferEndpoint>(endpoint, "re-published as a labelled crate");

            int total = Total(world, crate, ItemType.Scrap);
            CrateScreenRow scrap = world.Inventory.PackRows(crate).Single(r => r.ItemType == ItemType.Scrap);
            Assert.IsFalse(scrap.CanMove);
            Assert.AreEqual("This crate is for Copper Ore", scrap.Reason);
            Assert.AreEqual("This crate is for Copper Ore", world.Inventory.Put(crate, ItemType.Scrap, shift: false));
            Assert.AreEqual(total, Total(world, crate, ItemType.Scrap));
            Assert.AreEqual(5, crate.Buffer.GetQuantity(ItemType.Scrap), "goods already inside stay");

            Assert.AreEqual("Moved 5 Scrap to your pack", world.Inventory.Take(crate, ItemType.Scrap, shift: true));
            Assert.AreEqual(total, Total(world, crate, ItemType.Scrap));

            world.Inventory.SetLabel(crate, null);
            Assert.IsNull(crate.FilterItemType);
            Assert.IsNotInstanceOf<FilteredBufferEndpoint>(world.Endpoints.TryGetEndpoint(cell, out IItemEndpoint plain) ? plain : null);
        }

        [Test]
        public void APackAboveItsCap_GivesToACrate_ButTakesNothingBack()
        {
            SandboxWorld world = Compose();
            ICrate crate = PlaceDepot(world, new Vector2Int(4, 4));
            world.Wallet.Pack.RestoreQuantity(ItemType.Scrap, 140);

            CrateScreenRow scrap = world.Inventory.PackRows(crate).Single(r => r.ItemType == ItemType.Scrap);
            Assert.AreEqual("140/100", scrap.QuantityText);
            Assert.IsTrue(scrap.Bar.IsFull);
            Assert.AreEqual("Moved 1 Scrap into the crate", world.Inventory.Put(crate, ItemType.Scrap, shift: false));
            Assert.AreEqual("Pack full of Scrap", world.Inventory.Take(crate, ItemType.Scrap, shift: false));
            Assert.AreEqual("Pack full of Scrap", world.Inventory.CrateRows(crate).Single().Reason);
            Assert.AreEqual(139, Pack(world, ItemType.Scrap));
            Assert.AreEqual(1, crate.Buffer.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void TakeAll_IsEsCollect()
        {
            SandboxWorld world = Compose();
            ICrate crate = PlaceDepot(world, new Vector2Int(4, 4));
            world.Wallet.Pack.ClearContents();
            Give(world, ItemType.Scrap, 95);
            crate.Buffer.Deposit(ItemType.Scrap, 20);
            crate.Buffer.Deposit(ItemType.Coke, 3);

            Assert.AreEqual("Moved 5 Scrap, 3 Coke to your pack · Pack full of Scrap", world.Inventory.TakeAll(crate));
            Assert.AreEqual(100, Pack(world, ItemType.Scrap));
            Assert.AreEqual(15, crate.Buffer.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, crate.Buffer.GetQuantity(ItemType.Coke));
            Assert.AreEqual("Pack full of Scrap", world.Inventory.TakeAll(crate));
        }

        [Test]
        public void AMast_TakesNoLabel()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Brass, 20);
            Give(world, ItemType.Casing, 10);
            world.PackUpgrade.Restore(1); // the Mast's 120 Iron Plate needs rung 1 (tuning-data-spec §8.1)
            Give(world, ItemType.IronPlate, 120);
            var cell = new Vector2Int(6, 6);
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "FreightMastPrefab").Prefab);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
            ICrate mast = world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell).GetPart<ICrate>();

            Assert.AreEqual("Freight Mast (6,6)", CrateScreenReadout.Title(mast));
            Assert.AreEqual("A mast's crate takes no label", CrateScreenReadout.LabelText(mast, picking: false));
            Assert.AreEqual(0, world.Inventory.LabelOptions(mast).Count);
            Assert.AreEqual("A mast's crate takes no label", world.Inventory.SetLabel(mast, ItemType.Scrap));
            Assert.IsNull(mast.FilterItemType);
        }

        [Test]
        public void ASharedStoreCrate_IsTitledThePost()
        {
            var depot = new PlaceableDepot();
            depot.Configure("SharedStore");
            var buffers = new StorageBufferRegistry();
            buffers.SetCapacity("SharedStore", 200);
            Assert.IsTrue(depot.RegisterAsSpatialEndpoint(new SpatialEndpointRegistry(), buffers, new Vector2Int(1, 1)));

            Assert.AreEqual("Pneumatic Post", CrateScreenReadout.Title(depot));
            Assert.AreEqual("Shared by every Post crate · 200 of each good", CrateScreenReadout.CapLine(depot));
        }

        // --- [I] and the screens ---------------------------------------------------------------

        private sealed class FakePack : IPackScreen, IClosableScreen
        {
            public bool IsOpen { get; private set; }
            public void Open() => IsOpen = true;
            public void Close() => IsOpen = false;
        }

        private sealed class FakeCrate : ICrateScreen, IClosableScreen
        {
            public bool IsOpen { get; private set; }
            public ICrate Crate { get; private set; }
            public void Open(ICrate crate) { Crate = crate; IsOpen = true; }
            public void Close() => IsOpen = false;
        }

        [Test]
        public void I_OpensTheCrateAtACrate_AndThePackAnywhereElse()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(4, 4);
            ICrate crate = PlaceDepot(world, cell);
            var pack = new FakePack();
            var crateScreen = new FakeCrate();
            world.Screens.Register(pack);
            world.Screens.Register(crateScreen);
            world.ConfigureInventoryScreens(pack, crateScreen);

            world.Interactor.Position = new Vector3(cell.x, cell.y + 1f, 0f);
            world.Interactor.Poll();
            StringAssert.EndsWith("[I] open", world.Interactor.CurrentPrompt, "the crate's prompt advertises the key");
            Assert.IsTrue(world.Interactor.OpenInventory());
            Assert.IsTrue(crateScreen.IsOpen);
            Assert.AreSame(crate, crateScreen.Crate);
            Assert.IsFalse(pack.IsOpen);

            crateScreen.Close();
            world.Interactor.Position = new Vector3(-9f, 9f, 0f);
            Assert.IsTrue(world.Interactor.OpenInventory());
            Assert.IsTrue(pack.IsOpen);
            Assert.IsFalse(crateScreen.IsOpen);
        }

        [Test]
        public void AnyRegisteredScreen_HidesTheWorldPrompt()
        {
            // crates-design §6.7: a new screen gets this by registering, not by being named.
            SandboxWorld world = Compose();
            var cell = new Vector2Int(4, 4);
            PlaceDepot(world, cell);
            var pack = new FakePack();
            world.Screens.Register(pack);

            world.Interactor.Position = new Vector3(cell.x, cell.y + 1f, 0f);
            world.Interactor.Poll();
            Assert.AreNotEqual("", world.Interactor.CurrentPrompt);

            pack.Open();
            world.Interactor.Poll();
            Assert.AreEqual(InteractionAffordance.Hidden, world.Interactor.CurrentAffordance);
            Assert.AreEqual("", world.Interactor.CurrentPrompt);
        }
    }
}
