using UnityEngine;
using TMPro;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;

namespace GolemFactory.UI
{
    // The Hand-Crank Bench readout: what the bench is set to make, how far through it is, and
    // whether the player can actually afford it.
    //
    // A THIN VIEW over a pure formatter, the same split SteamFuelGaugeView has over
    // SteamGaugeUtility and ClockTowerPanelView has over ClockTowerReadout: every number and every
    // string comes from HandCrankReadout, which is unit-tested without a scene. This class decides
    // only when to repaint, what colour to use, and whether to be on screen at all.
    //
    // CONTEXTUAL, not permanent. It appears only while the player is standing at a bench, because
    // it is a readout of a thing you are doing rather than a standing gauge like the boiler's --
    // and the HUD already has four fixed corners spoken for.
    public sealed class HandCrankPanelView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor playerInteractor;
        [SerializeField] private StorageBufferRegistryHolder stockpileHolder;
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";
        // MUST BE A CHILD, never this component's own GameObject: a disabled GameObject stops
        // running Update, so a view that switched off the object it lives on could never switch
        // it back on. It hid itself on the first frame and stayed hidden.
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI readoutText;
        [SerializeField] private float refreshIntervalSeconds = 0.1f;

        // Warm brass while it can run, cool alert when it cannot. The text says "need N more X"
        // independently, so the warning never depends on colour alone -- the constraint
        // InventoryPanel's rate column and SteamFuelGaugeView both already follow.
        private static readonly Color NormalColor = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color BlockedColor = new Color(1f, 0.52f, 0.40f, 1f);

        private float _nextRefreshTime;

        public void Configure(PlayerInteractor interactor, StorageBufferRegistryHolder buffers, string bufferId)
        {
            playerInteractor = interactor;
            stockpileHolder = buffers;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        public void ConfigureUI(GameObject root, TextMeshProUGUI text)
        {
            panelRoot = root;
            readoutText = text;
        }

        /// <summary>The reading currently on screen. Exposed so a test can assert it directly.</summary>
        public HandCrankReading CurrentReading { get; private set; }

        private void OnEnable() => _nextRefreshTime = 0f;

        private void Update()
        {
            // Faster than the other HUD panels (0.1 s against 0.25 s): this one carries a progress
            // bar the player is actively watching, and a bar that steps four times a second reads
            // as stuttering rather than turning. Unscaled, so it keeps repainting while paused --
            // a paused bench makes no progress and the readout should show that immediately.
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, refreshIntervalSeconds);
            Refresh();
        }

        /// <summary>Recomputes and repaints. Public so a test can drive it without waiting on Update.</summary>
        public void Refresh()
        {
            HandCrankBench bench = playerInteractor != null ? playerInteractor.NearestBench : null;
            CurrentReading = bench == null ? HandCrankReading.Away() : Read(bench);

            if (panelRoot != null && panelRoot.activeSelf != CurrentReading.HasBench)
            {
                panelRoot.SetActive(CurrentReading.HasBench);
            }

            if (readoutText == null || !CurrentReading.HasBench)
            {
                return;
            }

            readoutText.text = HandCrankReadout.Format(CurrentReading);
            readoutText.color = CurrentReading.CanAfford ? NormalColor : BlockedColor;
        }

        private HandCrankReading Read(HandCrankBench bench)
        {
            PunchCards.RecipeDefinition recipe = bench.SelectedRecipe;
            bool canAfford = bench.CanAffordSelected();

            return new HandCrankReading(
                hasBench: true,
                recipeLabel: HandCrankReadout.RecipeLabel(recipe),
                conversionLabel: HandCrankReadout.ConversionLabel(recipe),
                progressPercent: HandCrankReadout.ProgressPercent(bench.ProgressTicks, bench.RequiredTicks),
                remainingSeconds: HandCrankReadout.RemainingSeconds(
                    bench.ProgressTicks, bench.RequiredTicks, NominalTicksPerSecond),
                canAfford: canAfford,
                shortfallLabel: canAfford ? string.Empty : HandCrankReadout.ShortfallLabel(recipe, StockOf),
                isCranking: bench.IsCranking,
                recipeCount: bench.CrankableRecipes.Count);
        }

        // Matches SimulationClock's default rate. The countdown is a guide at 1x -- the speed
        // controls genuinely make cranking faster, since the bench accrues on simulation ticks.
        private const float NominalTicksPerSecond = 10f;

        private int StockOf(string itemType)
        {
            if (stockpileHolder == null ||
                !stockpileHolder.Registry.TryGetBuffer(stockpileBufferId, out StorageBuffer buffer))
            {
                return 0;
            }

            return buffer.GetQuantity(itemType);
        }
    }
}
