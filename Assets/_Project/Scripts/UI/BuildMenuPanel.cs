using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Buildings;
using GolemFactory.Player;

namespace GolemFactory.UI
{
    // UGUI replacement for the original OnGUI build menu: lists BuildModeController's
    // available placeable prefabs with cost as buttons; picking one just calls
    // SetActivePrefab, so BuildModeController.PlaceOrRemove's existing click-to-place flow
    // (and its cost-gating) is unchanged by this panel's existence. Rows are rebuilt from
    // data on Start (AvailablePrefabs is set once via ConfigureEconomy and never changes at
    // runtime), matching the "always re-render from data" idiom used by WorkbenchController.
    public sealed class BuildMenuPanel : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildModeController;
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Sprite _rowSprite;
        [SerializeField] private Sprite _rowActiveSprite;

        // The panel body this hides when a full screen is open (as opposed to this whole
        // GameObject, which WorkbenchController's own hideWhileOpen list already toggles --
        // driving the same object from two mechanisms is how they end up fighting).
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private WorkbenchController _workbenchController;
        [SerializeField] private ManagementPanel _managementPanel;
        [SerializeField] private GolemConstructionPanel _constructionPanel;

        private readonly List<(PlaceableBuilding prefab, Image image)> _rows = new();

        private const float RowHeight = 30f;

        // Lit brass for the tool in hand, dim iron for the rest; captions invert with the
        // plate so both stay legible.
        private static readonly Color SelectedRowColor = new Color(0.88f, 0.68f, 0.35f, 1f);
        private static readonly Color UnselectedRowColor = new Color(0.36f, 0.32f, 0.27f, 1f);
        private static readonly Color SelectedLabelColor = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedLabelColor = new Color(0.82f, 0.78f, 0.71f, 1f);

        public void Configure(BuildModeController buildModeController) => _buildModeController = buildModeController;

        /// <summary>
        /// Wires the screens this panel must not draw over. Separate from Configure so a scene
        /// that has no full screens (or a test) simply never calls it and the panel stays
        /// always-visible exactly as before.
        /// </summary>
        public void ConfigureScreens(
            GameObject panelRoot, WorkbenchController workbench, ManagementPanel management, GolemConstructionPanel construction)
        {
            _panelRoot = panelRoot;
            _workbenchController = workbench;
            _managementPanel = management;
            _constructionPanel = construction;
        }

        /// <summary>Whether the panel body is currently drawn. Exposed for tests.</summary>
        public bool IsBodyVisible => _panelRoot == null || _panelRoot.activeSelf;

        private void Start()
        {
            if (_panelRoot == null && _rowContainer != null)
            {
                // Default to the row container's own panel parent, so a scene that never calls
                // ConfigureScreens still gets the right object hidden rather than nothing.
                Transform panel = _rowContainer.parent;
                if (panel != null)
                {
                    _panelRoot = panel.gameObject;
                }
            }

            RebuildUI();
            ApplyScreenVisibility();
        }

        // This panel lives on its own Canvas (it is not part of the shared
        // WorkbenchCanvas.prefab, which both scenes instantiate), and two root Canvases with
        // the same sortingOrder resolve by hierarchy order -- which is why it drew straight
        // over the Management screen. Rather than fight sorting across separate canvases, it
        // asks HudScreenPolicy the same question every other screen asks.
        private void Update() => ApplyScreenVisibility();

        private void ApplyScreenVisibility()
        {
            if (_panelRoot == null)
            {
                return;
            }

            bool show = HudScreenPolicy.ShouldShowWorldHud(
                _workbenchController != null && _workbenchController.IsOpen,
                _managementPanel != null && _managementPanel.IsOpen,
                _constructionPanel != null && _constructionPanel.IsOpen);

            if (_panelRoot.activeSelf != show)
            {
                _panelRoot.SetActive(show);
            }
        }

        private void RebuildUI()
        {
            if (_rowContainer == null)
            {
                return;
            }

            NormalizeContainerLayout();

            foreach (Transform child in _rowContainer)
            {
                Destroy(child.gameObject);
            }
            _rows.Clear();

            if (_buildModeController == null || _buildModeController.AvailablePrefabs == null)
            {
                return;
            }

            foreach (PlaceableBuilding prefab in _buildModeController.AvailablePrefabs)
            {
                if (prefab != null)
                {
                    CreateRow(prefab);
                }
            }

            // Last, under the things it undoes. Its own row rather than a modifier on the
            // others, because it is not "place a Demolish" -- it is the one tool in this menu
            // that costs nothing and gives goods back.
            CreateDemolishRow();

            FitPanelHeightToRows(_rows.Count + 1);
            RefreshHighlights();
        }

        // The layout landmine, hit for real here: the container was authored with
        // childControlHeight = false, which makes the layout system ignore every row's
        // LayoutElement.preferredHeight of 28 and keep whatever height the row's RectTransform
        // happened to have. That stayed invisible while rows were flat-coloured Images (which
        // report no useful size) and only appeared once the steampunk row sprites were
        // assigned -- rows rendered at 100px instead of 28px and the menu overflowed off the
        // bottom of the screen. Enforced from code, not just fixed in the scene, so a future
        // Inspector edit cannot silently reintroduce it.
        private void NormalizeContainerLayout()
        {
            var layout = _rowContainer.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
        }

        private void CreateRow(PlaceableBuilding prefab)
        {
            PlaceableBuilding captured = prefab;
            Image image = CreateRowShell(prefab.name, () =>
            {
                // TOGGLE, not just select: clicking the row you already hold puts the
                // placeable down. Two ways out of build mode (this and Escape / right-click),
                // because the one thing worse than no way out is one nobody finds.
                if (_buildModeController.ActivePrefab == captured)
                {
                    _buildModeController.CancelPlacement();
                }
                else
                {
                    _buildModeController.SetActivePrefab(captured);
                }

                RefreshHighlights();
            });

            // "GolemConstructionStationPrefab (Scrap 25, Brass 5)" wrapped onto a second line
            // and overflowed the row. The "Prefab" suffix is an asset-naming artifact the
            // player should never see, and ConstructionCostPolicy already words a cost more
            // compactly than the raw fields did.
            CreateLabel(image.transform,
                StripPrefabSuffix(prefab.name) + "   (" + ConstructionCostPolicy.FormatCost(prefab.Cost) + ")");

            _rows.Add((prefab, image));
        }

        // The wrecking bar's row. Held separately from _rows, which is keyed by prefab and has
        // no entry this could be: the demolish tool is not a placeable and giving it a null
        // prefab would make every "is this row the active one" test answer yes whenever nothing
        // was in hand.
        private Image _demolishRow;

        private void CreateDemolishRow()
        {
            _demolishRow = CreateRowShell("Demolish", () =>
            {
                if (_buildModeController.IsDemolishActive)
                {
                    _buildModeController.CancelPlacement();
                }
                else
                {
                    _buildModeController.EnterDemolishMode();
                }

                RefreshHighlights();
            });

            CreateLabel(_demolishRow.transform, "Demolish   (full refund)");
        }

        /// <summary>
        /// Grows the panel so every row fits, and never shrinks it below the height it was
        /// authored with.
        ///
        /// <para>
        /// <b>Measured, because the tenth row would not have fitted.</b> The authored panel is
        /// 280x356 with the row container inset 48px vertically, leaving 308px of row space --
        /// and nine rows at 30px with 4px gaps come to 302px. The Demolish row makes ten, which
        /// is 336px, so it would have hung off the bottom of a panel that has no ScrollRect and
        /// no ContentSizeFitter. Computed from the container's own layout rather than typed as a
        /// new magic number, so the eleventh placeable does not reintroduce this -- the same
        /// reason <see cref="NormalizeContainerLayout"/> enforces its settings from code instead
        /// of trusting the scene.
        /// </para>
        /// </summary>
        private void FitPanelHeightToRows(int rowCount)
        {
            if (_panelRoot == null || _rowContainer == null || rowCount <= 0)
            {
                return;
            }

            var root = _panelRoot.GetComponent<RectTransform>();
            if (root == null)
            {
                return;
            }

            var layout = _rowContainer.GetComponent<VerticalLayoutGroup>();
            float spacing = layout != null ? layout.spacing : 0f;
            float padding = layout != null ? layout.padding.top + layout.padding.bottom : 0f;

            // A stretched child's negative sizeDelta.y IS the chrome above and below it (title
            // bar, borders). Any other anchoring makes that number mean something else, so this
            // declines to guess rather than resizing the panel against a misread inset.
            if (!Mathf.Approximately(_rowContainer.anchorMin.y, 0f)
                || !Mathf.Approximately(_rowContainer.anchorMax.y, 1f))
            {
                return;
            }

            float chrome = Mathf.Max(0f, -_rowContainer.sizeDelta.y);
            float needed = rowCount * RowHeight + (rowCount - 1) * spacing + padding + chrome;
            root.sizeDelta = new Vector2(root.sizeDelta.x, Mathf.Max(root.sizeDelta.y, needed));
        }

        private Image CreateRowShell(string name, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_rowContainer, false);

            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 240f;
            layoutElement.preferredHeight = RowHeight;
            layoutElement.minHeight = RowHeight;
            layoutElement.flexibleWidth = 0f;
            // Explicit zero, not an unset -1: an unset flexibleHeight still lets the parent
            // VerticalLayoutGroup hand out whatever slack it has.
            layoutElement.flexibleHeight = 0f;

            Image image = go.GetComponent<Image>();
            if (_rowSprite != null)
            {
                image.sprite = _rowSprite;
                image.type = Image.Type.Sliced;
            }

            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            return image;
        }

        // What the rows were last drawn against. The highlight used to be refreshed ONLY when a
        // row was clicked, so Escape and right-click cleared the held placeable and left the row
        // still lit -- the player then had to click twice to get it back, because the first click
        // was toggling a selection the menu only believed it still had. Polled rather than
        // event-driven because the controller can lose its placeable several ways (a key, a
        // mouse button, a row) and one of them will always be the one nobody wired an event to.
        private PlaceableBuilding _highlightedPrefab;
        private bool _highlightedDemolish;
        private bool _hasDrawnHighlights;

        private void LateUpdate()
        {
            if (_buildModeController == null)
            {
                return;
            }

            PlaceableBuilding active = _buildModeController.ActivePrefab;
            bool demolishing = _buildModeController.IsDemolishActive;
            // Demolish is tracked alongside the prefab, not instead of it: both are "null
            // prefab" as far as ActivePrefab is concerned, so watching that alone would leave
            // the wrecking bar's row lit after Escape -- the exact bug this poll was added for.
            if (_hasDrawnHighlights && active == _highlightedPrefab && demolishing == _highlightedDemolish)
            {
                return;
            }

            _highlightedPrefab = active;
            _highlightedDemolish = demolishing;
            _hasDrawnHighlights = true;
            RefreshHighlights();
        }

        private void RefreshHighlights()
        {
            if (_buildModeController == null)
            {
                return;
            }

            foreach (var (prefab, image) in _rows)
            {
                ApplyRowHighlight(image, _buildModeController.ActivePrefab == prefab);
            }

            if (_demolishRow != null)
            {
                ApplyRowHighlight(_demolishRow, _buildModeController.IsDemolishActive);
            }
        }

        private void ApplyRowHighlight(Image image, bool isActive)
        {
            Sprite target = isActive ? _rowActiveSprite : _rowSprite;
            if (target != null)
            {
                image.sprite = target;
            }

            // The two row sprites are near-identical brass plates, so swapping them alone
            // left "which tool am I holding" almost unreadable. Brightness is the channel
            // that survives this warm palette -- the same lit/dim split ManagementPanel
            // uses for its selected tab.
            image.color = isActive ? SelectedRowColor : UnselectedRowColor;

            TMPro.TextMeshProUGUI label = image.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label != null)
            {
                label.color = isActive ? SelectedLabelColor : UnselectedLabelColor;
                label.fontStyle = isActive ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
            }
        }

        private static string StripPrefabSuffix(string name) =>
            name != null && name.EndsWith("Prefab") && name.Length > "Prefab".Length
                ? name.Substring(0, name.Length - "Prefab".Length)
                : name;

        private static void CreateLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.color = UnselectedLabelColor;
            label.fontSize = 11;
            // Single line, always: a wrapped caption is taller than the row that contains it,
            // which is what made the second entry spill past the panel.
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = false;
        }
    }
}
