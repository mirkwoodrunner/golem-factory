using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;

namespace GolemFactory.Progression
{
    /// <summary>
    /// Watches the live world and records what the player has achieved into a
    /// <see cref="TechTreeProgressLedger"/>. The Holder pattern's shape -- a thin
    /// <c>MonoBehaviour</c> owning one plain-C# object and exposing it -- with the scene-reading
    /// that only Unity can do kept here and every rule kept in the plain class.
    ///
    /// <para>
    /// <b>Two sources, because neither alone is honest.</b> <c>ItemAssembledEvent</c> catches a
    /// good the moment a golem makes it, including goods that go straight into another golem and
    /// never touch a buffer. The buffer sweep catches everything else: hand-cranked output, the
    /// opening stock, and anything made in a session before this component existed (a loaded save
    /// restores buffers, not events). Together they answer "has this factory ever produced X".
    /// </para>
    ///
    /// <para>
    /// <b>Polling, not eventing, for chassis and buildings.</b> There is no "golem built" or
    /// "building placed" event on the bus, and inventing two would mean touching
    /// <c>GolemConstructionStation</c> and <c>BuildModeController</c> to serve a read-only chart.
    /// A scene sweep every <see cref="pollSeconds"/> costs a <c>FindObjectsByType</c> pair at a
    /// tenth of a second's resolution on a screen the player opens deliberately.
    /// </para>
    /// </summary>
    public sealed class TechTreeProgressTracker : MonoBehaviour
    {
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private ClockTowerSiteHolder clockTowerHolder;

        // §8's claim ledger. Optional, like everything else this sweeps: a scene with no
        // Assembly Line simply records no claimed cards, and every Card-signalled node stays
        // unresearched rather than the chart failing to draw.
        [SerializeField] private GolemFactory.AssemblyLine.AssemblyLineStateHolder assemblyLineHolder;
        [SerializeField] private string claimUserId = "LocalPlayer";

        /// <summary>
        /// How often the scene is swept. Not every frame: the sweep is two
        /// <c>FindObjectsByType</c> calls, and nothing it looks for can change faster than the
        /// player can build.
        /// </summary>
        [SerializeField] private float pollSeconds = 1f;

        private float _nextPollTime;

        public TechTreeProgressLedger Ledger { get; } = new TechTreeProgressLedger();

        public void Configure(StorageBufferRegistryHolder buffers, ClockTowerSiteHolder clockTower)
        {
            bufferRegistryHolder = buffers;
            clockTowerHolder = clockTower;
        }

        /// <summary>
        /// Wires §8's claim ledger. Split out for the same reason every other Configure* here
        /// is: a scene that never calls it behaves exactly as it did, reading the world only.
        /// </summary>
        public void ConfigureCardClaims(
            GolemFactory.AssemblyLine.AssemblyLineStateHolder line, string userId)
        {
            assemblyLineHolder = line;
            if (!string.IsNullOrEmpty(userId))
            {
                claimUserId = userId;
            }
        }

        private void OnEnable()
        {
            EventBus.ItemAssembled += OnItemAssembled;
            EventBus.ClockTowerStageCompleted += OnStageCompleted;
            // Sweep on the next Update rather than here: Awake order does not guarantee the
            // holders have been wired by SandboxBootstrap yet.
            _nextPollTime = 0f;
        }

        private void OnDisable()
        {
            EventBus.ItemAssembled -= OnItemAssembled;
            EventBus.ClockTowerStageCompleted -= OnStageCompleted;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPollTime)
            {
                return;
            }

            _nextPollTime = Time.unscaledTime + Mathf.Max(0.1f, pollSeconds);
            Poll();
        }

        /// <summary>Sweeps every source once. Public so a test or a panel can force a read.</summary>
        public void Poll()
        {
            SweepBuffers();
            SweepChassis();
            SweepBuildings();
            SweepClockTower();
            SweepClaimedCards();
        }

        // Swept rather than event-driven, exactly as the buffers are, and for the same reason
        // recorded there: a load restores claimed cards without replaying the claims, so a
        // ledger fed only by events would forget everything the player had unlocked.
        private void SweepClaimedCards()
        {
            if (assemblyLineHolder == null || assemblyLineHolder.State == null)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<GolemFactory.AssemblyLine.DraftableCardDefinition>
                claimed = assemblyLineHolder.State.GetClaimedCards(claimUserId);
            for (int i = 0; i < claimed.Count; i++)
            {
                if (claimed[i] != null)
                {
                    Ledger.RecordClaimedCard(claimed[i].name);
                }
            }
        }

        private void OnItemAssembled(ItemAssembledEvent e) => Ledger.RecordItem(e.ItemType);

        private void OnStageCompleted(ClockTowerStageCompletedEvent e) =>
            Ledger.RecordCompletedTowerStages(e.StageNumber);

        private void SweepBuffers()
        {
            if (bufferRegistryHolder == null || bufferRegistryHolder.Registry == null)
            {
                return;
            }

            foreach (var pair in bufferRegistryHolder.Registry.Buffers)
            {
                foreach (var quantity in pair.Value.Quantities)
                {
                    if (quantity.Value > 0)
                    {
                        Ledger.RecordItem(quantity.Key);
                    }
                }
            }
        }

        private void SweepChassis()
        {
            GolemEntity[] golems = FindObjectsByType<GolemEntity>(FindObjectsInactive.Exclude);
            for (int i = 0; i < golems.Length; i++)
            {
                GolemProgram program = golems[i].Program;
                if (program != null && program.chassis != null)
                {
                    // The asset name, which is what TechTreeCatalog's chassis ids are: a
                    // ChassisDefinition has no display-name field, so the .asset filename is the
                    // only stable identity it carries.
                    Ledger.RecordChassis(program.chassis.name);
                }
            }
        }

        private void SweepBuildings()
        {
            // HandCrankBench and the belts/depots/boilers are separate component types rather
            // than one enum, so the mapping is spelled out. Anything not listed simply never
            // lights its node, which is the safe direction to be wrong in.
            if (Exists<HandCrankBench>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingHandCrankBench);
            }
            if (Exists<PlaceableBoiler>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingBoiler);
            }
            if (Exists<PlaceableSteamPipe>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingSteamPipe);
            }
            if (Exists<PlaceableBelt>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingBelt);
            }
            if (Exists<PlaceableDepot>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingDepot);
            }
            if (Exists<PlaceableClockTower>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingClockTower);
            }
        }

        private void SweepClockTower()
        {
            ClockTowerSite site = clockTowerHolder != null ? clockTowerHolder.Site : null;
            if (site == null)
            {
                return;
            }

            // StageIndex is the stage being worked, so the number COMPLETED is one less --
            // except once the tower is finished, when every stage is done.
            int completed = site.IsComplete ? site.StageCount : Mathf.Max(0, site.StageIndex);
            Ledger.RecordCompletedTowerStages(completed);
        }

        private static bool Exists<T>() where T : Component =>
            FindAnyObjectByType<T>(FindObjectsInactive.Exclude) != null;
    }
}
