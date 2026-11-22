using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// The Ledger's plaques as unlock buttons (2026-11-19): a recipe's or chassis's plaque claims
    /// its card through the Assembly Line tab's own Claim.
    /// </summary>
    public class LedgerUnlockTests
    {
        private static SandboxWorld Compose() => GolemFactory.Tests.World.CreativeTests.Compose(creative: false);

        [Test]
        public void EveryCardToClaim_HasAPlaque()
        {
            SandboxWorld world = Compose();
            try
            {
                var opening = world.Definitions.Decks[world.Setup.assemblyLine.deck].OpeningHand;
                var recipes = world.Definitions.Recipes.Values.ToList();
                var mapped = TechTreeCatalog.Nodes.Select(n => TechTreeReadout.CardFor(n, world.AssemblyLine.Deck, recipes))
                    .Where(c => c != null).ToList();
                var missing = world.AssemblyLine.Deck.Where(c => !opening.Contains(c) && !mapped.Contains(c)).Select(c => c.name).ToList();
                Assert.IsEmpty(missing, "cards with no plaque to claim them from: " + string.Join(", ", missing));
                Assert.AreEqual(mapped.Count, mapped.Distinct().Count(), "no two plaques claim the same card");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void APlaque_SaysWhatItsCardCosts_AndClaimsIt()
        {
            SandboxWorld world = Compose();
            try
            {
                world.TechTree.Ledger.RecordItem(ItemType.Scrap);
                world.LedgerReadout.Select("r2.reclamation");
                DraftableCardDefinition card = world.LedgerReadout.SelectedCard;
                Assert.AreEqual("Card_R2_ScrapReclamation", card?.name);
                Assert.AreEqual("Claim for " + RecipeLedger.FormatBundle(AssemblyLineState.PriceOf(card)), world.LedgerReadout.UnlockLine);

                foreach (var cost in AssemblyLineState.PriceOf(card))
                {
                    world.Buffers.Deposit(world.StockpileBufferId, cost.itemType, cost.quantity);
                }
                Assert.IsTrue(world.LedgerReadout.CanClaim);
                Assert.IsTrue(world.LedgerReadout.Claim(), world.AssemblyLineBoard.Status);

                Assert.AreEqual("Claimed", world.LedgerReadout.UnlockLine);
                Assert.IsFalse(world.LedgerReadout.CanClaim);
                Assert.IsTrue(world.Workbench.VaultAppendages.Any(a => a.name == "AssembleScrapReclamation"), "the vault offers it");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ALockedPlaque_SaysWhatItNeeds_AndHasNoClaim()
        {
            SandboxWorld world = Compose();
            try
            {
                world.LedgerReadout.Select("r7.brassalloying");
                StringAssert.StartsWith("Locked: needs ", world.LedgerReadout.UnlockLine);
                Assert.IsFalse(world.LedgerReadout.CanClaim);
                Assert.IsFalse(world.LedgerReadout.Claim());
            }
            finally
            {
                world.Dispose();
            }
        }

        // 2026-11-21: cards are claimed in the Ledger only, so the chart marks which are ready.
        [Test]
        public void APlaquesMark_FollowsTheOfferAndTheWallet()
        {
            SandboxWorld world = Compose();
            try
            {
                TechTreeCatalog.TryGetNode("r2.reclamation", out TechTreeNode node);
                TechTreeCatalog.TryGetNode("bldg.boiler", out TechTreeNode building);
                Assert.AreEqual(ClaimMark.None, world.LedgerReadout.MarkFor(node), "locked: no mark");
                Assert.AreEqual(ClaimMark.None, world.LedgerReadout.MarkFor(building), "no card: no mark");

                world.TechTree.Ledger.RecordItem(ItemType.Scrap);
                world.LedgerReadout.Select("r2.reclamation");
                DraftableCardDefinition card = world.LedgerReadout.SelectedCard;
                foreach (var cost in AssemblyLineState.PriceOf(card))
                {
                    world.Wallet.TryCharge(new[] { new RecipeIngredient(cost.itemType, world.Buffers.GetQuantity(world.StockpileBufferId, cost.itemType)) });
                }
                Assert.AreEqual(ClaimMark.OnOffer, world.LedgerReadout.MarkFor(node), "on offer, can't pay");
                Assert.That(world.LedgerReadout.OfferedCount, Is.GreaterThan(0));

                foreach (var cost in AssemblyLineState.PriceOf(card))
                {
                    world.Buffers.Deposit(world.StockpileBufferId, cost.itemType, cost.quantity);
                }
                Assert.AreEqual(ClaimMark.Affordable, world.LedgerReadout.MarkFor(node));

                int offered = world.LedgerReadout.OfferedCount;
                Assert.IsTrue(world.LedgerReadout.Claim(), world.LedgerReadout.Status);
                Assert.AreEqual(ClaimMark.Claimed, world.LedgerReadout.MarkFor(node));
                Assert.AreEqual(offered - 1, world.LedgerReadout.OfferedCount);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void TheGuidesCard_IsThePlaqueTheLedgerOpensOn_UntilClaimed()
        {
            SandboxWorld world = Compose();
            try
            {
                world.TechTree.Ledger.RecordItem(ItemType.Scrap);
                world.AssemblyLineBoard.ConfigureWanted(c => c?.name == "Card_R2_ScrapReclamation");
                Assert.AreEqual("r2.reclamation", world.LedgerReadout.WantedNodeId);

                world.LedgerReadout.Select("r2.reclamation");
                foreach (var cost in AssemblyLineState.PriceOf(world.LedgerReadout.SelectedCard))
                {
                    world.Buffers.Deposit(world.StockpileBufferId, cost.itemType, cost.quantity);
                }
                Assert.IsTrue(world.LedgerReadout.Claim(), world.LedgerReadout.Status);
                Assert.IsNull(world.LedgerReadout.WantedNodeId, "claimed: nothing left to open on");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ABuildingPlaque_HasNoUnlock()
        {
            SandboxWorld world = Compose();
            try
            {
                world.LedgerReadout.Select("bldg.boiler");
                Assert.IsNull(world.LedgerReadout.SelectedCard);
                Assert.AreEqual("", world.LedgerReadout.UnlockLine);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
