using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;
using Vector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Pneumatic Post (docs/crates-design.md §10), through the real Sandbox: every Post crate
    /// opens onto one shared store, capped at 200 of each good; golems reach it like any crate;
    /// the player's spending takes the pack first, then the Post, and refunds land in the pack only;
    /// collecting goes to the pack only; demolishing a Post crate refunds its cost, never the
    /// store; and the store saves by its id.
    /// </summary>
    public class PneumaticPostTests
    {
        private static int Post(SandboxWorld world, string item) => world.Buffers.GetQuantity(SandboxWorld.PostBufferId, item);

        // Paid for from the pack, as the player would.
        private static ICrate PlacePost(SandboxWorld world, Vector2Int cell)
        {
            Give(world, ItemType.Brass, 10);
            Give(world, ItemType.Casing, 5);
            Give(world, ItemType.IronPlate, 40);
            return Place(world, "PneumaticPostPrefab", cell).GetPart<ICrate>();
        }

        [Test]
        public void TheStore_ExistsFromTheStart_Empty_AtTwoHundredOfEachGood()
        {
            SandboxWorld world = Compose();
            Assert.IsTrue(world.Buffers.TryGetBuffer(SandboxWorld.PostBufferId, out StorageBuffer post));
            Assert.AreEqual(0, post.Quantities.Values.Sum());
            Assert.AreEqual(200, post.CapacityPerType);
            Assert.AreEqual(100, world.Wallet.Pack.CapacityPerType, "the pack keeps its own cap");

            Assert.AreEqual(200, world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Scrap, 250), "capped per good");
            Assert.AreEqual(200, world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Coke, 200), "per good, not per buffer");
        }

        [Test]
        public void TwoPostCrates_OpenOntoOneStore_AndDontOwnIt()
        {
            SandboxWorld world = Compose();
            ICrate a = PlacePost(world, new Vector2Int(4, 4));
            ICrate b = PlacePost(world, new Vector2Int(8, 8));

            Assert.IsFalse(a.OwnsBuffer);
            Assert.AreEqual(SandboxWorld.PostBufferId, a.CrateId);
            Assert.AreSame(a.Buffer, b.Buffer);
            Assert.AreEqual("Pneumatic Post", CrateScreenReadout.Title(a));
            Assert.AreEqual("Shared by every Post crate · 200 of each good", CrateScreenReadout.CapLine(a));

            // What a golem's push does at one, a golem's haul finds at the other.
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(new Vector2Int(4, 4), out IItemEndpoint into));
            Assert.IsTrue(into.TryGive(new GolemFactory.Belts.ItemStack { ItemType = ItemType.Coke }));
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(new Vector2Int(8, 8), out IItemEndpoint outOf));
            Assert.IsTrue(outOf.TryTake(out GolemFactory.Belts.ItemStack taken));
            Assert.AreEqual(ItemType.Coke, taken.ItemType);
            Assert.AreEqual(0, Post(world, ItemType.Coke));
        }

        [Test]
        public void Spending_TakesThePackFirst_ThenThePost()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Brass, 4);
            Give(world, ItemType.IronPlate, 40);
            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Brass, 30);
            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Casing, 5);

            // A Post crate's own price: 10 Brass (4 from the pack, 6 from the Post), 5 Casing and 40 Iron Plate.
            Assert.AreEqual(34, world.Wallet.Quantity(ItemType.Brass));
            Place(world, "PneumaticPostPrefab", new Vector2Int(5, 5));
            Assert.AreEqual(0, Pack(world, ItemType.Brass), "the pack first");
            Assert.AreEqual(24, Post(world, ItemType.Brass), "then the Post");
            Assert.AreEqual(0, Post(world, ItemType.Casing));

            // The other spenders read the same wallet: the bench's inputs, the market, a claim.
            Assert.AreEqual(0, world.Wallet.Quantity(ItemType.Casing));
            Assert.IsFalse(world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Brass, 25) }), "short by one: nothing taken");
            Assert.AreEqual(24, Post(world, ItemType.Brass));
        }

        [Test]
        public void TheBench_CranksFromThePost_WhenThePackIsShort()
        {
            SandboxWorld world = Compose();
            HandCrankBench bench = world.StarterBench;
            RecipeDefinition recipe = bench.SelectedRecipe;
            Assert.IsNotNull(recipe);
            Assert.IsFalse(bench.CanAffordSelected(), "an empty wallet");
            foreach (RecipeIngredient input in recipe.inputs)
            {
                world.Buffers.Deposit(SandboxWorld.PostBufferId, input.itemType, input.quantity);
            }
            Assert.IsTrue(bench.CanAffordSelected(), "the Post pays a crank's inputs");
        }

        [Test]
        public void Refunds_GoIntoThePackOnly_NeverThePost()
        {
            // crates-design §11 (user, 2026-10-18): a refund is the pack's alone, as before the
            // Post, so the wrecking bar is no remote move into the store.
            SandboxWorld world = Compose();
            var cell = new Vector2Int(5, 5);
            PlacePost(world, cell);
            Give(world, ItemType.Brass, 96);

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(cell);
            Assert.AreEqual("Not demolished: pack full of Brass.", world.Build.LastStatusMessage,
                "an empty Post does not take the overflow");
            Assert.IsTrue(world.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == cell), "the Post crate stands");
            Assert.AreEqual(0, Post(world, ItemType.Brass));

            Assert.IsTrue(world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Brass, 6) }));
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();

            Assert.IsFalse(world.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == cell), "room made: demolished");
            Assert.AreEqual(100, Pack(world, ItemType.Brass), "the pack to its cap");
            Assert.AreEqual(0, Post(world, ItemType.Brass), "nothing into the Post");
            Assert.AreEqual(5, Pack(world, ItemType.Casing));
        }

        [Test]
        public void DemolishingAPostCrate_RefundsItsCostOnly_AndTheStoreStays()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(5, 5);
            PlacePost(world, cell);
            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Scrap, 150);

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();

            Assert.AreEqual(0, Pack(world, ItemType.Scrap), "the store's goods are not the crate's");
            Assert.AreEqual(150, Post(world, ItemType.Scrap));
            Assert.AreEqual(10, Pack(world, ItemType.Brass), "its cost comes back");
            Assert.IsTrue(world.Buffers.TryGetBuffer(SandboxWorld.PostBufferId, out _), "the store outlives its crates");
            Assert.AreEqual(150, PlacePost(world, new Vector2Int(9, 9)).Buffer.GetQuantity(ItemType.Scrap), "and the next Post crate opens onto it");
        }

        [Test]
        public void CollectingAtAPostCrate_TakesIntoThePackOnly()
        {
            SandboxWorld world = Compose();
            var cell = new Vector2Int(6, 6);
            PlacePost(world, cell);
            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Scrap, 150);
            Give(world, ItemType.Scrap, 60);

            world.Interactor.Position = new Vector3(cell.x, cell.y + 1f, 0f);
            world.Interactor.Poll();
            Assert.AreEqual(InteractionKind.Sort, world.Interactor.CurrentPick.Kind);
            StringAssert.Contains("Pneumatic Post", world.Interactor.CurrentPrompt);
            Assert.IsTrue(world.Interactor.Interact(), world.Interactor.LastStatusMessage);

            Assert.AreEqual(100, Pack(world, ItemType.Scrap), "what fits in the pack");
            Assert.AreEqual(110, Post(world, ItemType.Scrap), "the rest stays in the store");
        }

        [Test]
        public void AGolem_HaulsFromOnePostCrate_AndPushesIntoTheStoreThroughAnother()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;
            Give(world, ItemType.Coke, 100);
            Give(world, ItemType.Scrap, 30);
            Give(world, ItemType.IronPlate, 10);
            PlaceableBoiler boiler = Place(world, "BoilerPrefab", new Vector2Int(5, 5)).GetPart<PlaceableBoiler>();
            world.Interactor.TryRefuelBoiler(boiler);
            ICrate behind = PlacePost(world, new Vector2Int(4, 4));
            PlacePost(world, new Vector2Int(4, 6));
            ICrate elsewhere = PlacePost(world, new Vector2Int(10, 10));
            Give(world, ItemType.Scrap, 20);
            CrateTransfer.Put(world.Wallet.Pack, elsewhere, ItemType.Scrap, 20);
            Assert.AreEqual(20, behind.Buffer.GetQuantity(ItemType.Scrap), "fed at one crate, there at every one");

            Give(world, ItemType.Scrap, 100);
            Give(world, ItemType.IronPlate, 20);
            Give(world, ItemType.Gear, 10);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity presser));
            presser.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            presser.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            presser.Program.TryAddAppendage(defs.Appendages["AssembleScrapReclamation"]);
            presser.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            presser.SetPlacement(new Vector2Int(4, 5), Facing.North);
            int packScrap = Pack(world, ItemType.Scrap);

            world.Clock.Play();
            for (int frame = 0; frame < 300; frame++)
            {
                world.Advance(1f / 30f);
            }

            Assert.Less(Post(world, ItemType.Scrap), 20, "it hauled from the store");
            Assert.Greater(Post(world, ItemType.IronPlate), 0, "and pushed into it");
            Assert.AreEqual(Post(world, ItemType.IronPlate), elsewhere.Buffer.GetQuantity(ItemType.IronPlate), "seen from any Post crate");
            Assert.AreEqual(packScrap, Pack(world, ItemType.Scrap), "the pack stays out of a golem's reach");
        }

        [Test]
        public void ThePackScreen_ShowsTheStoreUnderThePack_OnlyOnceItHoldsSomething()
        {
            SandboxWorld world = Compose();
            CollectionAssert.IsEmpty(world.Inventory.PostRows(), "no header over an empty store");

            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Brass, 12);
            var rows = world.Inventory.PostRows();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(ItemType.Brass, rows[0].ItemType);
            Assert.AreEqual("12/200", rows[0].QuantityText);
        }

        [Test]
        public void TheLedger_RecordsThePost_OnceAPostCrateStands()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 15);
            Place(world, "DepotPrefab", new Vector2Int(3, 3));
            world.TechTree.Poll();
            Assert.IsFalse(world.TechTree.Ledger.HasBuilding(TechTreeCatalog.BuildingPneumaticPost), "a depot is not a Post crate");

            PlacePost(world, new Vector2Int(5, 5));
            world.TechTree.Poll();
            Assert.IsTrue(world.TechTree.Ledger.HasBuilding(TechTreeCatalog.BuildingPneumaticPost));
            TechTreeNode node = TechTreeCatalog.Nodes.Single(n => n.Id == "bldg.pneumaticpost");
            Assert.IsFalse(node.IsPlanned, "it shipped");
        }

        [Test]
        public void TheStore_AndItsCrates_SaveAndLoadExactly()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-post-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld world = Compose();
                PlacePost(world, new Vector2Int(4, 4));
                PlacePost(world, new Vector2Int(8, 8)).SetFilter(ItemType.Coke);
                world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Scrap, 150);
                world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Coke, 7);
                world.SaveTo(path);

                world.Buffers.TryWithdraw(SandboxWorld.PostBufferId, ItemType.Scrap, 150);
                world.Build.EnterDemolishMode();
                world.Build.PlaceOrRemove(new Vector2Int(4, 4));
                world.Build.CancelPlacement();
                world.LoadFrom(path);

                Assert.AreEqual(150, Post(world, ItemType.Scrap));
                Assert.AreEqual(7, Post(world, ItemType.Coke));
                var crates = world.Build.Buildings.Where(b => !b.IsRemoved).Select(b => b.GetPart<ICrate>())
                    .Where(c => c != null && !c.OwnsBuffer).ToList();
                Assert.AreEqual(2, crates.Count, "both Post crates rebuilt");
                Assert.IsTrue(crates.All(c => c.Buffer == crates[0].Buffer), "onto the one store");
                Assert.AreEqual(ItemType.Coke, crates.Single(c => c.FilterItemType != null).FilterItemType, "the label kept");

                world.SaveTo(path);
                world.LoadFrom(path);
                Assert.AreEqual(150, Post(world, ItemType.Scrap), "save/load/save/load changes nothing");
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
