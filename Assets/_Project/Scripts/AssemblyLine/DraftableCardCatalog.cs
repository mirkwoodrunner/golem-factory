using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.AssemblyLine
{
    /// <summary>
    /// The deck the Assembly Line draws from, plus the hand the player starts holding.
    ///
    /// <para>
    /// A ScriptableObject rather than a list serialized on the scene's bootstrap, for the reason
    /// the recipe and chassis assets are: the deck is CONTENT. It is authored by
    /// <c>ProgressionAssetAuthoring</c> from the recipes and chassis themselves, it is the same
    /// deck in every scene that wants one, and an asset can be referenced from a prefab without
    /// the cross-prefab problem a scene list would have.
    /// </para>
    ///
    /// <para>
    /// <b>The opening hand is part of the content, not a special case in code.</b> §8's gating
    /// makes the vault show only claimed cards, and §9 Phase 1 has the player programming a
    /// golem within minutes -- so something has to be in hand at t=0 or the opening is a
    /// soft-lock. Naming those cards here keeps that decision visible and editable next to the
    /// deck it is drawn from, rather than buried as a hardcoded list of asset names.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "AssemblyLineDeck", menuName = "Golem Factory/Assembly Line/Deck")]
    public sealed class DraftableCardCatalog : ScriptableObject
    {
        [SerializeField] private List<DraftableCardDefinition> cards = new List<DraftableCardDefinition>();

        [Tooltip("Granted free at the start of a run. The movement verbs: without them a gated " +
                 "vault cannot program anything at all.")]
        [SerializeField] private List<DraftableCardDefinition> openingHand = new List<DraftableCardDefinition>();

        public IReadOnlyList<DraftableCardDefinition> Cards => cards;
        public IReadOnlyList<DraftableCardDefinition> OpeningHand => openingHand;

        /// <summary>Rebuilt wholesale by the authoring pass -- "always re-render from data", so
        /// a card removed from the deck actually leaves it.</summary>
        public void SetContents(
            List<DraftableCardDefinition> deck, List<DraftableCardDefinition> hand)
        {
            cards = deck ?? new List<DraftableCardDefinition>();
            openingHand = hand ?? new List<DraftableCardDefinition>();
        }
    }
}
