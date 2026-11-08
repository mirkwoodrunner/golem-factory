using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Supply;
using GolemFactory.Tests.EditMode;
using GolemFactory.Tests.World;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.EndlessPlay
{
    /// <summary>docs/endless-design.md §2, §4: the rungs past the campaign's, offered once the tower stands.</summary>
    public class EndlessRungTests
    {
        private SandboxWorld _world;

        [SetUp]
        public void SetUp() => _world = CrateWorld.Compose();

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void CompleteTheTower() =>
            _world.ClockTower.Site.RestoreProgress(_world.ClockTower.Site.StageCount, 0L, true);

        private CartSpot CoalCart()
        {
            CartSpot spot = _world.Supply.Spots.First(s => s.IsEmpty);
            Assert.IsTrue(_world.GrantCart(spot.Cell, ItemType.Coal).Done);
            return spot;
        }

        // Rungs given, as a contract gives them: the climb is what is under test, not its price.
        private void Climb(CartSpot spot, bool rate, int rungs)
        {
            for (int i = 0; i < rungs; i++)
            {
                Assert.IsTrue(_world.Supply.GiveRung(spot, rate).Done, $"rung {i + 1}");
            }
        }

        private static int Sum(IEnumerable<RecipeIngredient> price, string good) => price.Where(p => p.itemType == good).Sum(p => p.quantity);

        [Test]
        public void BeforeTheTower_EveryLadderStopsAtTheCampaignsTop()
        {
            Assert.IsFalse(_world.EndlessOpen);

            _world.PackUpgrade.Restore(3);
            Assert.IsTrue(_world.PackUpgrade.IsMaxed, "the pack at 250");

            CartSpot coal = CoalCart();
            Climb(coal, rate: true, 3);
            Climb(coal, rate: false, 3);
            Assert.IsNull(SupplyStreet.NextRateCost(coal.Cart), "rung 3 is the campaign's top");
            Assert.IsNull(SupplyStreet.NextCapacityCost(coal.Cart));

            _world.AssemblyBay.RestoreTier(AssemblyBayStructure.TierFor(100));
            Assert.AreEqual(100, _world.AssemblyBay.MaxGolemSlots);
            Assert.IsTrue(_world.AssemblyBay.IsMaxed);
            Assert.IsFalse(_world.AssemblyBay.TryUpgrade(_world.Buffers, _world.StockpileBufferId, SandboxWorld.PostBufferId));
            AssemblyLineRow bay = _world.AssemblyLineBoard.Rows().Single(r => r.Kind == AssemblyLineRowKind.Bay);
            StringAssert.EndsWith("more once the Clock Tower stands", bay.Text);
            Assert.IsNull(bay.ButtonLabel, "no button that always refuses");
        }

        [Test]
        public void OnceTheTowerStands_ThePackGrowsTo350_InTowerGoods()
        {
            CompleteTheTower();
            Assert.IsTrue(_world.EndlessOpen, "no switch: a complete tower is endless play");
            _world.PackUpgrade.Restore(3);
            Assert.IsFalse(_world.PackUpgrade.IsMaxed);
            Assert.AreEqual(300, _world.PackUpgrade.NextCapacity);
            Assert.Greater(Sum(_world.PackUpgrade.NextCost(), ItemType.Mechanism) + Sum(_world.PackUpgrade.NextCost(), ItemType.Regulator), 0,
                "priced in Mechanisms and Regulators (pack-upgrades-design §7 Q4)");

            EndgameLoad.Grant(_world, _world.PackUpgrade.NextCost());
            Assert.IsTrue(_world.PackUpgrade.TryUpgrade(), _world.PackUpgrade.LastStatusMessage);
            Assert.AreEqual(300, _world.Wallet.Pack.CapacityPerType);
            EndgameLoad.Grant(_world, _world.PackUpgrade.NextCost());
            Assert.IsTrue(_world.PackUpgrade.TryUpgrade(), _world.PackUpgrade.LastStatusMessage);
            Assert.AreEqual(350, _world.Wallet.Pack.CapacityPerType);
            Assert.IsTrue(_world.PackUpgrade.IsMaxed);
        }

        [Test]
        public void OnceTheTowerStands_ACartClimbsTwoMoreRungs()
        {
            CompleteTheTower();
            CartSpot coal = CoalCart();
            Climb(coal, rate: true, 3);
            int rate = coal.Cart.RatePerMinute;
            IReadOnlyList<RecipeIngredient> rung4 = SupplyStreet.NextRateCost(coal.Cart);
            Assert.IsNotNull(rung4);
            Assert.Greater(Sum(rung4, ItemType.Regulator), 0, "rung 4 in Regulators");
            EndgameLoad.Grant(_world, rung4);
            Assert.IsTrue(_world.Supply.UpgradeRate(coal).Done);
            Assert.Greater(coal.Cart.RatePerMinute, rate);
            IReadOnlyList<RecipeIngredient> rung5 = SupplyStreet.NextRateCost(coal.Cart);
            Assert.Greater(Sum(rung5, ItemType.ChronometerCore), 0, "then Chronometer Cores");
            EndgameLoad.Grant(_world, rung5);
            Assert.IsTrue(_world.Supply.UpgradeRate(coal).Done);
            Assert.IsNull(SupplyStreet.NextRateCost(coal.Cart), "rung 5 is the top");

            SupplyRow row = _world.SupplyPanel.Rows(coal).First(r => r.Action == SupplyAction.Rate);
            Assert.IsFalse(row.Enabled);
            Assert.AreEqual("At the top", row.Price);
        }

        [Test]
        public void OnceTheTowerStands_TheBaysGrowToTheGolemCap_AndNoFurther()
        {
            CompleteTheTower();
            Assert.AreEqual(112, _world.AssemblyBay.GolemCap, "docs/performance-report-endless.md");
            _world.AssemblyBay.RestoreTier(AssemblyBayStructure.TierFor(100));
            IReadOnlyList<RecipeIngredient> tier = _world.AssemblyBay.UpgradeCost;
            Assert.Greater(Sum(tier, ItemType.GreatCog), 0, "priced in Mechanisms and Great Cogs");
            while (!_world.AssemblyBay.IsMaxed)
            {
                EndgameLoad.Grant(_world, _world.AssemblyBay.UpgradeCost);
                Assert.IsTrue(_world.AssemblyBay.TryUpgrade(_world.Buffers, _world.StockpileBufferId, SandboxWorld.PostBufferId));
            }
            Assert.AreEqual(112, _world.AssemblyBay.MaxGolemSlots);
            // Refused by the cap, not the wallet: whatever it would charge is in the pack.
            EndgameLoad.Grant(_world, _world.AssemblyBay.UpgradeCost);
            Assert.IsFalse(_world.AssemblyBay.TryUpgrade(_world.Buffers, _world.StockpileBufferId, SandboxWorld.PostBufferId));
            Assert.AreEqual(112, _world.AssemblyBay.MaxGolemSlots, "no further");
            AssemblyLineRow bay = _world.AssemblyLineBoard.Rows().Single(r => r.Kind == AssemblyLineRowKind.Bay);
            StringAssert.EndsWith("the most the city allows", bay.Text);
        }

        [Test]
        public void EveryEndlessPrice_FitsThePackWhenItOpens()
        {
            // Invariant 7: a price never asks more of a good than the pack can hold when it is offered.
            // Endless rungs open with the pack at 250; the pack's own 350 opens at 300.
            SandboxSetup setup = SandboxSetupTests.LoadReal();
            int cap = 250;
            foreach (PackUpgradeRung rung in setup.packUpgrades.Where(r => r.endless))
            {
                Assert.IsTrue(rung.cost.All(c => c.quantity <= cap), $"pack {rung.capacityPerType}");
                cap = rung.capacityPerType;
            }
            foreach (SandboxSetup.CartLadderEntry cart in setup.supply.carts)
            {
                foreach (List<RecipeIngredient> price in cart.rateCosts.Skip(cart.campaignRungs).Concat(cart.capacityCosts.Skip(cart.campaignRungs)))
                {
                    Assert.IsTrue(price.All(c => c.quantity <= 250), cart.itemType);
                }
            }
            foreach (List<RecipeIngredient> price in setup.assemblyBays.endless)
            {
                Assert.IsTrue(price.All(c => c.quantity <= 250), "a bay tier");
            }
        }

        [Test]
        public void CreativeOffersEveryRung_ForNothing()
        {
            SandboxWorld creative = CreativeTests.Compose(creative: true);
            try
            {
                Assert.IsTrue(creative.EndlessOpen);
                while (!creative.PackUpgrade.IsMaxed)
                {
                    Assert.IsTrue(creative.PackUpgrade.TryUpgrade(), creative.PackUpgrade.LastStatusMessage);
                }
                Assert.AreEqual(350, creative.Wallet.Pack.CapacityPerType);
            }
            finally
            {
                creative.Dispose();
            }
        }

        [Test]
        public void ALoad_PutsBackEveryEndlessRung_Exactly()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-endless-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                CompleteTheTower();
                _world.PackUpgrade.Restore(5);
                _world.AssemblyBay.RestoreTier(AssemblyBayStructure.TierFor(112));
                CartSpot coal = CoalCart();
                Climb(coal, rate: true, 5);
                Climb(coal, rate: false, 4);
                _world.SaveTo(path);

                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    Assert.IsTrue(loaded.EndlessOpen, "the tower came back complete");
                    Assert.AreEqual(5, loaded.PackUpgrade.Tier);
                    Assert.AreEqual(350, loaded.Wallet.Pack.CapacityPerType);
                    Assert.AreEqual(112, loaded.AssemblyBay.MaxGolemSlots);
                    SupplyCart cart = loaded.Supply.SpotAt(coal.Cell).Cart;
                    Assert.AreEqual(5, cart.RateRung);
                    Assert.AreEqual(4, cart.CapacityRung);
                }
                finally
                {
                    loaded.Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
