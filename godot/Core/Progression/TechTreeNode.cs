using System;
using System.Collections.Generic;

namespace GolemFactory.Progression
{
    /// <summary>
    /// What a tech-tree node <em>is</em>, which drives its badge and nothing else. Kind is
    /// presentation; <see cref="TechTreeNode.Signal"/> is what decides whether it is researched.
    /// </summary>
    public enum TechTreeNodeKind
    {
        /// <summary>A chassis from docs/progression-design.md §6. The keystone of its phase.</summary>
        Chassis,

        /// <summary>One of the 19 authored <c>RecipeDefinition</c> assets (§5.2).</summary>
        Recipe,

        /// <summary>A placeable: Boiler, Steam Pipe, Belt, Depot, the Clock Tower.</summary>
        Building,

        /// <summary>
        /// A way of playing rather than a thing to build -- carrier golems, the steam grid, the
        /// two-extractor cap. Has no unlock signal of its own; it opens when its prerequisites do.
        /// </summary>
        Technique,

        /// <summary>A Clock Tower stage, or the win. Signalled by tower progress.</summary>
        Milestone
    }

    /// <summary>
    /// The observable fact that makes a node <see cref="TechTreeNodeState.Researched"/>.
    /// <para>
    /// Deliberately an <em>observation of the world</em> rather than a claim ledger: §8's
    /// Assembly-Line gating ("Assembly Line as tech tree", §11 item 12) is spec-only, so a chart
    /// keyed to claimed cards would read empty in the game as it actually ships today. Keyed to
    /// production instead, the track lights up as the player's factory genuinely reaches each
    /// good -- which is the same ordering §8 will later enforce, arrived at from the other end.
    /// </para>
    /// </summary>
    public enum TechTreeUnlockSignal
    {
        /// <summary>Nothing to observe -- resolves from prerequisites alone (see <see cref="TechTreeNodeKind.Technique"/>).</summary>
        None,

        /// <summary>An <c>ItemType</c> id the player has produced or holds.</summary>
        Item,

        /// <summary>A <c>ChassisDefinition</c> asset name carried by some golem in the world.</summary>
        Chassis,

        /// <summary>A building id that has been placed (or stands in the scene from the start).</summary>
        Building,

        /// <summary>A Clock Tower stage number (1-4) that has been completed.</summary>
        TowerStage,

        /// <summary>
        /// An Assembly Line card the player has CLAIMED. The signal §8's gating adds, and the
        /// reason the chart was built to read the world rather than a claim ledger: with gating
        /// live, a claim is the moment a technique genuinely becomes available, and the chart
        /// now reads the claim ledger AS WELL AS the world rather than instead of it.
        ///
        /// <para>
        /// APPENDED, like every other signal: nodes are authored against these by name in
        /// TechTreeCatalog, and inserting anywhere but the end would re-point them.
        /// </para>
        /// </summary>
        Card
    }

    /// <summary>Where a node stands for the player right now.</summary>
    public enum TechTreeNodeState
    {
        /// <summary>At least one prerequisite is not researched. Drawn shut.</summary>
        Locked,

        /// <summary>Every prerequisite is researched; this is a legal next step.</summary>
        Available,

        /// <summary>Its unlock signal has been observed.</summary>
        Researched
    }

    /// <summary>
    /// One entry on the research track. Immutable, engine-free and identified by a bare string id
    /// -- the same convention belts, buffers and nodes use, and for the same reason: these ids are
    /// referenced from prerequisite lists and from tests, so a rename is a data change.
    /// </summary>
    public sealed class TechTreeNode
    {
        private static readonly string[] NoPrerequisites = Array.Empty<string>();

        public string Id { get; }
        public string DisplayName { get; }

        /// <summary>One line of what this unlocks, shown under the name. Recipe nodes carry their ratio.</summary>
        public string Detail { get; }

        public TechTreeNodeKind Kind { get; }
        public TechTreeUnlockSignal Signal { get; }

        /// <summary>The item type / chassis asset name / building id / stage number the signal names.</summary>
        public string SignalId { get; }

        /// <summary>Column: index into <see cref="TechTreeCatalog.Phases"/>.</summary>
        public int PhaseIndex { get; }

        /// <summary>Row within the column, authored rather than derived so the chart reads top-to-bottom.</summary>
        public int Row { get; }

        public IReadOnlyList<string> Prerequisites { get; }

        /// <summary>
        /// True for the one node that defines its phase -- the chassis (or, in phase VI, the tower).
        /// Drawn larger. Purely visual weighting.
        /// </summary>
        public bool IsKeystone { get; }

        /// <summary>
        /// True when the node describes something docs/progression-design.md specifies but the
        /// build does not yet implement (docs/open-items.md §1.5-§1.6 and §11). A planned node can
        /// reach <see cref="TechTreeNodeState.Available"/> and never <c>Researched</c>, and is
        /// drawn as a drafting sketch. Without this the chart would claim the player had unlocked
        /// <c>Repeat</c> or the Freight Link, neither of which exists to unlock.
        /// </summary>
        public bool IsPlanned { get; }

        public TechTreeNode(
            string id,
            string displayName,
            string detail,
            TechTreeNodeKind kind,
            int phaseIndex,
            int row,
            TechTreeUnlockSignal signal,
            string signalId,
            string[] prerequisites,
            bool isKeystone = false,
            bool isPlanned = false)
        {
            Id = id;
            DisplayName = displayName;
            Detail = detail;
            Kind = kind;
            PhaseIndex = phaseIndex;
            Row = row;
            Signal = signal;
            SignalId = signalId;
            Prerequisites = prerequisites ?? NoPrerequisites;
            IsKeystone = isKeystone;
            IsPlanned = isPlanned;
        }

        public override string ToString() => Id;
    }

    /// <summary>A column of the chart: one of §9's six phases.</summary>
    public sealed class TechTreePhase
    {
        /// <summary>Roman numeral plus name, e.g. "IV · The Metal Lines".</summary>
        public string Title { get; }

        /// <summary>§9's one-line goal for the phase.</summary>
        public string Goal { get; }

        public TechTreePhase(string title, string goal)
        {
            Title = title;
            Goal = goal;
        }
    }
}
