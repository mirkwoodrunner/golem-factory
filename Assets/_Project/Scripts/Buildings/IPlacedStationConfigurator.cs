namespace GolemFactory.Buildings
{
    /// <summary>
    /// Wires a <see cref="GolemConstructionStation"/> that came into the world after the
    /// scene's one-shot bootstrap sweep had already run -- one the player built, or one a save
    /// file rebuilt.
    ///
    /// <para>
    /// This exists because a station is the one placeable whose job needs *scene* references
    /// (the buffer registry it spends from, the clock its golems tick on, the Workbench it
    /// hands them to), and a prefab cannot carry those. Until this seam existed, a placed
    /// station was a building the player could pay 25 Scrap + 5 Brass for and get nothing
    /// from: every serialized reference on the prefab is null, so <c>TryConstructGolem</c>
    /// early-outed and the station built nothing, silently.
    /// </para>
    ///
    /// <para>
    /// Same shape and same reasoning as <c>Save/IGolemRespawner</c> and
    /// <c>Save/IBuildingRebuilder</c>: the thing that already knows how to wire a station is
    /// the scene's bootstrap, so it implements this rather than
    /// <c>Player/BuildModeController</c> growing a second copy of that knowledge (and half a
    /// dozen holder fields to hold it in). Unwired -- Main.unity, and every test rig that does
    /// not care -- placement behaves exactly as it did.
    /// </para>
    /// </summary>
    public interface IPlacedStationConfigurator
    {
        /// <summary>
        /// Gives <paramref name="station"/> everything it needs to actually build a golem.
        /// Returns false when the configurator has nothing to give (no chassis roster, no golem
        /// prefab), so a caller can tell "wired" from "wired to nothing".
        /// </summary>
        bool ConfigureStation(GolemConstructionStation station);
    }
}
