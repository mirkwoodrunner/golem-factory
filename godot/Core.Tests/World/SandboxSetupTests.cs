using System.IO;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    /// <summary>
    /// <c>godot/data/sandbox.json</c> and the rules SandboxBootstrap applied to it.
    /// </summary>
    public class SandboxSetupTests
    {
        internal static SandboxSetup LoadReal() =>
            SandboxSetup.Parse(File.ReadAllText(Path.Combine(AuthoredData.DataDirectory, "sandbox.json")));

        [Test]
        public void TheRealSetup_HasSeventeenSpots_AndStartsWithOneScrapCart()
        {
            // docs/supply-design.md §1: seventeen spots at the four-cell pitch, x -32 to 32.
            SandboxSetup setup = LoadReal();
            CollectionAssert.AreEqual(Enumerable.Range(0, 17).Select(i => -32 + 4 * i), setup.supply.spots.Select(s => s.x));
            Assert.IsTrue(setup.supply.spots.All(s => s.y == -16), "one row");
            Assert.AreEqual(1, setup.supply.startingCarts.Count);
            Assert.AreEqual(ItemType.Scrap, setup.supply.startingCarts[0].itemType);
            Assert.AreEqual(-8, setup.supply.startingCarts[0].x, "where the guide's first step points");
            Assert.IsFalse(setup.creativeMode, "Sandbox ships in normal mode");
            Assert.IsTrue(setup.requireSteamPower, "Sandbox.unity turns steam power on");
        }

        [Test]
        public void ANewWorld_HasOnlyTheStartingScrapCart_Full()
        {
            SandboxWorld world = Tests.EditMode.CrateWorld.Compose();
            Assert.AreEqual(1, world.Supply.Carts.Count());
            GolemFactory.Supply.SupplyCart scrap = world.Supply.SpotAt(new GolemFactory.Compat.Vector2Int(-8, -16)).Cart;
            Assert.AreEqual(ItemType.Scrap, scrap.ItemType);
            Assert.AreEqual(scrap.Capacity, scrap.Stock, "it starts full (§1)");
            Assert.AreEqual(1, world.Markers.Count, "no stalls beside it");
        }

        [Test]
        public void TheHomeSpots_AreTheOldStallsNearestTheCentre()
        {
            // §6: the guide marks Coal at -4, Copper Ore at 0, Zinc Ore at 4 and Aether at 8.
            SandboxSetup setup = LoadReal();
            Assert.AreEqual(new GolemFactory.Compat.Vector2Int(-8, -16), setup.HomeSpot(ItemType.Scrap));
            Assert.AreEqual(new GolemFactory.Compat.Vector2Int(-4, -16), setup.HomeSpot(ItemType.Coal));
            Assert.AreEqual(new GolemFactory.Compat.Vector2Int(0, -16), setup.HomeSpot(ItemType.CopperOre));
            Assert.AreEqual(new GolemFactory.Compat.Vector2Int(4, -16), setup.HomeSpot(ItemType.ZincOre));
            Assert.AreEqual(new GolemFactory.Compat.Vector2Int(8, -16), setup.HomeSpot(ItemType.Aether));
            Assert.IsNull(setup.HomeSpot(ItemType.Gear), "no cart sells it");
        }

        [Test]
        public void ThePackHoldsAHundredAndEveryOtherBufferIsAFiftyCrate()
        {
            // docs/crates-design.md §0.3 and §4.3: the crate is the default, the pack the override.
            SandboxSetup setup = LoadReal();
            var buffers = new StorageBufferRegistry();
            setup.ApplyBufferPolicy(buffers);

            Assert.AreEqual(50, buffers.DefaultCapacityPerType);
            Assert.AreEqual(100, buffers.GetOrCreate(setup.stockpileBufferId).CapacityPerType);
            Assert.AreEqual(50, buffers.GetOrCreate("Crate(3,-2)").CapacityPerType);
        }

        [Test]
        public void EveryCartsSpriteExists()
        {
            string art = Path.Combine(Path.GetDirectoryName(AuthoredData.DataDirectory), "art");
            FileAssert.Exists(Path.Combine(art, GolemFactory.Supply.SupplyArt.SpotSprite + ".png"));
            foreach (SandboxSetup.CartLadderEntry cart in LoadReal().supply.carts)
            {
                FileAssert.Exists(Path.Combine(art, GolemFactory.Supply.SupplyArt.CartSprite(cart.itemType) + ".png"), cart.itemType);
            }
        }
    }
}
