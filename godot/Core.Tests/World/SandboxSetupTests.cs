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
        public void TheRealSetupLoads_NineStallsEachWithAnOffer()
        {
            SandboxSetup setup = LoadReal();
            Assert.AreEqual(9, setup.nodes.Count);
            CollectionAssert.AreEquivalent(
                setup.nodes.Select(n => n.id), setup.market.Select(m => m.nodeId),
                "every stall trades, and nothing trades that is not a stall");
            Assert.IsFalse(setup.creativeMode, "Sandbox ships in normal mode");
            Assert.IsTrue(setup.requireSteamPower, "Sandbox.unity turns steam power on");
        }

        [Test]
        public void NormalMode_StallsStartEmptyExceptTheSeededScrapStall()
        {
            SandboxSetup setup = LoadReal();
            var registry = new ResourceNodeRegistry();
            setup.RegisterNodes(registry);

            foreach (SandboxSetup.NodeEntry node in setup.nodes)
            {
                Assert.IsTrue(registry.TryGetNode(node.id, out ResourceNode registered), node.id);
                int expected = node.id == setup.freeStallSeedNode ? setup.freeStallSeedQuantity : 0;
                Assert.AreEqual(expected, registered.RemainingQuantity, node.id);
            }
            Assert.AreEqual(60, setup.freeStallSeedQuantity);
        }

        [Test]
        public void CreativeMode_StallsNeverRunDry()
        {
            SandboxSetup setup = LoadReal();
            setup.creativeMode = true;
            var registry = new ResourceNodeRegistry();
            setup.RegisterNodes(registry);

            foreach (SandboxSetup.NodeEntry node in setup.nodes)
            {
                registry.TryGetNode(node.id, out ResourceNode registered);
                Assert.AreEqual(ResourceNode.Infinite, registered.RemainingQuantity, node.id);
            }
        }

        [Test]
        public void TheStockpileIsUnlimitedAndProductionBuffersAreCapped()
        {
            SandboxSetup setup = LoadReal();
            var buffers = new StorageBufferRegistry();
            setup.ApplyBufferPolicy(buffers);

            Assert.AreEqual(100, buffers.DefaultCapacityPerType);
            Assert.AreEqual(StorageBuffer.Unlimited, buffers.GetOrCreate(setup.stockpileBufferId).CapacityPerType);
            Assert.AreEqual(100, buffers.GetOrCreate("SomeProductionBuffer").CapacityPerType);
        }

        [Test]
        public void TheMarketOffersWhatTheStallsSell()
        {
            SandboxSetup setup = LoadReal();
            var registry = new ResourceNodeRegistry();
            setup.RegisterNodes(registry);
            TruckloadMarket market = setup.BuildMarket(registry);

            Assert.IsTrue(market.TryGetOffer("ScrapNode", out MarketOffer scrap));
            Assert.AreEqual(0, scrap.Price.Count, "scrap is free");
            Assert.AreEqual(30, scrap.TruckloadSize);
            Assert.IsTrue(market.TryGetOffer("AetherNode", out MarketOffer aether));
            Assert.AreEqual(40, aether.Price.Single().quantity);
        }

        [Test]
        public void EveryStallSpriteExists()
        {
            string art = Path.Combine(Path.GetDirectoryName(AuthoredData.DataDirectory), "art");
            foreach (SandboxSetup.NodeEntry node in LoadReal().nodes)
            {
                FileAssert.Exists(Path.Combine(art, node.sprite + ".png"), node.id);
            }
        }
    }
}
