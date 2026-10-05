using System.Collections.Generic;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// The Clock Tower: publishes its input tile, giving the megaproject somewhere for a golem
    /// to Push into. Without it a placed tower was inert -- the win condition could be built
    /// and then never delivered to. Ported from Unity's sibling component (G2b).
    ///
    /// <para>
    /// Unity held a <c>ClockTowerSiteHolder</c>; this holds the <see cref="ClockTowerSite"/> it
    /// owned. The holder's other two jobs -- ticking the site and feeding it fresh production
    /// from <c>ItemAssembled</c> -- belong to whoever owns the site in the scene.
    /// </para>
    /// </summary>
    public sealed class PlaceableClockTower : IBuildingPart
    {
        private ClockTowerSite site;
        private string displayName = "Clock Tower";

        // The stages a tower brings with it, for a site nobody configured yet.
        private List<ClockTowerStageDefinition> stages = new List<ClockTowerStageDefinition>();

        public ClockTowerInputEndpoint Endpoint { get; private set; }
        public ClockTowerSite Site => site;

        public void Configure(ClockTowerSite towerSite) => site = towerSite;

        public void ConfigureStages(IEnumerable<ClockTowerStageDefinition> stageDefinitions) =>
            stages = stageDefinitions == null
                ? new List<ClockTowerStageDefinition>()
                : new List<ClockTowerStageDefinition>(stageDefinitions);

        // The site is a SCENE reference a Unity prefab carried by Inspector link; a clone keeps
        // pointing at the same site, as an instantiated prefab did.
        public IBuildingPart CloneForInstance() =>
            new PlaceableClockTower { site = site, displayName = displayName, stages = stages };

        public bool RegisterAsSpatialEndpoint(
            SpatialEndpointRegistry endpoints, ClockTowerSite towerSite, Vector2Int cell)
        {
            if (endpoints == null)
            {
                return false;
            }

            if (towerSite != null)
            {
                site = towerSite;
            }

            if (site == null)
            {
                return false;
            }

            if (site.StageCount == 0 && stages != null && stages.Count > 0)
            {
                site.SetStages(stages);
            }

            Endpoint = new ClockTowerInputEndpoint(site, displayName);
            endpoints.Register(cell, Endpoint);
            return true;
        }

        public bool UnregisterFromSpatialEndpoints(SpatialEndpointRegistry endpoints, Vector2Int cell)
        {
            if (endpoints == null)
            {
                return false;
            }

            endpoints.Unregister(cell);
            Endpoint = null;
            return true;
        }
    }
}
