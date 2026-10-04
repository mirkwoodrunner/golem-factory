using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Save
{
    /// <summary>
    /// Rebuilds saved golems through a <see cref="GolemConstructionStation"/> -- the one object
    /// in the scene that already holds every reference a golem needs to work.
    ///
    /// <para>
    /// THE STATION IS THE RESPAWNER BECAUSE IT IS ALREADY THE SPAWNER. A golem needs the conveyor
    /// system, the node and buffer registries, the clock, and optionally the spatial endpoint
    /// registry, the steam network and the extractor cap -- seven references, wired per scene,
    /// which the station is handed at bootstrap. Duplicating that wiring onto a save-specific
    /// component would create a second definition of "a working golem", and the two would drift
    /// the first time a new subsystem was added to one of them.
    /// </para>
    ///
    /// <para>
    /// Which station, when a factory has several, does not matter: they are interchangeable as
    /// wiring, and the golem's own saved cell decides where it stands. It is not respawned at
    /// the station or facing the way the station faces.
    /// </para>
    /// </summary>
    public sealed class StationGolemRespawner : IGolemRespawner
    {
        private readonly GolemConstructionStation _station;

        public StationGolemRespawner(GolemConstructionStation station)
        {
            _station = station;
        }


        public bool TryRespawn(
            string golemId, ChassisDefinition chassis, Vector2Int cell, Facing facing,
            out GolemEntity golem)
        {
            golem = null;
            return _station != null &&
                   _station.TryRespawnGolem(golemId, chassis, cell, facing, out golem);
        }
    }
}
