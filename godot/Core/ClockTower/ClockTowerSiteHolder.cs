using System.Collections.Generic;
using GolemFactory.Simulation;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// Owns the scene's <see cref="ClockTowerSite"/> and ticks it.
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (G2d). It used to feed the site fresh
    /// production from <c>EventBus.ItemAssembled</c>; that rule went (docs/contracts-design.md
    /// §11), so it listens to nothing. Awake's "seed the stages if nobody configured the site"
    /// happens in <see cref="Attach"/>.
    /// </para>
    /// </summary>
    public sealed class ClockTowerSiteHolder : ITickable
    {
        private List<ClockTowerStageDefinition> stages = new List<ClockTowerStageDefinition>();
        private readonly ClockTowerSite _site = new ClockTowerSite();

        public ClockTowerSite Site => _site;

        public void Configure(IEnumerable<ClockTowerStageDefinition> stageDefinitions)
        {
            stages = stageDefinitions == null
                ? new List<ClockTowerStageDefinition>()
                : new List<ClockTowerStageDefinition>(stageDefinitions);
            _site.SetStages(stages);
        }

        /// <summary>Seeds the stages if nobody configured the site -- Unity's Awake.</summary>
        public void Attach()
        {
            if (_site.StageCount == 0)
            {
                _site.SetStages(stages);
            }
        }

        /// <summary>Nothing to release: the holder listens to no event.</summary>
        public void Detach()
        {
        }

        public void Tick(long tick) => _site.Tick(tick);
    }
}
