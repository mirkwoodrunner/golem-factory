using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.AssemblyLine
{
    // Solo digital translation of the tabletop Assembly Line: a fixed number of slots,
    // each holding a card that gets cheaper to draft the longer it sits unclaimed.
    // Claiming refills that slot from a cycling candidate pool ("drip-feeds new unlocks
    // over time" per the multiplayer-compatible-seams note in the implementation plan).
    // TryClaimSlot is keyed by userId from day one, even though v1 only ever has one
    // claimer -- same convention as PatentRegistry/Blueprint.
    public sealed class AssemblyLineState
    {
        private readonly DraftableCardDefinition[] _slots;
        private readonly float[] _secondsOnLine;
        private readonly Queue<DraftableCardDefinition> _refillQueue = new Queue<DraftableCardDefinition>();
        private readonly Dictionary<string, List<DraftableCardDefinition>> _claimedByUser =
            new Dictionary<string, List<DraftableCardDefinition>>();

        // §8.3: cards whose prerequisites are not met yet. They are NOT in the refill queue --
        // that is the whole mechanism. A locked card must not be able to reach a slot, because a
        // slot is a purchase offer and offering something unbuyable is the "why can't I claim
        // that?" question §8's legibility table exists to answer.
        private readonly List<DraftableCardDefinition> _waiting = new List<DraftableCardDefinition>();

        // What the world has produced, asked rather than stored, so the Assembly Line never
        // becomes a second ledger of the same facts. Null means "no gating context wired", which
        // is how every pre-§8 caller keeps its old behaviour: prerequisites simply pass.
        private Func<string, bool> _hasProducedItem;

        public int SlotCount { get; }

        public AssemblyLineState(int slotCount)
        {
            SlotCount = slotCount;
            _slots = new DraftableCardDefinition[slotCount];
            _secondsOnLine = new float[slotCount];
        }

        /// <summary>
        /// Tells the line how to ask whether an item has ever been produced, for §8.3's
        /// <c>prerequisiteItemProduced</c>. Optional: unwired, item prerequisites pass, which is
        /// exactly the behaviour every caller had before §8.
        /// </summary>
        public void ConfigureUnlockContext(Func<string, bool> hasProducedItem)
        {
            _hasProducedItem = hasProducedItem;
            PromoteUnlockedCards();
        }

        // Seeds the pool of candidates that refill claimed/empty slots, then immediately
        // fills any still-empty slots so the line starts populated rather than empty.
        //
        // §8.3 SPLITS THE POOL IN TWO on the way in: a card whose prerequisites are unmet waits
        // outside the queue entirely rather than sitting in it unclaimable.
        public void SeedCandidates(IEnumerable<DraftableCardDefinition> candidates)
        {
            foreach (DraftableCardDefinition card in candidates)
            {
                if (card == null)
                {
                    continue;
                }

                if (IsUnlocked(card))
                {
                    _refillQueue.Enqueue(card);
                }
                else
                {
                    _waiting.Add(card);
                }
            }

            RefillEmptySlots();
        }

        /// <summary>
        /// Whether every prerequisite on <paramref name="card"/> is satisfied: each named card
        /// claimed by someone, and the named item produced.
        /// </summary>
        public bool IsUnlocked(DraftableCardDefinition card)
        {
            if (card == null)
            {
                return false;
            }

            if (card.prerequisiteCards != null)
            {
                for (int i = 0; i < card.prerequisiteCards.Count; i++)
                {
                    DraftableCardDefinition required = card.prerequisiteCards[i];
                    if (required != null && !IsClaimedByAnyone(required))
                    {
                        return false;
                    }
                }
            }

            // No context wired means the question cannot be asked, and an unanswerable
            // prerequisite passes rather than locking the card out of a scene that never opted
            // into gating.
            if (_hasProducedItem == null)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(card.prerequisiteItemProduced) && !_hasProducedItem(card.prerequisiteItemProduced))
            {
                return false;
            }

            // G10, from playtest ("it shouldn't be random, it should be ones I am capable of
            // claiming"): a card is offered only once the factory has made every good its PRICE
            // asks for. Casing Press used to sit in a slot asking for Brass before Brass existed,
            // claimable by nobody. Answered from the same ever-growing ledger as the
            // prerequisite, so it never flickers as stock rises and falls -- once you could pay
            // for a card in principle, it stays offered.
            if (card.HasBundleCost)
            {
                foreach (RecipeIngredient c in card.claimCost)
                {
                    if (c.quantity > 0 && !string.IsNullOrEmpty(c.itemType) && !_hasProducedItem(c.itemType))
                    {
                        return false;
                    }
                }
            }
            else if (card.baseCost > 0 && !_hasProducedItem(ItemType.Scrap))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// What a locked card is still waiting for, for the panel's "Requires: ..." line
        /// (§8's legibility table). Empty when nothing is outstanding.
        /// </summary>
        public string DescribeMissingPrerequisites(DraftableCardDefinition card)
        {
            if (card == null || IsUnlocked(card))
            {
                return "";
            }

            var missing = new List<string>();
            if (card.prerequisiteCards != null)
            {
                for (int i = 0; i < card.prerequisiteCards.Count; i++)
                {
                    DraftableCardDefinition required = card.prerequisiteCards[i];
                    if (required != null && !IsClaimedByAnyone(required))
                    {
                        missing.Add(required.DisplayName);
                    }
                }
            }

            if (!string.IsNullOrEmpty(card.prerequisiteItemProduced)
                && _hasProducedItem != null && !_hasProducedItem(card.prerequisiteItemProduced))
            {
                missing.Add(card.prerequisiteItemProduced);
            }

            // The price gate IsUnlocked applies (G10): a good the factory has never made. Without
            // it a card locked only by its price read "needs " and then nothing (from review).
            if (_hasProducedItem != null)
            {
                if (card.HasBundleCost)
                {
                    foreach (RecipeIngredient c in card.claimCost)
                    {
                        if (c.quantity > 0 && !string.IsNullOrEmpty(c.itemType) && !_hasProducedItem(c.itemType)
                            && !missing.Contains(c.itemType))
                        {
                            missing.Add(c.itemType);
                        }
                    }
                }
                else if (card.baseCost > 0 && !_hasProducedItem(ItemType.Scrap) && !missing.Contains(ItemType.Scrap))
                {
                    missing.Add(ItemType.Scrap);
                }
            }

            return missing.Count == 0 ? "" : string.Join(", ", missing);
        }

        /// <summary>Cards still locked out of the pool. Exposed so the panel can show the
        /// player what is coming and why it is not here yet.</summary>
        public IReadOnlyList<DraftableCardDefinition> WaitingCards => _waiting;

        private bool IsClaimedByAnyone(DraftableCardDefinition card)
        {
            foreach (KeyValuePair<string, List<DraftableCardDefinition>> pair in _claimedByUser)
            {
                if (pair.Value.Contains(card))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Moves every newly-satisfied waiting card into the refill queue. Called whenever the
        /// world changes in a way a prerequisite could depend on -- a claim, or a produced item.
        /// </summary>
        public int PromoteUnlockedCards()
        {
            // To the FRONT of the queue, in deck order (G10, found writing the guide's second
            // chapter). A card unlocks the moment the factory makes what it needs, which is the
            // moment the player needs it -- and it used to join the BACK, behind the Aether-Hauler
            // and Casing Press cards, so R2 Scrap Reclamation, the first recipe a Presser runs,
            // came up only after claiming through half a dozen cards, two of them unaffordable
            // that early and so stuck in their slots. (The loop also ran backwards, so cards
            // unlocked together arrived in reverse deck order.) Same complaint as the skip-owned
            // rule in DequeueNextOffer: a card track, not a slot machine.
            var promotedCards = new List<DraftableCardDefinition>();
            for (int i = 0; i < _waiting.Count; i++)
            {
                if (IsUnlocked(_waiting[i]))
                {
                    promotedCards.Add(_waiting[i]);
                }
            }

            if (promotedCards.Count > 0)
            {
                foreach (DraftableCardDefinition card in promotedCards)
                {
                    _waiting.Remove(card);
                }
                var rest = _refillQueue.ToArray();
                _refillQueue.Clear();
                foreach (DraftableCardDefinition card in promotedCards)
                {
                    _refillQueue.Enqueue(card);
                }
                foreach (DraftableCardDefinition card in rest)
                {
                    _refillQueue.Enqueue(card);
                }
                RefillEmptySlots();
                Rebalance();
            }

            return promotedCards.Count;
        }

        public DraftableCardDefinition GetCard(int slotIndex) => _slots[slotIndex];

        public int GetCurrentCost(int slotIndex)
        {
            DraftableCardDefinition card = _slots[slotIndex];
            if (card == null)
            {
                return 0;
            }

            // A bundle-priced card reports its SCRAP component here, so every pre-§8 caller
            // (and the Scrap-only wallet check below) keeps working unchanged and simply reads
            // zero for a card that asks for no Scrap.
            if (card.HasBundleCost)
            {
                IReadOnlyList<RecipeIngredient> bundle = GetCurrentCostBundle(slotIndex);
                for (int i = 0; i < bundle.Count; i++)
                {
                    if (bundle[i].itemType == ItemType.Scrap)
                    {
                        return bundle[i].quantity;
                    }
                }

                return 0;
            }

            float decayed = card.baseCost - card.decayPerSecond * _secondsOnLine[slotIndex];
            return Mathf.Max(card.minCost, Mathf.RoundToInt(decayed));
        }

        /// <summary>
        /// §8.2: the whole claim cost as it stands right now, with decay scaling every good in
        /// the bundle by the same factor.
        ///
        /// <para>
        /// SCALED, not decremented per good, and that is what keeps a bundle's shape meaningful:
        /// subtracting a flat amount from each entry would make a 2-unit ingredient free while a
        /// 40-unit one barely moved, so a card's ratio of goods would drift as it sat there.
        /// </para>
        ///
        /// <para>
        /// A legacy Scrap-priced card reports its single decayed Scrap entry, so one method
        /// answers for both authoring styles.
        /// </para>
        /// </summary>
        public IReadOnlyList<RecipeIngredient> GetCurrentCostBundle(int slotIndex)
        {
            DraftableCardDefinition card = _slots[slotIndex];
            if (card == null)
            {
                return Array.Empty<RecipeIngredient>();
            }

            if (!card.HasBundleCost)
            {
                return new[] { new RecipeIngredient(ItemType.Scrap, GetCurrentCost(slotIndex)) };
            }

            float factor = 1f - card.decayFractionPerSecond * _secondsOnLine[slotIndex];
            float floor = Mathf.Clamp01(card.minCostFraction);
            factor = Mathf.Clamp(factor, floor, 1f);

            var bundle = new RecipeIngredient[card.claimCost.Count];
            for (int i = 0; i < card.claimCost.Count; i++)
            {
                RecipeIngredient authored = card.claimCost[i];
                // At least one unit of anything the card asks for: decaying an ingredient to
                // zero would quietly change what the card costs rather than what it costs.
                int quantity = Mathf.Max(1, Mathf.RoundToInt(authored.quantity * factor));
                bundle[i] = new RecipeIngredient(authored.itemType, quantity);
            }

            return bundle;
        }

        public void Tick(float deltaTime)
        {
            Rebalance();

            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != null)
                {
                    _secondsOnLine[i] += deltaTime;
                }
            }
        }

        public bool TryClaimSlot(int slotIndex, string userId, StorageBufferRegistry buffers, string bufferId)
        {
            DraftableCardDefinition card = _slots[slotIndex];
            if (card == null || buffers == null)
            {
                return false;
            }

            // §8.3, ENFORCED AT THE TILL AS WELL AS AT THE DOOR. Keeping a locked card out of
            // the slots is the mechanism; refusing to sell one that got into a slot anyway is
            // what makes the gate a gate. Without this the whole of §8.3 was advisory -- a
            // scene that seeded its pool before wiring the unlock context (which SandboxBootstrap
            // did) put the entire deck on the line, and payment was the only thing standing
            // between the player and a recipe four tiers ahead of them.
            //
            // Safe only because the unlock context is MONOTONE: it answers from
            // TechTreeProgressLedger, which never forgets. Pointed at live buffer contents
            // instead, spending your last Scrap would take a card off the market mid-decision.
            if (!IsUnlocked(card))
            {
                return false;
            }

            // Atomic with a full refund on shortfall, exactly as a chassis, a building and a
            // bay upgrade are: a bundle-priced card that took the Scrap and refused on the Iron
            // Plate would be theft at the panel.
            if (card.HasBundleCost)
            {
                if (!buffers.TryWithdrawBundle(bufferId, GetCurrentCostBundle(slotIndex)))
                {
                    return false;
                }
            }
            else
            {
                // A ZERO COST MUST TRIVIALLY SUCCEED, even against a buffer that has never held
                // Scrap. StorageBuffer.TryWithdraw looks the item type up before it looks at the
                // amount and fails on a miss, so a literal TryWithdraw(id, Scrap, 0) refuses a
                // genuinely free card -- which is precisely what made the free movement verbs
                // unclaimable. TryWithdrawScrapAndBrass and TryWithdrawBundle both already guard
                // this; this was the one payment path that did not.
                int cost = GetCurrentCost(slotIndex);
                if (cost > 0 && !buffers.TryWithdraw(bufferId, ItemType.Scrap, cost))
                {
                    return false;
                }
            }

            if (!_claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed))
            {
                claimed = new List<DraftableCardDefinition>();
                _claimedByUser[userId] = claimed;
            }
            claimed.Add(card);

            _slots[slotIndex] = null;
            _secondsOnLine[slotIndex] = 0f;

            // §8.3: claiming a card can satisfy another card's prerequisite, so the waiting list
            // is re-checked before the slots are refilled -- otherwise the newly unlocked card
            // could not appear until some later, unrelated claim happened to trigger a refill.
            PromoteUnlockedCards();
            RefillEmptySlots();

            return true;
        }

        /// <summary>
        /// Puts a card in a player's claimed set without payment, and takes it out of the pool
        /// if it is unique.
        ///
        /// <para>
        /// TWO CALLERS, both of which must not charge. The OPENING HAND: §9 Phase 1 has the
        /// player programming a Scavenger in the first minutes, and with §8's gating on they
        /// cannot do that until they hold the movement verbs -- waiting for Push to drift to
        /// the top of a drip-fed queue is §10's soft-lock wearing a slot machine's face. And a
        /// LOAD, which restores what was already bought in the session that bought it.
        /// </para>
        /// </summary>
        public bool GrantClaim(string userId, DraftableCardDefinition card)
        {
            if (card == null || userId == null)
            {
                return false;
            }

            if (!_claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed))
            {
                claimed = new List<DraftableCardDefinition>();
                _claimedByUser[userId] = claimed;
            }

            if (claimed.Contains(card))
            {
                return false;
            }

            claimed.Add(card);

            // A granted unique card must leave the pool too, or the line goes on offering the
            // player something they already own -- the exact slot-wasting §8.4 exists to stop.
            if (card.isUnique)
            {
                _waiting.Remove(card);
                RemoveFromQueue(card);
                for (int i = 0; i < SlotCount; i++)
                {
                    if (_slots[i] == card)
                    {
                        _slots[i] = null;
                        _secondsOnLine[i] = 0f;
                    }
                }
            }

            PromoteUnlockedCards();
            RefillEmptySlots();
            return true;
        }

        private void RemoveFromQueue(DraftableCardDefinition card)
        {
            int count = _refillQueue.Count;
            for (int i = 0; i < count; i++)
            {
                DraftableCardDefinition next = _refillQueue.Dequeue();
                if (next != card)
                {
                    _refillQueue.Enqueue(next);
                }
            }
        }

        /// <summary>
        /// A load: forget every claim, slot and queue, then rebuild the line as a fresh session
        /// would -- the claims first (so the first fill skips what is owned), then the deck. A
        /// load replaces progress rather than merging into it, or a card bought after the save
        /// would survive a load that also handed back the goods it cost. The unlock context is
        /// kept, and should already answer from the restored ledger.
        /// </summary>
        public void Restore(string userId, IEnumerable<DraftableCardDefinition> claimed, IEnumerable<DraftableCardDefinition> deck)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                _slots[i] = null;
                _secondsOnLine[i] = 0f;
            }
            _refillQueue.Clear();
            _waiting.Clear();
            _claimedByUser.Clear();
            foreach (DraftableCardDefinition card in claimed)
            {
                GrantClaim(userId, card);
            }
            SeedCandidates(deck);
        }

        public IReadOnlyList<DraftableCardDefinition> GetClaimedCards(string userId) =>
            _claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed)
                ? claimed
                : (IReadOnlyList<DraftableCardDefinition>)Array.Empty<DraftableCardDefinition>();

        // §8.4: a UNIQUE card is gone once claimed; everything else is re-enqueued behind the
        // rest of the pool and keeps cycling.
        //
        // The old comment said cycling forever "matches the sandbox's no-forced-end-condition
        // design". That reasoning holds for a generic Logic Core and stops holding for a recipe:
        // re-offering the Brass Presser card the player already owns costs them a slot for the
        // rest of the game, and §8 records the consequence plainly -- without isUnique the tech
        // tree never terminates.
        private void RefillEmptySlots()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null && _refillQueue.Count > 0)
                {
                    DraftableCardDefinition next = DequeueNextOffer();
                    _slots[i] = next;
                    _secondsOnLine[i] = 0f;

                    // A cycling card goes straight back to the end of the queue, exactly as it
                    // always did. A UNIQUE one does not: it is on the line now, and if it is
                    // claimed it is gone. Nothing puts it back, which is what makes the track
                    // terminate (§8.4).
                    if (!next.isUnique)
                    {
                        _refillQueue.Enqueue(next);
                    }
                }
            }
        }

        /// <summary>
        /// The next card worth OFFERING: the first candidate nobody already owns, with the
        /// owned ones rotated to the back of the queue rather than dropped.
        ///
        /// <para>
        /// §8.4 already says this about unique cards -- "the line goes on offering the player
        /// something they already own", the slot-wasting it exists to stop -- and the argument
        /// never depended on uniqueness. The three movement verbs are non-unique and granted at
        /// t=0, and they appear in the deck TWICE, so a plain <c>Dequeue</c> spent most of the
        /// line's throughput re-offering Extract, Haul and Push to a player already holding
        /// them. With §8.3's gating actually working, that is the difference between a card
        /// track and a slot machine: measured against the shipping deck, the first real recipe
        /// card sat NINE claims deep, eight of which bought nothing.
        /// </para>
        ///
        /// <para>
        /// The fallback matters as much as the rule: if every candidate is already owned the
        /// head is offered anyway, so the line is never blank. A non-unique card therefore still
        /// cycles -- it just yields to anything the player has not got yet.
        /// </para>
        /// </summary>
        private DraftableCardDefinition DequeueNextOffer()
        {
            // G10, from playtest ("it says to claim scrap reclamation, but I don't see it"): a
            // ONE-OFF card nobody owns comes before a cycling verb. Repeat Assembly and Freight
            // Launch cycle forever and only matter to the Overclocker and the Zeppelin, yet they
            // sat in all three slots -- Repeat twice -- while R2 Scrap Reclamation waited behind
            // them, out of sight until two claims emptied their slots. Then an unowned card not
            // already on the line, then any unowned card, then the head, as before.
            DraftableCardDefinition[] queue = _refillQueue.ToArray();
            int pick = FirstIndex(queue, c => c.isUnique && !IsClaimedByAnyone(c));
            if (pick < 0)
            {
                pick = FirstIndex(queue, c => !IsClaimedByAnyone(c) && !IsOnTheLine(c));
            }
            if (pick < 0)
            {
                pick = FirstIndex(queue, c => !IsClaimedByAnyone(c));
            }
            if (pick < 0)
            {
                pick = 0;
            }

            // The cards ahead of the pick are rotated behind the rest, not discarded, keeping
            // the queue's relative order -- the skipped cards come back round after this offer.
            _refillQueue.Clear();
            for (int i = pick + 1; i < queue.Length; i++)
            {
                _refillQueue.Enqueue(queue[i]);
            }
            for (int i = 0; i < pick; i++)
            {
                _refillQueue.Enqueue(queue[i]);
            }
            return queue[pick];
        }

        private static int FirstIndex(DraftableCardDefinition[] cards, Func<DraftableCardDefinition, bool> test)
        {
            for (int i = 0; i < cards.Length; i++)
            {
                if (test(cards[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        private bool IsOnTheLine(DraftableCardDefinition card)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == card)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A slot holding a cycling verb gives way while a one-off card nobody owns is waiting in
        /// the queue (G10). The verb is not lost: a cycling card keeps its place in the queue
        /// while it is on the line, so it comes round again once nothing better is waiting.
        /// Called on promotion and every tick, so a line that was already full of verbs when a
        /// card unlocked -- a saved game, say -- puts the card on show the next frame.
        /// </summary>
        /// <summary>
        /// The card the player is being asked for right now (the guide's claim steps), or null.
        /// <see cref="Rebalance"/> keeps it on show (G10: "I don't see the ones you want me to claim").
        /// </summary>
        public Func<DraftableCardDefinition, bool> Wanted { get; set; }

        public void Rebalance()
        {
            ShowWanted();

            bool cleared = false;
            for (int i = 0; i < SlotCount; i++)
            {
                // Never the card the guide is asking for: a wanted cycling verb (Freight Launch)
                // was put on show by ShowWanted and cleared again by this loop in the same call,
                // so it never stayed on the line (from review).
                if (_slots[i] != null && !_slots[i].isUnique && !(Wanted?.Invoke(_slots[i]) ?? false)
                    && QueueHoldsUnownedOneOff())
                {
                    _slots[i] = null;
                    _secondsOnLine[i] = 0f;
                    cleared = true;
                    RefillEmptySlots();
                }
            }
            if (cleared)
            {
                RefillEmptySlots();
            }
        }

        /// <summary>
        /// Puts a wanted card that is unlocked and waiting in the queue onto the line: into a
        /// cycling verb's slot if there is one, else the last slot, whose card goes back to the
        /// FRONT of the queue so it is next up again.
        /// </summary>
        private void ShowWanted()
        {
            if (Wanted == null)
            {
                return;
            }
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] != null && Wanted(_slots[i]))
                {
                    return; // already on show
                }
            }

            DraftableCardDefinition wanted = null;
            foreach (DraftableCardDefinition card in _refillQueue)
            {
                if (Wanted(card) && !IsClaimedByAnyone(card))
                {
                    wanted = card;
                    break;
                }
            }
            if (wanted == null)
            {
                return;
            }

            int slot = SlotCount - 1;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null || !_slots[i].isUnique)
                {
                    slot = i;
                    break;
                }
            }

            var rest = _refillQueue.Where(c => c != wanted).ToList();
            _refillQueue.Clear();
            DraftableCardDefinition displaced = _slots[slot];
            if (displaced != null && displaced.isUnique)
            {
                _refillQueue.Enqueue(displaced); // next up again, not lost
            }
            foreach (DraftableCardDefinition card in rest)
            {
                _refillQueue.Enqueue(card);
            }
            _slots[slot] = wanted;
            _secondsOnLine[slot] = 0f;
        }

        private bool QueueHoldsUnownedOneOff()
        {
            foreach (DraftableCardDefinition card in _refillQueue)
            {
                if (card.isUnique && !IsClaimedByAnyone(card))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
