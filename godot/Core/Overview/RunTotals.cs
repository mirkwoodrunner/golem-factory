using System.Globalization;

namespace GolemFactory.Overview
{
    /// <summary>
    /// How far the factory has come (docs/endless-design.md §3): the overview's one line of running
    /// totals, over the whole game, not the session: goods made, contracts done, and civic work
    /// stages done once there are civic works.
    /// </summary>
    public static class RunTotals
    {
        /// <summary>"Since the start: 12,340 goods made · 5 contracts done".</summary>
        public static string Text(long goodsMade, int contractsDone, int civicStagesDone = -1, int golemsSold = -1)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            string line = "Since the start: " + goodsMade.ToString("#,0", c) + " goods made · "
                + contractsDone.ToString(c) + (contractsDone == 1 ? " contract" : " contracts") + " done";
            if (civicStagesDone >= 0)
            {
                line += " · " + civicStagesDone.ToString(c) + (civicStagesDone == 1 ? " civic stage" : " civic stages") + " built";
            }
            if (golemsSold >= 0)
            {
                line += " · " + golemsSold.ToString(c) + (golemsSold == 1 ? " golem" : " golems") + " sold";
            }
            return line;
        }
    }
}
