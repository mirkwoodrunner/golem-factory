using UnityEngine;
using GolemFactory.ClockTower;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // The Clock Tower (docs/progression-design.md §7). Sibling component alongside
    // PlaceableBuilding -- which is sealed, so every distinct building's behaviour is a sibling
    // rather than a subclass -- exactly the arrangement PlaceableDepot, PlaceableBelt and
    // PlaceableBoiler use.
    //
    // The scene component owns none of the mechanic: it publishes an input endpoint onto the cell
    // it occupies and withdraws it on removal, and everything after that (the windows, the
    // meters, the multiplier, the stages) is plain C# in ClockTower/. Same division of labour as
    // PlaceableDepot, which holds no items either.
    //
    // NO BUILD COST. §7 places the tower "in the world at the start of Phase 6" and prices it at
    // nothing; §6 costs chassis and §3.1 costs the Boiler, but the megaproject itself is the
    // goal, not a purchase. An invented cost here would be a tuning number no reviewer has seen,
    // and PlaceableBuilding.Cost is empty by default, so a Clock Tower prefab is free to place
    // unless somebody deliberately authors one.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableClockTower : MonoBehaviour
    {
        [SerializeField] private ClockTowerSiteHolder siteHolder;
        [SerializeField] private string displayName = "Clock Tower";

        /// <summary>
        /// §7's stages, carried by the TOWER rather than by the always-present
        /// <see cref="ClockTowerSiteHolder"/>, and handed over when the tower is placed.
        ///
        /// <para>
        /// THE STAGES MUST NOT BE LIVE BEFORE THE TOWER IS BUILT. Wired onto the holder they
        /// start stage 1 in <c>Awake</c>, so a fresh Sandbox opened with the HUD readout showing
        /// "Stage 1 Foundation - 0%" and an alert reading "Clock Tower starved of FrameSection -
        /// progress frozen" from the first frame -- for a megaproject that does not exist and
        /// which §7 does not place until Phase 6. A site with no stages reports
        /// <c>Dormant()</c>, which is the honest reading and already has its own formatting
        /// ("Clock Tower dormant") and its own idle colour.
        /// </para>
        /// </summary>
        [SerializeField] private System.Collections.Generic.List<ClockTowerStageDefinition> stages =
            new System.Collections.Generic.List<ClockTowerStageDefinition>();

        /// <summary>The endpoint this component published, or null before/after registration.</summary>
        public ClockTowerInputEndpoint Endpoint { get; private set; }

        public ClockTowerSiteHolder SiteHolder => siteHolder;

        /// <summary>Test/bootstrap-friendly setter, matching the Configure(...) idiom.</summary>
        public void Configure(ClockTowerSiteHolder holder) => siteHolder = holder;

        /// <summary>
        /// Registers the tower's input tile on <paramref name="cell"/>. Called by
        /// BuildModeController after placement (and by a bootstrap for a tower authored directly
        /// into the scene), for the same reason belts, depots and boilers are registered there:
        /// one place owns "placed in the world", so a building can never exist visually but not
        /// logically.
        ///
        /// <para>
        /// ONE CELL, WHICH IS THE INPUT TILE. §7 gives the tower "an input buffer tile golems
        /// Push into like any other" and says nothing about the structure's footprint, and
        /// SpatialEndpointRegistry allows exactly one endpoint per cell by design -- a multi-cell
        /// building would immediately raise "which tile does the golem push into?", which is the
        /// ambiguity facing-based routing exists to remove. A taller sprite is presentation.
        /// </para>
        /// </summary>
        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistryHolder endpointHolder,
            ClockTowerSiteHolder site,
            Vector2Int cell)
        {
            if (endpointHolder == null)
            {
                return false;
            }

            if (site != null)
            {
                siteHolder = site;
            }

            if (siteHolder == null)
            {
                return false;
            }

            // Building the tower is what starts the megaproject. Guarded so a SECOND tower --
            // or a reload that re-registers this one -- cannot reset a stage already in
            // progress: SetStages resets progress, and losing a forty-minute stage to a
            // re-registration would be the worst bug in the endgame.
            if (siteHolder.Site.StageCount == 0 && stages != null && stages.Count > 0)
            {
                siteHolder.Configure(stages);
            }

            Endpoint = new ClockTowerInputEndpoint(siteHolder.Site, displayName);
            endpointHolder.Registry.Register(cell, Endpoint);
            return true;
        }

        /// <summary>
        /// Withdraws the tower's input tile. Golems facing it stall NoTargetAtTile on their next
        /// push, which is the honest outcome of demolishing the thing they were delivering to.
        /// </summary>
        public bool UnregisterFromSpatialEndpoints(
            SpatialEndpointRegistryHolder endpointHolder, Vector2Int cell)
        {
            if (endpointHolder == null)
            {
                return false;
            }

            endpointHolder.Registry.Unregister(cell);
            Endpoint = null;
            return true;
        }
    }
}
