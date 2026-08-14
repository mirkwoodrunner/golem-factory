namespace GolemFactory.Steam
{
    /// <summary>
    /// How much Coke one hand-load moves from the player's stockpile into a boiler.
    ///
    /// <para>
    /// THIS EXISTS BECAUSE THE STEAM ECONOMY HAD NO WAY IN. A boiler's only Coke writer was
    /// <c>BoilerFuelEndpoint</c>, which a golem <c>Push</c>es into -- and a golem needs a powered
    /// boiler to run. A player-built boiler starts at zero, so with the steam requirement on, the
    /// first boiler could never be lit: every golem stalls <c>NoSteam</c>, and nothing that is
    /// stalled can carry it fuel. §10 calls the Hand-Crank Bench the total-blackout backstop, and
    /// the bench does solve the goods half -- it can make Coke -- but nothing moved that Coke the
    /// last three feet into the firebox. This is that last step.
    /// </para>
    ///
    /// <para>
    /// A BATCH, NOT THE WHOLE STOCKPILE, and the size is doing real work. 20 Coke is 2000
    /// powered-golem-ticks: about 200 seconds of one golem, or 25 seconds of a fully subscribed
    /// 8-golem boiler. So one press is a meaningful emergency top-up, and keeping a real factory
    /// alive by hand would mean standing at the boiler pressing a key forever -- which is what
    /// keeps hand-fuelling a way to RESTART a dead factory rather than a way to RUN a live one.
    /// §3.1 wants Coke to be "the contended throat of the entire game"; a hand-load that
    /// comfortably sustained a factory would quietly void that.
    /// </para>
    ///
    /// <para>
    /// Capping also lets a player split what they have between two cold boilers, which moving the
    /// entire stockpile in one press would not.
    /// </para>
    /// </summary>
    public static class BoilerRefuelPolicy
    {
        /// <summary>Coke moved by a single hand-load, when the player has at least that much.</summary>
        public const int HandLoadBatch = 20;

        /// <summary>
        /// How much a hand-load actually moves: the batch, or everything the player has if that
        /// is less. Returns 0 for an empty stockpile, which the caller reports as a refusal
        /// rather than performing a transfer of nothing.
        /// </summary>
        public static int AmountToLoad(int stockpileCoke, int batch = HandLoadBatch)
        {
            if (stockpileCoke <= 0 || batch <= 0)
            {
                return 0;
            }

            return stockpileCoke < batch ? stockpileCoke : batch;
        }
    }
}
