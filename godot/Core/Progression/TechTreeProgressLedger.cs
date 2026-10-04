using System.Collections.Generic;

namespace GolemFactory.Progression
{
    /// <summary>
    /// What the player has demonstrably done, accumulated over a session and fed to
    /// <see cref="TechTreeStatusRules"/>. Engine-free so the rules can be tested without a scene.
    ///
    /// <para>
    /// <b>It only ever grows.</b> Deleting the last Brass Presser does not un-invent the Brass
    /// Presser, and a stockpile that runs dry does not un-discover Iron Plate. Monotonicity is
    /// also what keeps the chart still: read live from buffer contents alone, half the track would
    /// flicker between Researched and Available every time a golem emptied a depot.
    /// </para>
    /// </summary>
    public sealed class TechTreeProgressLedger
    {
        private readonly HashSet<string> _items = new HashSet<string>();
        private readonly HashSet<string> _chassis = new HashSet<string>();
        private readonly HashSet<string> _buildings = new HashSet<string>();
        private readonly HashSet<string> _claimedCards = new HashSet<string>();

        /// <summary>
        /// Bumped whenever anything is recorded for the first time. The panel re-tints on a
        /// version change rather than every frame -- rebuilding forty-odd node views per frame is
        /// what <see cref="TechTreeStatusRules"/> being cheap is for, not an excuse to do it.
        /// </summary>
        public int Version { get; private set; }

        public int CompletedTowerStages { get; private set; }

        public bool RecordItem(string itemType) => Record(_items, itemType);

        public bool RecordChassis(string chassisName) => Record(_chassis, chassisName);

        public bool RecordBuilding(string buildingId) => Record(_buildings, buildingId);

        /// <summary>
        /// §8: an Assembly Line card the player has claimed. Recorded by name, like a chassis
        /// and for the same reason -- the asset's filename is its identity across a save.
        /// </summary>
        public bool RecordClaimedCard(string cardName) => Record(_claimedCards, cardName);

        /// <summary>Highest completed stage wins; a reload that reports fewer stages is ignored.</summary>
        public bool RecordCompletedTowerStages(int completed)
        {
            if (completed <= CompletedTowerStages)
            {
                return false;
            }

            CompletedTowerStages = completed;
            Version++;
            return true;
        }

        public bool HasItem(string itemType) => _items.Contains(itemType ?? string.Empty);
        public bool HasChassis(string chassisName) => _chassis.Contains(chassisName ?? string.Empty);
        public bool HasBuilding(string buildingId) => _buildings.Contains(buildingId ?? string.Empty);
        public bool HasClaimedCard(string cardName) => _claimedCards.Contains(cardName ?? string.Empty);

        /// <summary>
        /// Whether this node's own unlock signal has been observed, ignoring prerequisites.
        /// A <see cref="TechTreeUnlockSignal.None"/> node has nothing to observe and answers
        /// <c>false</c> here -- the rules resolve it from its prerequisites instead.
        /// </summary>
        public bool SignalObserved(TechTreeNode node)
        {
            if (node == null)
            {
                return false;
            }

            switch (node.Signal)
            {
                case TechTreeUnlockSignal.Item:
                    return HasItem(node.SignalId);
                case TechTreeUnlockSignal.Chassis:
                    return HasChassis(node.SignalId);
                case TechTreeUnlockSignal.Building:
                    return HasBuilding(node.SignalId);
                case TechTreeUnlockSignal.TowerStage:
                    return int.TryParse(node.SignalId, out int stage) && CompletedTowerStages >= stage;
                case TechTreeUnlockSignal.Card:
                    return HasClaimedCard(node.SignalId);
                default:
                    return false;
            }
        }

        public void Clear()
        {
            _items.Clear();
            _chassis.Clear();
            _buildings.Clear();
            _claimedCards.Clear();
            CompletedTowerStages = 0;
            Version++;
        }

        private bool Record(HashSet<string> set, string id)
        {
            if (string.IsNullOrEmpty(id) || !set.Add(id))
            {
                return false;
            }

            Version++;
            return true;
        }
    }
}
