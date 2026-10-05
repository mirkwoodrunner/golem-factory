namespace GolemFactory.Steam
{
    /// <summary>
    /// WHY steam does not reach a golem (G10, from playtest). <see cref="Golems.StallReason.NoSteam"/>
    /// names the tile, but the three causes have three different fixes, and a player whose pipe
    /// visibly ran to the golem was told only "no steam" while the boiler sat empty.
    /// Presentational only: never serialized, so it is free to grow.
    /// </summary>
    public enum SteamShortage
    {
        /// <summary>Powered, or not on a steam network at all.</summary>
        None,

        /// <summary>No boiler's pipes reach this tile: lay a pipe, or move the golem.</summary>
        NoPipe,

        /// <summary>Pipes reach it, but every boiler they lead to has no Coke: fuel one.</summary>
        BoilerOutOfCoke,

        /// <summary>A fuelled boiler reaches it but already powers its 8 golems: build another.</summary>
        BoilerAtCapacity,
    }
}
