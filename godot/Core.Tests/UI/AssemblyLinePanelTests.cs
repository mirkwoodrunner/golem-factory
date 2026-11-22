using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode AssemblyLinePanelTests, ported onto Core's <see cref="AssemblyLineBoard"/>
    /// (G8). Unity's rig used a "ScrapBuffer" wallet; the rig keeps a named wallet of its own
    /// because the tests are about the panel, not the Sandbox's choice of buffer (which is the
    /// stockpile -- see AssemblyLineBoard).
    /// </summary>
    public class AssemblyLinePanelTests
    {
        private const string Wallet = "ScrapBuffer";

        private static (AssemblyLineBoard board, AssemblyLineState line, StorageBufferRegistry buffers) Build()
        {
            var line = new AssemblyLineState();
            var buffers = new StorageBufferRegistry();
            // One real card; DisplayName reads the WRAPPED appendage's name.
            var card = new DraftableCardDefinition
            {
                appendage = new AppendageActionDefinition { name = "Coking" },
                claimCost = new List<RecipeIngredient> { new RecipeIngredient(ItemType.Scrap, 10) },
            };
            line.SeedCandidates(new[] { card });
            return (new AssemblyLineBoard(line, buffers, Wallet), line, buffers);
        }

        private static DraftableCardDefinition OnlyCard(AssemblyLineState line) => line.Offers.Single();

        // 2026-11-21: cards are claimed in the Ledger. The tab lists none, only how many wait there.
        [Test]
        public void Rows_ListNoCards_OnlyTheWalletAndTheLedgersCount()
        {
            var (board, line, _) = Build();

            // Row 0 is the wallet balance -- the cost columns below are meaningless without it.
            List<AssemblyLineRow> rows = board.Rows();
            Assert.AreEqual(AssemblyLineRowKind.Wallet, rows[0].Kind);
            Assert.IsFalse(rows.Any(r => r.ButtonLabel == "Claim" || r.Kind == AssemblyLineRowKind.Slot), "no card rows");
            AssemblyLineRow cards = rows.Single(r => r.Kind == AssemblyLineRowKind.Cards);
            Assert.AreEqual(AssemblyLineBoard.ReadyToClaim(line.Offers.Count), cards.Text);
            Assert.IsTrue(cards.Bright);
            Assert.IsNull(cards.ButtonLabel);
        }

        [Test]
        public void WithNothingOnOffer_TheCardsRowSaysHowMoreUnlock()
        {
            var board = new AssemblyLineBoard(new AssemblyLineState(), new StorageBufferRegistry(), Wallet);
            AssemblyLineRow row = board.Rows().Single(r => r.Kind == AssemblyLineRowKind.Cards);
            Assert.AreEqual(AssemblyLineBoard.NothingToClaim, row.Text);
            Assert.IsFalse(row.Bright);
        }

        [Test]
        public void TheGuidesCard_IsTheWantedOne()
        {
            var line = new AssemblyLineState();
            DraftableCardDefinition a = new DraftableCardDefinition { name = "A", appendage = new AppendageActionDefinition { name = "A" } };
            DraftableCardDefinition b = new DraftableCardDefinition { name = "B", appendage = new AppendageActionDefinition { name = "B" } };
            line.SeedCandidates(new[] { a, b });
            var board = new AssemblyLineBoard(line, new StorageBufferRegistry(), Wallet);
            Assert.IsFalse(board.IsWanted(b), "nothing wanted until the guide says");

            board.ConfigureWanted(c => c == b);

            Assert.IsTrue(board.IsWanted(b));
            Assert.IsFalse(board.IsWanted(a));
            Assert.IsFalse(board.IsWanted(null));
        }

        // The header used to read "Wallet: 7 Scrap (ScrapBuffer)": a buffer id shown to the
        // player, and blind to the Post a claim also spends from (pack-upgrades critique).
        [Test]
        public void TheHeader_NamesThePack_NeverABufferId()
        {
            var (board, _, buffers) = Build();
            buffers.Deposit(Wallet, ItemType.Scrap, 7);

            Assert.AreEqual("Pack: 7 Scrap", board.Rows()[0].Text);
        }

        [Test]
        public void TheHeader_AddsThePost_OnceOneExists()
        {
            var line = new AssemblyLineState();
            var buffers = new StorageBufferRegistry();
            bool postBuilt = false;
            var board = new AssemblyLineBoard(line, buffers, Wallet, postBufferId: "PostStore");
            board.ConfigurePostShown(() => postBuilt);
            buffers.Deposit(Wallet, ItemType.Scrap, 100);

            Assert.AreEqual("Pack: 100 Scrap", board.Rows()[0].Text, "no Post built, none mentioned");

            postBuilt = true;
            Assert.AreEqual("Pack: 100 Scrap  ·  Post: 0 Scrap", board.Rows()[0].Text);

            buffers.Deposit("PostStore", ItemType.Scrap, 40);
            Assert.AreEqual("Pack: 100 Scrap  ·  Post: 40 Scrap", board.Rows()[0].Text);

            postBuilt = false; // say every Post crate is gone: a store still holding Scrap is still spent
            Assert.AreEqual("Pack: 100 Scrap  ·  Post: 40 Scrap", board.Rows()[0].Text);
        }

        [Test]
        public void AnUnaffordableCard_SaysSoBeforeTheClick()
        {
            var (board, line, buffers) = Build();
            Assert.IsFalse(board.CanAffordCard(OnlyCard(line)), "An unaffordable card should say so before the click, not after");

            buffers.Deposit(Wallet, ItemType.Scrap, 1000);

            Assert.IsTrue(board.CanAffordCard(OnlyCard(line)));
        }

        [Test]
        public void Claim_InsufficientFunds_ShowsStatusMessage()
        {
            var (board, line, _) = Build();

            board.Claim(OnlyCard(line));

            // The refusal names the PRICE, not just the fact of it.
            StringAssert.Contains("Cannot afford", board.Status);
            StringAssert.Contains("Coking", board.Status);
            StringAssert.Contains("10 Scrap", board.Status);
        }

        [Test]
        public void Claim_SufficientFunds_WithdrawsAndTheCountDrops()
        {
            var (board, line, buffers) = Build();
            buffers.Deposit(Wallet, ItemType.Scrap, 10);

            Assert.IsTrue(board.Claim(OnlyCard(line)));

            Assert.AreEqual(0, buffers.GetQuantity(Wallet, ItemType.Scrap));
            Assert.IsTrue(board.Status.StartsWith("Claimed"));
            Assert.AreEqual(AssemblyLineBoard.NothingToClaim, board.Rows().Single(r => r.Kind == AssemblyLineRowKind.Cards).Text,
                "claimed once and gone");
        }
    }
}
