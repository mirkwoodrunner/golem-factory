using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.World;

namespace GolemFactory.Save
{
    /// <summary>
    /// How <see cref="SaveLoadService"/> asks the scene to put the player's built world back.
    ///
    /// <para>
    /// Exactly the shape of <see cref="IGolemRespawner"/>, and for the same reason: restoring is
    /// arithmetic on data, but *building* is <c>Instantiate</c> plus grid occupancy plus every
    /// endpoint registration a placement performs (the belt network, the spatial endpoint
    /// registry, the steam network). Only a live scene has those, and keeping them behind one
    /// interface is what lets the service stay a plain static an EditMode test can drive with a
    /// fake.
    /// </para>
    ///
    /// <para>
    /// The two halves are separate calls on purpose. A load must REPLACE the built world rather
    /// than merge into it -- loading twice would otherwise stack two factories on one floor, and
    /// the second copy would silently fail to place because the first already occupies every
    /// cell. That is the same reason <c>StorageBufferRegistry.Clear()</c> runs before buffers are
    /// replayed.
    /// </para>
    /// </summary>
    public interface IBuildingRebuilder
    {
        /// <summary>
        /// Demolishes every building the player placed, leaving scene-authored ones alone.
        /// Returns how many went, for the load's own report.
        /// </summary>
        int ClearPlacedBuildings();

        /// <summary>
        /// Rebuilds one saved building at its cell and facing, without charging its cost -- the
        /// player paid in the session that placed it. Returns false if the prefab cannot be
        /// resolved or the cell is already taken, in which case the caller counts it as skipped
        /// rather than failing the whole load.
        /// </summary>
        bool TryRebuild(string prefabKey, Vector2Int cell, Facing facing, out PlaceableBuilding building);
    }
}
