namespace GolemFactory.Economy
{
    /// <summary>
    /// Which rules the world is running under. One flag today -- Creative Mode -- held as a
    /// plain object behind a Holder rather than as a `static bool`, for the reason every other
    /// manager in this project is: a static would be shared across scenes and test runs, and a
    /// test that turned it on would leak into the next one.
    ///
    /// <para>
    /// <b>Creative Mode is a STATE, not a build flag.</b> The Game Director's requirement in
    /// progression-design §13.2 is that the pre-truckload market behaviour -- infinite, free,
    /// steady -- remains something the game can be put into, rather than a version of the game
    /// that was deleted. Everything that consults this must therefore be able to answer both
    /// ways at runtime, which is also what keeps `Main.unity`, every existing test and every
    /// pre-truckload save valid: they simply run in the mode that behaves the way they expect.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT a general "difficulty" or a bag of tuning knobs. One question, asked in
    /// one place, so a reader can find every consequence by finding the callers.
    /// </para>
    /// </summary>
    public sealed class GameMode
    {
        /// <summary>
        /// True to bypass the truckload economy: stalls are infinite, orders cost nothing and
        /// arrive instantly, exactly as the market behaved when the stalls were boulders.
        /// </summary>
        public bool IsCreativeMode { get; set; }

        public GameMode(bool isCreativeMode = false)
        {
            IsCreativeMode = isCreativeMode;
        }
    }
}
