using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.EditMode;
using GolemFactory.World;

namespace GolemFactory.Tests.World
{
    /// <summary>docs/menus-design.md §9: Creative Mode, decision 2.</summary>
    public class CreativeTests
    {
        private string _folder;

        [SetUp]
        public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "golem-factory-creative-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        public static SandboxWorld Compose(bool creative)
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxSetup setup = SandboxSetupTests.LoadReal();
            setup.creativeMode = creative;
            return SandboxWorld.Compose(definitions, setup, PlaceableCatalogTests.LoadReal(definitions));
        }

        private static int Pack(SandboxWorld world, string item) => world.Buffers.GetQuantity(world.StockpileBufferId, item);

        [Test]
        public void ACreativeWorld_HasEverything_AndNoGuideOrTower()
        {
            SandboxWorld world = Compose(creative: true);
            try
            {
                Assert.IsTrue(world.Creative);
                Assert.IsNull(world.Tutorial, "no guide");
                Assert.IsTrue(world.Buffers.CostsWaived);

                string user = world.Setup.assemblyLine.claimUserId;
                var deck = world.Definitions.Decks[world.Setup.assemblyLine.deck];
                var claimed = world.AssemblyLine.GetClaimedCards(user);
                Assert.IsTrue(deck.Cards.Concat(deck.OpeningHand).All(claimed.Contains), "every card in the deck is claimed");
                Assert.IsTrue(deck.Cards.Where(c => c.appendage != null).All(c => world.Workbench.IsCardAvailable(c.appendage)),
                    "and on the Workbench");
                Assert.IsTrue(world.Definitions.Chassis.Values.All(world.Workbench.IsChassisAvailable), "every chassis on the rack");
                Assert.IsTrue(ItemTiers.CanonicalOrder.All(world.TechTree.Ledger.HasItem), "every good is seen, so Haul offers it");
                Assert.IsTrue(world.Definitions.Chassis.Keys.All(world.TechTree.Ledger.HasChassis));

                Assert.IsFalse(world.ClockTower.Site.IsOpen);
                Assert.AreEqual(SandboxWorld.CreativeTowerReason, world.ClockTower.Site.ClosedReason);

                int cap = world.Wallet.PackCapacity;
                Assert.IsTrue(ItemTiers.CanonicalOrder.All(i => Pack(world, i) == cap), "the pack holds every good, full");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void NothingCostsAThing_AndNothingIsRefused()
        {
            SandboxWorld world = Compose(creative: true);
            try
            {
                int scrap = Pack(world, ItemType.Scrap);
                PlaceableBuilding boiler = CrateWorld.Place(world, "BoilerPrefab", new Vector2Int(4, 4));
                Assert.IsNotNull(boiler);
                Assert.AreEqual(scrap, Pack(world, ItemType.Scrap), "building charged nothing");

                ChassisDefinition zeppelin = world.Definitions.Chassis[SandboxWorld.ZeppelinChassis];
                Assert.IsTrue(world.StarterStation.TryConstructGolem(zeppelin, out GolemEntity golem), "the dearest chassis, at once");
                Assert.IsNotNull(golem);

                // Even an empty pack affords everything: the waiver, not the stock, says yes.
                foreach (string item in ItemTiers.CanonicalOrder)
                {
                    world.Buffers.TryWithdraw(world.StockpileBufferId, item, Pack(world, item));
                }
                Assert.IsTrue(world.Wallet.CanAfford(zeppelin.cost));
                Assert.IsTrue(world.StarterStation.CanAfford(zeppelin));
                Assert.IsTrue(world.FloorExpansion.CanAfford());
                Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor(), world.AssemblyLineBoard.Status);
                Assert.IsTrue(world.AssemblyLineBoard.UpgradePack(), world.AssemblyLineBoard.Status);
                Assert.IsTrue(world.AssemblyLineBoard.UpgradeBays(), world.AssemblyLineBoard.Status);

                // A demolition is never refused for a full pack: a refund is nothing.
                world.Advance(SandboxWorld.CreativeRefillSeconds + 0.01f);
                Assert.AreEqual(world.Wallet.PackCapacity, Pack(world, ItemType.Scrap), "the pack refilled");
                world.Build.EnterDemolishMode();
                world.Build.PlaceOrRemove(new Vector2Int(4, 4));
                world.Build.CancelPlacement();
                Assert.IsTrue(boiler.IsRemoved, world.Build.LastStatusMessage);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ACampaignGame_IsAsItWas()
        {
            SandboxWorld world = Compose(creative: false);
            try
            {
                Assert.IsFalse(world.Creative);
                Assert.IsFalse(world.Buffers.CostsWaived);
                Assert.IsNotNull(world.Tutorial);
                CrateWorld.Give(world, ItemType.Scrap, 15);
                int before = Pack(world, ItemType.Scrap);
                CrateWorld.Place(world, "DepotPrefab", new Vector2Int(6, 6));
                Assert.AreEqual(before - 15, Pack(world, ItemType.Scrap));
                Assert.IsFalse(world.Wallet.CanAfford(world.Definitions.Chassis[SandboxWorld.ZeppelinChassis].cost));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void TheTwoKindsOfSave_NeverLoadAsEachOther()
        {
            SandboxWorld creative = Compose(creative: true);
            SandboxWorld campaign = Compose(creative: false);
            try
            {
                string creativeSave = Path.Combine(_folder, "creative.json");
                string campaignSave = Path.Combine(_folder, "campaign.json");
                CrateWorld.Place(creative, "DepotPrefab", new Vector2Int(6, 6));
                creative.SaveTo(creativeSave);
                campaign.SaveTo(campaignSave);
                Assert.IsTrue(SaveFileIO.ReadFromFile(creativeSave).creative);
                Assert.IsFalse(SaveFileIO.ReadFromFile(campaignSave).creative);

                int loads = campaign.LoadsMade;
                Assert.AreEqual(SandboxWorld.WrongKind(saveIsCreative: true), campaign.LoadFrom(creativeSave));
                Assert.AreEqual(loads, campaign.LoadsMade, "a refusal isn't a load");
                Assert.IsFalse(campaign.Build.Buildings.Any(b => !b.IsRemoved), "and changed nothing");

                Assert.AreEqual(SandboxWorld.WrongKind(saveIsCreative: false), creative.LoadFrom(campaignSave));
                StringAssert.StartsWith("Loaded", creative.LoadFrom(creativeSave));

                // In a slot folder, a save of the other kind is never offered.
                Directory.CreateDirectory(Path.Combine(_folder, "slots"));
                File.Copy(campaignSave, SaveSlots.PathIn(Path.Combine(_folder, "slots"), "slot-1"));
                File.Copy(creativeSave, SaveSlots.PathIn(Path.Combine(_folder, "slots"), "slot-2"));
                var asCreative = SaveSlots.Read(Path.Combine(_folder, "slots"), creative: true);
                Assert.IsFalse(asCreative.Single(s => s.Key == "slot-1").Readable);
                Assert.IsTrue(asCreative.Single(s => s.Key == "slot-2").Readable);
                Assert.IsTrue(asCreative.All(s => s.Creative));
                Assert.IsFalse(SaveSlots.Read(Path.Combine(_folder, "slots")).Single(s => s.Key == "slot-2").Readable);
            }
            finally
            {
                creative.Dispose();
                campaign.Dispose();
            }
        }

        [Test]
        public void TheTitleOffersCreative_AndSaysWhichWorldContinueOpens()
        {
            var now = new DateTime(2026, 11, 5, 16, 0, 0);
            var menu = new TitleMenu(null, now);
            Assert.IsTrue(menu.IsEnabled(TitleChoice.Creative));
            Assert.AreEqual("Creative", TitleMenu.Label(TitleChoice.Creative));
            Assert.AreEqual(TitleMenu.CreativeDetail, menu.Detail(TitleChoice.Creative));

            var world = new SaveSummary("c.json", new DateTime(2026, 11, 5, 15, 0, 0), creative: true);
            var game = new SaveSummary("g.json", new DateTime(2026, 11, 5, 9, 0, 0));
            var both = new TitleMenu(new[] { game, world }, now);
            Assert.IsTrue(both.Newest?.Creative == true);
            Assert.AreEqual("Creative, today at 15:00", both.Detail(TitleChoice.Continue));
            Assert.AreEqual("2 saves", both.Detail(TitleChoice.Load));
        }

        [Test]
        public void TheLaunchCarriesCreative_NeverToTheTitle()
        {
            GameLaunch.Reset();
            try
            {
                GameLaunch.Request(LaunchKind.NewGame, creative: true);
                GameLaunch.Take(underScenario: false);
                Assert.IsTrue(GameLaunch.Creative);
                GameLaunch.Request(LaunchKind.Title, creative: true);
                GameLaunch.Take(underScenario: false);
                Assert.IsFalse(GameLaunch.Creative, "the showcase is the guide's");
                GameLaunch.Take(underScenario: true);
                Assert.IsFalse(GameLaunch.Creative, "used up");
            }
            finally
            {
                GameLaunch.Reset();
            }
        }
    }
}
