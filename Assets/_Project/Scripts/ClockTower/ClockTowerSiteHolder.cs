using System.Collections.Generic;
using UnityEngine;
using GolemFactory.Events;
using GolemFactory.Simulation;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// Thin scene wrapper owning one <see cref="ClockTowerSite"/>, per the Holder pattern
    /// (<c>GridMapHolder</c>, <c>ConveyorSystemHolder</c>, <c>SteamNetworkHolder</c>,
    /// <c>SpatialEndpointRegistryHolder</c>...). All the arithmetic -- the windows, the meters,
    /// the multiplier -- lives in plain C# so it is unit-testable without a scene; this class
    /// exists only to give it a scene presence, to be registered with the clock, and to relay one
    /// event.
    ///
    /// <para>
    /// <c>ITickable</c>, because progress must accrue on simulation ticks rather than in
    /// <c>Update</c>: §7's rates are per unit of WORK DONE, so a factory run at 4x speed must
    /// reach a stage's nominal duration after the same number of ticks, not the same number of
    /// wall seconds. It also means a paused simulation pauses the tower, which is the only
    /// honest behaviour when every input to it is also paused.
    /// </para>
    ///
    /// <para>
    /// THE EVENT SUBSCRIPTION IS PLAY-MODE ONLY. There is no <c>[ExecuteAlways]</c> anywhere in
    /// this project, so <c>OnEnable</c>/<c>OnDisable</c> do not run in EditMode -- the same
    /// gotcha that forces <c>GolemEntity</c>'s Signal-trigger tests to be PlayMode. EditMode
    /// tests therefore drive <see cref="ClockTowerSite.RecordFreshProduction"/> directly, and a
    /// PlayMode test covers the wiring itself.
    /// </para>
    /// </summary>
    public sealed class ClockTowerSiteHolder : MonoBehaviour, ITickable
    {
        [SerializeField] private List<ClockTowerStageDefinition> stages =
            new List<ClockTowerStageDefinition>();

        private readonly ClockTowerSite _site = new ClockTowerSite();

        public ClockTowerSite Site => _site;

        /// <summary>
        /// Test/bootstrap-friendly setter, matching the <c>Configure(...)</c> idiom the project
        /// uses wherever Inspector wiring alone is not enough.
        /// </summary>
        public void Configure(IEnumerable<ClockTowerStageDefinition> stageDefinitions)
        {
            stages = stageDefinitions == null
                ? new List<ClockTowerStageDefinition>()
                : new List<ClockTowerStageDefinition>(stageDefinitions);

            _site.SetStages(stages);
        }

        private void Awake()
        {
            // Guarded, so a bootstrap that called Configure before Awake ran is not silently
            // reset back to whatever the Inspector happened to hold -- SetStages resets progress,
            // and a tower that forgot its stage on scene load would be the worst possible bug in
            // a forty-minute phase.
            if (_site.StageCount == 0)
            {
                _site.SetStages(stages);
            }
        }

        private void OnEnable() => EventBus.ItemAssembled += OnItemAssembled;

        private void OnDisable() => EventBus.ItemAssembled -= OnItemAssembled;

        // The fresh-production half of §7's multiplier. Every Assemble in the factory reports
        // here; the site keeps only the item types some stage actually demands.
        private void OnItemAssembled(ItemAssembledEvent e) =>
            _site.RecordFreshProduction(e.ItemType, e.Quantity, e.Tick);

        public void Tick(long tick) => _site.Tick(tick);
    }
}
