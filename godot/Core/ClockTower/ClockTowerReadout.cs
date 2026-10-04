using System.Collections.Generic;
using System.Globalization;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// One demanded item's row of §8's readout: <c>Required / Delivered / Fresh / xmultiplier</c>.
    ///
    /// <para>
    /// FOUR COLUMNS, NOT THREE, and the design says why in one sentence: "a player whose
    /// multiplier is capped by *fresh production* rather than delivery needs to see exactly
    /// that". Collapsing Delivered and Fresh into the effective rate would hide the single most
    /// confusing state the tower can be in -- a full input buffer, goods arriving, and the bar
    /// crawling, because the factory is emptying a warehouse rather than making anything.
    /// </para>
    /// </summary>
    public readonly struct ClockTowerDemandReading
    {
        public ClockTowerDemandReading(
            string itemType, int requiredPerMinute, int deliveredPerMinute, int freshPerMinute,
            int multiplierMilli, int meterUnits, bool isStarved)
        {
            ItemType = itemType;
            RequiredPerMinute = requiredPerMinute;
            DeliveredPerMinute = deliveredPerMinute;
            FreshPerMinute = freshPerMinute;
            MultiplierMilli = multiplierMilli;
            MeterUnits = meterUnits;
            IsStarved = isStarved;
        }

        public string ItemType { get; }

        /// <summary>The stage's sustained demand, in items per minute.</summary>
        public int RequiredPerMinute { get; }

        /// <summary>Units pushed into the tower over the last 60 s.</summary>
        public int DeliveredPerMinute { get; }

        /// <summary>Units genuinely Assembled anywhere in the factory over the last 60 s.</summary>
        public int FreshPerMinute { get; }

        /// <summary>This line's own <c>clamp(effective/demand, 0, 3)</c>, in thousandths.</summary>
        public int MultiplierMilli { get; }

        /// <summary>The supply-pressure meter, in whole units of <c>[0, 60]</c>.</summary>
        public int MeterUnits { get; }

        /// <summary>True when this line's meter has bottomed out and is freezing the stage.</summary>
        public bool IsStarved { get; }

        /// <summary>
        /// How many more items per minute this line needs. Measured against DELIVERY, because
        /// the meter is fed by deliveries and by nothing else.
        /// </summary>
        public int DeficitPerMinute =>
            RequiredPerMinute > DeliveredPerMinute ? RequiredPerMinute - DeliveredPerMinute : 0;
    }

    /// <summary>The whole tower readout, already resolved to the numbers §8 asks for.</summary>
    public readonly struct ClockTowerReading
    {
        private static readonly ClockTowerDemandReading[] NoDemands = new ClockTowerDemandReading[0];

        private ClockTowerReading(
            bool hasActiveStage, bool isComplete, int stageNumber, string stageName,
            int progressPercent, int stageMultiplierMilli, string limitingItemType,
            string starvedItemType, int starvedDeficitPerMinute, long completedTick,
            IReadOnlyList<ClockTowerDemandReading> demands)
        {
            HasActiveStage = hasActiveStage;
            IsComplete = isComplete;
            StageNumber = stageNumber;
            StageName = stageName;
            ProgressPercent = progressPercent;
            StageMultiplierMilli = stageMultiplierMilli;
            LimitingItemType = limitingItemType;
            StarvedItemType = starvedItemType;
            StarvedDeficitPerMinute = starvedDeficitPerMinute;
            CompletedTick = completedTick;
            Demands = demands ?? NoDemands;
        }

        public bool HasActiveStage { get; }

        /// <summary>The win. §7 leaves the save running, so this is a state, not an ending.</summary>
        public bool IsComplete { get; }

        public int StageNumber { get; }
        public string StageName { get; }
        public int ProgressPercent { get; }
        public int StageMultiplierMilli { get; }

        /// <summary>The weakest line -- the one holding the whole stage's multiplier down.</summary>
        public string LimitingItemType { get; }

        public string StarvedItemType { get; }
        public int StarvedDeficitPerMinute { get; }
        public long CompletedTick { get; }

        public IReadOnlyList<ClockTowerDemandReading> Demands { get; }

        public bool IsStarved => !string.IsNullOrEmpty(StarvedItemType);

        public static ClockTowerReading Dormant() =>
            new ClockTowerReading(false, false, 0, null, 0, 0, null, null, 0, -1, NoDemands);

        public static ClockTowerReading Completed(string finalStageName, long completedTick) =>
            new ClockTowerReading(
                false, true, 0, finalStageName, 100, 0, null, null, 0, completedTick, NoDemands);

        public static ClockTowerReading Running(
            int stageNumber, string stageName, int progressPercent, int stageMultiplierMilli,
            string limitingItemType, string starvedItemType, int starvedDeficitPerMinute,
            IReadOnlyList<ClockTowerDemandReading> demands) =>
            new ClockTowerReading(
                true, false, stageNumber, stageName, progressPercent, stageMultiplierMilli,
                limitingItemType, starvedItemType, starvedDeficitPerMinute, -1, demands);
    }

    /// <summary>
    /// The Clock Tower's §8 legibility surface, as pure text -- beside a thin view
    /// (<c>UI/ClockTowerPanelView</c>), the same split <c>SteamGaugeUtility</c> has under
    /// <c>SteamFuelGaugeView</c> and <c>StallDiagnostics</c> has under <c>AlertsPanel</c>.
    ///
    /// <para>
    /// §12's self-assessment makes this load-bearing rather than polish: cut the legibility
    /// surfaces and "the economy becomes invisible". A stage bar that crawls with no explanation
    /// is the single worst state Phase 6 can present, because every possible cause -- a starved
    /// line, a capped multiplier, a warehouse standing in for a factory -- looks identical from
    /// the outside.
    /// </para>
    ///
    /// <para>
    /// PLAIN ASCII THROUGHOUT. No multiplication sign, no middle dot, no arrows: TextMeshPro's
    /// default LiberationSans SDF atlas is an ASCII range and anything outside it renders as a
    /// missing-glyph box. The same constraint keeps <c>SteamGaugeUtility.Format</c> on " - ",
    /// <c>BufferTrendUtility.TrendGlyph</c> on "^"/"v" and
    /// <c>StallDiagnostics.ComposeStripText</c> on "[!]".
    /// </para>
    /// </summary>
    public static class ClockTowerReadout
    {
        /// <summary>Column header for the four-column demand table.</summary>
        public const string DemandHeader = "Item  Req / Del / Fresh  Mult";

        /// <summary>
        /// The stage line: <c>Stage 1 Foundation - 42% - x1.00</c>, or the win, or nothing at all
        /// before the tower has stages.
        /// </summary>
        public static string FormatHeadline(ClockTowerReading reading)
        {
            if (reading.IsComplete)
            {
                // Named as a win, not an ending -- §7's "the Clock Tower is a win, not a
                // game-over", and the save is still running behind this line.
                return "Clock Tower complete - the Clockwork Metropolis runs";
            }

            if (!reading.HasActiveStage)
            {
                return "Clock Tower dormant";
            }

            string name = string.IsNullOrEmpty(reading.StageName) ? "Stage" : reading.StageName;
            return "Stage " + reading.StageNumber.ToString(CultureInfo.InvariantCulture) +
                   " " + name +
                   " - " + reading.ProgressPercent.ToString(CultureInfo.InvariantCulture) + "%" +
                   " - " + FormatMultiplier(reading.StageMultiplierMilli);
        }

        /// <summary>
        /// One demand row: <c>FrameSection  6 / 4 / 9  x0.66</c>. Required, then Delivered, then
        /// Fresh, then this line's own multiplier.
        /// </summary>
        public static string FormatDemandRow(ClockTowerDemandReading demand)
        {
            string item = string.IsNullOrEmpty(demand.ItemType) ? "goods" : demand.ItemType;

            return item +
                   "  " + demand.RequiredPerMinute.ToString(CultureInfo.InvariantCulture) +
                   " / " + demand.DeliveredPerMinute.ToString(CultureInfo.InvariantCulture) +
                   " / " + demand.FreshPerMinute.ToString(CultureInfo.InvariantCulture) +
                   "  " + FormatMultiplier(demand.MultiplierMilli);
        }

        /// <summary>The header plus one row per demanded item, newline separated.</summary>
        public static string FormatDemandTable(ClockTowerReading reading)
        {
            if (!reading.HasActiveStage || reading.Demands.Count == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(DemandHeader);
            for (int i = 0; i < reading.Demands.Count; i++)
            {
                builder.Append('\n').Append(FormatDemandRow(reading.Demands[i]));
            }

            return builder.ToString();
        }

        /// <summary>
        /// §7's "an alert names the starved item; the HUD shows the deficit in items/min", or an
        /// empty string when nothing is starved.
        ///
        /// <para>
        /// Names the ITEM, never the tower: "the Clock Tower is starved" is not actionable on a
        /// three-line stage, and the whole reason §8 asks for this row is that the player has to
        /// know which of their lines to go and look at.
        /// </para>
        /// </summary>
        public static string FormatStarvedAlert(ClockTowerReading reading)
        {
            if (!reading.IsStarved)
            {
                return string.Empty;
            }

            return FormatStarvedAlert(reading.StarvedItemType, reading.StarvedDeficitPerMinute);
        }

        public static string FormatStarvedAlert(string itemType, int deficitPerMinute)
        {
            string item = string.IsNullOrEmpty(itemType) ? "goods" : itemType;

            string text = "[!] Clock Tower starved of " + item;
            if (deficitPerMinute > 0)
            {
                text += " - needs " + deficitPerMinute.ToString(CultureInfo.InvariantCulture) +
                        "/min more";
            }

            // No deficit to quote means deliveries are arriving at or above the demand rate and
            // the meter is still empty -- a line that has only just restarted. Saying "needs
            // 0/min more" there would read as a contradiction, so the sentence just stops.
            return text + " - progress frozen";
        }

        /// <summary>
        /// A multiplier in thousandths as the player reads it: <c>x1.00</c>, <c>x3.00</c>,
        /// <c>x0.66</c>. Truncated rather than rounded, so the printed figure never claims more
        /// throughput than the tower is actually crediting.
        /// </summary>
        public static string FormatMultiplier(int multiplierMilli)
        {
            int milli = multiplierMilli > 0 ? multiplierMilli : 0;
            int whole = milli / 1000;
            int hundredths = milli % 1000 / 10;

            return "x" + whole.ToString(CultureInfo.InvariantCulture) + "." +
                   hundredths.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
