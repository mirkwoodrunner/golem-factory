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
            var line = new AssemblyLineState(3);
            var buffers = new StorageBufferRegistry();
            // One real card in slot 0; DisplayName reads the WRAPPED appendage's name.
            var card = new DraftableCardDefinition
            {
                appendage = new AppendageActionDefinition { name = "Coking" },
                baseCost = 10,
                minCost = 2,
                decayPerSecond = 0f,
            };
            line.SeedCandidates(new[] { card });
            return (new AssemblyLineBoard(line, buffers, Wallet), line, buffers);
        }

        private static AssemblyLineRow ClaimRow(AssemblyLineBoard board) =>
            board.Rows().First(r => r.ButtonLabel == "Claim");

        [Test]
        public void Refresh_BuildsOneRowPerSlot_PlusTheWalletHeader()
        {
            var (board, line, _) = Build();

            // Row 0 is the wallet balance -- the cost columns below are meaningless without it.
            List<AssemblyLineRow> rows = board.Rows();
            Assert.AreEqual(line.SlotCount + 1, rows.Count);
            Assert.AreEqual(AssemblyLineRowKind.Wallet, rows[0].Kind);
        }

        [Test]
        public void Refresh_UnaffordableSlot_DisablesItsClaimButton()
        {
            var (board, _, buffers) = Build();
            Assert.IsFalse(ClaimRow(board).Affordable, "An unaffordable card should say so before the click, not after");

            buffers.Deposit(Wallet, ItemType.Scrap, 1000);

            Assert.IsTrue(ClaimRow(board).Affordable);
        }

        [Test]
        public void ClaimButton_InsufficientFunds_ShowsStatusMessage()
        {
            var (board, _, _) = Build();

            board.Claim(ClaimRow(board).SlotIndex);

            // The refusal names the PRICE, not just the fact of it.
            StringAssert.Contains("Cannot afford", board.Status);
            StringAssert.Contains("Coking", board.Status);
            StringAssert.Contains("10 Scrap", board.Status);
        }

        [Test]
        public void ClaimButton_SufficientFunds_WithdrawsAndRefillsSlot()
        {
            var (board, _, buffers) = Build();
            buffers.Deposit(Wallet, ItemType.Scrap, 10);

            Assert.IsTrue(board.Claim(ClaimRow(board).SlotIndex));

            Assert.AreEqual(0, buffers.GetQuantity(Wallet, ItemType.Scrap));
            Assert.IsTrue(board.Status.StartsWith("Claimed"));
        }
    }
}
