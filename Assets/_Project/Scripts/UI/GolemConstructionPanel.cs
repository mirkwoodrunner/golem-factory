using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // The screen PlayerInteractor opens when the player interacts with a GolemConstructionStation:
    // the station's chassis roster, what each costs, what the stockpile can currently pay for,
    // and what to go and harvest if it can't.
    //
    // *** This was the last OnGUI panel in the project, and converting it is the root-cause fix
    // for the HUD overlap. *** OnGUI always draws over Canvas UGUI regardless of sorting order,
    // so as an IMGUI box it could not be layered against the Workbench or Management screens no
    // matter how the wiring was fixed -- it would always punch through. As UGUI it sorts like
    // everything else, and HudScreenPolicy's mutual exclusion is now enforceable rather than
    // aspirational.
    //
    // The entire hierarchy is assembled in code (Awake) rather than authored in the scene, for
    // two concrete reasons: it needs no per-scene wiring, so Main.unity and Sandbox.unity get
    // identical chrome from one source; and it cannot fall into the cross-prefab-reference
    // landmine that left WorkbenchController.focusMeterHolder null in Sandbox. Only the
    // steampunk sprites and the two sibling screens are serialized, and every one of them is
    // optional. Rows are destroyed and rebuilt from data on every Refresh, the same
    // "always re-render from data" idiom as WorkbenchController.RebuildUI/InventoryPanel.
    public sealed class GolemConstructionPanel : MonoBehaviour
    {
        [SerializeField] private WorkbenchController workbenchController;
        [SerializeField] private ManagementPanel managementPanel;

        [Header("Chrome (optional -- flat colours are used when unset)")]
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite rowSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private int sortingOrder = 40;

        // Same warm brass/parchment vocabulary as InventoryPanel and the Workbench, so the
        // three screens read as one HUD. Brightness carries the hierarchy: affordable rows are
        // lit, unaffordable rows drop to a dim steel that is cool against everything else on
        // screen and therefore unmistakably inert.
        private static readonly Color BackdropColor = new Color(0.04f, 0.03f, 0.02f, 0.78f);
        private static readonly Color WindowColor = new Color(0.13f, 0.11f, 0.09f, 0.99f);
        private static readonly Color TitleColor = new Color(0.90f, 0.72f, 0.36f, 1f);
        private static readonly Color SubtitleColor = new Color(0.62f, 0.58f, 0.52f, 1f);
        private static readonly Color StockColor = new Color(0.92f, 0.88f, 0.80f, 1f);

        private static readonly Color RowAffordableTint = new Color(0.86f, 0.66f, 0.34f, 0.20f);
        private static readonly Color RowUnaffordableTint = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color NameAffordableColor = new Color(1f, 0.96f, 0.88f, 1f);
        private static readonly Color NameUnaffordableColor = new Color(0.56f, 0.57f, 0.58f, 1f);
        private static readonly Color CostAffordableColor = new Color(0.94f, 0.78f, 0.44f, 1f);
        private static readonly Color CostUnaffordableColor = new Color(0.52f, 0.55f, 0.58f, 1f);
        private static readonly Color StatusColor = new Color(0.94f, 0.78f, 0.44f, 1f);

        private const float WindowWidth = 560f;
        private const float RowHeight = 52f;
        private const float PortraitSize = 40f;
        private const float CostWidth = 176f;

        private GolemConstructionStation _station;
        private string _statusMessage = "";

        private GameObject _screenRoot;
        private RectTransform _rowContainer;
        private TextMeshProUGUI _stockLabel;
        private TextMeshProUGUI _statusLabel;
        private readonly List<GameObject> _rows = new List<GameObject>();

        // What the currently-rendered rows were built from. Rows are rebuilt only when this
        // changes, NOT every frame like InventoryPanel's rows: these rows carry Buttons, and a
        // Button destroyed between pointer-down and pointer-up never raises onClick -- a
        // per-frame rebuild would make the panel look right and be unclickable.
        private string _renderedSignature;
        private GolemConstructionStation _renderedStation;

        public bool IsOpen { get; private set; }

        /// <summary>Rows currently rendered. Exposed so a test can assert the re-render.</summary>
        public int RowCount => _rows.Count;

        // Test/bootstrap-friendly setup for the mutual-exclusion wiring, same Configure*
        // idiom as WorkbenchController/PlayerInteractor.
        public void ConfigureVisibility(WorkbenchController workbench, ManagementPanel management)
        {
            workbenchController = workbench;
            managementPanel = management;
        }

        public void ConfigureChrome(Sprite panel, Sprite row, Sprite button)
        {
            panelSprite = panel;
            rowSprite = row;
            buttonSprite = button;
        }

        private void Awake()
        {
            EnsureBuilt();
            ApplyVisibility();
        }

        public void Open(GolemConstructionStation station)
        {
            _station = station;
            _statusMessage = "";
            IsOpen = true;
            // Force-closing both siblings here is half the mutual exclusion; the other half is
            // WorkbenchController.Open/ManagementPanel.Open closing this one. Both directions
            // were wired to null in Sandbox.unity, which is why three screens could stack.
            workbenchController?.Close();
            managementPanel?.Close();
            EnsureBuilt();
            ApplyVisibility();
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            _statusMessage = "";
            ApplyVisibility();
        }

        // Polled while open so the stockpile readout and every row's affordability track a
        // golem depositing into the same buffer in the background -- the panel is a live view
        // of a simulation that does not stop while it is open. Same choice ManagementPanel
        // makes for its active tab.
        private void Update()
        {
            if (IsOpen)
            {
                Refresh();
            }
        }

        /// <summary>
        /// Rebuilds every row from the station's roster and the current stockpile. Public so a
        /// test can render without entering Play mode.
        /// </summary>
        public void Refresh()
        {
            EnsureBuilt();
            if (_rowContainer == null)
            {
                return;
            }

            SetStatus(_statusMessage);

            if (_station == null)
            {
                if (_renderedSignature != "none")
                {
                    ClearRows();
                    _renderedSignature = "none";
                }

                SetStock("No construction station selected.", SubtitleColor);
                return;
            }

            int scrapStock;
            int brassStock;
            bool hasStock = _station.TryGetStockpile(out scrapStock, out brassStock);
            SetStock(
                hasStock
                    ? "Stockpile:  " + scrapStock + " Scrap    " + brassStock + " Brass"
                    : "Stockpile:  empty -- harvest a resource node to start one.",
                hasStock ? StockColor : SubtitleColor);

            ChassisDefinition[] roster = _station.ChassisRoster;
            // Stock is part of the signature because affordability -- row tint, portrait
            // brightness, button interactability and the shortfall line -- is derived from it,
            // so a deposit landing while the panel is open must re-render.
            string signature = scrapStock + "|" + brassStock + "|" + (roster == null ? 0 : roster.Length);
            if (signature == _renderedSignature && ReferenceEquals(_renderedStation, _station) && _rows.Count > 0)
            {
                return;
            }

            _renderedSignature = signature;
            _renderedStation = _station;
            ClearRows();

            if (roster == null || roster.Length == 0)
            {
                CreateMessageRow("This station has no chassis roster assigned.");
                return;
            }

            foreach (ChassisDefinition chassis in roster)
            {
                if (chassis != null)
                {
                    CreateChassisRow(chassis, scrapStock, brassStock);
                }
            }
        }

        /// <summary>
        /// Buys a chassis and hands the new golem to the Workbench. Public (rather than only
        /// living inside the button callback) so the flow is drivable from a test, matching
        /// BuildModeController.PlaceOrRemove.
        /// </summary>
        public bool TryConstruct(ChassisDefinition chassis)
        {
            if (_station == null || chassis == null)
            {
                _statusMessage = "No construction station selected.";
                return false;
            }

            GolemEntity golem;
            if (!_station.TryConstructGolem(chassis, out golem))
            {
                int scrapStock;
                int brassStock;
                _station.TryGetStockpile(out scrapStock, out brassStock);
                // Names the missing resource and the amount, not just "not enough" -- the
                // shortfall is the only part of the failure the player can act on.
                string shortfall = ConstructionCostPolicy.FormatShortfall(
                    scrapStock, brassStock, chassis.scrapCost, chassis.brassCost);
                _statusMessage = string.IsNullOrEmpty(shortfall)
                    ? "Could not build " + chassis.name + "."
                    : shortfall + " to build " + chassis.name + ".";
                return false;
            }

            _statusMessage = "";
            // The golem exists but is a bare chassis with no logic core -- useless until
            // programmed. Handing straight to the Workbench (which closes this panel on the
            // way) is the whole point of the station, and leaving this screen up on top of it
            // was the overlap the player actually saw.
            if (workbenchController != null)
            {
                workbenchController.Open();
                workbenchController.RetargetGolem(golem);
            }
            else
            {
                Close();
            }

            return true;
        }

        private void ApplyVisibility()
        {
            if (_screenRoot != null)
            {
                _screenRoot.SetActive(IsOpen);
            }
        }

        private void SetStock(string text, Color color)
        {
            if (_stockLabel != null)
            {
                _stockLabel.text = text;
                _stockLabel.color = color;
            }
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text ?? "";
            }
        }

        private void ClearRows()
        {
            for (int i = _rowContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = _rowContainer.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }

            _rows.Clear();
        }

        private void CreateChassisRow(ChassisDefinition chassis, int scrapStock, int brassStock)
        {
            bool affordable = ConstructionCostPolicy.CanAfford(
                scrapStock, brassStock, chassis.scrapCost, chassis.brassCost);

            GameObject row = CreateRowRoot("Chassis_" + chassis.name, RowHeight,
                affordable ? RowAffordableTint : RowUnaffordableTint);

            var button = row.AddComponent<Button>();
            button.targetGraphic = row.GetComponent<Image>();
            button.interactable = affordable;
            ChassisDefinition captured = chassis;
            button.onClick.AddListener(delegate { TryConstruct(captured); });

            CreatePortrait(row.transform, chassis.chassisSprite, affordable);

            var textColumn = new GameObject("Text", typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            textColumn.transform.SetParent(row.transform, false);
            LayoutElement columnElement = textColumn.GetComponent<LayoutElement>();
            columnElement.flexibleWidth = 1f;
            columnElement.flexibleHeight = 0f;
            VerticalLayoutGroup columnLayout = textColumn.GetComponent<VerticalLayoutGroup>();
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;
            columnLayout.childAlignment = TextAnchor.MiddleLeft;
            columnLayout.spacing = 1f;

            CreateLabel(textColumn.transform, "Name", chassis.name,
                affordable ? NameAffordableColor : NameUnaffordableColor, 16, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft, 1f, 0f, 20f);

            string shortfall = ConstructionCostPolicy.FormatShortfall(
                scrapStock, brassStock, chassis.scrapCost, chassis.brassCost);
            string secondLine = affordable
                ? "Tier " + chassis.tier + "   -   " + chassis.maxAppendageSlots + " appendage slots"
                : shortfall;
            CreateLabel(textColumn.transform, "Detail", secondLine,
                affordable ? SubtitleColor : CostUnaffordableColor, 12, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft, 1f, 0f, 16f);

            CreateLabel(row.transform, "Cost",
                ConstructionCostPolicy.FormatCost(chassis.scrapCost, chassis.brassCost),
                affordable ? CostAffordableColor : CostUnaffordableColor, 14,
                affordable ? FontStyles.Bold : FontStyles.Normal,
                TextAlignmentOptions.MidlineRight, 0f, CostWidth, RowHeight - 8f);

            _rows.Add(row);
        }

        private void CreateMessageRow(string message)
        {
            GameObject row = CreateRowRoot("Message", 32f, Color.clear);
            CreateLabel(row.transform, "Label", message, SubtitleColor, 13, FontStyles.Italic,
                TextAlignmentOptions.MidlineLeft, 1f, 0f, 24f);
            _rows.Add(row);
        }

        // Every row goes through here so the layout landmine is handled in exactly one place:
        // a flat-colour Image reports no useful preferred size, so a mis-configured layout
        // group stays invisible until a child gets a real sprite (the chassis portraits below)
        // and then rows balloon to the sprite's native size. childControlWidth/Height true,
        // childForceExpandWidth false, and an explicit flexibleHeight of 0 are the three
        // settings that actually matter -- an unset -1 still lets the parent hand out slack.
        private GameObject CreateRowRoot(string name, float height, Color tint)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_rowContainer, false);

            LayoutElement element = row.GetComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            element.flexibleHeight = 0f;

            Image background = row.GetComponent<Image>();
            background.color = tint;
            if (rowSprite != null)
            {
                background.sprite = rowSprite;
                background.type = Image.Type.Sliced;
            }

            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 10f;
            layout.padding = new RectOffset(10, 12, 4, 4);
            return row;
        }

        private static void CreatePortrait(Transform parent, Sprite sprite, bool affordable)
        {
            // Created whether or not a sprite resolves, so every row's text starts at the same
            // x -- a ragged left edge is what makes a list unscannable.
            var go = new GameObject("Portrait", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            go.transform.SetParent(parent, false);

            LayoutElement element = go.GetComponent<LayoutElement>();
            element.preferredWidth = PortraitSize;
            element.minWidth = PortraitSize;
            element.preferredHeight = PortraitSize;
            element.minHeight = PortraitSize;
            // A sprited Image reports its own native size as preferred; without these explicit
            // zeros it grows past PortraitSize and drags the row height with it.
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            if (sprite == null)
            {
                image.color = Color.clear;
                return;
            }

            image.sprite = sprite;
            // Unaffordable portraits are dimmed rather than hidden: the player still needs to
            // recognise the chassis they are saving up for.
            image.color = affordable ? Color.white : new Color(0.45f, 0.47f, 0.50f, 0.85f);
        }

        private static void CreateLabel(
            Transform parent, string name, string text, Color color, float fontSize, FontStyles style,
            TextAlignmentOptions alignment, float flexibleWidth, float fixedWidth, float preferredHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            LayoutElement element = go.GetComponent<LayoutElement>();
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = 0f;
            element.preferredHeight = preferredHeight;
            // minHeight too, not just preferred: a layout group short on space shrinks children
            // toward their min, and a min of zero is what let the title collapse to nothing.
            element.minHeight = preferredHeight;
            if (fixedWidth > 0f)
            {
                element.preferredWidth = fixedWidth;
                element.minWidth = fixedWidth;
            }

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = alignment;
            label.color = color;
            label.fontSize = fontSize;
            label.fontStyle = style;
            // Truncate, not Ellipsis: an ellipsis on a chassis name costs a character and adds
            // nothing, the same call the Workbench label pass settled on.
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = false;
        }

        private void EnsureBuilt()
        {
            if (_screenRoot != null)
            {
                return;
            }

            var canvasGo = new GameObject("ConstructionCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _screenRoot = canvasGo;

            // Full-screen backdrop. It is a raycast target on purpose: it swallows clicks that
            // miss the window so a modal screen cannot be clicked through into build mode.
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(canvasGo.transform, false);
            Stretch(backdrop.GetComponent<RectTransform>());
            backdrop.GetComponent<Image>().color = BackdropColor;

            var window = new GameObject("Window",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            window.transform.SetParent(canvasGo.transform, false);
            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.sizeDelta = new Vector2(WindowWidth, 0f);
            windowRect.anchoredPosition = Vector2.zero;

            // The window sizes itself to its content vertically. A fixed height was wrong in
            // both directions: it did not adapt to a station's roster length, and when the
            // content exceeded it the VerticalLayoutGroup shrank every child toward its min
            // height -- which for the title and subtitle was zero, so both silently vanished
            // and the screen opened with an unlabelled dark box at the top.
            ContentSizeFitter fitter = window.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Image windowImage = window.GetComponent<Image>();
            windowImage.color = WindowColor;
            if (panelSprite != null)
            {
                windowImage.sprite = panelSprite;
                windowImage.type = Image.Type.Sliced;
            }

            VerticalLayoutGroup windowLayout = window.GetComponent<VerticalLayoutGroup>();
            windowLayout.childControlWidth = true;
            windowLayout.childControlHeight = true;
            windowLayout.childForceExpandWidth = true;
            windowLayout.childForceExpandHeight = false;
            windowLayout.spacing = 6f;
            windowLayout.padding = new RectOffset(16, 16, 14, 14);

            CreateLabel(window.transform, "Title", "Construct Golem", TitleColor, 22, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft, 1f, 0f, 28f);
            CreateLabel(window.transform, "Subtitle",
                "Pick a chassis. It arrives bare -- the Workbench opens next to fit its logic core.",
                SubtitleColor, 12, FontStyles.Italic, TextAlignmentOptions.MidlineLeft, 1f, 0f, 18f);

            var stockGo = new GameObject("Stock", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            stockGo.transform.SetParent(window.transform, false);
            LayoutElement stockElement = stockGo.GetComponent<LayoutElement>();
            stockElement.preferredHeight = 22f;
            stockElement.flexibleHeight = 0f;
            _stockLabel = stockGo.GetComponent<TextMeshProUGUI>();
            _stockLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _stockLabel.color = StockColor;
            _stockLabel.fontSize = 14;
            _stockLabel.fontStyle = FontStyles.Bold;
            _stockLabel.raycastTarget = false;

            // No LayoutElement height override: the container's own VerticalLayoutGroup reports
            // the exact height of however many chassis rows exist, which is what lets the
            // window fit itself around any roster size.
            var rowsGo = new GameObject("RowContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            rowsGo.transform.SetParent(window.transform, false);
            VerticalLayoutGroup rowsLayout = rowsGo.GetComponent<VerticalLayoutGroup>();
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.spacing = 4f;
            _rowContainer = rowsGo.GetComponent<RectTransform>();

            var statusGo = new GameObject("Status", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            statusGo.transform.SetParent(window.transform, false);
            LayoutElement statusElement = statusGo.GetComponent<LayoutElement>();
            statusElement.preferredHeight = 20f;
            statusElement.flexibleHeight = 0f;
            _statusLabel = statusGo.GetComponent<TextMeshProUGUI>();
            _statusLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _statusLabel.color = StatusColor;
            _statusLabel.fontSize = 13;
            _statusLabel.raycastTarget = false;
            _statusLabel.text = "";

            CreateCloseButton(window.transform);

            canvasGo.SetActive(false);
        }

        private void CreateCloseButton(Transform parent)
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            LayoutElement element = go.GetComponent<LayoutElement>();
            element.preferredHeight = 32f;
            element.minHeight = 32f;
            element.flexibleHeight = 0f;

            Image image = go.GetComponent<Image>();
            image.color = buttonSprite != null ? Color.white : new Color(0.30f, 0.24f, 0.18f, 1f);
            if (buttonSprite != null)
            {
                image.sprite = buttonSprite;
                image.type = Image.Type.Sliced;
            }

            go.GetComponent<Button>().onClick.AddListener(Close);

            CreateLabel(go.transform, "Label", "Close", new Color(0.92f, 0.88f, 0.80f, 1f), 14,
                FontStyles.Bold, TextAlignmentOptions.Center, 1f, 0f, 30f);
            RectTransform labelRect = go.transform.GetChild(0).GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
