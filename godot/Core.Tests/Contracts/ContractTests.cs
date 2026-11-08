using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Contracts;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Supply;
using GolemFactory.Tests.EditMode;
using GolemFactory.Tests.World;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Contracts
{
    /// <summary>docs/contracts-design.md §0 and §3: the authored sequence.</summary>
    public class ContractDataTests
    {
        private static List<ContractDefinition> Sequence() =>
            AuthoredData.Load().Contracts.Values.OrderBy(c => c.order).ToList();

        private static readonly string[] RawGoods =
        {
            ItemType.Scrap, ItemType.Coal, ItemType.CopperOre, ItemType.ZincOre, ItemType.Aether,
        };

        /// <summary>
        /// A good's cost in raw goods (§4): a raw good is 1, a made good the cheapest recipe's
        /// inputs over its output, and Slag, which only a byproduct makes, 1.
        /// </summary>
        internal static Dictionary<string, double> RawCosts()
        {
            var cost = RawGoods.ToDictionary(g => g, g => 1.0);
            cost[ItemType.Slag] = 1.0;
            List<RecipeDefinition> recipes = AuthoredData.All<RecipeDefinition>().ToList();
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (RecipeDefinition recipe in recipes)
                {
                    if (!recipe.inputs.All(i => cost.ContainsKey(i.itemType)))
                    {
                        continue;
                    }
                    double each = recipe.inputs.Sum(i => cost[i.itemType] * i.quantity) / recipe.outputQuantity;
                    if (!cost.TryGetValue(recipe.outputItemType, out double known) || each < known - 1e-9)
                    {
                        cost[recipe.outputItemType] = each;
                        grew = true;
                    }
                }
            }
            return cost;
        }

        private static double Raw(IEnumerable<RecipeIngredient> goods, Dictionary<string, double> cost) =>
            goods.Sum(g => cost[g.itemType] * g.quantity);

        [Test]
        public void TheSequence_HasItsOrder_AndEveryContractNamesWhoAsks()
        {
            List<ContractDefinition> all = Sequence();
            Assert.GreaterOrEqual(all.Count, 16, "about sixteen in the campaign (§3)");
            CollectionAssert.AllItemsAreUnique(all.Select(c => c.order));
            foreach (ContractDefinition c in all)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(c.requester), c.name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(c.words), c.name);
                Assert.Greater(c.parSeconds, 0, c.name);
                Assert.IsFalse(c.reward.IsEmpty, c.name + ": a reward");
            }
        }

        [Test]
        public void EveryContract_AsksOneOrTwoGoods_AndPostsOnlyOnceTheyAreSeen()
        {
            // Invariant 8: a contract never asks for a good the factory hasn't made by then.
            foreach (ContractDefinition c in Sequence())
            {
                Assert.That(c.asks.Count, Is.InRange(1, 2), c.name);
                CollectionAssert.AllItemsAreUnique(c.asks.Select(a => a.itemType), c.name);
                foreach (RecipeIngredient ask in c.asks)
                {
                    Assert.Greater(ask.quantity, 0, c.name);
                    Assert.IsTrue(ItemTiers.IsKnown(ask.itemType), $"{c.name}: {ask.itemType}");
                    CollectionAssert.Contains(c.postWhenSeen, ask.itemType, $"{c.name} waits for {ask.itemType} to be seen");
                }
            }
        }

        [Test]
        public void TheOffice_OpensWithTheFirstSmelter_AndTheTowerContractsComeInStageOrder()
        {
            List<ContractDefinition> all = Sequence();
            CollectionAssert.Contains(all[0].postWhenSeen, ItemType.Slag, "chapter 5: Slag is first made by smelting iron");
            int stage = 0;
            foreach (ContractDefinition c in all)
            {
                Assert.GreaterOrEqual(c.postWhenTowerStage, stage, c.name + ": stages never go back");
                stage = c.postWhenTowerStage;
            }
            Assert.AreEqual(4, stage, "the last ones come with stage 4");
            for (int s = 1; s <= 4; s++)
            {
                Assert.AreEqual(2, all.Count(c => c.postWhenTowerStage == s), $"two contracts for stage {s}");
            }
        }

        [Test]
        public void NoRewardsGoods_AreWorthMoreThanTheContractCostsToFill()
        {
            // §4, counted in raw goods, reward and bonus together. A rung is not goods: a given
            // rung is never paid, so removing its cart refunds nothing of it (GivenRung tests).
            Dictionary<string, double> cost = RawCosts();
            foreach (ContractDefinition c in Sequence())
            {
                double asked = Raw(c.asks, cost);
                double given = Raw(c.reward.goods, cost) + Raw(c.bonus.goods, cost);
                Assert.LessOrEqual(given, asked, $"{c.name}: {given} raw given for {asked} raw asked");
            }
        }

        [Test]
        public void EveryRung_IsForACartTheStreetSells()
        {
            SandboxWorld world = CrateWorld.Compose();
            try
            {
                foreach (ContractDefinition c in Sequence())
                {
                    foreach (ContractReward reward in new[] { c.reward, c.bonus }.Where(r => r.HasRung))
                    {
                        Assert.IsTrue(world.Supply.TryGetLadder(reward.rungGood, out _), $"{c.name}: no {reward.rungGood} cart");
                    }
                }
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EveryText_UsesTheFontsGlyphs_AndNoId()
        {
            // Invariant 13: printable Latin-1 (the project font), and never a definition's name.
            foreach (ContractDefinition c in Sequence())
            {
                foreach (string text in new[] { c.requester, c.words, ContractText.Reward(c.reward), ContractText.Reward(c.bonus), ContractText.Goods(c.asks) })
                {
                    Assert.IsTrue(text.All(ch => ch >= 0x20 && ch <= 0xFF && ch != 0x7F), $"{c.name}: '{text}'");
                    Assert.IsNull(PlayerText.RawIdIn(text), $"{c.name}: '{text}'");
                    StringAssert.DoesNotContain(c.name, text);
                }
            }
        }
    }

    /// <summary>docs/contracts-design.md §0, §2, §4, §7, §9: the board in a composed world.</summary>
    public class ContractBoardTests
    {
        private SandboxWorld _world;
        private ConsumptionTally _spent;
        private readonly List<ContractEvent> _events = new List<ContractEvent>();

        [SetUp]
        public void SetUp()
        {
            _world = CrateWorld.Compose();
            _spent = new ConsumptionTally();
            _spent.Attach();
            _events.Clear();
            EventBus.Contract += Heard;
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Contract -= Heard;
            _spent.Detach();
            _world.Dispose();
        }

        private void Heard(ContractEvent e) => _events.Add(e);

        private ContractBoard Board => _world.Contracts;

        private ContractDefinition First => Board.Sequence[0];

        private void See(params string[] goods)
        {
            foreach (string good in goods)
            {
                _world.TechTree.Ledger.RecordItem(good);
            }
        }

        private void Frame() => _world.Advance(0.1f);

        /// <summary>The first contract, posted and taken.</summary>
        private ContractDefinition TakeFirst()
        {
            See(First.postWhenSeen.ToArray());
            Frame();
            Assert.AreEqual(ContractState.Posted, Board.State, "precondition: posted");
            Assert.IsTrue(Board.Accept());
            return Board.Current;
        }

        private IItemEndpoint OfficeEndpoint()
        {
            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(TownSquare.OfficeCell, out IItemEndpoint endpoint));
            return endpoint;
        }

        private int Spent(string item) => _spent.Totals.TryGetValue(item, out int n) ? n : 0;

        // --- When contracts come (§3) -----------------------------------------------------------

        [Test]
        public void TheOffice_IsRopedOff_UntilItsFirstContractCanPost()
        {
            Frame();
            Assert.IsFalse(Board.IsOpen);
            Assert.IsNull(Board.Current);
            StringAssert.StartsWith("The City Office opens once you make ", ContractText.Closed(Board));

            See(First.postWhenSeen.Take(First.postWhenSeen.Count - 1).ToArray());
            Frame();
            Assert.IsFalse(Board.IsOpen, "every named good, not most of them");

            See(First.postWhenSeen.Last());
            Frame();
            Assert.IsTrue(Board.IsOpen);
            Assert.AreSame(First, Board.Current);
            Assert.AreEqual(ContractState.Posted, Board.State);
            Assert.AreEqual(ContractEventKind.Posted, _events.Single().Kind);
        }

        [Test]
        public void OneAtATime_TheNextPostsOnlyWhenDone_AndItsConditionHolds()
        {
            ContractDefinition first = TakeFirst();
            ContractDefinition second = Board.Sequence[1];
            See(second.postWhenSeen.ToArray());
            Frame();
            Assert.AreSame(first, Board.Current, "the second waits for the first");

            _world.TechTree.Ledger.Clear();
            See(first.postWhenSeen.ToArray());
            Fill(first);
            Assert.IsNull(Board.Current, "done");
            Assert.AreEqual(ContractState.Waiting, Board.State);
            string unseen = second.postWhenSeen.First(g => !_world.TechTree.Ledger.HasItem(g));
            Assert.AreEqual(unseen, Board.WaitingForGood);
            Assert.AreEqual("No work today. The city will have more once you make " + ItemTiers.DisplayName(unseen) + ".", ContractText.Waiting(Board));

            See(second.postWhenSeen.ToArray());
            Frame();
            Assert.AreSame(second, Board.Current);
        }

        [Test]
        public void ATowerContract_WaitsForItsStage()
        {
            ContractDefinition tower = Board.Sequence.First(c => c.postWhenTowerStage == 2);
            Board.Restore(new Save.ContractsEntry { done = Board.Sequence.TakeWhile(c => c != tower).Select(c => c.name).ToList() });
            See(tower.postWhenSeen.ToArray());
            Frame();
            Assert.IsNull(Board.Current, "the tower is still roped off");
            Assert.AreEqual(2, Board.WaitingForTowerStage);

            _world.TechTree.Ledger.RecordChassis(SandboxWorld.ZeppelinChassis);
            _world.ClockTower.Site.RestoreProgress(1, 0L, false);
            Frame();
            Assert.AreSame(tower, Board.Current, "stage 2 has begun");
        }

        // --- Filling it (§0.3, §0.4) ------------------------------------------------------------

        private void Fill(ContractDefinition c)
        {
            foreach (RecipeIngredient ask in c.asks)
            {
                IItemEndpoint office = OfficeEndpoint();
                for (int i = 0; i < ask.quantity; i++)
                {
                    Assert.IsTrue(office.TryGive(new Belts.ItemStack { ItemType = ask.itemType }), $"unit {i} of {ask.itemType}");
                }
            }
            Frame();
        }

        [Test]
        public void APostedContract_TakesNothing_UntilItIsTaken()
        {
            See(First.postWhenSeen.ToArray());
            Frame();
            string good = First.asks[0].itemType;
            Assert.IsFalse(OfficeEndpoint().CanGive(good));
            CrateWorld.Give(_world, good, 5);
            Assert.AreEqual("Take the contract first", CrateTransfer.PlanPut(_world.Wallet.Pack, _world.OfficeCrate, good, 5).Refusal);
            Assert.AreEqual(5, CrateWorld.Pack(_world, good), "nothing moved");
        }

        [Test]
        public void AnySourceCounts_Once_AndNothingPastTheAmount()
        {
            ContractDefinition c = TakeFirst();
            RecipeIngredient ask = c.asks[0];

            // A golem's push or a belt: the endpoint on the office's cells.
            Assert.IsTrue(OfficeEndpoint().TryGive(new Belts.ItemStack { ItemType = ask.itemType }));
            Frame();
            Assert.AreEqual(1, Board.Delivered(ask.itemType));
            Assert.AreEqual(0, Board.OfficeBuffer.GetQuantity(ask.itemType), "counted and spent, not left in the crate");
            Assert.AreEqual(1, Spent(ask.itemType), "goods delivered are spent (invariant 4)");

            // By hand, from the Crate screen: Shift moves all that fits, and only what is still needed.
            CrateWorld.Give(_world, ask.itemType, ask.quantity + 10);
            string said = _world.Inventory.Put(_world.OfficeCrate, ask.itemType, shift: true);
            StringAssert.Contains((ask.quantity - 1).ToString(), said);
            Assert.AreEqual(11, CrateWorld.Pack(_world, ask.itemType), "the rest stays in the pack");
            Frame();

            Assert.IsNull(Board.Current, "filled");
            Assert.AreEqual(ask.quantity, Spent(ask.itemType), "each good counted once");
        }

        [Test]
        public void TheCrate_RefusesWhatTheContractDoesntAsk_AndSaysWhy()
        {
            ContractDefinition c = TakeFirst();
            string other = ItemType.Scrap;
            Assert.IsFalse(c.AsksFor(other), "precondition");
            Assert.IsFalse(OfficeEndpoint().CanGive(other));
            Assert.IsFalse(OfficeEndpoint().TryGive(new Belts.ItemStack { ItemType = other }));
            CrateWorld.Give(_world, other, 5);
            StringAssert.StartsWith("The contract asks for ", CrateTransfer.PlanPut(_world.Wallet.Pack, _world.OfficeCrate, other, 5).Refusal);
            Assert.AreEqual(ContractText.OfficeName, CrateScreenReadout.Title(_world.OfficeCrate));
            StringAssert.StartsWith("Takes " + c.asks[0].quantity + " more ", CrateScreenReadout.CapLine(_world.OfficeCrate));
        }

        // --- Never fails; the bonus only adds (§0.1, §0.2) -----------------------------------------

        [Test]
        public void WithinPar_EarnsTheReward_AndTheBonus()
        {
            ContractDefinition c = TakeFirst();
            StringAssert.StartsWith("Bonus if done in ", ContractText.BonusLine(Board));
            int before = CrateWorld.Pack(_world, c.reward.goods[0].itemType);
            Fill(c);

            Assert.AreEqual(before + c.reward.goods[0].quantity, CrateWorld.Pack(_world, c.reward.goods[0].itemType), "the reward is in the pack");
            Assert.IsTrue(Board.EarnedBonus(c.name));
            Assert.AreEqual(1, Board.ShelfRungs.Count, "the bonus rung waits for its cart");
            Assert.AreEqual(c.bonus.rungGood, Board.ShelfRungs[0].ItemType);
            ContractEvent done = _events.Last();
            Assert.AreEqual(ContractEventKind.Completed, done.Kind);
            Assert.IsTrue(done.BonusEarned);
            Assert.AreEqual("Contract done for the " + c.requester + " · bonus earned", ContractText.Alert(done));
        }

        [Test]
        public void PastPar_TheContractCarriesOn_AndGivesTheFullReward()
        {
            ContractDefinition c = TakeFirst();
            long par = Board.ParTicks;
            for (long t = 0; t <= par + 50; t++)
            {
                Board.Tick(t);
            }
            Assert.AreEqual(ContractState.Accepted, Board.State, "a contract never fails");
            Assert.AreEqual("Bonus missed", ContractText.BonusLine(Board));
            Assert.IsTrue(OfficeEndpoint().CanGive(c.asks[0].itemType), "and still takes goods");

            int before = CrateWorld.Pack(_world, c.reward.goods[0].itemType);
            Fill(c);
            Assert.AreEqual(before + c.reward.goods[0].quantity, CrateWorld.Pack(_world, c.reward.goods[0].itemType), "the full ordinary reward");
            Assert.IsFalse(Board.EarnedBonus(c.name));
            Assert.AreEqual(0, Board.ShelfRungs.Count, "no bonus");
            Assert.IsFalse(_events.Last().BonusEarned);
        }

        [Test]
        public void ThePar_RunsOnTheSimulationClock()
        {
            TakeFirst();
            int left = Board.ParSecondsLeft;
            _world.Clock.Pause();
            for (int i = 0; i < 100; i++)
            {
                Frame();
            }
            Assert.AreEqual(left, Board.ParSecondsLeft, "a paused factory's par stands still");
            _world.Clock.Play();
            for (int i = 0; i < 100; i++)
            {
                Frame();
            }
            Assert.AreEqual(left - 10, Board.ParSecondsLeft, 1);
        }

        // --- Rewards are never lost (§0.5, §4) --------------------------------------------------

        [Test]
        public void ARewardTooBigForThePack_WaitsOnTheShelf_UntilCollected()
        {
            ContractDefinition c = TakeFirst();
            RecipeIngredient reward = c.reward.goods[0];
            StorageBuffer pack = _world.Wallet.Pack;
            int cap = pack.CapacityPerType;
            Assume.That(cap, Is.GreaterThan(0), "the pack has a cap");
            pack.Deposit(reward.itemType, cap - pack.GetQuantity(reward.itemType) - 2);

            Fill(c);
            Assert.AreEqual(cap, pack.GetQuantity(reward.itemType), "what fits went in");
            Assert.AreEqual(reward.quantity - 2, Board.ShelfGoods.Single().quantity, "the rest is on the shelf");
            StringAssert.StartsWith("Waiting for you: ", ContractText.ShelfGoodsLine(Board));

            Assert.AreEqual(0, Board.CollectGoods().Count, "no room yet: nothing moves, nothing is lost");
            pack.TryWithdraw(reward.itemType, 5);
            List<RecipeIngredient> moved = Board.CollectGoods();
            Assert.AreEqual(5, moved.Single().quantity);
            Assert.AreEqual(reward.quantity - 7, Board.ShelfGoods.Single().quantity);
        }

        [Test]
        public void AGivenRung_GoesOnTheCartPicked_ForNothing()
        {
            ContractDefinition c = TakeFirst();
            Fill(c);
            ShelfRung rung = Board.ShelfRungs.Single();
            Assert.IsEmpty(Board.CartsFor(rung), "no " + rung.ItemType + " cart yet");

            CartSpot spot = _world.Supply.Spots.First(s => s.IsEmpty);
            Assert.IsTrue(_world.GrantCart(spot.Cell, rung.ItemType).Done);
            CollectionAssert.AreEqual(new[] { spot }, Board.CartsFor(rung));
            int paid = spot.Cart.Paid.Sum(p => p.quantity);

            Assert.IsTrue(Board.ApplyRung(0, spot).Done);
            Assert.AreEqual(rung.Kind == ContractRungKind.Rate ? 1 : 0, spot.Cart.RateRung);
            Assert.AreEqual(rung.Kind == ContractRungKind.Capacity ? 1 : 0, spot.Cart.CapacityRung);
            Assert.AreEqual(paid, spot.Cart.Paid.Sum(p => p.quantity), "a given rung is recorded as paid nothing, so a removal refunds nothing of it");
            Assert.IsEmpty(Board.ShelfRungs);
        }

        [Test]
        public void ARung_RefusesACartAtTheTop_AndACartOfAnotherGood()
        {
            Fill(TakeFirst());
            ShelfRung rung = Board.ShelfRungs.Single();
            CartSpot spot = _world.Supply.Spots.First(s => s.IsEmpty);
            _world.GrantCart(spot.Cell, rung.ItemType);
            bool rate = rung.Kind == ContractRungKind.Rate;
            while ((rate ? SupplyStreet.NextRateCost(spot.Cart) : SupplyStreet.NextCapacityCost(spot.Cart)) != null)
            {
                Assert.IsTrue(_world.Supply.GiveRung(spot, rate).Done);
            }
            Assert.IsEmpty(Board.CartsFor(rung), "a cart at the top can't be chosen");
            Assert.IsFalse(Board.ApplyRung(0, spot).Done);
            CartSpot scrap = _world.Supply.Spots.First(s => s.Cart != null && s.Cart.ItemType != rung.ItemType);
            Assert.IsFalse(Board.ApplyRung(0, scrap).Done);
            Assert.AreEqual(1, Board.ShelfRungs.Count, "it waits on the shelf");
            StringAssert.StartsWith("No ", ContractText.NoCartFor(rung));
        }

        // --- Saves (§7, invariant 11) ------------------------------------------------------------

        [Test]
        public void ASave_RoundTrips_TheContractItsCountsItsParAndTheShelf()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-contracts-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                // One done with a rung on the shelf, the next taken and part filled.
                ContractDefinition first = TakeFirst();
                Fill(first);
                ContractDefinition second = Board.Sequence[1];
                See(second.postWhenSeen.ToArray());
                Frame();
                Assert.IsTrue(Board.Accept());
                _world.Clock.Play();
                RecipeIngredient ask = second.asks[0];
                for (int i = 0; i < 3; i++)
                {
                    OfficeEndpoint().TryGive(new Belts.ItemStack { ItemType = ask.itemType });
                }
                for (int i = 0; i < 20; i++)
                {
                    Frame();
                }
                long elapsed = Board.ElapsedTicks;
                Assert.Greater(elapsed, 0L);
                _world.SaveTo(path);

                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    ContractBoard board = loaded.Contracts;
                    Assert.AreSame(loaded.Definitions.Contracts[second.name], board.Current);
                    Assert.AreEqual(ContractState.Accepted, board.State);
                    Assert.AreEqual(3, board.Delivered(ask.itemType));
                    Assert.AreEqual(elapsed, board.ElapsedTicks);
                    CollectionAssert.AreEqual(new[] { first.name }, board.Done);
                    Assert.IsTrue(board.EarnedBonus(first.name));
                    Assert.AreEqual(first.bonus.rungGood, board.ShelfRungs.Single().ItemType);
                    Assert.AreEqual(first.bonus.rungKind, board.ShelfRungs.Single().Kind);
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
        public void AnOldSave_StartsTheSequenceAtItsFirstContract_NothingDelivered()
        {
            See(First.postWhenSeen.ToArray());
            Board.Restore(null);
            Assert.AreSame(First, Board.Current, "its condition holds, so it is up");
            Assert.AreEqual(ContractState.Posted, Board.State);
            Assert.IsEmpty(Board.Done);
            Assert.IsEmpty(_events, "a load doesn't log a posting again");
        }

        // --- E and I at the office ---------------------------------------------------------------

        private sealed class FakeContractScreen : IContractScreen
        {
            public bool IsOpen { get; set; }
            public void Open() => IsOpen = true;
        }

        private sealed class FakeCrateScreen : ICrateScreen
        {
            public Buildings.ICrate Opened;
            public bool IsOpen => Opened != null;
            public void Open(Buildings.ICrate crate) => Opened = crate;
        }

        [Test]
        public void E_OpensTheNotice_AndI_TheOfficesCrate_OnceItIsOpen()
        {
            var notice = new FakeContractScreen();
            var crates = new FakeCrateScreen();
            _world.Interactor.ConfigureContractScreen(notice);
            _world.ConfigureInventoryScreens(null, crates);
            PlayerInteractor hands = _world.Interactor;
            Vector2Int front = TownSquare.OfficeCell + new Vector2Int(0, -1);
            hands.Position = new Vector3(front.x, front.y, 0f);
            hands.RefreshInteractables();

            hands.RefreshAffordance();
            Assert.AreEqual(InteractionKind.Office, hands.CurrentPick.Kind);
            Assert.AreEqual(InteractionAffordance.Unavailable, hands.CurrentAffordance, "roped off");
            StringAssert.Contains("City Office  -  roped off", hands.CurrentPrompt);
            Assert.IsFalse(hands.Interact());
            Assert.IsFalse(notice.IsOpen);

            TakeFirst();
            hands.RefreshAffordance();
            Assert.AreEqual(InteractionAffordance.Ready, hands.CurrentAffordance);
            StringAssert.Contains("City Office  -  " + First.requester + " · 0 of ", hands.CurrentPrompt);
            Assert.IsTrue(hands.Interact());
            Assert.IsTrue(notice.IsOpen);

            Assert.IsTrue(hands.OpenInventory());
            Assert.AreSame(_world.OfficeCrate, crates.Opened);
        }

        [Test]
        public void AGolemsCard_NamesTheOffice_NotACrate()
        {
            foreach (Vector2Int cell in TownSquare.OfficeCells())
            {
                Assert.AreEqual("the City Office", _world.TileNames.Name(cell));
            }
        }

        [Test]
        public void TheOverviewsAlerts_LogAContractPostedAndDone_AsNotes()
        {
            ContractDefinition c = TakeFirst();
            Fill(c);
            List<GolemFactory.Overview.AlertEntry> notes = _world.Alerts.Log.Entries.Where(e => e.IsNote).ToList();
            Assert.AreEqual(2, notes.Count, "posted and done; taking it isn't logged");
            Assert.AreEqual("Contract done for the " + c.requester + " · bonus earned", notes[0].Text);
            Assert.AreEqual("The " + c.requester + " posted a contract at the City Office", notes[1].Text);
            Assert.IsFalse(notes[0].Open, "a note is never a stop that is still open");
            StringAssert.EndsWith(" · City Office", GolemFactory.Overview.AlertLog.Line(notes[0], _world.Alerts.Seconds));
        }

        [Test]
        public void TheOffice_IsAFixture_TheWreckingBarRefuses()
        {
            foreach (Vector2Int cell in TownSquare.OfficeCells())
            {
                Assert.IsTrue(_world.Grid.IsOccupied(cell), $"{cell} is the office's");
                Assert.IsFalse(_world.Build.HasRemovableThing(cell));
            }
            Assert.IsTrue(TownSquare.OfficeCells().All(TownSquare.Contains), "in the square");
            Assert.IsFalse(TownSquare.OfficeCells().Any(TownSquare.IsTowerCell));
            Assert.IsFalse(_world.Build.Buildings.Contains(_world.CityOfficeBuilding), "not the player's factory");
        }
    }

    /// <summary>Invariant 12: Creative Mode has no contracts.</summary>
    public class ContractCreativeTests
    {
        [Test]
        public void ACreativeWorld_NeverOpensTheOffice()
        {
            SandboxWorld world = CreativeTests.Compose(creative: true);
            try
            {
                world.Advance(0.1f);
                Assert.IsFalse(world.Contracts.Enabled);
                Assert.IsFalse(world.Contracts.IsOpen, "every good is seen, and still no contract");
                Assert.IsNull(world.Contracts.Current);
                Vector2Int front = TownSquare.OfficeCell + new Vector2Int(0, -1);
                world.Interactor.Position = new Vector3(front.x, front.y, 0f);
                world.Interactor.RefreshInteractables();
                world.Interactor.RefreshAffordance();
                Assert.AreNotEqual(InteractionKind.Office, world.Interactor.CurrentPick.Kind);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
