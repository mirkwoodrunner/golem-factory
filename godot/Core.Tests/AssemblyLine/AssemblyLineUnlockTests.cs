using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Assembly Line as direct unlocks (2026-11-19): every card on offer at once, the player's
    /// pick, each at its whole fixed price, claimed once. It replaced a three-slot draft row whose
    /// prices decayed while they sat.
    /// </summary>
    public class AssemblyLineUnlockTests
    {
        private const string User = "LocalPlayer";
        private const string Wallet = "FactoryStockpile";

        private static DraftableCardDefinition Card(string name, params (string good, int quantity)[] cost)
        {
            return new DraftableCardDefinition
            {
                name = name,
                claimCost = cost.Select(c => new RecipeIngredient(c.good, c.quantity)).ToList(),
                appendage = new AppendageActionDefinition { name = name + "Card" },
            };
        }

        private static StorageBufferRegistry Funded(params (string good, int quantity)[] goods)
        {
            var buffers = new StorageBufferRegistry();
            foreach ((string good, int quantity) in goods)
            {
                buffers.Deposit(Wallet, good, quantity);
            }
            return buffers;
        }

        [Test]
        public void EveryUnlockedCard_IsOffered_AtOnce_InDeckOrder()
        {
            DraftableCardDefinition a = Card("A", (ItemType.Scrap, 4)), b = Card("B", (ItemType.Scrap, 4)), c = Card("C", (ItemType.Scrap, 4));
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { a, b, c });

            CollectionAssert.AreEqual(new[] { a, b, c }, line.Offers, "no slots: the player sees every unlock");
        }

        [Test]
        public void APrice_NeverDecays()
        {
            DraftableCardDefinition card = Card("Coking", (ItemType.Coal, 4));
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { card });

            CollectionAssert.AreEqual(card.claimCost, AssemblyLineState.PriceOf(card));
            Assert.IsFalse(line.TryClaim(card, User, Funded((ItemType.Coal, 3)), Wallet), "a card costs its whole price, however long it waits");
        }

        [Test]
        public void AClaim_ChargesEveryGoodAtOnce_AndTheCardLeavesTheOffers()
        {
            DraftableCardDefinition card = Card("Smelting", (ItemType.Scrap, 8), (ItemType.Coke, 4));
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { card });
            StorageBufferRegistry buffers = Funded((ItemType.Scrap, 10), (ItemType.Coke, 4));

            Assert.IsTrue(line.TryClaim(card, User, buffers, Wallet));

            Assert.AreEqual(2, buffers.GetQuantity(Wallet, ItemType.Scrap));
            Assert.AreEqual(0, buffers.GetQuantity(Wallet, ItemType.Coke));
            CollectionAssert.Contains(line.GetClaimedCards(User), card);
            CollectionAssert.DoesNotContain(line.Offers, card, "claimed once and gone");
            Assert.IsFalse(line.TryClaim(card, User, buffers, Wallet), "a card can't be bought twice");
        }

        [Test]
        public void AClaimShortOfOneGood_TakesNothing()
        {
            DraftableCardDefinition card = Card("Smelting", (ItemType.Scrap, 8), (ItemType.Coke, 4));
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { card });
            StorageBufferRegistry buffers = Funded((ItemType.Scrap, 10), (ItemType.Coke, 3));

            Assert.IsFalse(line.TryClaim(card, User, buffers, Wallet));

            Assert.AreEqual(10, buffers.GetQuantity(Wallet, ItemType.Scrap));
            Assert.AreEqual(3, buffers.GetQuantity(Wallet, ItemType.Coke));
            Assert.IsEmpty(line.GetClaimedCards(User));
        }

        [Test]
        public void AFreeCard_IsClaimedForNothing()
        {
            DraftableCardDefinition repeat = Card("Repeat");
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { repeat });

            Assert.IsTrue(line.TryClaim(repeat, User, new StorageBufferRegistry(), Wallet));
        }

        [Test]
        public void ACardWithAnUnmadeGood_WaitsAndSaysWhy_ThenIsOffered()
        {
            DraftableCardDefinition lens = Card("Lens", (ItemType.Glass, 12), (ItemType.Brass, 4));
            lens.prerequisiteItemProduced = ItemType.Glass;
            var seen = new HashSet<string>();
            var line = new AssemblyLineState();
            line.ConfigureUnlockContext(seen.Contains);
            line.SeedCandidates(new[] { lens });

            CollectionAssert.IsEmpty(line.Offers);
            CollectionAssert.AreEqual(new[] { lens }, line.WaitingCards);
            Assert.AreEqual("Glass, Brass", line.DescribeMissingPrerequisites(lens));
            Assert.IsFalse(line.TryClaim(lens, User, Funded((ItemType.Glass, 99), (ItemType.Brass, 99)), Wallet), "a locked card can't be bought");

            seen.Add(ItemType.Glass);
            seen.Add(ItemType.Brass);

            CollectionAssert.AreEqual(new[] { lens }, line.Offers, "offered the moment the Ledger sees its goods");
            Assert.AreEqual("", line.DescribeMissingPrerequisites(lens));
        }

        [Test]
        public void ACardPrerequisite_IsSatisfiedByClaimingIt()
        {
            DraftableCardDefinition first = Card("First"), second = Card("Second");
            second.prerequisiteCards.Add(first);
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { first, second });

            CollectionAssert.AreEqual(new[] { first }, line.Offers);
            Assert.AreEqual("FirstCard", line.DescribeMissingPrerequisites(second));

            Assert.IsTrue(line.TryClaim(first, User, new StorageBufferRegistry(), Wallet));

            CollectionAssert.AreEqual(new[] { second }, line.Offers);
        }

        [Test]
        public void WithNoUnlockContextWired_GoodsPrerequisitesPass()
        {
            DraftableCardDefinition card = Card("Lens", (ItemType.Glass, 12));
            card.prerequisiteItemProduced = ItemType.Glass;
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { card });

            Assert.IsTrue(line.IsUnlocked(card));
        }

        [Test]
        public void GrantClaim_GivesTheOpeningHandWithoutCharging_Once()
        {
            DraftableCardDefinition verb = Card("PushOutput", (ItemType.Scrap, 5));
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { verb });

            Assert.IsTrue(line.GrantClaim(User, verb));
            Assert.IsFalse(line.GrantClaim(User, verb), "granting twice is a no-op");
            Assert.AreEqual(1, line.GetClaimedCards(User).Count);
            CollectionAssert.IsEmpty(line.Offers, "an owned card is never offered");
        }

        [Test]
        public void ARestore_ReplacesTheClaims()
        {
            DraftableCardDefinition a = Card("A"), b = Card("B");
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { a, b });
            line.GrantClaim(User, b);

            line.Restore(User, new[] { a }, new[] { a, b });

            CollectionAssert.AreEqual(new[] { a }, line.GetClaimedCards(User));
            CollectionAssert.AreEqual(new[] { b }, line.Offers);
        }

        [Test]
        public void TheDeck_SkipsNullsAndRepeats()
        {
            DraftableCardDefinition a = Card("A");
            var line = new AssemblyLineState();
            line.SeedCandidates(new[] { a, null, a });

            CollectionAssert.AreEqual(new[] { a }, line.Deck);
        }

        [Test]
        public void GetClaimedCards_UnknownUser_ReturnsEmpty()
        {
            Assert.IsEmpty(new AssemblyLineState().GetClaimedCards("Nobody"));
        }

        [Test]
        public void TheShippingDeck_HasNoDecayLeft()
        {
            // The data's cards carry a price and prerequisites only: DefinitionLoader is strict, so
            // a leftover decay or uniqueness key would fail the load before this line.
            var defs = AuthoredData.Load();
            Assert.IsTrue(defs.Cards.Values.All(c => c.claimCost != null));
        }

        // --- The gated vault (WorkbenchSession) -------------------------------------------------

        [Test]
        public void AnUngatedWorkbench_OffersEverything()
        {
            var workbench = new GolemFactory.UI.WorkbenchSession(5);
            Assert.IsFalse(workbench.IsRosterGated);
            Assert.IsTrue(workbench.IsCardAvailable(new AppendageActionDefinition()));
        }

        [Test]
        public void AGatedWorkbench_OffersOnlyClaimedCards()
        {
            var workbench = new GolemFactory.UI.WorkbenchSession(5);
            var line = new AssemblyLineState();
            DraftableCardDefinition claimed = Card("Claimed"), unclaimed = Card("Unclaimed");
            line.SeedCandidates(new[] { claimed, unclaimed });
            line.GrantClaim(User, claimed);

            workbench.ConfigureCardGating(line, User);

            Assert.IsTrue(workbench.IsRosterGated);
            Assert.IsTrue(workbench.IsCardAvailable(claimed.appendage));
            Assert.IsFalse(workbench.IsCardAvailable(unclaimed.appendage));
        }
    }
}
