using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Save
{
    /// <summary>
    /// Rebuilds saved buildings through <see cref="BuildModeController"/> -- the one object that
    /// already knows how to put a building into the world.
    ///
    /// <para>
    /// THE CONTROLLER IS THE REBUILDER BECAUSE IT IS ALREADY THE BUILDER, the same argument
    /// <see cref="StationGolemRespawner"/> makes for golems. Placing a building means occupying a
    /// grid cell and then registering it with up to three other systems depending on what it is;
    /// a save-specific copy of that would be a second definition of "a building that works", and
    /// the two would drift the first time a new placeable was added to one of them.
    /// </para>
    /// </summary>
    public sealed class BuildModeBuildingRebuilder : IBuildingRebuilder
    {
        private readonly BuildModeController _buildMode;

        public BuildModeBuildingRebuilder(BuildModeController buildMode)
        {
            _buildMode = buildMode;
        }


        public int ClearPlacedBuildings() =>
            _buildMode != null ? _buildMode.ClearRuntimePlacedBuildings() : 0;

        public bool TryRebuild(
            string prefabKey, Vector2Int cell, Facing facing, out PlaceableBuilding building)
        {
            building = null;
            return _buildMode != null &&
                   _buildMode.TryRebuildSavedBuilding(prefabKey, cell, facing, out building);
        }
    }
}
