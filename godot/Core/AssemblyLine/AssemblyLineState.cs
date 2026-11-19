using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.AssemblyLine
{
    /// <summary>
    /// The player's unlocks: every card in the deck, each claimed once at its fixed price
    /// (2026-11-19). A card is offered the moment its prerequisites hold, and the player picks
    /// which to claim, from the Assembly Line tab or its plaque in the Ledger.
    ///
    /// <para>
    /// It replaced a three-slot draft row whose cards grew cheaper the longer they sat and refilled
    /// from a queue. Solo, nobody else could take a card, so waiting was always right, and the
    /// player could not choose what to unlock next: three playtest fixes had already bent the row
    /// into a tech tree ("it shouldn't be random", "I don't see it", "I don't see the ones you want
    /// me to claim"). The Ledger draws the tree; this is the ledger of what is owned.
    /// </para>
    ///
    /// <para>
    /// Claims are keyed by userId (the ownership convention Blueprint's OwnerId follows), though
    /// only one claimer exists.
    /// </para>
    /// </summary>
    public sealed class AssemblyLineState
    {
        private readonly List<DraftableCardDefinition> _deck = new List<DraftableCardDefinition>();
        private readonly Dictionary<string, List<DraftableCardDefinition>> _claimedByUser =
            new Dictionary<string, List<DraftableCardDefinition>>();

        // What the world has produced, asked rather than stored, so the line never becomes a
        // second ledger of the same facts. Null means "no gating context wired": prerequisites on
        // goods pass.
        private Func<string, bool> _hasProducedItem;

        /// <summary>
        /// Tells the line how to ask whether an item has ever been produced. Optional: unwired,
        /// every good counts as seen.
        /// </summary>
        public void ConfigureUnlockContext(Func<string, bool> hasProducedItem) => _hasProducedItem = hasProducedItem;

        /// <summary>Adds the deck's cards, in its order, skipping nulls and repeats.</summary>
        public void SeedCandidates(IEnumerable<DraftableCardDefinition> candidates)
        {
            foreach (DraftableCardDefinition card in candidates ?? Enumerable.Empty<DraftableCardDefinition>())
            {
                if (card != null && !_deck.Contains(card))
                {
                    _deck.Add(card);
                }
            }
        }

        /// <summary>Every card that can ever be claimed, in the deck's order.</summary>
        public IReadOnlyList<DraftableCardDefinition> Deck => _deck;

        /// <summary>What claiming <paramref name="card"/> costs: its whole bundle, never less. Empty is free.</summary>
        public static IReadOnlyList<RecipeIngredient> PriceOf(DraftableCardDefinition card) =>
            card?.claimCost ?? (IReadOnlyList<RecipeIngredient>)Array.Empty<RecipeIngredient>();

        /// <summary>The cards the player can claim now: unlocked and not yet owned, in deck order.</summary>
        public IReadOnlyList<DraftableCardDefinition> Offers =>
            _deck.Where(c => !IsClaimedByAnyone(c) && IsUnlocked(c)).ToList();

        /// <summary>Cards still locked, so the panel can show what is coming and why it is not here yet.</summary>
        public IReadOnlyList<DraftableCardDefinition> WaitingCards =>
            _deck.Where(c => !IsClaimedByAnyone(c) && !IsUnlocked(c)).ToList();

        /// <summary>Whether anyone holds <paramref name="card"/>.</summary>
        public bool IsClaimed(DraftableCardDefinition card) => IsClaimedByAnyone(card);

        /// <summary>Whether <paramref name="card"/> is on offer now: unlocked and unowned.</summary>
        public bool IsOffered(DraftableCardDefinition card) => card != null && _deck.Contains(card) && !IsClaimedByAnyone(card) && IsUnlocked(card);

        /// <summary>
        /// Whether every prerequisite on <paramref name="card"/> is satisfied: each named card
        /// claimed by someone, the named item produced, and every good its price names produced
        /// (G10: "it shouldn't be random, it should be ones I am capable of claiming"). Answered
        /// from the ever-growing ledger, so a card never flickers off as stock rises and falls.
        /// </summary>
        public bool IsUnlocked(DraftableCardDefinition card)
        {
            if (card == null)
            {
                return false;
            }

            if (card.prerequisiteCards != null && card.prerequisiteCards.Any(r => r != null && !IsClaimedByAnyone(r)))
            {
                return false;
            }

            if (_hasProducedItem == null)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(card.prerequisiteItemProduced) && !_hasProducedItem(card.prerequisiteItemProduced))
            {
                return false;
            }

            return PriceOf(card).All(c => c.quantity <= 0 || string.IsNullOrEmpty(c.itemType) || _hasProducedItem(c.itemType));
        }

        /// <summary>
        /// What a locked card is still waiting for, for the panel's "needs ..." line. Empty when
        /// nothing is outstanding.
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
                foreach (DraftableCardDefinition required in card.prerequisiteCards)
                {
                    if (required != null && !IsClaimedByAnyone(required))
                    {
                        missing.Add(required.DisplayName);
                    }
                }
            }

            if (_hasProducedItem != null)
            {
                if (!string.IsNullOrEmpty(card.prerequisiteItemProduced) && !_hasProducedItem(card.prerequisiteItemProduced))
                {
                    missing.Add(card.prerequisiteItemProduced);
                }
                foreach (RecipeIngredient c in PriceOf(card))
                {
                    if (c.quantity > 0 && !string.IsNullOrEmpty(c.itemType) && !_hasProducedItem(c.itemType)
                        && !missing.Contains(c.itemType))
                    {
                        missing.Add(c.itemType);
                    }
                }
            }

            return string.Join(", ", missing);
        }

        /// <summary>
        /// Claims <paramref name="card"/> at its price, paid through the wallet (pack, then the
        /// Post) whole or not at all. Refused for a card not on offer: locked, owned, or not in
        /// the deck.
        /// </summary>
        public bool TryClaim(DraftableCardDefinition card, string userId, StorageBufferRegistry buffers, string bufferId, string postBufferId = null)
        {
            if (!IsOffered(card) || buffers == null)
            {
                return false;
            }

            // A zero cost trivially succeeds, even against a buffer that has never held anything:
            // PlayerWallet.TryCharge treats an empty bundle as free.
            if (!new PlayerWallet(buffers, bufferId, postBufferId).TryCharge(PriceOf(card)))
            {
                return false;
            }

            Claimed(userId).Add(card);
            return true;
        }

        /// <summary>
        /// Puts a card in a player's claimed set without payment. TWO CALLERS, neither of which
        /// charges: the opening hand (the movement verbs, held from the first minute), and a load,
        /// which restores what was already bought.
        /// </summary>
        public bool GrantClaim(string userId, DraftableCardDefinition card)
        {
            if (card == null || userId == null)
            {
                return false;
            }
            List<DraftableCardDefinition> claimed = Claimed(userId);
            if (claimed.Contains(card))
            {
                return false;
            }
            claimed.Add(card);
            return true;
        }

        /// <summary>
        /// A load: forget every claim, then rebuild as a fresh session would, the claims first and
        /// then the deck. A load replaces progress rather than merging into it, or a card bought
        /// after the save would survive a load that also handed back the goods it cost.
        /// </summary>
        public void Restore(string userId, IEnumerable<DraftableCardDefinition> claimed, IEnumerable<DraftableCardDefinition> deck)
        {
            _claimedByUser.Clear();
            _deck.Clear();
            foreach (DraftableCardDefinition card in claimed ?? Enumerable.Empty<DraftableCardDefinition>())
            {
                GrantClaim(userId, card);
            }
            SeedCandidates(deck);
        }

        public IReadOnlyList<DraftableCardDefinition> GetClaimedCards(string userId) =>
            userId != null && _claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed)
                ? claimed
                : (IReadOnlyList<DraftableCardDefinition>)Array.Empty<DraftableCardDefinition>();

        private List<DraftableCardDefinition> Claimed(string userId)
        {
            if (!_claimedByUser.TryGetValue(userId, out List<DraftableCardDefinition> claimed))
            {
                claimed = new List<DraftableCardDefinition>();
                _claimedByUser[userId] = claimed;
            }
            return claimed;
        }

        private bool IsClaimedByAnyone(DraftableCardDefinition card) =>
            card != null && _claimedByUser.Values.Any(list => list.Contains(card));
    }
}
