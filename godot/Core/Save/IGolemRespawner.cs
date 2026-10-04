using GolemFactory.Compat;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Save
{
    /// <summary>
    /// How <see cref="SaveLoadService"/> asks the scene to rebuild a golem that the save
    /// describes but the world no longer contains.
    ///
    /// <para>
    /// THIS INTERFACE EXISTS TO KEEP THE SERVICE PURE. Restoring a golem is arithmetic on data;
    /// *creating* one is `Instantiate` plus half a dozen scene references (the conveyor system,
    /// two registries, the clock, the spatial endpoint registry, the steam network, the extractor
    /// cap) that only a live scene has. Putting that behind one method is what lets
    /// <c>SaveLoadService</c> stay a plain static that an EditMode test can drive with a fake --
    /// the same split <c>GridCoordinateConverter</c>, <c>PlayerMovement.ComputeDisplacement</c>
    /// and <c>YSortUtility</c> already use for engine-free math.
    /// </para>
    ///
    /// <para>
    /// It is deliberately NOT handed a <see cref="GolemEntry"/>. A respawner's job is to produce
    /// a correctly wired, correctly placed, bare-chassis golem; loading the *program* into it is
    /// the service's job, and it is the identical code path a golem that was already alive goes
    /// through. Handing the entry over would invite a second, divergent restore path -- which is
    /// precisely the bug class this feature is fixing.
    /// </para>
    /// </summary>
    public interface IGolemRespawner
    {
        /// <summary>
        /// Builds a bare-chassis golem with the given id, standing on <paramref name="cell"/>
        /// facing <paramref name="facing"/>, wired to the scene exactly as a player-built golem
        /// is. Returns false if it cannot -- a missing prefab, an unresolvable chassis -- in
        /// which case the caller skips that entry as it always did rather than failing the load.
        /// </summary>
        bool TryRespawn(
            string golemId, ChassisDefinition chassis, Vector2Int cell, Facing facing,
            out GolemEntity golem);
    }
}
