using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Contracts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.PunchCards;
using GolemFactory.Tests.EditMode;
using GolemFactory.Tests.World;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Contracts
{
    /// <summary>docs/endless-design.md §1.1: contracts after the authored sequence, from a formula.</summary>
    public class GeneratedContractTests
    {
        private static ContractGenerator Generator()
        {
            SandboxSetup setup = SandboxSetupTests.LoadReal();
            return new ContractGenerator(setup.endlessContracts, AuthoredData.Load().Recipes.Values);
        }

        private static double Raw(ContractGenerator g, IEnumerable<RecipeIngredient> goods) => goods.Sum(x => g.Raw(x.itemType) * x.quantity);

        [Test]
        public void TheSameIndex_IsTheSameContract_InEveryGame()
        {
            // Invariant 4: deterministic, so a test can play any point of it.
            ContractGenerator a = Generator(), b = Generator();
            for (int k = 0; k < 40; k++)
            {
                ContractDefinition x = a.Generate(k), y = b.Generate(k);
                Assert.AreEqual(x.name, y.name);
                Assert.AreEqual(x.requester, y.requester);
                Assert.AreEqual(x.words, y.words);
                CollectionAssert.AreEqual(x.asks.Select(s => s.itemType + s.quantity), y.asks.Select(s => s.itemType + s.quantity));
                Assert.AreSame(x, a.Generate(k), "asked again, the same object");
            }
        }

        [Test]
        public void ItRotatesWhoAsksAndWhat_ThroughTheTowersGoodsToo()
        {
            ContractGenerator g = Generator();
            var asked = new HashSet<string>();
            var requesters = new HashSet<string>();
            for (int k = 0; k < 10; k++)
            {
                ContractDefinition c = g.Generate(k);
                Assert.That(c.asks.Count, Is.InRange(1, 2), c.name);
                CollectionAssert.AllItemsAreUnique(c.asks.Select(a => a.itemType));
                asked.UnionWith(c.asks.Select(a => a.itemType));
                requesters.Add(c.requester);
                Assert.IsTrue(c.postWhenTowerComplete);
                CollectionAssert.IsSubsetOf(c.asks.Select(a => a.itemType), c.postWhenSeen);
            }
            CollectionAssert.IsSubsetOf(new[] { ItemType.FrameSection, ItemType.GreatCog, ItemType.AetherConduit, ItemType.ChronometerCore }, asked,
                "the tower's goods have a use again");
            Assert.AreEqual(5, requesters.Count, "every guild and office asks in turn");
        }

        [Test]
        public void AmountsGrow_ThenLevelOffAtAPlateau()
        {
            // §6 Q2: level off, so every contract stays fillable at the golem cap.
            ContractGenerator g = Generator();
            int growth = g.Setup.growthContracts;
            for (int k = 1; k < growth; k++)
            {
                Assert.Greater(g.BudgetRaw(k), g.BudgetRaw(k - 1), $"contract {k + 1} asks more than {k}");
            }
            for (int k = growth; k < 200; k++)
            {
                Assert.AreEqual(g.Setup.plateauRaw, g.BudgetRaw(k), $"contract {k + 1} holds the plateau");
                Assert.LessOrEqual(Raw(g, g.Generate(k).asks), g.Setup.plateauRaw, "never asks past the plateau");
            }
            // Ten minutes at the golem cap: stage 4's demand, about 1,390 raw goods a minute, is what
            // a campaign factory already sustains, and the plateau is under a quarter of it.
            Assert.Less(g.Setup.plateauRaw, 1390 * 10 / 4);
        }

        [Test]
        public void NoRewardOrBonusBeatsItsContract()
        {
            // contracts-design §4, as the authored ones: goods given never exceed goods asked, in raw goods.
            ContractGenerator g = Generator();
            for (int k = 0; k < 60; k++)
            {
                ContractDefinition c = g.Generate(k);
                Assert.IsFalse(c.reward.IsEmpty, c.name);
                Assert.LessOrEqual(Raw(g, c.reward.goods) + Raw(g, c.bonus.goods), Raw(g, c.asks), c.name);
            }
        }

        [Test]
        public void TheFirstBonusesAreEndlessCartRungs_ThenGoods()
        {
            ContractGenerator g = Generator();
            for (int k = 0; k < g.Setup.rungBonuses; k++)
            {
                Assert.IsTrue(g.Generate(k).bonus.HasRung, $"contract {k + 1}");
            }
            Assert.IsFalse(g.Generate(g.Setup.rungBonuses).bonus.HasRung);
            Assert.IsNotEmpty(g.Generate(g.Setup.rungBonuses).bonus.goods);
            // Rate then Capacity on every cart's good: each of the 20 endless rungs can be given.
            var rungs = Enumerable.Range(0, g.Setup.rungBonuses).Select(k => g.Generate(k).bonus).Select(b => b.rungGood + b.rungKind).ToList();
            Assert.AreEqual(10, rungs.Distinct().Count(), "five goods, two ladders");
        }

        [Test]
        public void ThePacksEndlessRungs_AreNeeded_ToCarryAContractsGoodInOneTrip()
        {
            // The pack's 300 and 350 (endless-design §2) have nothing to buy that needs them; what
            // needs them is a contract filled by hand: at the plateau some single good asks more
            // than 250, and some more than 300, so each rung is a trip saved.
            ContractGenerator g = Generator();
            List<int> amounts = Enumerable.Range(g.Setup.growthContracts, 20).SelectMany(k => g.Generate(k).asks).Select(a => a.quantity).ToList();
            Assert.IsTrue(amounts.Any(n => n > 250 && n <= 300), "the 300 rung carries one: " + string.Join(",", amounts));
            Assert.IsTrue(amounts.Any(n => n > 300 && n <= 350), "the 350 rung carries one");
            Assert.IsTrue(amounts.All(n => n <= 350), "and none needs more than the top rung");
        }

        [Test]
        public void EveryText_UsesTheFontsGlyphs_AndNoId()
        {
            ContractGenerator g = Generator();
            for (int k = 0; k < 40; k++)
            {
                ContractDefinition c = g.Generate(k);
                foreach (string text in new[] { c.requester, c.words, ContractText.Reward(c.reward), ContractText.Reward(c.bonus) })
                {
                    Assert.IsTrue(text.All(ch => ch >= 0x20 && ch <= 0xFF), $"{c.name}: '{text}'");
                    Assert.IsNull(PlayerText.RawIdIn(text), $"{c.name}: '{text}'");
                    StringAssert.DoesNotContain("{goods}", text);
                    StringAssert.DoesNotContain(c.name, text);
                }
            }
        }
    }

    /// <summary>The board after the authored sequence: generated contracts, once the tower stands.</summary>
    public class GeneratedContractBoardTests
    {
        private SandboxWorld _world;

        [SetUp]
        public void SetUp() => _world = CrateWorld.Compose();

        [TearDown]
        public void TearDown() => _world.Dispose();

        private ContractBoard Board => _world.Contracts;

        private void AllAuthoredDone() =>
            Board.Restore(new Save.ContractsEntry { done = Board.Sequence.Select(c => c.name).ToList() });

        private void SeeEverything()
        {
            foreach (string good in ItemTiers.CanonicalOrder)
            {
                _world.TechTree.Ledger.RecordItem(good);
            }
        }

        [Test]
        public void AfterTheAuthoredSequence_TheNextWaitsForTheTower()
        {
            SeeEverything();
            AllAuthoredDone();
            _world.Advance(0.1f);
            Assert.IsNull(Board.Current);
            Assert.IsTrue(Board.WaitingForTowerComplete);
            Assert.AreEqual("No work today. The city will have more once the Clock Tower stands.", ContractText.Waiting(Board));

            _world.ClockTower.Site.RestoreProgress(_world.ClockTower.Site.StageCount, 0L, true);
            _world.Advance(0.1f);
            Assert.AreEqual("Endless_1", Board.Current?.name);
        }

        [Test]
        public void GeneratedContracts_FollowOneAnother_AndSave()
        {
            SeeEverything();
            AllAuthoredDone();
            _world.ClockTower.Site.RestoreProgress(_world.ClockTower.Site.StageCount, 0L, true);
            _world.Advance(0.1f);
            ContractDefinition first = Board.Current;
            Assert.IsTrue(Board.Accept());
            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(TownSquare.OfficeCell, out IItemEndpoint office));
            foreach (RecipeIngredient ask in first.asks)
            {
                for (int i = 0; i < ask.quantity; i++)
                {
                    Assert.IsTrue(office.TryGive(new Belts.ItemStack { ItemType = ask.itemType }));
                }
            }
            _world.Advance(0.1f);
            Assert.AreEqual("Endless_2", Board.Current?.name, "the next follows");
            Assert.AreEqual(1, Board.GeneratedDone);

            string path = Path.Combine(Path.GetTempPath(), "golem-factory-generated-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.IsTrue(Board.Accept());
                _world.SaveTo(path);
                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    Assert.AreEqual("Endless_2", loaded.Contracts.Current?.name);
                    Assert.AreEqual(ContractState.Accepted, loaded.Contracts.State);
                    CollectionAssert.Contains(loaded.Contracts.Done, "Endless_1");
                    Assert.AreEqual(1, loaded.Contracts.GeneratedDone);
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

        [Test]
        public void CreativeHasNone()
        {
            SandboxWorld creative = CreativeTests.Compose(creative: true);
            try
            {
                creative.Advance(0.1f);
                Assert.IsNull(creative.Contracts.Current);
                Assert.IsFalse(creative.Contracts.IsOpen);
            }
            finally
            {
                creative.Dispose();
            }
        }
    }
}
