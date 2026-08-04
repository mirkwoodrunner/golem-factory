namespace GolemFactory.UI
{
    /// <summary>
    /// Pure affordability arithmetic for the golem construction panel: can this chassis be
    /// paid for, and if not, what exactly is missing.
    /// <para>
    /// Split out from the panel because "not enough resources" is a useless thing to tell a
    /// player -- they need to know *which* resource and *how much more*, which is a small
    /// piece of real logic worth testing without a scene. Mirrors StorageBufferRegistry
    /// .TryWithdrawScrapAndBrass's own rule that a non-positive cost is always payable, so the
    /// panel's preview can never disagree with what the withdrawal actually does.
    /// </para>
    /// </summary>
    public static class ConstructionCostPolicy
    {
        public static bool CanAfford(int scrapStock, int brassStock, int scrapCost, int brassCost) =>
            (scrapCost <= 0 || scrapStock >= scrapCost) && (brassCost <= 0 || brassStock >= brassCost);

        /// <summary>
        /// The cost line shown on every row. Free is stated explicitly rather than shown as an
        /// empty string, so a zero-cost chassis doesn't look like a rendering failure.
        /// </summary>
        public static string FormatCost(int scrapCost, int brassCost)
        {
            if (scrapCost <= 0 && brassCost <= 0)
            {
                return "Free";
            }

            if (scrapCost <= 0)
            {
                return brassCost + " Brass";
            }

            if (brassCost <= 0)
            {
                return scrapCost + " Scrap";
            }

            return scrapCost + " Scrap  +  " + brassCost + " Brass";
        }

        /// <summary>
        /// What the player still has to go and get. Returns an empty string when affordable,
        /// so the caller can use emptiness as the "no problem" signal instead of duplicating
        /// the CanAfford test.
        /// </summary>
        public static string FormatShortfall(int scrapStock, int brassStock, int scrapCost, int brassCost)
        {
            int scrapShort = scrapCost > 0 ? scrapCost - scrapStock : 0;
            int brassShort = brassCost > 0 ? brassCost - brassStock : 0;
            if (scrapShort <= 0 && brassShort <= 0)
            {
                return "";
            }

            if (scrapShort > 0 && brassShort > 0)
            {
                return "Need " + scrapShort + " more Scrap and " + brassShort + " more Brass";
            }

            return scrapShort > 0
                ? "Need " + scrapShort + " more Scrap"
                : "Need " + brassShort + " more Brass";
        }
    }
}
