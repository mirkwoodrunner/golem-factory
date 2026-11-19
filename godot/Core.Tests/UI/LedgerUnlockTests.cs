using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.Progression;
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
