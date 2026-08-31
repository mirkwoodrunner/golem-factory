using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §8's four changes: bundle claim costs, prerequisites, unique cards leaving the pool, and
    /// the vault showing only what has been claimed.
    /// </summary>
    public class AssemblyLineGatingTests
    {
        private const string User = "LocalPlayer";
        private const string Wallet = "FactoryStockpile";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        private DraftableCardDefinition MakeCard(
            string name, List<RecipeIngredient> cost = null, bool unique = true)
        {
            var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
            card.name = name;
            card.claimCost = cost ?? new List<RecipeIngredient>();

            // An empty bundle falls back to the LEGACY Scrap price, whose default is 20 -- so a
            // card meant to be free has to say so on both paths. This is the same trap that
            // priced the generated verb cards at 20 Scrap apiece.
            if (card.claimCost.Count == 0)
            {
                card.baseCost = 0;
                card.minCost = 0;
                card.decayPerSecond = 0f;
            }

            card.isUnique = unique;
            card.prerequisiteCards = new List<DraftableCardDefinition>();
            card.appendage = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            // Named, because DisplayName reads the WRAPPED asset's name -- an unnamed appendage
            // makes a card that reports itself as "" in a prerequisite list.
            card.appendage.name = name + "Card";
            _created.Add(card.appendage);
            _created.Add(card);
            return card;
        }

        private static List<RecipeIngredient> Cost(params (string item, int qty)[] entries)
        {
            var list = new List<RecipeIngredient>();
            foreach ((string item, int qty) in entries)
            {
                list.Add(new RecipeIngredient(item, qty));
            }

            return list;
        }

        // --- §8.2 bundle costs ----------------------------------------------------------------

        [Test]
        public void ABundlePricedCard_ChargesEveryGoodAtomically()
        {
            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[]
            {
                MakeCard("Casing", Cost((ItemType.IronPlate, 8), (ItemType.Brass, 2))),
            });

            var buffers = new StorageBufferRegistry();
            buffers.Deposit(Wallet, ItemType.IronPlate, 8);
            buffers.Deposit(Wallet, ItemType.Brass, 1);

            Assert.IsFalse(line.TryClaimSlot(0, User, buffers, Wallet), "one good short");
            Assert.AreEqual(8, buffers.GetOrCreate(Wallet).GetQuantity(ItemType.IronPlate),
                "a refused claim refunds in full, like every other bundle withdrawal");

            buffers.Deposit(Wallet, ItemType.Brass, 1);
            Assert.IsTrue(line.TryClaimSlot(0, User, buffers, Wallet));
            Assert.AreEqual(0, buffers.GetOrCreate(Wallet).GetQuantity(ItemType.IronPlate));
            Assert.AreEqual(0, buffers.GetOrCreate(Wallet).GetQuantity(ItemType.Brass));
        }

        [Test]
        public void DecayScalesTheWholeBundle_KeepingItsShape()
        {
            DraftableCardDefinition card = MakeCard(
                "Casing", Cost((ItemType.IronPlate, 40), (ItemType.Brass, 4)));
            card.decayFractionPerSecond = 0.1f;
            card.minCostFraction = 0.25f;

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { card });
            line.Tick(5f); // factor 0.5

            IReadOnlyList<RecipeIngredient> bundle = line.GetCurrentCostBundle(0);

            Assert.AreEqual(20, bundle[0].quantity);
            Assert.AreEqual(2, bundle[1].quantity,
                "scaled, not decremented -- a flat subtraction would make the small ingredient " +
                "free while the large one barely moved");
        }

        [Test]
        public void DecayNeverTakesAnIngredientBelowOne_OrBelowTheFloor()
        {
            DraftableCardDefinition card = MakeCard("Cheap", Cost((ItemType.Brass, 2)));
            card.decayFractionPerSecond = 0.5f;
            card.minCostFraction = 0.25f;

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { card });
            line.Tick(100f);

            Assert.AreEqual(1, line.GetCurrentCostBundle(0)[0].quantity,
                "a card never becomes free -- that would change what it costs, not how much");
        }

        [Test]
        public void ALegacyScrapCard_IsUnaffected()
        {
            // M9's demo deck is authored against baseCost/decayPerSecond/minCost, and Main.unity
            // drafts from it. §8 must not reprice a single one of them.
            var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
            card.baseCost = 20;
            card.decayPerSecond = 1f;
            card.minCost = 2;
            _created.Add(card);

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { card });

            Assert.AreEqual(20, line.GetCurrentCost(0));
            line.Tick(5f);
            Assert.AreEqual(15, line.GetCurrentCost(0));
        }

        // --- §8.3 prerequisites ---------------------------------------------------------------

        [Test]
        public void ACardWithAnUnmetItemPrerequisite_NeverReachesASlot()
        {
            DraftableCardDefinition locked = MakeCard("AetherContainment");
            locked.prerequisiteItemProduced = ItemType.Lens;

            var line = new AssemblyLineState(2);
            bool lensMade = false;
            line.ConfigureUnlockContext(item => lensMade);
            line.SeedCandidates(new[] { locked });

            Assert.IsNull(line.GetCard(0), "a locked card must not be offered");
            Assert.AreEqual(1, line.WaitingCards.Count);

            lensMade = true;
            line.PromoteUnlockedCards();

            Assert.AreSame(locked, line.GetCard(0));
            Assert.AreEqual(0, line.WaitingCards.Count);
        }

        [Test]
        public void ACardPrerequisite_IsSatisfiedByClaimingIt()
        {
            DraftableCardDefinition first = MakeCard("Smelting");
            DraftableCardDefinition second = MakeCard("Casing");
            second.prerequisiteCards = new List<DraftableCardDefinition> { first };

            var line = new AssemblyLineState(2);
            line.SeedCandidates(new[] { first, second });
            var buffers = new StorageBufferRegistry();

            Assert.AreSame(first, line.GetCard(0));
            Assert.IsNull(line.GetCard(1), "the dependent card is still waiting");

            Assert.IsTrue(line.TryClaimSlot(0, User, buffers, Wallet));

            Assert.AreSame(second, line.GetCard(0),
                "claiming the prerequisite promotes the card that was waiting on it");
        }

        [Test]
        public void MissingPrerequisites_AreNameableForThePanel()
        {
            DraftableCardDefinition required = MakeCard("Smelting");
            DraftableCardDefinition locked = MakeCard("Casing");
            locked.prerequisiteCards = new List<DraftableCardDefinition> { required };
            locked.prerequisiteItemProduced = ItemType.Lens;

            var line = new AssemblyLineState(1);
            line.ConfigureUnlockContext(item => false);
            line.SeedCandidates(new[] { required, locked });

            string missing = line.DescribeMissingPrerequisites(locked);

            // DisplayName is the wrapped asset's name, not the DraftableCardDefinition's.
            StringAssert.Contains("SmeltingCard", missing);
            StringAssert.Contains(ItemType.Lens, missing);
        }

        [Test]
        public void WithNoUnlockContextWired_ItemPrerequisitesPass()
        {
            // Every pre-§8 caller: an unanswerable prerequisite must not lock a card out of a
            // scene that never opted into gating.
            DraftableCardDefinition card = MakeCard("Anything");
            card.prerequisiteItemProduced = ItemType.Lens;

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { card });

            Assert.AreSame(card, line.GetCard(0));
        }

        [Test]
        public void ALockedCardThatReachedASlotAnyway_CannotBeBought()
        {
            // The gate at the till, and it is not belt-and-braces -- it is the half that was
            // missing. Keeping locked cards out of the slots is enforced on the way IN, so a
            // scene that seeded its pool BEFORE wiring the unlock context (which SandboxBootstrap
            // did) put the whole deck on the line with an unanswerable prerequisite, and payment
            // was all that stood between the player and a recipe four tiers ahead. TryClaimSlot
            // never asked.
            DraftableCardDefinition locked = MakeCard("AetherContainment");
            locked.prerequisiteItemProduced = ItemType.Lens;

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { locked });
            Assume.That(line.GetCard(0), Is.SameAs(locked),
                "seeded with no context, exactly as the broken bootstrap order did");

            bool lensMade = false;
            line.ConfigureUnlockContext(item => lensMade);
            var buffers = new StorageBufferRegistry();

            Assert.IsFalse(line.TryClaimSlot(0, User, buffers, Wallet),
                "a free card is still not for sale while its prerequisite is unmet");
            Assert.AreEqual(0, line.GetClaimedCards(User).Count);

            lensMade = true;
            Assert.IsTrue(line.TryClaimSlot(0, User, buffers, Wallet),
                "and it sells the moment the prerequisite is satisfied");
        }

        [Test]
        public void ContextWiredBeforeSeeding_KeepsTheLockedCardOutOfThePool()
        {
            // The order SandboxBootstrap now uses, pinned as a rule rather than as scene wiring:
            // seeding asks IsUnlocked of every card on the way in, so the answer has to be
            // answerable by then.
            // Unique, so it fills slot 0 and does NOT cycle back into the queue -- otherwise
            // slot 1 gets a second copy of it and the assertion below tests nothing.
            DraftableCardDefinition open = MakeCard("Coking", unique: true);
            DraftableCardDefinition locked = MakeCard("AetherContainment");
            locked.prerequisiteItemProduced = ItemType.Lens;

            var line = new AssemblyLineState(2);
            line.ConfigureUnlockContext(item => false);
            line.SeedCandidates(new[] { open, locked });

            Assert.AreSame(open, line.GetCard(0));
            Assert.IsNull(line.GetCard(1), "the locked card never reaches the second slot");
            CollectionAssert.Contains(line.WaitingCards, locked);
        }

        // --- §8.4 unique cards ------------------------------------------------------------------

        [Test]
        public void AUniqueCard_LeavesThePoolOnceClaimed()
        {
            DraftableCardDefinition unique = MakeCard("BrassPresser", unique: true);
            DraftableCardDefinition other = MakeCard("Filler", unique: false);

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { unique, other });
            var buffers = new StorageBufferRegistry();

            Assert.IsTrue(line.TryClaimSlot(0, User, buffers, Wallet));

            // Claim the rest of the pool repeatedly; the unique card must never come back.
            for (int i = 0; i < 5; i++)
            {
                Assert.AreNotSame(unique, line.GetCard(0),
                    "a claimed unique card must never be offered again -- without this the " +
                    "track never terminates");
                line.TryClaimSlot(0, User, buffers, Wallet);
            }
        }

        [Test]
        public void ANonUniqueCard_KeepsCycling()
        {
            DraftableCardDefinition cycling = MakeCard("HaulScrap", unique: false);

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { cycling });
            var buffers = new StorageBufferRegistry();

            line.TryClaimSlot(0, User, buffers, Wallet);

            // With nothing else in the pool it comes straight back: the line is never blank.
            Assert.AreSame(cycling, line.GetCard(0));
        }

        [Test]
        public void AnAlreadyOwnedCard_YieldsItsSlotToOneThePlayerHasNot()
        {
            // §8.4's argument, applied where it always belonged. The three movement verbs are
            // non-unique, granted at t=0, AND listed twice in the shipping deck, so a plain
            // dequeue spent most of the line's throughput re-offering cards the player was
            // already holding -- the first real recipe card sat nine claims deep, eight of
            // which bought nothing.
            DraftableCardDefinition owned = MakeCard("PushOutput", unique: false);
            DraftableCardDefinition fresh = MakeCard("Coking", unique: true);

            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { owned, fresh });
            Assume.That(line.GetCard(0), Is.SameAs(owned));

            line.GrantClaim(User, owned);
            var buffers = new StorageBufferRegistry();
            line.TryClaimSlot(0, User, buffers, Wallet);

            Assert.AreSame(fresh, line.GetCard(0),
                "a slot is a purchase offer; spending one on something already owned wastes it");
        }

        [Test]
        public void GrantClaim_GivesTheOpeningHandWithoutCharging()
        {
            DraftableCardDefinition verb = MakeCard("PushOutput", unique: false);
            var line = new AssemblyLineState(1);
            line.SeedCandidates(new[] { verb });

            Assert.IsTrue(line.GrantClaim(User, verb));
            Assert.AreEqual(1, line.GetClaimedCards(User).Count);
            Assert.IsFalse(line.GrantClaim(User, verb), "granting twice is a no-op");
        }

        // --- §8.1 the gated vault ---------------------------------------------------------------

        [Test]
        public void AnUngatedWorkbench_OffersEverything()
        {
            var go = new GameObject("Workbench");
            _created.Add(go);
            var workbench = go.AddComponent<WorkbenchController>();
            var card = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            _created.Add(card);

            Assert.IsFalse(workbench.IsRosterGated);
            Assert.IsTrue(workbench.IsCardAvailable(card),
                "four milestones shipped with every card available; that stays valid unwired");
        }

        [Test]
        public void AGatedWorkbench_OffersOnlyClaimedCards()
        {
            var go = new GameObject("Workbench");
            _created.Add(go);
            var workbench = go.AddComponent<WorkbenchController>();

            var lineGo = new GameObject("Line");
            _created.Add(lineGo);
            var holder = lineGo.AddComponent<AssemblyLineStateHolder>();

            DraftableCardDefinition claimed = MakeCard("Claimed", unique: false);
            DraftableCardDefinition unclaimed = MakeCard("Unclaimed", unique: false);
            holder.State.SeedCandidates(new[] { claimed, unclaimed });
            holder.State.GrantClaim(User, claimed);

            workbench.ConfigureCardGating(holder, User);

            Assert.IsTrue(workbench.IsRosterGated);
            Assert.IsTrue(workbench.IsCardAvailable(claimed.appendage));
            Assert.IsFalse(workbench.IsCardAvailable(unclaimed.appendage));
        }
    }
}
