using GolemFactory.ClockTower;
using GolemFactory.Events;

namespace GolemFactory.Civic
{
    /// <summary>What the player reads about the civic works (docs/endless-design.md §1.2, §3). Never an id.</summary>
    public static class CivicText
    {
        /// <summary>The alerts' note: "The Tramway: The Rails built", or "The Tramway is finished".</summary>
        public static string Built(CivicStageCompletedEvent e) =>
            e.WorkFinished
                ? e.WorkName + " is finished"
                : e.WorkName + ": " + (string.IsNullOrEmpty(e.StageName) ? "stage " + e.StageNumber : e.StageName) + " built";

        /// <summary>
        /// The won tower's panel headline while a work is under way: "The Tramway 1/2 - 42% - x1.00",
        /// as long as the tower's own so it fits the panel on one line (the stage's name is in the
        /// alerts' note).
        /// </summary>
        public static string Headline(CivicWorks civic, ClockTowerReading reading) =>
            civic.Current == null || !reading.HasActiveStage
                ? AllBuilt
                : civic.Current.workName + " " + reading.StageNumber + "/" + civic.Current.stages.Count
                  + " - " + reading.ProgressPercent + "% - " + ClockTowerReadout.FormatMultiplier(reading.StageMultiplierMilli);

        /// <summary>A starved line: "[!] The Tramway starved of Casing - needs 6/min more - progress frozen".</summary>
        public static string Starved(CivicWorks civic, ClockTowerReading reading) =>
            !reading.IsStarved || civic.Current == null
                ? ""
                : ClockTowerReadout.FormatStarvedAlert(reading.StarvedItemType, reading.StarvedDeficitPerMinute, civic.Current.workName);

        public const string AllBuilt = "Every civic work stands";
    }
}
