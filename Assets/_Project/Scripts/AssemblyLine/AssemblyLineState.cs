using System;
using System.Collections.Generic;
using UnityEngine;
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

            if (!string.IsNullOrEmpty(card.prerequisiteItemProduced))
            {
                // No context wired means the question cannot be asked, and an unanswerable
                // prerequisite passes rather than locking the card out of a scene that never
                // opted into gating.
                return _hasProducedItem == null || _hasProducedItem(card.prerequisiteItemProduced);
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
            int promoted = 0;
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                if (!IsUnlocked(_waiting[i]))
                {
                    continue;
                }

                _refillQueue.Enqueue(_waiting[i]);
                _waiting.RemoveAt(i);
                promoted++;
            }

            if (promoted > 0)
            {
                RefillEmptySlots();
            }

            return promoted;
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
                    DraftableCardDefinition next = _refillQueue.Dequeue();
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
    }
}
