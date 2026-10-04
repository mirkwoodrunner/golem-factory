using System.Collections.Generic;

namespace GolemFactory.AssemblyLine
{
    /// <summary>
    /// The deck the Assembly Line draws from, plus the hand the player starts holding.
    ///
    /// <para>
    /// Authored CONTENT, like the recipe and chassis definitions: it lives in
    /// <c>godot/data/assembly_line_decks.json</c> (converted from Unity's AssemblyLineDeck.asset)
    /// and is the same deck in every scene that wants one.
    /// </para>
    ///
    /// <para>
    /// <b>The opening hand is part of the content, not a special case in code.</b> §8's gating
    /// makes the vault show only claimed cards, and §9 Phase 1 has the player programming a
    /// golem within minutes -- so something has to be in hand at t=0 or the opening is a
    /// soft-lock. Naming those cards here keeps that decision visible and editable next to the
    /// deck it is drawn from, rather than buried as a hardcoded list of names.
    /// </para>
    /// </summary>
    public sealed class DraftableCardCatalog
    {
        // ScriptableObject.name in Unity: the asset's file name.
        public string name;

        private List<DraftableCardDefinition> cards = new List<DraftableCardDefinition>();

        // Granted free at the start of a run. The movement verbs: without them a gated vault
        // cannot program anything at all.
        private List<DraftableCardDefinition> openingHand = new List<DraftableCardDefinition>();

        public IReadOnlyList<DraftableCardDefinition> Cards => cards;
        public IReadOnlyList<DraftableCardDefinition> OpeningHand => openingHand;

        /// <summary>Rebuilt wholesale -- "always re-render from data", so a card removed from the
        /// deck actually leaves it.</summary>
        public void SetContents(
            List<DraftableCardDefinition> deck, List<DraftableCardDefinition> hand)
        {
            cards = deck ?? new List<DraftableCardDefinition>();
            openingHand = hand ?? new List<DraftableCardDefinition>();
        }
    }
}
