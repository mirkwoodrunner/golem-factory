using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Simulation;

namespace GolemFactory.UI
{
    // Surfaces the simulation clock the player's factory actually runs on: tick counter,
    // effective rate, pause, and the speed presets. SimulationClockRunner has exposed
    // Play/Pause/SetSpeed since M2 but nothing ever surfaced them -- the M2 notes deferred a
    // "play/pause/speed HUD" and it was never built, so a player had no way to pause a running
    // factory or slow it down to watch a cycle.
    //
    // Built from code rather than authored into the shared prefab, the same
    // GolemStallIndicator does, so it needs no per-scene wiring beyond the runner reference.
    public sealed class SimulationControlBar : MonoBehaviour
    {
        [SerializeField] private SimulationClockRunner runner;
        [SerializeField] private Sprite buttonSprite;

        private TextMeshProUGUI _tickLabel;
        private TextMeshProUGUI _stateLabel;
        private Button _playPauseButton;
        private TextMeshProUGUI _playPauseLabel;
        private readonly Button[] _speedButtons = new Button[4];

        public void Configure(SimulationClockRunner clockRunner) => runner = clockRunner;

        private void Awake()
        {
            if (runner == null)
            {
                runner = Object.FindFirstObjectByType<SimulationClockRunner>();
            }

            BuildUI();
        }

        private void Update()
        {
            if (runner == null || _tickLabel == null)
            {
                return;
            }

            SimulationClock clock = runner.Clock;
            _tickLabel.text = "TICK " + ClockReadout.FormatTick(clock.CurrentTick);
            _stateLabel.text = ClockReadout.Describe(clock.State, clock.TicksPerSecond, clock.Speed);

            bool paused = clock.State == ClockState.Paused;
            // Label the action, not the state -- a button reading "PAUSED" is ambiguous about
            // whether pressing it pauses or resumes.
            _playPauseLabel.text = paused ? "PLAY" : "PAUSE";
            _stateLabel.color = paused
                ? new Color(1f, 0.78f, 0.35f)
                : new Color(0.82f, 0.86f, 0.78f);

            int active = ClockReadout.IndexOfSpeed(clock.Speed);
            for (int i = 0; i < _speedButtons.Length; i++)
            {
                if (_speedButtons[i] == null)
                {
                    continue;
                }

                var image = _speedButtons[i].GetComponent<Image>();
                image.color = i == active
                    ? new Color(0.95f, 0.62f, 0.20f)
                    : new Color(0.38f, 0.28f, 0.19f);
            }
        }

        public void TogglePlayPause()
        {
            if (runner == null)
            {
                return;
            }

            if (runner.Clock.State == ClockState.Paused)
            {
                runner.Play();
            }
            else
            {
                runner.Pause();
            }
        }

        public void SetSpeedPreset(int index)
        {
            if (runner == null || index < 0 || index >= ClockReadout.SpeedPresets.Length)
            {
                return;
            }

            runner.SetSpeed(ClockReadout.SpeedPresets[index]);
        }

        private void BuildUI()
        {
            var canvasGO = new GameObject("SimControlCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Under the Workbench/Management screens, which are modal when open.
            canvas.sortingOrder = 50;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var barGO = new GameObject("Bar", typeof(RectTransform), typeof(Image),
                typeof(HorizontalLayoutGroup));
            barGO.transform.SetParent(canvasGO.transform, false);

            var barRect = barGO.GetComponent<RectTransform>();
            // Bottom-centre: out of the way of the top alerts strip and the bottom-left build
            // menu, both of which are always-on in Sandbox.
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 16f);
            barRect.sizeDelta = new Vector2(560f, 52f);

            var barImage = barGO.GetComponent<Image>();
            barImage.color = new Color(0.16f, 0.11f, 0.07f, 0.96f);
            if (buttonSprite != null)
            {
                barImage.sprite = buttonSprite;
                barImage.type = Image.Type.Sliced;
            }

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            // childControl* true and per-child flexibleHeight 0 -- a sprited Image otherwise
            // reports its native size and balloons the row. This has bitten three passes.
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;

            _tickLabel = CreateLabel(barGO.transform, "TickLabel", 130f);
            _stateLabel = CreateLabel(barGO.transform, "StateLabel", 130f);

            _playPauseButton = CreateButton(barGO.transform, "PlayPause", 96f, out _playPauseLabel);
            _playPauseButton.onClick.AddListener(TogglePlayPause);

            for (int i = 0; i < ClockReadout.SpeedPresets.Length; i++)
            {
                TextMeshProUGUI label;
                Button button = CreateButton(barGO.transform, "Speed" + i, 48f, out label);
                label.text = ClockReadout.FormatSpeed(ClockReadout.SpeedPresets[i]);
                int captured = i;
                button.onClick.AddListener(delegate { SetSpeedPreset(captured); });
                _speedButtons[i] = button;
            }
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string name, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 32f;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            var label = go.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 20f;
            label.color = new Color(0.93f, 0.86f, 0.72f);
            label.raycastTarget = false;
            return label;
        }

        private Button CreateButton(Transform parent, string name, float width,
            out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 34f;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            var image = go.GetComponent<Image>();
            image.color = new Color(0.38f, 0.28f, 0.19f);
            if (buttonSprite != null)
            {
                image.sprite = buttonSprite;
                image.type = Image.Type.Sliced;
            }

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(go.transform, false);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            label = labelGO.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.97f, 0.92f, 0.82f);
            label.raycastTarget = false;

            return go.GetComponent<Button>();
        }
    }
}
