using System;
using System.Collections.Generic;
using UnityEngine;
using GolemFactory.Economy;

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

        public int SlotCount { get; }

        public AssemblyLineState(int slotCount)
        {
            SlotCount = slotCount;
            _slots = new DraftableCardDefinition[slotCount];
            _secondsOnLine = new float[slotCount];
        }

        // Seeds the pool of candidates that refill claimed/empty slots, then immediately
        // fills any still-empty slots so the line starts populated rather than empty.
        public void SeedCandidates(IEnumerable<DraftableCardDefinition> candidates)
        {
            foreach (DraftableCardDefinition card in candidates)
            {
                _refillQueue.Enqueue(card);
            }
            RefillEmptySlots();
        }

        public DraftableCardDefinition GetCard(int slotIndex) => _slots[slotIndex];

        public int GetCurrentCost(int slotIndex)
        {
            DraftableCardDefinition card = _slots[slotIndex];
            if (card == null)
            {
                return 0;
            }

            float decayed = card.baseCost - card.decayPerSecond * _secondsOnLine[slotIndex];
            return Mathf.Max(card.minCost, Mathf.RoundToInt(decayed));
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

            int cost = GetCurrentCost(slotIndex);
            if (!buffers.TryWithdraw(bufferId, ItemType.Scrap, cost))
            {
                return false;
            }

            if (!_claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed))
            {
                claimed = new List<DraftableCardDefinition>();
                _claimedByUser[userId] = claimed;
            }
            claimed.Add(card);

            _slots[slotIndex] = null;
            _secondsOnLine[slotIndex] = 0f;
            RefillEmptySlots();

            return true;
        }

        public IReadOnlyList<DraftableCardDefinition> GetClaimedCards(string userId) =>
            _claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed)
                ? claimed
                : (IReadOnlyList<DraftableCardDefinition>)Array.Empty<DraftableCardDefinition>();

        // Re-enqueues the drafted card behind the rest of the pool rather than removing it
        // permanently, so the line cycles forever -- matches the sandbox's "no forced end
        // condition" design (same infinite-resource precedent as the demo's ScrapNode).
        private void RefillEmptySlots()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null && _refillQueue.Count > 0)
                {
                    DraftableCardDefinition next = _refillQueue.Dequeue();
                    _slots[i] = next;
                    _secondsOnLine[i] = 0f;
                    _refillQueue.Enqueue(next);
                }
            }
        }
    }
}
