using NUnit.Framework;
using UnityEngine;
using GolemFactory.AssemblyLine;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    public class AssemblyLineStateTests
    {
        private static DraftableCardDefinition MakeCard(int baseCost, float decayPerSecond, int minCost)
        {
            var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
            card.baseCost = baseCost;
            card.decayPerSecond = decayPerSecond;
            card.minCost = minCost;
            card.appendage = ScriptableObject.CreateInstance<PunchCards.AppendageActionDefinition>();
            return card;
        }

        [Test]
        public void SeedCandidates_FillsEmptySlots()
        {
            var line = new AssemblyLineState(2);
            DraftableCardDefinition a = MakeCard(20, 1f, 2);
            DraftableCardDefinition b = MakeCard(20, 1f, 2);

            line.SeedCandidates(new[] { a, b });

            Assert.IsNotNull(line.GetCard(0));
            Assert.IsNotNull(line.GetCard(1));
        }

        [Test]
        public void GetCurrentCost_DecaysOverTime_FloorsAtMinCost()
        {
            var line = new AssemblyLineState(1);
            DraftableCardDefinition card = MakeCard(baseCost: 20, decayPerSecond: 5f, minCost: 2);
            line.SeedCandidates(new[] { card });

            Assert.AreEqual(20, line.GetCurrentCost(0));

            line.Tick(2f);
            Assert.AreEqual(10, line.GetCurrentCost(0));

            line.Tick(100f);
            Assert.AreEqual(2, line.GetCurrentCost(0));
        }

        [Test]
        public void TryClaimSlot_InsufficientScrap_Fails_NoStateChange()
        {
            var line = new AssemblyLineState(1);
            DraftableCardDefinition card = MakeCard(20, 1f, 2);
            line.SeedCandidates(new[] { card });
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Wallet", ItemType.Scrap, 5);

            bool result = line.TryClaimSlot(0, "LocalPlayer", buffers, "Wallet");

            Assert.IsFalse(result);
            Assert.AreEqual(card, line.GetCard(0));
            Assert.AreEqual(5, buffers.GetOrCreate("Wallet").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void TryClaimSlot_SufficientScrap_WithdrawsDecayedCost_AddsToClaimed()
        {
            var line = new AssemblyLineState(1);
            DraftableCardDefinition card = MakeCard(baseCost: 20, decayPerSecond: 5f, minCost: 2);
            line.SeedCandidates(new[] { card });
            line.Tick(2f); // cost decays to 10
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Wallet", ItemType.Scrap, 20);

            bool result = line.TryClaimSlot(0, "LocalPlayer", buffers, "Wallet");

            Assert.IsTrue(result);
            Assert.AreEqual(10, buffers.GetOrCreate("Wallet").GetQuantity(ItemType.Scrap));
            CollectionAssert.Contains(line.GetClaimedCards("LocalPlayer"), card);
        }

        [Test]
        public void TryClaimSlot_RefillsSlot_FromCyclingPool()
        {
            var line = new AssemblyLineState(1);
            DraftableCardDefinition card = MakeCard(0, 0f, 0);
            line.SeedCandidates(new[] { card });
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Wallet", ItemType.Scrap, 10);

            bool claimed = line.TryClaimSlot(0, "LocalPlayer", buffers, "Wallet");

            // The pool cycles forever (no forced end condition) -- the same card
            // reappears rather than leaving the slot permanently empty.
            Assert.IsTrue(claimed);
            Assert.AreEqual(card, line.GetCard(0));
        }

        [Test]
        public void TryClaimSlot_RefilledSlot_DecayTimerResets()
        {
            var line = new AssemblyLineState(1);
            DraftableCardDefinition card = MakeCard(baseCost: 20, decayPerSecond: 5f, minCost: 2);
            line.SeedCandidates(new[] { card });
            line.Tick(3f);
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Wallet", ItemType.Scrap, 100);

            bool claimed = line.TryClaimSlot(0, "LocalPlayer", buffers, "Wallet");

            Assert.IsTrue(claimed);
            Assert.AreEqual(20, line.GetCurrentCost(0));
        }

        [Test]
        public void TryClaimSlot_EmptySlotIndex_Fails()
        {
            var line = new AssemblyLineState(1);
            var buffers = new StorageBufferRegistry();

            Assert.IsFalse(line.TryClaimSlot(0, "LocalPlayer", buffers, "Wallet"));
        }

        [Test]
        public void GetClaimedCards_UnknownUser_ReturnsEmpty()
        {
            var line = new AssemblyLineState(1);

            Assert.AreEqual(0, line.GetClaimedCards("NoSuchUser").Count);
        }
    }
}
