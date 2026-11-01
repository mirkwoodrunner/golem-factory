using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Pack upgrades (docs/pack-upgrades-design.md): the pack's cap ladder, charged through the
    /// wallet, only growing in play, saved by rung, and the Assembly Line tab's Pack row.
    /// </summary>
    public class PackUpgradeTests
    {
        private static void GiveRung1(SandboxWorld world)
        {
            Give(world, ItemType.IronPlate, 60);
            Give(world, ItemType.Gear, 30);
        }

        [Test]
        public void ARung_RaisesTheCap_AndChargesThePackThenThePost()
        {
            SandboxWorld world = Compose();
            Assert.AreEqual(100, world.PackUpgrade.CapacityPerType);
            Give(world, ItemType.IronPlate, 40);
            Give(world, ItemType.Gear, 30);
            world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.IronPlate, 50);

            Assert.IsTrue(world.PackUpgrade.TryUpgrade(), world.PackUpgrade.LastStatusMessage);

            Assert.AreEqual(1, world.PackUpgrade.Tier);
            Assert.AreEqual(150, world.Wallet.Pack.CapacityPerType);
            Assert.AreEqual(0, Pack(world, ItemType.IronPlate), "the pack first");
            Assert.AreEqual(30, world.Buffers.GetQuantity(SandboxWorld.PostBufferId, ItemType.IronPlate), "then the Post");
            Assert.AreEqual(0, Pack(world, ItemType.Gear));
            Assert.AreEqual("Carries 150 of each good", world.Inventory.PackCapLine);
            Assert.AreEqual(150, world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 500), "the pack takes 150 now");
        }

        [Test]
        public void AShortWallet_IsRefused_AndNothingMoves()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.IronPlate, 60);
            Give(world, ItemType.Gear, 29);

            Assert.IsFalse(world.PackUpgrade.TryUpgrade());

            Assert.AreEqual(0, world.PackUpgrade.Tier);
            Assert.AreEqual(100, world.Wallet.Pack.CapacityPerType);
            Assert.AreEqual(60, Pack(world, ItemType.IronPlate));
            Assert.AreEqual(29, Pack(world, ItemType.Gear));
            StringAssert.Contains("Gear", world.PackUpgrade.LastStatusMessage);
        }

        [Test]
        public void TheTopRung_OffersNothing()
        {
            SandboxWorld world = Compose();
            world.PackUpgrade.Restore(world.PackUpgrade.Rungs.Count);

            Assert.IsTrue(world.PackUpgrade.IsMaxed);
            Assert.AreEqual(250, world.Wallet.Pack.CapacityPerType);
            Assert.IsEmpty(world.PackUpgrade.NextCost());
            Assert.IsFalse(world.PackUpgrade.TryUpgrade());
            Assert.AreEqual(250, world.Wallet.Pack.CapacityPerType);

            AssemblyLineRow row = world.AssemblyLineBoard.Rows().Single(r => r.Kind == AssemblyLineRowKind.Pack);
            Assert.AreEqual("Pack  ·  carries 250 of each good", row.Text);
            Assert.IsNull(row.ButtonLabel, "no button at the top");
        }

        [Test]
        public void AnUpgrade_NeverLowersAnUncappedPack()
        {
            var buffers = new StorageBufferRegistry();
            var service = new PackUpgradeService();
            service.Configure(buffers, "Pack", null, 100, new[]
            {
                new PackUpgradeRung { capacityPerType = 150, cost = { new RecipeIngredient(ItemType.Scrap, 1) } },
            });
            buffers.Deposit("Pack", ItemType.Scrap, 1);

            Assert.IsTrue(service.TryUpgrade());
            Assert.AreEqual(StorageBuffer.Unlimited, buffers.GetOrCreate("Pack").CapacityPerType);
        }

        [Test]
        public void ALoadOfALowerTier_KeepsAboveCapGoods_SpendableButNotAddable()
        {
            SandboxWorld world = Compose();
            GiveRung1(world);
            Assert.IsTrue(world.PackUpgrade.TryUpgrade());
            Give(world, ItemType.Scrap, 140);

            world.PackUpgrade.Restore(0);

            Assert.AreEqual(100, world.Wallet.Pack.CapacityPerType);
            Assert.AreEqual(140, Pack(world, ItemType.Scrap), "nothing clamps");
            Assert.AreEqual(0, world.Wallet.RoomFor(ItemType.Scrap));
            Assert.IsTrue(world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 30) }));
            Assert.AreEqual(110, Pack(world, ItemType.Scrap));
        }

        [Test]
        public void TheTier_RoundTripsThroughASave_AndAnOlderSaveLoadsAtTheBase()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-pack-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld world = Compose();
                GiveRung1(world);
                Assert.IsTrue(world.PackUpgrade.TryUpgrade());
                Give(world, ItemType.Scrap, 140);
                world.SaveTo(path);

                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);
                Assert.AreEqual(1, fresh.PackUpgrade.Tier);
                Assert.AreEqual(150, fresh.Wallet.Pack.CapacityPerType);
                Assert.AreEqual(140, Pack(fresh, ItemType.Scrap));

                // A save from before pack upgrades has no packUpgradeTier: the base cap, nothing clamped.
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"packUpgradeTier\"", "\"_droppedField\""));
                fresh.LoadFrom(path);
                Assert.AreEqual(0, fresh.PackUpgrade.Tier);
                Assert.AreEqual(100, fresh.Wallet.Pack.CapacityPerType);
                Assert.AreEqual(140, Pack(fresh, ItemType.Scrap));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ThePackRow_ShowsTheRung_AndALockedPriceReadsRequires()
        {
            SandboxWorld world = Compose();
            AssemblyLineRow Row() => world.AssemblyLineBoard.Rows().Single(r => r.Kind == AssemblyLineRowKind.Pack);

            Assert.AreEqual("Pack  ·  carries 100 of each good  ·  +50", Row().Text);
            Assert.AreEqual("Upgrade", Row().ButtonLabel);
            Assert.AreEqual("Requires: Iron Plate, Gear", Row().CostText, "a fresh factory has made neither");
            Assert.IsFalse(Row().Affordable);

            GiveRung1(world);
            world.TechTree.Poll();
            Assert.AreEqual("60 Iron Plate  +  30 Gear", Row().CostText);
            Assert.IsTrue(Row().Affordable);

            Assert.IsTrue(world.AssemblyLineBoard.UpgradePack(), world.AssemblyLineBoard.Status);
            Assert.AreEqual("Pack  ·  carries 150 of each good  ·  +50", Row().Text);
            Assert.AreEqual("Requires: Brass", Row().CostText);
        }

        [Test]
        public void TheLedger_HasABiggerPackNode_AfterGearCutting()
        {
            // docs/pack-upgrades-design.md §5 and §8.10.
            Assert.IsTrue(TechTreeCatalog.TryGetNode("pack.upgrades", out TechTreeNode node));
            Assert.AreEqual("A Bigger Pack", node.DisplayName);
            Assert.AreEqual(TechTreeNodeKind.Technique, node.Kind);
            Assert.AreEqual(TechTreeUnlockSignal.None, node.Signal);
            Assert.AreEqual(1, node.PhaseIndex);
            Assert.AreEqual(8, node.Row);
            CollectionAssert.AreEqual(new[] { "r8.gearcutting" }, node.Prerequisites);
            Assert.AreEqual(47, TechTreeCatalog.Nodes.Count);
        }

        [Test]
        public void AnUpgrade_GrowsThePackAlone_CratesStayAt50_AndThePostAt200()
        {
            // §0.7: a crate's size is what makes layout matter; the Post is a factory store.
            SandboxWorld world = Compose();
            ICrate crate = PlaceDepot(world, new Vector2Int(6, 6));
            GiveRung1(world);

            Assert.IsTrue(world.PackUpgrade.TryUpgrade(), world.PackUpgrade.LastStatusMessage);

            Assert.AreEqual(150, world.Wallet.Pack.CapacityPerType);
            Assert.AreEqual(50, crate.Buffer.CapacityPerType, "a crate keeps 50");
            Assert.AreEqual(200, world.Buffers.GetOrCreate(SandboxWorld.PostBufferId).CapacityPerType, "the Post keeps 200");
        }

        [Test]
        public void ADemolitionTheOldCapRefused_FitsAfterTheUpgrade_AndRefundsInFull()
        {
            // §0.6: still a full refund into the pack; RoomFor reads the live cap, so a bigger
            // pack means fewer refusals.
            SandboxWorld world = Compose();
            var cell = new Vector2Int(6, 6);
            ICrate crate = PlaceDepot(world, cell);
            PlaceableBuilding depot = world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell);
            crate.Buffer.Deposit(ItemType.Scrap, 20);
            Give(world, ItemType.Scrap, 100);

            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(cell);
            Assert.IsFalse(depot.IsRemoved, "at 100 the 15 + 20 Scrap won't fit");

            GiveRung1(world);
            Assert.IsTrue(world.PackUpgrade.TryUpgrade(), world.PackUpgrade.LastStatusMessage);
            world.Build.PlaceOrRemove(cell);

            Assert.IsTrue(depot.IsRemoved, "150 has room");
            Assert.AreEqual(135, Pack(world, ItemType.Scrap), "100 + 15 cost + 20 contents");
        }

        [Test]
        public void TheSecondExtension_NeedsTheUpgrade()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 80);
            Give(world, ItemType.IronPlate, 40);
            Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor(), world.AssemblyLineBoard.Status);

            Give(world, ItemType.Scrap, 500);
            Give(world, ItemType.IronPlate, 60);
            Give(world, ItemType.Gear, 30);
            Assert.AreEqual(100, Pack(world, ItemType.Scrap), "a 100 pack");
            Assert.IsFalse(world.FloorExpansion.CanAfford(), "120 Scrap is more than it holds");

            Assert.IsTrue(world.PackUpgrade.TryUpgrade(), world.PackUpgrade.LastStatusMessage);
            Give(world, ItemType.Scrap, 500);
            GiveRung1(world);
            Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor(), world.AssemblyLineBoard.Status);
        }
    }
}
