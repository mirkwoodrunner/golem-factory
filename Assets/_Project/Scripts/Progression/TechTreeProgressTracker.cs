using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.World;
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

        // Optional, and found by type when unwired -- Floor Expansion has no component to count,
        // so the ledger has to read the room's shape instead. Left null in a test scene, the
        // sweep simply never fires, which is the safe direction to be wrong in.
        [SerializeField] private FloorBoundsHolder floorBoundsHolder;

        public TechTreeProgressLedger Ledger { get; } = new TechTreeProgressLedger();

        /// <summary>
        /// Wires the floor bounds this tracker reads Floor Expansion from. Separate from
        /// <c>Configure</c> for the reason every other Configure* in this project is separate:
        /// existing call sites keep working by simply never calling it.
        /// </summary>
        public void ConfigureFloorBounds(FloorBoundsHolder bounds) => floorBoundsHolder = bounds;

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
            int before = Ledger.Version;

            SweepBuffers();
            SweepChassis();
            SweepBuildings();
            SweepClockTower();
            SweepClaimedCards();

            // §8.3's other half. The Assembly Line only re-checks its waiting list when
            // something is CLAIMED, so with the ledger as its unlock context a card whose
            // prerequisite the player has just satisfied would sit in the waiting list until
            // some unrelated claim happened to shake it loose -- "I made the Iron Plate, why is
            // the card still not offered". This tracker is already the thing that notices the
            // world changed, so it is the thing that tells the line.
            //
            // Gated on the ledger's version rather than run every poll: promotion walks the
            // whole waiting list, and nothing can newly unlock in a tick where nothing was
            // recorded.
            if (Ledger.Version != before && assemblyLineHolder != null && assemblyLineHolder.State != null)
            {
                assemblyLineHolder.State.PromoteUnlockedCards();
            }
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

            // --- THREE SIGNALS THE CATALOG NAMED AND NOTHING EVER RECORDED --------------------
            // bldg.slagheap, bldg.freightmast and bldg.floorexpansion have been on the chart
            // since their features shipped, and could never light: this sweep listed six
            // component types and the catalog named nine. §3z says "nothing on it should read as
            // planned any more -- every node is a shipped feature", which was true of the
            // catalog and quietly false of the readout. A player who built a Slag Heap was told
            // by the Ledger that they had not.
            //
            // BuildingSignalCoverageTests now stands one of every placeable in a scene and
            // asserts every Building-signalled node reaches Researched, so a tenth building
            // cannot repeat this.
            if (Exists<PlaceableSlagHeap>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingSlagHeap);
            }
            if (Exists<PlaceableFreightMast>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingFreightMast);
            }
            if (Exists<PlaceableScrapRecycler>())
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingScrapRecycler);
            }

            SweepFloorExpansion();
        }

        /// <summary>
        /// Floor Expansion is the one "building" with no component to count, because what the
        /// player buys is rows of floor rather than an object. The observable fact is that the
        /// room has grown past the shape it started in -- which <see cref="FloorBounds"/> already
        /// tracks, and which cannot be reached any other way.
        /// </summary>
        private void SweepFloorExpansion()
        {
            FloorBoundsHolder holder = floorBoundsHolder != null
                ? floorBoundsHolder
                : FindAnyObjectByType<FloorBoundsHolder>();

            FloorBounds bounds = holder != null ? holder.Bounds : null;
            if (bounds != null && bounds.NorthExtent > bounds.HalfExtent)
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingFloorExpansion);
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
