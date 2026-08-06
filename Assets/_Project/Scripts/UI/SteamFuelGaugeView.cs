using UnityEngine;
using TMPro;
using GolemFactory.Steam;

namespace GolemFactory.UI
{
    // The boiler fuel gauge (docs/progression-design.md §8): current Coke, burn rate and a
    // countdown, with an alert at 25 %. §12's self-assessment makes this load-bearing rather
    // than polish -- cut the legibility surfaces and "the economy becomes invisible and this
    // point fails", because steam upkeep ends up ~41 % of the endgame factory and a running
    // cost the player cannot see is one they cannot plan against.
    //
    // A THIN VIEW over a pure formatter, the same split AlertsPanel has over StallDiagnostics
    // and InventoryPanel has over BufferTrendUtility: every number and every string comes from
    // SteamGaugeUtility, which is unit-tested without a scene. This class only decides when to
    // repaint and what colour to use.
    //
    // Reads the aggregate across all boilers rather than one per boiler. §8's example line is a
    // single reading ("240 Coke - 24/min - 10:00 left") and Phase 1 has exactly one boiler; a
    // per-boiler breakdown is a panel, not a HUD strip, and belongs with the §8 "Golems 96 -
    // Powered 92 - Upkeep 576 Coke/min" line that is still unbuilt.
    public sealed class SteamFuelGaugeView : MonoBehaviour
    {
        [SerializeField] private SteamNetworkHolder steamNetworkHolder;
        [SerializeField] private TextMeshProUGUI gaugeText;
        [SerializeField] private float refreshIntervalSeconds = 0.25f;

        // Same warm-brass/cool-alert vocabulary the rest of the HUD uses. The text says "left"
        // and prints a falling countdown independently, so the alert never depends on colour
        // alone -- the constraint InventoryPanel's rate column already follows.
        private static readonly Color NormalColor = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color LowColor = new Color(1f, 0.52f, 0.40f, 1f);
        private static readonly Color IdleColor = new Color(0.55f, 0.52f, 0.47f, 1f);

        private float _nextRefreshTime;

        public void Configure(SteamNetworkHolder holder) => steamNetworkHolder = holder;

        public void ConfigureUI(TextMeshProUGUI text) => gaugeText = text;

        /// <summary>The reading currently on screen. Exposed so a test can assert it directly.</summary>
        public SteamGaugeReading CurrentReading { get; private set; }

        private void OnEnable() => _nextRefreshTime = 0f;

        private void Update()
        {
            // Throttled: the countdown moves in whole seconds, so repainting a TMP mesh every
            // frame buys nothing. Unscaled time, because the gauge must keep updating while the
            // simulation is paused -- a paused factory burns nothing and the reading should
            // show that immediately rather than freezing on the last running value.
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
            if (steamNetworkHolder == null)
            {
                CurrentReading = SteamGaugeUtility.Compute(0, 0, 0);
                if (gaugeText != null)
                {
                    gaugeText.text = "No boiler";
                    gaugeText.color = IdleColor;
                }

                return;
            }

            SteamNetwork network = steamNetworkHolder.Network;

            // PoweredGolemCount is what makes §3.1's "the countdown responds live to every golem
            // placed" true: the burn rate is the current powered count times 6/min, so placing a
            // golem next to a pipe shortens the countdown on the very next refresh.
            //
            // LastEvaluatedPoweredCount, not PoweredGolemCount(tick): the view has no tick of
            // its own, and evaluating at an invented one would let a 60fps HUD decide when the
            // simulation re-derives power. See the accessor's own note.
            CurrentReading = SteamGaugeUtility.Compute(
                network.TotalCokeStock,
                network.LastEvaluatedPoweredCount,
                network.TotalPeakCokeStock);

            if (gaugeText == null)
            {
                return;
            }

            gaugeText.text = SteamGaugeUtility.Format(CurrentReading);
            gaugeText.color = CurrentReading.IsLow
                ? LowColor
                : CurrentReading.HasCountdown ? NormalColor : IdleColor;
        }
    }
}
