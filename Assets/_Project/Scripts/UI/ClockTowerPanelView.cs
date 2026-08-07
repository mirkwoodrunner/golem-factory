using UnityEngine;
using TMPro;
using GolemFactory.ClockTower;

namespace GolemFactory.UI
{
    // The Clock Tower readout (docs/progression-design.md §8's "Why is my rate low?" row):
    // Required / Delivered / Fresh / xmultiplier during a stage, plus an alert naming the
    // starved item and its deficit in items/min.
    //
    // §12's self-assessment makes this load-bearing rather than polish -- cut the legibility
    // surfaces and "the economy becomes invisible". Four columns specifically, because a player
    // whose multiplier is capped by FRESH PRODUCTION rather than delivery has a full input buffer
    // and a crawling bar, and no other readout in the game can tell them why.
    //
    // A THIN VIEW over a pure formatter, the same split SteamFuelGaugeView has over
    // SteamGaugeUtility and AlertsPanel has over StallDiagnostics: every number and every string
    // comes from ClockTowerReadout, which is unit-tested without a scene. This class only decides
    // when to repaint and what colour to use.
    public sealed class ClockTowerPanelView : MonoBehaviour
    {
        [SerializeField] private ClockTowerSiteHolder siteHolder;
        [SerializeField] private TextMeshProUGUI headlineText;
        [SerializeField] private TextMeshProUGUI demandTableText;
        [SerializeField] private TextMeshProUGUI alertText;
        [SerializeField] private float refreshIntervalSeconds = 0.25f;

        // Same warm-brass/cool-alert vocabulary the rest of the HUD uses. The alert line carries
        // the word "starved" and a number, so the warning never depends on colour alone -- the
        // constraint InventoryPanel's rate column and SteamFuelGaugeView both already follow.
        private static readonly Color NormalColor = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color AlertColor = new Color(1f, 0.52f, 0.40f, 1f);
        private static readonly Color IdleColor = new Color(0.55f, 0.52f, 0.47f, 1f);
        private static readonly Color WinColor = new Color(0.62f, 0.86f, 0.55f, 1f);

        private float _nextRefreshTime;

        public void Configure(ClockTowerSiteHolder holder) => siteHolder = holder;

        public void ConfigureUI(
            TextMeshProUGUI headline, TextMeshProUGUI demandTable, TextMeshProUGUI alert)
        {
            headlineText = headline;
            demandTableText = demandTable;
            alertText = alert;
        }

        /// <summary>The reading currently on screen. Exposed so a test can assert it directly.</summary>
        public ClockTowerReading CurrentReading { get; private set; }

        private void OnEnable() => _nextRefreshTime = 0f;

        private void Update()
        {
            // Throttled: the rates move in whole items per minute over a sixty-second window, so
            // repainting a TMP mesh every frame buys nothing. Unscaled time, because the panel
            // must keep updating while the simulation is paused -- a paused tower accrues
            // nothing and the reading should say so immediately rather than freezing on the last
            // running value.
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
            Refresh();
        }

        /// <summary>Recomputes and repaints. Public so a test can drive it without waiting on Update.</summary>
        public void Refresh()
        {
            // BuildReading reads only the last settled evaluation and never advances the site --
            // the same rule SteamNetwork.LastEvaluatedPoweredCount exists for. A HUD repainting
            // at 60 fps must not get to decide when the simulation re-derives anything.
            CurrentReading = siteHolder == null
                ? ClockTowerReading.Dormant()
                : siteHolder.Site.BuildReading();

            if (headlineText != null)
            {
                headlineText.text = ClockTowerReadout.FormatHeadline(CurrentReading);
                headlineText.color = CurrentReading.IsComplete
                    ? WinColor
                    : CurrentReading.HasActiveStage ? NormalColor : IdleColor;
            }

            if (demandTableText != null)
            {
                demandTableText.text = ClockTowerReadout.FormatDemandTable(CurrentReading);
                demandTableText.color = NormalColor;
            }

            if (alertText != null)
            {
                alertText.text = ClockTowerReadout.FormatStarvedAlert(CurrentReading);
                alertText.color = AlertColor;
            }
        }
    }
}
