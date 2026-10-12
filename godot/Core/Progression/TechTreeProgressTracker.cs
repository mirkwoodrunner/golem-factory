using System;
using System.Collections.Generic;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Progression
{
    /// <summary>
    /// Watches the factory and records into the <see cref="TechTreeProgressLedger"/> what the
    /// player has produced, built and completed -- which is what lights the Artificer's Ledger.
    /// The Ledger is a READOUT, never a gate: nothing here withholds anything.
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (G2d). The sweeps are unchanged.
    /// FindObjectsByType / FindAnyObjectByType became providers the scene hands over
    /// (<see cref="ConfigureWorld"/>): a building kind "exists" when any building has that part.
    /// OnEnable/OnDisable became <see cref="Attach"/>/<see cref="Detach"/>; Update's unscaled
    /// poll timer is <see cref="Update"/>.
    /// </para>
    ///
    /// <para>
    /// <b>The ledger only ever grows</b>, and <see cref="Poll"/> calls
    /// <c>AssemblyLineState.PromoteUnlockedCards</c> when its version changes, because the line
    /// otherwise only re-checks its waiting list on a claim (root CLAUDE.md: the Assembly Line's
    /// unlock context is monotone).
    /// </para>
    /// </summary>
    public sealed class TechTreeProgressTracker
    {
        private StorageBufferRegistry bufferRegistry;
        private ClockTowerSite clockTowerSite;
        private AssemblyLineState assemblyLine;
        private string claimUserId = "LocalPlayer";
        private float pollSeconds = 1f;
        private float _nextPollTime;
        private FloorBounds floorBounds;
        private Func<IEnumerable<GolemEntity>> _golems;
        private Func<IEnumerable<PlaceableBuilding>> _buildings;
        private bool _attached;

        public TechTreeProgressLedger Ledger { get; } = new TechTreeProgressLedger();

        public void ConfigureFloorBounds(FloorBounds bounds) => floorBounds = bounds;

        public void Configure(StorageBufferRegistry buffers, ClockTowerSite clockTower)
        {
            bufferRegistry = buffers;
            clockTowerSite = clockTower;
        }

        public void ConfigureCardClaims(AssemblyLineState line, string userId)
        {
            assemblyLine = line;
            if (!string.IsNullOrEmpty(userId))
            {
                claimUserId = userId;
            }
        }

        /// <summary>What exists to sweep -- Unity's FindObjectsByType.</summary>
        public void ConfigureWorld(Func<IEnumerable<GolemEntity>> golems, Func<IEnumerable<PlaceableBuilding>> buildings)
        {
            _golems = golems;
            _buildings = buildings;
        }

        /// <summary>Starts listening for production and stage completions -- Unity's OnEnable.</summary>
        public void Attach()
        {
            if (!_attached)
            {
                EventBus.ItemAssembled += OnItemAssembled;
                EventBus.ClockTowerStageCompleted += OnStageCompleted;
                _attached = true;
            }
            _nextPollTime = 0f;
        }

        /// <summary>Stops listening -- Unity's OnDisable. The bus is static.</summary>
        public void Detach()
        {
            if (_attached)
            {
                EventBus.ItemAssembled -= OnItemAssembled;
                EventBus.ClockTowerStageCompleted -= OnStageCompleted;
                _attached = false;
            }
        }

        /// <summary>Unity's Update: polls once per <c>pollSeconds</c> of unscaled time.</summary>
        public void Update(float unscaledTime)
        {
            if (unscaledTime < _nextPollTime)
            {
                return;
            }

            _nextPollTime = unscaledTime + Mathf.Max(0.1f, pollSeconds);
            Poll();
        }

        public void Poll()
        {
            int before = Ledger.Version;
            SweepBuffers();
            SweepChassis();
            SweepBuildings();
            SweepClockTower();
            SweepClaimedCards();

            if (Ledger.Version != before && assemblyLine != null)
            {
                assemblyLine.PromoteUnlockedCards();
            }
        }

        private void SweepClaimedCards()
        {
            if (assemblyLine == null)
            {
                return;
            }

            IReadOnlyList<DraftableCardDefinition> claimed = assemblyLine.GetClaimedCards(claimUserId);
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
            if (bufferRegistry == null)
            {
                return;
            }

            foreach (var pair in bufferRegistry.Buffers)
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
            if (_golems == null)
            {
                return;
            }

            foreach (GolemEntity golem in _golems())
            {
                GolemProgram program = golem != null && !golem.IsRemoved ? golem.Program : null;
                if (program != null && program.chassis != null)
                {
                    Ledger.RecordChassis(program.chassis.name);
                }
            }
        }

        private void SweepBuildings()
        {
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
            // The town square's fixture stands from the first frame, so standing is not building
            // it: it counts once a delivery has started stage 1. (It counted at t=0, lighting the
            // endgame keystone on a fresh game -- from review.) A placed tower counts as placed.
            if (Exists<PlaceableClockTower>(b => !b.IsFixture || b.GetPart<PlaceableClockTower>().Site?.HasStarted == true))
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingClockTower);
            }
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

        // A room grown past its starting north wall is the Floor Expansion node, built.
        private void SweepFloorExpansion()
        {
            if (floorBounds != null && floorBounds.NorthExtent > floorBounds.HalfExtent)
            {
                Ledger.RecordBuilding(TechTreeCatalog.BuildingFloorExpansion);
            }
        }

        private void SweepClockTower()
        {
            if (clockTowerSite == null)
            {
                return;
            }

            int completed = clockTowerSite.IsComplete
                ? clockTowerSite.StageCount
                : Mathf.Max(0, clockTowerSite.StageIndex);
            Ledger.RecordCompletedTowerStages(completed);
        }

        private bool Exists<T>(Func<PlaceableBuilding, bool> counts = null) where T : class
        {
            if (_buildings == null)
            {
                return false;
            }

            foreach (PlaceableBuilding building in _buildings())
            {
                if (building != null && !building.IsRemoved && building.GetPart<T>() != null
                    && (counts == null || counts(building)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
