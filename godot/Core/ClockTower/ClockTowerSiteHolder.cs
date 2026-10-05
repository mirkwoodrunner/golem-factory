using System.Collections.Generic;
using GolemFactory.Events;
using GolemFactory.Simulation;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// Owns the scene's <see cref="ClockTowerSite"/> and does its two scene-side jobs: ticks it,
    /// and feeds it fresh production from <see cref="EventBus.ItemAssembled"/> -- which is what
    /// the tower's supply readout measures against.
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (G2d). Kept as a class rather than
    /// folded into whoever owns the site because the event wiring is real behaviour with its
    /// own tests. OnEnable/OnDisable became <see cref="Attach"/>/<see cref="Detach"/>; Awake's
    /// "seed the stages if nobody configured the site" happens in <see cref="Attach"/>.
    /// </para>
    /// </summary>
    public sealed class ClockTowerSiteHolder : ITickable
    {
        private List<ClockTowerStageDefinition> stages = new List<ClockTowerStageDefinition>();
        private readonly ClockTowerSite _site = new ClockTowerSite();
        private bool _attached;

        public ClockTowerSite Site => _site;

        public void Configure(IEnumerable<ClockTowerStageDefinition> stageDefinitions)
        {
            stages = stageDefinitions == null
                ? new List<ClockTowerStageDefinition>()
                : new List<ClockTowerStageDefinition>(stageDefinitions);
            _site.SetStages(stages);
        }

        /// <summary>Starts listening for fresh production -- Unity's Awake + OnEnable.</summary>
        public void Attach()
        {
            if (_site.StageCount == 0)
            {
                _site.SetStages(stages);
            }

            if (!_attached)
            {
                EventBus.ItemAssembled += OnItemAssembled;
                _attached = true;
            }
        }

        /// <summary>Stops listening -- Unity's OnDisable. The bus is static.</summary>
        public void Detach()
        {
            if (_attached)
            {
                EventBus.ItemAssembled -= OnItemAssembled;
                _attached = false;
            }
        }

        private void OnItemAssembled(ItemAssembledEvent e) =>
            _site.RecordFreshProduction(e.ItemType, e.Quantity, e.Tick);

        public void Tick(long tick) => _site.Tick(tick);
    }
}
